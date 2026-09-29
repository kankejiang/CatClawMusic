package com.catclaw.music.kt.core.equalizer

import org.junit.Assert.assertEquals
import org.junit.Assert.assertTrue
import org.junit.Test

/**
 * 均衡器规则。重点是**预设怎么从 10 段落到设备那 5 段**：
 * 插错方向（线性而不是对数）会让低频三段几乎挤在同一点上，用户拉起来感觉"三根滑杆是一个东西"。
 */
class EqualizerRulesTest {

    @Test
    fun labelsReadLikeAudioGearsReadThem() {
        assertEquals("60Hz", EqualizerRules.label(60))
        assertEquals("250Hz", EqualizerRules.label(250))
        assertEquals("1kHz", EqualizerRules.label(1000))
        assertEquals("12kHz", EqualizerRules.label(12000))
        assertEquals(listOf("60Hz", "250Hz", "1kHz", "4kHz", "12kHz"), EqualizerRules.labels(EqualizerRules.NATIVE_FREQUENCIES))
    }

    @Test
    fun presetResamplesOnLogFrequencyNotLinear() {
        val gains = EqualizerRules.gainsForPreset("rock", EqualizerRules.NATIVE_FREQUENCIES)
        assertEquals(5, gains.size)
        // 60Hz 落在基准的 31(+8) 与 62(+5) 之间：对数位置 ≈ 0.952 → 8 + 0.952*(5-8) ≈ 5.14
        assertEquals(5.14, gains[0], 0.02)
        // 12kHz 落在 8k(+11) 与 16k(+11) 之间，两端同值 → 还是 11
        assertEquals(11.0, gains[4], 0.001)
        // 线性插值会给出的数明显不同（约 6.4），这条就是防它
        assertTrue("对数插值不该退化成线性", kotlin.math.abs(gains[0] - 6.4) > 0.5)
    }

    @Test
    fun outOfRangeBandsTakeTheEndInsteadOfExtrapolating() {
        val gains = EqualizerRules.gainsForPreset(
            "rock",
            intArrayOf(10, 31, 1000, 16000, 40000),
        )
        assertEquals("低于最低基准段就取第一段的值", 8.0, gains[0], 0.001)
        assertEquals("高于最高基准段就取最后一段的值", 11.0, gains[4], 0.001)
    }

    @Test
    fun millibelsAreClampedToWhatTheDeviceActuallyOffers() {
        fun mb(db: Double, lo: Int, hi: Int) = EqualizerRules.toMillibels(db, lo.toShort(), hi.toShort()).toInt()
        assertEquals("超出设备上界就贴住上界", 1500, mb(20.0, -1500, 1500))
        assertEquals(-1500, mb(-99.0, -1500, 1500))
        assertEquals(500, mb(5.0, -1500, 1500))
        // 有的设备给的区间是反的，不能因为 min>max 就崩
        assertEquals(300, mb(9.0, 300, -300))
    }

    @Test
    fun gainsRoundTripThroughThePrefsString() {
        val gains = doubleArrayOf(0.0, 5.5, -2.0, 11.0, 3.25)
        val raw = EqualizerRules.encodeGains(gains)
        assertEquals("0,5.5,-2,11,3.3", raw)
        val back = EqualizerRules.decodeGains(raw, 5)
        assertEquals(0.0, back[0], 0.001)
        assertEquals(5.5, back[1], 0.001)
        assertEquals(-2.0, back[2], 0.001)
    }

    @Test
    fun decodingMissingOrShorterDataFillsZerosNotCrashes() {
        assertEquals(5, EqualizerRules.decodeGains("", 5).size)
        assertEquals(0.0, EqualizerRules.decodeGains("", 5)[3], 0.001)
        val short = EqualizerRules.decodeGains("3,4", 5)
        assertEquals("存下来的段数少了，缺的按 0 补而不是猜", listOf(3.0, 4.0, 0.0, 0.0, 0.0), short.toList())
    }

    @Test
    fun everyPresetHasTenBandsAndStaysInTheUiRange() {
        EqualizerRules.PRESETS.forEach { (key, gains) ->
            assertEquals("$key 该是 10 段基准", 10, gains.size)
            gains.forEach { assertTrue("$key 超出 ±12dB", it in -12.0..12.0) }
        }
        assertEquals(9, EqualizerRules.PRESET_KEYS.size)
        assertEquals("custom", EqualizerRules.PRESET_KEYS.first())
        // 页面上的每个档位都得在预设表里，否则点上去拿到的是空曲线
        EqualizerRules.PRESET_KEYS.forEach { key ->
            if (key != "custom") assertTrue("$key 没有对应预设", EqualizerRules.PRESETS.containsKey(key))
        }
    }
}
