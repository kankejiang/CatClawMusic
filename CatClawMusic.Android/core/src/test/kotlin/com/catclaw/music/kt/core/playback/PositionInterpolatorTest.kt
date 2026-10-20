package com.catclaw.music.kt.core.playback

import org.junit.Assert.assertEquals
import org.junit.Assert.assertTrue
import org.junit.Test

/**
 * 位置插值的算术。
 *
 * 真机帧率量不到，但「粗粒度上报 → 每 tick 连续前进」这件事是算术，可以钉死。
 * 触发这次修改的真机现象：`player.currentPosition` 约 45ms 才变一次（≈22 次/秒），
 * 逐字着色跟着它就是一格一格跳；而滚动是帧驱动的，所以看着流畅。
 */
class PositionInterpolatorTest {

    private var clock = 1_000L
    private fun tick(ms: Long = 16) { clock += ms }
    private val interp = PositionInterpolator { clock }

    @Test
    fun `同一个粗值不再让位置停住`() {
        // 播放器 45ms 才报一次新值：中间这些 tick 过去都是同一个值，被 StateFlow 去重掉
        assertEquals(5_000L, interp.sample(5_000L, isPlaying = true))
        tick(16)
        assertEquals(5_016L, interp.sample(5_000L, isPlaying = true))
        tick(16)
        assertEquals(5_032L, interp.sample(5_000L, isPlaying = true))
    }

    @Test
    fun `播放器报来新值时立刻对齐`() {
        interp.sample(5_000L, isPlaying = true)
        tick(16)
        interp.sample(5_000L, isPlaying = true)
        tick(16)
        // 播放器上报跳到 5100：以它为准，不能把推算值叠上去
        assertEquals(5_100L, interp.sample(5_100L, isPlaying = true))
    }

    @Test
    fun `暂停时停在原地`() {
        interp.sample(7_000L, isPlaying = false)
        tick(500)
        assertEquals(7_000L, interp.sample(7_000L, isPlaying = false))
    }

    @Test
    fun `一秒里能给出约 60 个不同的位置`() {
        val seen = mutableListOf<Long>()
        repeat(63) {
            seen += interp.sample(10_000L, isPlaying = true)
            tick(16)
        }
        val distinct = seen.distinct().size
        assertTrue(distinct >= 55)
    }
}
