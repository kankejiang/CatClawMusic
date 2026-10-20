package com.catclaw.music.kt.core.playback

/**
 * 把播放器**粗粒度**上报的位置插值成连续位置。
 *
 * 为什么需要（2026-10-18 真机定位「逐字歌词卡、但上下滚动流畅」）：
 * `player.currentPosition` 只在音频缓冲边界更新 —— 实测约 **45ms 一跳**（≈22 次/秒）。
 * 逐字着色直接拿它当输入时，每秒只有 20 来格：屏幕明明能跑 120Hz，歌词却一格一格跳；
 * 而滚动是**帧驱动**的（每个 vsync 都动），所以两者体感差这么远。
 *
 * 更隐蔽的一层：位置流是 `StateFlow`，**相同值会被去重** —— 于是「把轮询间隔设成 16ms」
 * 根本不起作用：值没变，下游一次都收不到。插值让每个 tick 的值都在前进，去重不再吃掉帧。
 *
 * 纯 JVM（时钟从外面注入），所以能用假时钟单测：真机帧率量不到，但算术可以钉死。
 */
class PositionInterpolator(private val nowMs: () -> Long = { System.nanoTime() / 1_000_000 }) {

    private var anchorPos = 0L
    private var anchorAt = 0L
    private var lastRaw = Long.MIN_VALUE
    private var playing = false
    private var started = false

    /**
     * 喂一次播放器上报值，拿回这一 tick 该用的位置。
     *
     * 只有上报值**变化**时才重设锚点：否则每 tick 都拿旧的粗值当锚，会把已经走过的进度又拉回去。
     */
    fun sample(rawMs: Long, isPlaying: Boolean): Long {
        val now = nowMs()
        if (!started || rawMs != lastRaw) {
            anchorPos = rawMs
            anchorAt = now
            lastRaw = rawMs
            started = true
        }
        playing = isPlaying
        return valueAt(now)
    }

    /** 当前推算位置：暂停时停在锚点，播放中按单调钟前进 */
    fun valueAt(now: Long = nowMs()): Long {
        if (!playing) return anchorPos
        val elapsed = (now - anchorAt).coerceAtLeast(0L)
        return anchorPos + elapsed
    }
}
