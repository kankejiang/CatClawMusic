package com.catclaw.music.kt.core.equalizer

/**
 * 均衡器的纯规则 —— 对应 C# `EqualizerSettings` 里与平台无关的那一半。
 *
 * 只移植**原生 5 段**这条路。C# 还有一条「FFmpeg 烘焙 10 段」，那要先把整台
 * FFmpeg 转码引擎搬过来（本端没有），所以这里不预留 10 段开关：
 * 摆一个切了没反应的档位，比少一个档位更糟。
 *
 * 预设仍以 10 段为基准定义（与 C# 同源，VLC 预设取整 dB），应用到设备真实频段时
 * 按**对数频率**插值重采样 —— 人耳对频率是对数感知的，线性插值会把低频段挤成一团。
 */
object EqualizerRules {

    /** Android 原生 Equalizer 在 MIUI/澎湃 OS 上的真实档位 */
    val NATIVE_FREQUENCIES = intArrayOf(60, 250, 1000, 4000, 12000)

    /** 预设的基准频段（10 段） */
    val PRESET_FREQUENCIES = intArrayOf(31, 62, 125, 250, 500, 1000, 2000, 4000, 8000, 16000)

    const val MIN_GAIN_DB = -12.0
    const val MAX_GAIN_DB = 12.0

    private fun db(vararg values: Int): DoubleArray = values.map { it.toDouble() }.toDoubleArray()

    /** key → 10 段增益(dB)。数值逐条对齐 C# `Presets` */
    val PRESETS: Map<String, DoubleArray> = linkedMapOf(
        "flat" to db(0, 0, 0, 0, 0, 0, 0, 0, 0, 0),
        "pop" to db(-2, 5, 7, 7, 3, 0, -2, -2, -2, -2),
        "rock" to db(8, 5, -6, -3, 0, 4, 9, 11, 11, 11),
        "jazz" to db(0, 0, 2, 4, 3, 0, -2, -3, -2, 0),
        "classical" to db(0, 0, 0, 0, 0, 0, -7, -7, -7, -10),
        "vocal" to db(-2, -1, 2, 5, 5, 3, 1, 0, -1, -2),
        "hiphop" to db(6, 5, 2, 0, -2, -1, 2, 4, 5, 3),
        "blues" to db(2, 3, 4, 2, 1, 0, -1, -2, -1, 0),
        "dance" to db(10, 7, 2, 0, 0, -6, -7, -7, -2, -2),
        "bass" to db(8, 10, 7, 4, 1, -2, -5, -7, -8, -9),
        "electronic" to db(8, 6, 0, -6, -5, 0, 8, 10, 10, 9),
        "metal" to db(9, 7, 0, -5, -3, 0, 7, 10, 11, 10),
        "piano" to db(-1, 0, 2, 5, 4, 2, 1, -1, -1, -2),
        "cinema" to db(6, 5, 3, 2, 2, 3, 4, 4, 3, 2),
        "car" to db(7, 6, 4, 2, 1, 1, 2, 3, 3, 3),
    )

    /** 页面上的九宫格顺序，与 C# `PresetList` 一致（"custom" 是"当前曲线被手改过"的态，不是预设） */
    val PRESET_CELLS = listOf(
        "custom" to "自定义", "rock" to "摇滚", "jazz" to "爵士",
        "pop" to "流行", "classical" to "古典", "hiphop" to "嘻哈",
        "blues" to "布鲁斯", "electronic" to "电子音乐", "metal" to "金属",
    )

    /** 60 → "60Hz"，1000 → "1kHz"，12000 → "12kHz"（与 C# FormatHz 同形） */
    fun label(hz: Int): String = when {
        hz >= 1000 && hz % 1000 == 0 -> "${hz / 1000}kHz"
        hz >= 1000 -> String.format("%.1fkHz", hz / 1000.0)
        else -> "${hz}Hz"
    }

    fun labels(frequencies: IntArray): List<String> = frequencies.map { label(it) }

    /** 把 10 段基准的预设落到目标频段上 */
    fun gainsForPreset(key: String, targetFrequencies: IntArray): DoubleArray =
        resample(PRESETS[key] ?: PRESETS.getValue("flat"), PRESET_FREQUENCIES, targetFrequencies)

    /**
     * 对数频率线性插值。超出源范围时**取端点值**而不是外推 ——
     * 外推会把 16kHz 的 +11dB 一路推到 20kHz 以上，那是设备根本不给的增益。
     */
    fun resample(srcGains: DoubleArray, srcFreqs: IntArray, dstFreqs: IntArray): DoubleArray {
        require(srcGains.size == srcFreqs.size) { "增益与频段长度必须一致" }
        if (srcFreqs.isEmpty()) return DoubleArray(dstFreqs.size)
        return DoubleArray(dstFreqs.size) { i ->
            val f = dstFreqs[i]
            when {
                srcFreqs.size == 1 -> srcGains[0]
                f <= srcFreqs.first() -> srcGains.first()
                f >= srcFreqs.last() -> srcGains.last()
                else -> {
                    var j = 0
                    while (j + 1 < srcFreqs.size && srcFreqs[j + 1] < f) j += 1
                    val lo = srcFreqs[j]
                    val hi = srcFreqs.getOrElse(j + 1) { lo }
                    if (hi <= lo) srcGains[j]
                    else {
                        val t = ln(f.toDouble() / lo) / ln(hi.toDouble() / lo)
                        srcGains[j] + t * (srcGains.getOrElse(j + 1) { srcGains[j] } - srcGains[j])
                    }
                }            }
        }
    }

    fun clampGain(db: Double, minDb: Double = MIN_GAIN_DB, maxDb: Double = MAX_GAIN_DB): Double =
        db.coerceIn(min(minDb, maxDb), max(minDb, maxDb))

    /** dB → 原生 Equalizer 的毫贝（mB），并且先按设备给的增益范围夹住 */
    fun toMillibels(db: Double, deviceMinMb: Short, deviceMaxMb: Short): Short {
        val mb = (db * 100.0).toInt()
        return mb.coerceIn(min(deviceMinMb.toInt(), deviceMaxMb.toInt()), max(deviceMinMb.toInt(), deviceMaxMb.toInt())).toShort()
    }

    /** 持久化形状：逗号分隔、最多一位小数、不依赖区域设置（C# 用 "0.#" + InvariantCulture） */
    fun encodeGains(gains: DoubleArray): String =
        gains.joinToString(",") { g -> if (g == g.toLong().toDouble()) g.toLong().toString() else String.format("%.1f", g) }

    fun decodeGains(raw: String, size: Int): DoubleArray {
        if (raw.isBlank()) return DoubleArray(size)
        val parsed = raw.split(',').mapNotNull { it.trim().toDoubleOrNull() }
        // 段数变了（换了设备/换了频段配置）时按位置补齐或截断：这里不猜频率，
        // 因为存下来的就是"当前这套频段"的增益，频段本身由调用方决定
        return DoubleArray(size) { i -> parsed.getOrElse(i) { 0.0 } }
    }

    private fun ln(v: Double): Double = kotlin.math.ln(v)
    private fun min(a: Int, b: Int) = if (a < b) a else b
    private fun max(a: Int, b: Int) = if (a > b) a else b
    private fun min(a: Double, b: Double) = if (a < b) a else b
    private fun max(a: Double, b: Double) = if (a > b) a else b
}
