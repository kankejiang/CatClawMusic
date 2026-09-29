package com.catclaw.music.kt.data.playback

import com.catclaw.music.kt.data.db.LibraryDao
import java.lang.reflect.InvocationHandler
import java.lang.reflect.Method
import java.lang.reflect.Proxy
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.SupervisorJob
import org.junit.Assert.assertEquals
import org.junit.Assert.assertTrue
import org.junit.Test

/**
 * 快速连续切歌时，「活着的会话」必须是最后一次 record 的那一首。
 *
 * record() 是两步：先写库拿到会话行 id，再把 (歌曲, id) 记进状态。两步之间有等待，
 * 所以多 worker 并发跑会错序：后一首先写完、前一首后写完，状态里最终留下**前一首**的 id；
 * 之后 sample() 因为 songId 对不上一直早退，这一首的时长永远写不回库
 * ——次数记了、时长 0，统计页看不出异常。落库作用域因此收成单 worker（见 IoScopeModule）。
 *
 * 第二条测试是**正控制**：同一个场景换成并行作用域跑，必须坏（一次库都不落）。
 * 没有它，第一条通过可能只是因为场景根本不成立。
 */
class PlayHistoryRecorderOrderTest {

    private class FakeDao(private val stallOn: Map<Int, Long> = emptyMap()) : InvocationHandler {
        val started = mutableListOf<Pair<Int, Long>>()
        val calls = mutableListOf<String>()
        val flushed = mutableListOf<Pair<Long, Long>>()
        private var nextId = 100L

        override fun invoke(proxy: Any, method: Method, args: Array<Any?>?): Any? {
            val a = args ?: emptyArray()
            return when (method.name) {
                "recordPlay" -> { calls += method.name; null }
                "logListenSession" -> {
                    val songId = a[0] as Int
                    stallOn[songId]?.let { Thread.sleep(it) }
                    calls += "log-" + songId
                    val id = ++nextId
                    synchronized(started) { started += songId to id }
                    id
                }
                "flushListenSession" -> {
                    // 形参是 Int：按 Long 取会 ClassCastException，而它会被 runCatching 吞掉，
                    // 表现就是"一次库都没落"——排掉这个坑之前那条断言假红过一轮
                    val sid = (a[0] as Number).toLong()
                    calls += "flush-" + sid
                    synchronized(flushed) { flushed += sid to (a[1] as Number).toLong() }
                    0L
                }
                "toString" -> "FakeDao"
                "hashCode" -> System.identityHashCode(proxy)
                "equals" -> proxy === a[0]
                else -> defaultFor(method.returnType)
            }
        }

        fun idOf(songId: Int): Long = started.first { it.first == songId }.second

        fun build(): LibraryDao = Proxy.newProxyInstance(
            LibraryDao::class.java.classLoader,
            arrayOf(LibraryDao::class.java),
            this,
        ) as LibraryDao

        private fun defaultFor(t: Class<*>): Any? = when (t) {
            java.util.List::class.java -> emptyList<Any>()
            java.lang.Integer::class.java -> 0
            java.lang.Long::class.java -> 0L
            java.lang.Boolean::class.java -> false
            else -> null
        }
    }

    private fun settle() = Thread.sleep(400)

    @Test
    fun `单 worker 下最后一次 record 的那首才是活着的会话`() {
        val dao = FakeDao(stallOn = mapOf(SONG_A to 250L))
        val recorder = PlayHistoryRecorder(dao.build(), serialScope())
        recorder.record(SONG_A)
        recorder.record(SONG_B)
        settle()
        repeat(31) { recorder.sample(SONG_B, 5_000L + it, playing = true) }
        settle()
        val bId = dao.idOf(SONG_B)
        assertTrue(
            "B 的会话时长必须落回它自己那一行。DAO 调用序列=" + dao.calls,
            dao.flushed.any { it.first == bId },
        )
    }

    @Test
    fun `正控制 并行作用域下同一场景会丢掉后一首的时长`() {
        val dao = FakeDao(stallOn = mapOf(SONG_A to 250L))
        val recorder = PlayHistoryRecorder(dao.build(), CoroutineScope(SupervisorJob() + Dispatchers.IO))
        recorder.record(SONG_A)
        recorder.record(SONG_B)
        settle()
        repeat(31) { recorder.sample(SONG_B, 5_000L + it, playing = true) }
        settle()
        val bId = dao.idOf(SONG_B)
        assertTrue(
            "并行作用域下这条要坏（状态留下 A 的 id，B 的采样全部早退）才说明场景成立。DAO 调用序列=" + dao.calls,
            dao.flushed.none { it.first == bId },
        )
    }

    private fun serialScope(): CoroutineScope =
        CoroutineScope(SupervisorJob() + Dispatchers.IO.limitedParallelism(1))

    private companion object {
        const val SONG_A = 11
        const val SONG_B = 22
    }
}
