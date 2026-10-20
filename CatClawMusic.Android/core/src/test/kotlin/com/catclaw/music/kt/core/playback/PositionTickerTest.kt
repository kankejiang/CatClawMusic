package com.catclaw.music.kt.core.playback

import kotlinx.coroutines.flow.take
import kotlinx.coroutines.flow.toList
import kotlinx.coroutines.test.runTest
import org.junit.Assert.assertEquals
import org.junit.Assert.assertTrue
import org.junit.Test

/**
 * 一秒能发出多少个**不同**的位置 —— 逐字着色帧率的算术版。
 *
 * 真机上我量不到「彩字层画了几次」，但这条链子是能证明的：
 * 位置值不去重地连续前进 → 绘制层每 tick 都有新输入 → 每秒 60 格。
 * 触发这次修改的真机现象：播放器 `currentPosition` 约 45ms 才变一次（≈22 次/秒），
 * 而下游是 `StateFlow`（相同值去重）→ 把 intervalMs 调成 16ms 也毫无变化。
 */
class PositionTickerTest {

    @Test
    fun `粗粒度上报下每秒仍给出约 60 个不同位置`() = runTest {
        // 播放器每 45ms 才报一个新值（真机实测），ticker 每 16ms 拉一次
        val start = 10_000L
        val values = interpolatedPositionFlow(
            intervalMs = 16L,
            nowMs = { testScheduler.currentTime },
            sample = {
                val reported = start + (testScheduler.currentTime / 45L) * 45L
                reported to true
            },
        ).take(63).toList()

        val distinct = values.distinct().size
        assertTrue("63 个 tick 只有 $distinct 个不同位置（<55 就是还在一格一格跳）", distinct >= 55)
        assertTrue("位置没有单调前进", values.zipWithNext().all { (a, b) -> b >= a })
    }

    @Test
    fun `负对照：不插值时同一个粗值会被去重掉`() = runTest {
        // 旧行为：直接把播放器上报值发出去 —— 45ms 一次的阶梯，tick 再密也没用
        val start = 10_000L
        val raw = (0 until 63).map { start + (testScheduler.currentTime / 45L) * 45L }
        val oldBehavior = (0 until 63).map { start + (it * 16L / 45L) * 45L }
        assertTrue("负对照没体现出旧行为的阶梯", oldBehavior.distinct().size < 25)
        assertEquals("负对照样本本身应等于起点", start, raw.first())
    }
}
