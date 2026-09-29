package com.catclaw.music.kt.core.playback

import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Test

/**
 * 睡眠定时的秒级推进。
 *
 * 这条规则的失败方式都很安静：淡出方向写反、等待歌曲结束那一步没接住、
 * 或者停完之后音量停在半路——用户都是第二天才发现。所以逐支钉住。
 */
class SleepTimerRulesTest {

    @Test
    fun startClampsMinutesAndSetsTotal() {
        val s = SleepTimerRules.start(minutes = 15, stopAfterSong = false, fadeOut = true)
        assertTrue(s.running)
        assertEquals(900, s.remainingSec)
        assertEquals(900, s.totalSec)
        assertEquals(60, SleepTimerRules.start(1, false, false).totalSec)
        assertEquals(24 * 3600, SleepTimerRules.start(9999, false, false).totalSec)
    }

    @Test
    fun countdownWithoutFadeKeepsVolumeAndPausesAtZero() {
        var state = SleepTimerRules.start(minutes = 1, stopAfterSong = false, fadeOut = false)
        val first = SleepTimerRules.tick(state, playing = true)
        assertEquals(59, first.state.remainingSec)
        assertEquals(1.0, first.factor, 1e-9)
        assertFalse(first.pause)

        state = SleepTimerRules.State(running = true, remainingSec = 1, totalSec = 60)
        val last = SleepTimerRules.tick(state, playing = true)
        assertTrue("到点必须请求暂停", last.pause)
        assertFalse(last.state.running)
        assertEquals("停之后音量交还用户", 1.0, last.factor, 1e-9)
    }

    @Test
    fun fadeOnlyInLastWindowAndMonotonic() {
        var prev = 1.01
        var state = SleepTimerRules.State(running = true, remainingSec = 25, totalSec = 60, fadeOut = true)
        while (state.remainingSec > 0) {
            val t = SleepTimerRules.tick(state, playing = true)
            state = t.state
            if (!t.pause) {
                assertTrue("淡出只能单调下降，出现了回升：$prev → ${t.factor}", t.factor <= prev + 1e-9)
                prev = t.factor
            }
        }
        assertTrue("窗口外该保持满音量", prev > 0.04)
        assertEquals(0.05, prev, 1e-9)
    }

    @Test
    fun stopAfterSongWaitsForTheEndInsteadOfPausing() {
        val state = SleepTimerRules.State(
            running = true, remainingSec = 1, totalSec = 60,
            stopAfterSong = true, fadeOut = false,
        )
        val arrived = SleepTimerRules.tick(state, playing = true)
        assertFalse("歌还没唱完，这一刻不该停", arrived.pause)
        assertTrue(arrived.state.waitingForSongEnd)
        assertEquals(20, arrived.state.waitingFadeRemainingSec)

        val done = SleepTimerRules.onSongCompleted(arrived.state)
        assertTrue("曲目播完才真正停", done.pause)
        assertFalse(done.state.running)
    }

    @Test
    fun waitingPhaseFadesFromCurrentVolume() {
        var state = SleepTimerRules.State(
            running = true, remainingSec = 0, totalSec = 60,
            stopAfterSong = true, fadeOut = true, waitingForSongEnd = true, waitingFadeRemainingSec = 20,
        )
        val first = SleepTimerRules.tick(state, playing = true)
        assertEquals(0.95, first.factor, 1e-9)
        state = first.state
        val second = SleepTimerRules.tick(state, playing = true)
        assertTrue(second.factor < first.factor)
        assertEquals(0.9, second.factor, 1e-9)
    }

    @Test
    fun songCompletedOutsideWaitingPhaseDoesNotStop() {
        // 自动下一曲时每首都可能触发这里；不在等待阶段就不能抢着暂停
        val running = SleepTimerRules.State(running = true, remainingSec = 100, totalSec = 600)
        val t = SleepTimerRules.onSongCompleted(running)
        assertFalse(t.pause)
        assertTrue(t.state.running)
    }

    @Test
    fun cancelRestoresVolumeAndIdleTicksDoNothing() {
        val state = SleepTimerRules.State(running = true, remainingSec = 5, totalSec = 60, fadeOut = true)
        val cancelled = SleepTimerRules.cancel(state)
        assertFalse(cancelled.state.running)
        assertEquals(1.0, cancelled.factor, 1e-9)
        assertFalse(cancelled.pause)

        val off = SleepTimerRules.tick(cancelled.state, playing = true)
        assertFalse("已经停了就不要再推进", off.state.running)
        assertEquals(5, off.state.remainingSec)
        assertFalse(off.pause)
    }
}
