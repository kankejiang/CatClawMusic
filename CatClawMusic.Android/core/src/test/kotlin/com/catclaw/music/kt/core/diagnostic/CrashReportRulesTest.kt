package com.catclaw.music.kt.core.diagnostic

import org.junit.After
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Test

class CrashReportRulesTest {

    @After
    fun tearDown() {
        AppLog.attach(null)
    }

    @Test
    fun reportStartsWithTimestampAndStack() {
        val text = CrashReportRules.format("2026-10-18 00:05:12", "thread=main\nat Foo.bar(Foo.kt:1)", emptyList())
        assertEquals("[2026-10-18 00:05:12] UnhandledException\nthread=main\nat Foo.bar(Foo.kt:1)\n", text)
    }

    @Test
    fun anEmptyTrailOmitsTheWholeSectionInsteadOfPrintingABareHeader() {
        val text = CrashReportRules.format("t", "boom", emptyList())
        assertFalse("空轨迹还留段头，等于告诉用户「日志丢了」", text.contains(CrashReportRules.LOG_SECTION_HEAD))
    }

    @Test
    fun onlyTheLastLinesOfALongTrailAreKept() {
        val trail = (1..100).map { "line-$it" }
        val text = CrashReportRules.format("t", "boom", trail, maxLines = 3)
        assertTrue(text.contains("line-100"))
        assertFalse("只保留最近 3 条：报告是要被用户看完的", text.contains("line-97"))
    }

    @Test
    fun theRingBufferKeepsLoggingEvenWhenDiskLoggingIsOff() {
        // 落盘默认关着 —— 而崩溃恰恰最需要崩溃前那几行，所以缓冲必须独立于开关
        AppLog.attach(null)
        AppLog.clock = { "00:00:01.000" }
        AppLog.i("Player", "开始播放")
        AppLog.e("Player", "解码失败", IllegalStateException("boom"))
        // 缓冲是**进程级单例**，同一个 JVM 里的其它测试也会往里写（`AppLog` 没有清空接口，
        // 也不该为测试加一个）。所以这里只断言相对位置，不断言总数
        val ring = AppLog.recentLines()
        assertTrue(ring.size >= 2)
        assertTrue("最近一条在最末", ring.last().contains("解码失败"))
        assertTrue("次新一条是它前面那条", ring[ring.size - 2].contains("开始播放"))
        assertTrue("环形缓冲里也是脱敏后的文本", ring.last().contains("IllegalStateException"))
    }

    @Test
    fun theRingBufferIsBounded() {
        AppLog.attach(null)
        AppLog.clock = { "00:00:01.000" }
        repeat(AppLog.RING_CAPACITY + 25) { AppLog.d("T", "第 $it 条") }
        val ring = AppLog.recentLines()
        assertEquals("取满时就是上限：缓冲有界，否则跑一天就是一次 OOM 的种子", AppLog.RING_CAPACITY, ring.size)
        assertFalse("最早那些已经被挤出去了", ring.any { it.endsWith("第 0 条") })
        assertTrue("留的是最近的", ring.last().endsWith("第 ${AppLog.RING_CAPACITY + 24} 条"))
    }

    @Test
    fun recentLinesCanAskForFewer() {
        AppLog.attach(null)
        AppLog.clock = { "00:00:01.000" }
        repeat(10) { AppLog.d("T", "第 $it 条") }
        assertEquals(listOf("第 6 条", "第 7 条", "第 8 条", "第 9 条").size, AppLog.recentLines(4).size)
        assertTrue(AppLog.recentLines(4).first().endsWith("第 6 条"))
    }
}
