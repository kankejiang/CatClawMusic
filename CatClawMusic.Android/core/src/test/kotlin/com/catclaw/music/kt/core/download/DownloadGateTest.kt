package com.catclaw.music.kt.core.download

import kotlinx.coroutines.CompletableDeferred
import kotlinx.coroutines.launch
import kotlinx.coroutines.test.runTest
import kotlinx.coroutines.yield
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Test

/**
 * 并发闸门的三条语义：上限内直接放行、超出要排队、**调高上限要立刻放行排队者**。
 *
 * 第三条最容易被写成「等下一次 release 才生效」—— 那样用户把滑杆从 1 调到 5，界面上还是只有一个在跑，
 * 看着像设置没生效。
 */
class DownloadGateTest {

    @Test
    fun permitsUpToTheLimitThenQueues() = runTest {
        val gate = DownloadGate(limit = 1)
        val order = mutableListOf<String>()
        // 第一条占住名额后**停在那里**（靠一个 Deferred 悬住），否则它会在我们断言之前就跑完 ——
        // 用 yield() 排序是靠不住的：虚拟时间调度下它可能一次把整段跑完
        val hold = CompletableDeferred<Unit>()
        val first = launch {
            gate.acquire()
            order += "a"
            hold.await()
            order += "a-end"
            gate.release()
        }
        yield()
        val second = launch { gate.acquire(); order += "b" }
        yield()
        assertEquals("上限 1 时第二个必须还在排队", listOf("a"), order)
        hold.complete(Unit)
        first.join(); second.join()
        assertEquals("交还名额后排队的那条才进来", listOf("a", "a-end", "b"), order)
    }

    @Test
    fun raisingTheLimitWakesQueuedTasksImmediately() = runTest {
        val gate = DownloadGate(limit = 1)
        val started = mutableListOf<Int>()
        val jobs = (1..3).map { id ->
            launch { gate.acquire(); started += id }
        }
        yield()
        assertEquals("初始上限 1：只该有一个跑起来", 1, started.size)
        gate.setLimit(3)
        yield()
        assertEquals("调高上限后排队的三条要立刻都开工", listOf(1, 2, 3), started)
        jobs.forEach { it.join() }
    }

    @Test
    fun loweringTheLimitDoesNotInterruptRunningTasks() = runTest {
        val gate = DownloadGate(limit = 3)
        val started = mutableListOf<Int>()
        val jobs = (1..3).map { id -> launch { gate.acquire(); started += id } }
        yield()
        assertEquals(3, started.size)
        gate.setLimit(1)
        yield()
        assertEquals("调低不能去打断已经在跑的", 3, started.size)
        // 已经跑完的两条交还名额后，队列里那条才能进 —— 计数不能因为调低上限而错乱
        val queued = launch { gate.acquire(); started += 9 }
        yield()
        assertFalse("名额被占满时第五条要排队", started.contains(9))
        gate.release()
        yield()
        assertTrue("交还一个名额后它该进来了", started.contains(9))
        jobs.forEach { it.join() }
        queued.join()
    }

    @Test
    fun theLimitIsClampedLikeCSharp() {
        assertEquals(1, DownloadGate.clampConcurrent(0))
        assertEquals(1, DownloadGate.clampConcurrent(-7))
        assertEquals(5, DownloadGate.clampConcurrent(99))
        assertEquals(3, DownloadGate.clampConcurrent(3))
        assertEquals("默认值与 C# DefaultConcurrent 同值", 2, DownloadGate.DEFAULT_CONCURRENT)
    }
}
