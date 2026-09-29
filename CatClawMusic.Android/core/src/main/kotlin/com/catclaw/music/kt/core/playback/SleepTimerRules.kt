package com.catclaw.music.kt.core.playback

/**
 * 睡眠定时的纯规则 —— 对应 C# `SleepTimerService.OnTimerTick`。
 *
 * 每秒推进一次，规则里有三处容易写错、都在测试里钉住：
 * 1. **淡出必须单调下降**。C# 早期版本按"歌曲剩余时长"淡，结果最后 20 秒跳回满音量；
 *    现在倒计时淡出用 `remaining / 20`，等待歌曲结束阶段用"进入等待后经过的秒数"，两条都不看歌曲时长。
 * 2. `stopAfterSong` 到点时**不立刻停**，而是进 `WaitingSongEnd` 等当前曲目播完
 *    （由 [PlayerController] 在播放结束回调里调 `finishWaiting`），否则会切到下一首。
 * 3. 到点暂停之后系数必须回 1.0 —— 音量是用户下次播放的起点，不能停在淡出的尾巴上。
 */
object SleepTimerRules {

    /** 淡出窗口（秒），与 C# `FadeOutSeconds` 同值 */
    const val FADE_OUT_SECONDS = 20

    data class State(
        val running: Boolean = false,
        val remainingSec: Int = 0,
        val totalSec: Int = 0,
        val stopAfterSong: Boolean = false,
        val fadeOut: Boolean = false,
        /** 时间到但还在等当前曲目播完 */
        val waitingForSongEnd: Boolean = false,
        val waitingFadeRemainingSec: Int = 0,
    )

    /** [factor] 是乘在用户音量上的淡出系数；[pause] 表示这一刻该暂停播放 */
    data class Tick(val state: State, val factor: Double, val pause: Boolean)

    fun start(minutes: Int, stopAfterSong: Boolean, fadeOut: Boolean): State {
        val total = minutes.coerceIn(1, 24 * 60) * 60
        return State(
            running = true,
            remainingSec = total,
            totalSec = total,
            stopAfterSong = stopAfterSong,
            fadeOut = fadeOut,
        )
    }

    fun cancel(state: State): Tick = Tick(state.copy(running = false, waitingForSongEnd = false), 1.0, false)

    fun tick(state: State, playing: Boolean): Tick {
        if (!state.running) return Tick(state, 1.0, false)
        if (state.waitingForSongEnd) {
            if (!state.fadeOut) return Tick(state, 1.0, false)
            val left = (state.waitingFadeRemainingSec - 1).coerceAtLeast(0)
            return Tick(state.copy(waitingFadeRemainingSec = left), left.toDouble() / FADE_OUT_SECONDS, false)
        }
        val remaining = state.remainingSec - 1
        val faded = state.fadeOut && remaining in 1..FADE_OUT_SECONDS
        val factor = if (faded) remaining.toDouble() / FADE_OUT_SECONDS else 1.0
        return if (remaining > 0) {
            Tick(state.copy(remainingSec = remaining), factor, false)
        } else if (state.stopAfterSong && playing) {
            // 时间到了但歌还没唱完：进等待阶段，并从"此刻的音量"重新起一段 20 秒淡出，
            // 衔接处不跳变（C# 同注释）
            Tick(
                state.copy(remainingSec = 0, waitingForSongEnd = true, waitingFadeRemainingSec = FADE_OUT_SECONDS),
                factor = 1.0,
                pause = false,
            )
        } else {
            Tick(state.copy(running = false, remainingSec = 0, waitingForSongEnd = false), 1.0, pause = true)
        }
    }

    /** 歌曲播完时调用：只有真在等待阶段才停，否则交给自动下一曲 */
    fun onSongCompleted(state: State): Tick =
        if (state.running && state.waitingForSongEnd) {
            Tick(state.copy(running = false, waitingForSongEnd = false), 1.0, pause = true)
        } else {
            Tick(state, 1.0, pause = false)
        }
}
