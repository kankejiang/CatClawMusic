package com.catclaw.music.kt.data.download

import java.io.File
import java.util.concurrent.CountDownLatch
import java.util.concurrent.Executors
import java.util.concurrent.TimeUnit
import kotlinx.coroutines.flow.MutableStateFlow
import org.junit.Assert.assertEquals
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Test

/**
 * 任务表的写必须原子。
 *
 * 原来 `MusicDownloader` 里是 `_tasks.value = _tasks.value.filterNot{…} + task`：读-改-写三步，
 * 进度回调在 IO 线程、排队/暂停在主线程，两边同时写时后写的那一份拿的是**旧快照**，
 * 会把对方刚写进去的那一行整个抹掉（页面上任务行闪一下没了；「已暂停」被并发的进度回调翻回「下载中」）。
 */
class DownloadTaskRegistryTest {

    private fun task(id: Int, done: Long = 0L, state: DownloadState = DownloadState.Running) =
        DownloadTask(id, "t$id", "f$id.mp3", done, 100L, state)

    @Test
    fun `同 songId 覆盖成一行，不同 songId 各占一行`() {
        val reg = DownloadTaskRegistry()
        reg.replace(task(1))
        reg.replace(task(2))
        reg.replace(task(1, done = 60L))
        assertEquals(listOf(2, 1), reg.tasks.value.map { it.songId })
        assertEquals(60L, reg.find(1)?.downloaded)
    }

    @Test
    fun `remove 只删那一行，find 读不到就是 null`() {
        val reg = DownloadTaskRegistry()
        reg.replace(task(1))
        reg.replace(task(2))
        reg.remove(1)
        assertNull(reg.find(1))
        assertEquals(listOf(2), reg.tasks.value.map { it.songId })
    }

    @Test
    fun `已取消的 songId 不许被迟到的回调复活`() {
        val reg = DownloadTaskRegistry { it == 7 }
        reg.replace(task(7))
        assertTrue(reg.tasks.value.isEmpty())
    }

    @Test
    fun `64 个线程同时各写一行，一行都不许丢`() {
        val reg = DownloadTaskRegistry()
        val n = 64
        val pool = Executors.newFixedThreadPool(n)
        val go = CountDownLatch(1)
        val done = CountDownLatch(n)
        repeat(n) { i ->
            pool.execute {
                go.await()
                reg.replace(task(i + 1))
                done.countDown()
            }
        }
        go.countDown()
        assertTrue("并发写超时", done.await(15, TimeUnit.SECONDS))
        pool.shutdown()
        assertEquals("丢行了：$n 次写只剩 ${reg.tasks.value.size} 行", n, reg.tasks.value.size)
    }

    /**
     * 正控制：把「A 先读快照 → B 写完 → A 再写」这个交错钉死，旧的读-改-写形状必然抹掉 B 那一行。
     * 没有这条，上面那条通过可能只是因为并发没真发生。
     */
    @Test
    fun `正控制 读-改-写形状在同一样交错下必然丢行`() {
        val flow = MutableStateFlow<List<DownloadTask>>(emptyList())
        val stale = flow.value.filterNot { it.songId == 1 } + task(1)   // A 先算好自己要写什么
        flow.value = flow.value.filterNot { it.songId == 2 } + task(2)  // B 写进去
        flow.value = stale                                              // A 用旧快照写回
        assertTrue("这次没丢行 = 交错没造出来，正控制失效", flow.value.none { it.songId == 2 })
    }

    @Test
    fun `MusicDownloader 里不再自己读改写任务表`() {
        val f = listOf(
            "src/main/kotlin/com/catclaw/music/kt/data/download/MusicDownloader.kt",
            "data/src/main/kotlin/com/catclaw/music/kt/data/download/MusicDownloader.kt",
        ).map(::File).firstOrNull { it.isFile } ?: error("找不到 MusicDownloader.kt（测试工作目录应为 :data 模块根）")
        val text = f.readText()
        assertTrue(
            "任务表只能经 DownloadTaskRegistry 写（读-改-写不原子会丢行）",
            "_tasks" !in text && "registry.replace" in text,
        )
    }
}
