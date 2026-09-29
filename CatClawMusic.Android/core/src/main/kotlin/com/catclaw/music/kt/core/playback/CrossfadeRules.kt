package com.catclaw.music.kt.core.playback

/** 淡入淡出的相位，与 C# 的 `XfadeIdle / XfadeIn / XfadeOut` 一一对应 */
enum class CrossfadePhase { Idle, FadingIn, FadingOut }

/**
 * 淡入淡出的纯规则 —— 对应 C# `AudioPlayerService.UpdateCrossfade`。
 *
 * 说清楚它**不是**真正的交叉淡化：本端与 C# 一样只有一个播放器，做的是
 * "尾段淡出 + 新曲淡入"，两首歌不会同时出声。所以窗口长度是"各淡多少秒"，
 * 不是重叠秒数 —— 界面上别写"重叠 4 秒"那种话。
 *
 * 两条时间轴故意不同：
 * - 淡入按**墙钟**推进（新曲刚开播，位置与墙钟等价，但用墙钟不受 seek 干扰）；
 * - 淡出按**剩余时长**推进（用户把进度拖回中段时，淡出要能自己取消）。
 */
object CrossfadeRules {

    const val DEFAULT_WINDOW_SEC = 4
    const val MAX_WINDOW_SEC = 12

    data class Tick(val factor: Double, val phase: CrossfadePhase)

    /** 换曲那一刻的起始状态：没开就直接满音量，别留个淡入把开头吃掉的 */
    fun onSongChanged(enabled: Boolean, windowSec: Int): Tick =
        if (enabled && windowSec > 0) Tick(0.0, CrossfadePhase.FadingIn) else Tick(1.0, CrossfadePhase.Idle)

    /**
     * @param elapsedInPhaseSec 进入当前相位后经过的墙钟秒数（淡入用）
     * @param positionSec 当前进度；[durationSec] <= 0 表示时长未知（直播/未就绪），此时不做淡出
     */
    fun tick(
        phase: CrossfadePhase,
        elapsedInPhaseSec: Double,
        positionSec: Double,
        durationSec: Double,
        windowSec: Int,
    ): Tick {
        val n = windowSec.toDouble()
        if (n <= 0.0) return Tick(1.0, CrossfadePhase.Idle)
        if (phase == CrossfadePhase.FadingIn) {
            val f = min(1.0, elapsedInPhaseSec / n)
            return if (f >= 1.0) Tick(1.0, CrossfadePhase.Idle) else Tick(f, CrossfadePhase.FadingIn)
        }
        val remaining = durationSec - positionSec
        return when {
            durationSec <= 0.0 -> Tick(1.0, CrossfadePhase.Idle)
            // 含"已经到头"：淡到 0 就是这条曲线的极限，切歌回调随即把新曲置成淡入
            remaining <= n -> Tick(max(0.0, remaining / n), CrossfadePhase.FadingOut)
            // 用户把进度拖回中段（或从中间恢复播放）→ 取消淡出，回到满音量
            phase == CrossfadePhase.FadingOut -> Tick(1.0, CrossfadePhase.Idle)
            else -> Tick(1.0, CrossfadePhase.Idle)
        }
    }

    /** 实际写播放器的音量 = 用户音量 × 系数；两端都夹在 0..1，超出的部分没有意义 */
    fun volumeOf(userVolume: Float, factor: Double): Float =
        (userVolume * factor).toFloat().coerceIn(0f, 1f)

    private fun min(a: Double, b: Double) = if (a < b) a else b
    private fun max(a: Double, b: Double) = if (a > b) a else b
}
