package com.catclaw.music.kt.data.download

import java.util.concurrent.ConcurrentHashMap
import kotlinx.coroutines.Job
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Test

/** 任务收尾时「摘掉自己那一份」这件事，见 [DownloadJobTable] 的注释 */
class DownloadJobTableTest {

    @Test
    fun `release 只摘自己那一份，换上去的新任务必须留着`() {
        val table = DownloadJobTable()
        val first = Job()
        val second = Job()
        table.put(1, first)
        table.put(1, second)
        assertFalse("J1 已经不是表里那一份，不该顺手摘走 J2", table.release(1, first))
        assertEquals(second, table.jobOf(1))
        assertTrue(table.release(1, second))
        assertNull(table.jobOf(1))
    }

    @Test
    fun `cancel 取走并取消，没有这一首时返回 null`() {
        val table = DownloadJobTable()
        val job = Job()
        table.put(7, job)
        assertEquals(job, table.cancel(7))
        assertTrue(job.isCancelled)
        assertNull(table.cancel(7))
    }

    @Test
    fun `已经结束的 Job 不入表，isActive 只认真着的那一份`() {
        val table = DownloadJobTable()
        val dead = Job().apply { cancel() }
        table.put(3, dead)
        assertNull("死任务不该占格子", table.jobOf(3))
        assertFalse(table.isActive(3))
        val live = Job()
        table.put(3, live)
        assertTrue(table.isActive(3))
    }

    /**
     * 正控制：旧写法是收尾时 `jobs.remove(songId)` —— 无条件按键删。
     * 把「pause 摘走 J1 → resume 放进 J2 → J1 的 finally 才跑到」这个顺序钉死，
     * 结果就是 J2 被摘走：之后 `pause()` / `cancel()` 找不到它，这一首再也停不下来，
     * 而 `enqueue()` 的去重也看不见它，会再起一个写同一个 `.part` 的任务。
     */
    @Test
    fun `正控制 无条件按键删会把新任务变成停不掉的幽灵任务`() {
        val jobs = ConcurrentHashMap<Int, Job>()
        val first = Job()
        val second = Job()
        jobs[1] = first
        jobs.remove(1)?.cancel()   // pause()
        jobs[1] = second           // resume() → 重新排队
        jobs.remove(1)             // J1 的 finally（旧写法）
        assertTrue("旧写法这次没弄丢 J2 = 顺序没造出来，正控制失效", jobs[1] == null)
        assertFalse("表里空了，J2 拿不到取消入口，只能一直跑", second.isCancelled)
    }
}
