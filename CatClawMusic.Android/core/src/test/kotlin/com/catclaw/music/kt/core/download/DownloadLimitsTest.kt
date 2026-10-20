package com.catclaw.music.kt.core.download

import org.junit.Assert.assertEquals
import org.junit.Assert.assertTrue
import org.junit.Test

class DownloadLimitsTest {

    @Test
    fun zeroMeansUnlimitedNotZeroSpeed() {
        assertEquals(0L, DownloadLimits.speedBytesPerSecond(0))
        assertEquals(0L, DownloadLimits.speedBytesPerSecond(-100))
        assertEquals("限速 0 会被理解成「一字节都不许下」，下载会卡死", 0L, DownloadLimits.throttleDelayMs(1024, 0, 0))
    }

    @Test
    fun kbpsBecomesBytesPerSecond() {
        assertEquals(256L * 1024, DownloadLimits.speedBytesPerSecond(256))
    }

    @Test
    fun nothingToWaitForWhenWeAreBehindTheLimit() {
        // 下了 100ms 才 64KB，而限速 1024KB/s：这段时间本可以下 102KB，不用等
        assertEquals(0L, DownloadLimits.throttleDelayMs(sessionBytes = 64 * 1024, elapsedMs = 100, bytesPerSecond = 1024L * 1024))
    }

    @Test
    fun waitIsTheGapBetweenExpectedAndElapsed() {
        // 限速 1024KB/s，瞬间写完 1024KB：应该等够 1 秒
        assertEquals(1000L, DownloadLimits.throttleDelayMs(sessionBytes = 1024L * 1024, elapsedMs = 0, bytesPerSecond = 1024L * 1024))
        assertEquals(600L, DownloadLimits.throttleDelayMs(sessionBytes = 1024L * 1024, elapsedMs = 400, bytesPerSecond = 1024L * 1024))
    }

    @Test
    fun theSteppersCoverUnlimitedAndBack() {
        val steps = DownloadLimits.SPEED_STEPS_KBPS
        assertEquals("第一档必须是不限（默认值）", 0, steps.first())
        assertTrue("档位递增", steps.zipWithNext().all { (a, b) -> a < b })
    }
}
