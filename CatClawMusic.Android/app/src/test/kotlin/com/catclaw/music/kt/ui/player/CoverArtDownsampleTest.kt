package com.catclaw.music.kt.ui.player

import org.junit.Assert.assertEquals
import org.junit.Assert.assertTrue
import org.junit.Test

/**
 * 封面降采样。`BitmapFactory` 的 `inSampleSize` 只认 2 的幂，算错一档就是：
 * 小一档 → 一张 3000px 的封面按原尺寸进内存（列表里几十张就是几百 MB，直接 OOM）；
 * 大一档 → 播放页那张大封面糊得能看见色块。
 */
class CoverArtDownsampleTest {

    @Test
    fun imagesAlreadyUnderTheCapAreNotSampled() {
        assertEquals(1, inSampleSizeFor(400, 300, 512))
        assertEquals(1, inSampleSizeFor(512, 512, 512))
    }

    @Test
    fun samplingStaysWithinOnePowerOfTwoOfTheTarget() {
        listOf(1000 to 1, 2000 to 2, 3000 to 4, 5000 to 8).forEach { (edge, expected) ->
            val sample = inSampleSizeFor(edge, edge, 512)
            assertEquals("边长 $edge 该取的那一档", expected, sample)
            val decoded = edge / sample
            assertTrue(
                "边长 $edge 降到 $decoded：不该小于目标 512（白降），也不该超过两倍（白吃内存）",
                decoded in 512..1023,
            )
        }
    }

    @Test
    fun degenerateInputsFallBackToNoSampling() {
        // 解码失败时 outWidth/outHeight 都是 -1：这里必须回 1，不能让 while 除到 0 上
        assertEquals(1, inSampleSizeFor(-1, -1, 512))
        assertEquals(1, inSampleSizeFor(0, 0, 512))
        assertEquals(1, inSampleSizeFor(2000, 2000, 0))
    }
}
