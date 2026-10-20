package com.catclaw.music.kt.core.playback

import kotlinx.coroutines.delay
import kotlinx.coroutines.flow.Flow
import kotlinx.coroutines.flow.flow

/**
 * 位置 ticker：每 [intervalMs] 发一个**连续前进**的位置。
 *
 * 抽成独立函数是为了能在核心模块里用虚拟时钟测 —— 真机帧率我量不到，
 * 但「一秒能发出多少个不同位置」是纯算术，必须钉住（这正是这次 bug 的要害）。
 *
 * [sample] 返回 (播放器上报位置, 是否在播)：播放器上报是粗粒度的（音频缓冲边界，约 45ms 一跳），
 * 插值把它补成每 tick 都在前进的值 —— 否则下游 `StateFlow` 会把重复值去重，
 * 每秒只剩 20 来次，逐字着色就一格一格跳。
 */
fun interpolatedPositionFlow(
    intervalMs: Long,
    nowMs: () -> Long,
    sample: () -> Pair<Long, Boolean>,
): Flow<Long> = flow {
    val interpolator = PositionInterpolator(nowMs)
    while (true) {
        val (raw, playing) = sample()
        emit(interpolator.sample(raw, playing))
        delay(intervalMs)
    }
}
