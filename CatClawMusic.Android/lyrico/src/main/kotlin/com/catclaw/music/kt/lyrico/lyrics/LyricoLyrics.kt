package com.catclaw.music.kt.lyrico.lyrics

/**
 * Lyrico 插件歌词返回模型（与 Lyrico `data/model/lyrics/Lyrics.kt` 对齐）。
 *
 * 插件返回值是一个候选数组，每个候选是下列形态之一：
 *   - `{ type: "structured", tags, original, translated?, romanization? }`
 *     其中 original 是 `[[start, end, [[wordStart, wordEnd, text], ...]], ...]`
 *   - `{ type: "rawPlainLrc" | "rawVerbatimLrc" | "rawEnhancedLrc" | "rawTtml"
 *           | "rawMultiPersonEnhancedLrc", ...对应 raw 字段 }`
 *
 * 保留结构化形态是关键：只有这样才能无损保留词级时间戳、翻译、罗马音，
 * 后续也才能在「普通 LRC / 逐字 / 增强 / TTML」之间互转。
 */
enum class LyricoPayloadType(val wireName: String) {
    STRUCTURED("structured"),
    RAW_PLAIN_LRC("rawPlainLrc"),
    RAW_VERBATIM_LRC("rawVerbatimLrc"),
    RAW_ENHANCED_LRC("rawEnhancedLrc"),
    RAW_TTML("rawTtml"),
    RAW_MULTI_PERSON_ENHANCED_LRC("rawMultiPersonEnhancedLrc"),
    ;

    companion object {
        fun fromWire(value: String?): LyricoPayloadType? =
            entries.firstOrNull { it.wireName.equals(value, ignoreCase = true) }
    }
}

/** 一个词（逐字歌词的最小单位）。时间单位与插件一致：毫秒。 */
data class LyricoWord(
    val start: Long,
    val end: Long,
    val text: String,
)

/** 一行歌词：行时间 + 若干词。整行歌词时 [words] 只有一个元素。 */
data class LyricoLine(
    val start: Long,
    val end: Long,
    val words: List<LyricoWord>,
) {
    /** 行文本（把词拼起来，去掉首尾空白） */
    val text: String get() = words.joinToString("") { it.text }.trim()

    val isWordByWord: Boolean get() = words.size > 1
}

/** 插件返回的一个歌词候选。 */
data class LyricoLyrics(
    val tags: Map<String, String> = emptyMap(),
    val payloadType: LyricoPayloadType = LyricoPayloadType.STRUCTURED,
    val original: List<LyricoLine> = emptyList(),
    val translated: List<LyricoLine>? = null,
    val romanization: List<LyricoLine>? = null,
    val rawPlainLrc: String = "",
    val rawVerbatimLrc: String = "",
    val rawEnhancedLrc: String = "",
    val rawTtml: String = "",
    val rawMultiPersonEnhancedLrc: String = "",
) {
    /** 是否有可写入的歌词内容 */
    val hasContent: Boolean
        get() = original.isNotEmpty() ||
            rawPlainLrc.isNotBlank() || rawVerbatimLrc.isNotBlank() ||
            rawEnhancedLrc.isNotBlank() || rawTtml.isNotBlank() ||
            rawMultiPersonEnhancedLrc.isNotBlank()

    /** 是否逐字（词级时间戳） */
    val isWordByWord: Boolean get() = original.any { it.isWordByWord }
}
