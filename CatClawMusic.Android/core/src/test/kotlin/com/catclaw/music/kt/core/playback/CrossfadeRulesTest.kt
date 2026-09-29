package com.catclaw.music.kt.core.playback

import org.junit.Assert.assertEquals
import org.junit.Test

/**
 * 淡入淡出的系数曲线。
 *
 * 这条规则失败的方式是"听感不对"而不是崩溃：算错方向会把结尾唱词淡掉，
 * 取消条件写漏会让音量停在半路回不来。所以每个分支都钉一个具体数字。
 */
class CrossfadeRulesTest {

    private fun assertTick(expectedFactor: Double, expectedPhase: CrossfadePhase, actual: CrossfadeRules.Tick) {
        assertEquals(expectedPhase, actual.phase)
        assertEquals(expectedFactor, actual.factor, 1e-9)
    }

    @Test
    fun offLeavesVolumeAlone() {
        assertTick(1.0, CrossfadePhase.Idle, CrossfadeRules.onSongChanged(enabled = false, windowSec = 4))
        // 窗口为 0 等于关掉：哪怕相位还挂着淡入，也必须立刻回满
        assertTick(1.0, CrossfadePhase.Idle, CrossfadeRules.tick(CrossfadePhase.FadingIn, 1.0, 5.0, 200.0, 0))
    }

    @Test
    fun songStartFadesInOnWallClock() {
        assertTick(0.0, CrossfadePhase.FadingIn, CrossfadeRules.onSongChanged(enabled = true, windowSec = 4))
        assertTick(0.5, CrossfadePhase.FadingIn, CrossfadeRules.tick(CrossfadePhase.FadingIn, 2.0, 2.0, 200.0, 4))
        assertTick(0.999, CrossfadePhase.FadingIn, CrossfadeRules.tick(CrossfadePhase.FadingIn, 3.996, 4.0, 200.0, 4))
        // 到点就交还 Idle，之后音量归用户所有，不再被每 tick 重算
        assertTick(1.0, CrossfadePhase.Idle, CrossfadeRules.tick(CrossfadePhase.FadingIn, 9.0, 9.0, 200.0, 4))
    }

    @Test
    fun tailFadesOutOnRemainingTime() {
        assertTick(0.5, CrossfadePhase.FadingOut, CrossfadeRules.tick(CrossfadePhase.Idle, 0.0, 198.0, 200.0, 4))
        assertTick(0.0, CrossfadePhase.FadingOut, CrossfadeRules.tick(CrossfadePhase.Idle, 0.0, 200.0, 200.0, 4))
        // 窗口之外一律满音量
        assertTick(1.0, CrossfadePhase.Idle, CrossfadeRules.tick(CrossfadePhase.Idle, 0.0, 150.0, 200.0, 4))
    }

    @Test
    fun seekingBackCancelsFadeOut() {
        // 已经在淡出（factor 0.25），用户把进度拖回中段：必须回 1.0 并交还 Idle，
        // 漏掉这一支的话音量会一直卡在低处，表现是"歌越听越小"
        assertTick(1.0, CrossfadePhase.Idle, CrossfadeRules.tick(CrossfadePhase.FadingOut, 0.0, 100.0, 200.0, 4))
    }

    @Test
    fun unknownDurationNeverFadesOut() {
        // 直播/远端未就绪时 duration=0，拿剩余时长算会得到负数并把音量压死
        assertTick(1.0, CrossfadePhase.Idle, CrossfadeRules.tick(CrossfadePhase.Idle, 0.0, 30.0, 0.0, 4))
        assertTick(1.0, CrossfadePhase.Idle, CrossfadeRules.tick(CrossfadePhase.FadingOut, 0.0, 30.0, -1.0, 4))
    }

    @Test
    fun volumeMultipliesUserLevelAndStaysInRange() {
        assertEquals(0.5f, CrossfadeRules.volumeOf(1.0f, 0.5), 1e-6f)
        assertEquals(0.15f, CrossfadeRules.volumeOf(0.3f, 0.5), 1e-6f)
        // 系数与音量都可能有浮点溢出，音量出口必须夹住
        assertEquals(1.0f, CrossfadeRules.volumeOf(2.0f, 1.0), 1e-6f)
        assertEquals(0.0f, CrossfadeRules.volumeOf(0.5f, -0.2), 1e-6f)
    }
}
