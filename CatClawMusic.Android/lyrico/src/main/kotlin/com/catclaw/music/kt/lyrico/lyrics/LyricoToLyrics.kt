package com.catclaw.music.kt.lyrico.lyrics

import com.catclaw.music.kt.core.lyrics.LyricLine
import com.catclaw.music.kt.core.lyrics.Lyrics
import com.catclaw.music.kt.core.lyrics.LyricsMetadata
import com.catclaw.music.kt.core.lyrics.WordTimestamp
import com.catclaw.music.kt.core.lyrics.LrcParser
import com.catclaw.music.kt.core.lyrics.TtmlParser

/**
 * 把 Lyrico 插件返回的候选转成 App 的歌词模型（`:core` 的 [Lyrics]）。
 *
 * 转换而不是另起一套模型，是为了直接复用既有的播放页/全屏歌词渲染、
 * 翻译与罗马音开关、逐字高亮等能力 —— 不需要为移植再写一套歌词 UI 数据流。
 *
 * raw 形态（插件只给了一段 LRC/TTML 文本）走既有解析器回落，保证两条路都能显示。
 */
object LyricoToLyrics {

    fun convert(candidate: LyricoLyrics, fallbackTitle: String? = null): Lyrics? {
        if (!candidate.hasContent) return null
        val lyrics = when (candidate.payloadType) {
            LyricoPayloadType.STRUCTURED -> fromStructured(candidate)
            LyricoPayloadType.RAW_PLAIN_LRC,
            LyricoPayloadType.RAW_VERBATIM_LRC,
            LyricoPayloadType.RAW_ENHANCED_LRC,
            LyricoPayloadType.RAW_MULTI_PERSON_ENHANCED_LRC,
            -> fromRawLrc(candidate)
            LyricoPayloadType.RAW_TTML -> fromRawTtml(candidate)
        } ?: return null

        // 插件给的标签优先，缺失时用调用方传入的歌名兜底
        val tags = candidate.tags
        lyrics.metadata = LyricsMetadata(
            title = tags["ti"] ?: tags["title"] ?: fallbackTitle,
            artist = tags["ar"] ?: tags["artist"],
            album = tags["al"] ?: tags["album"],
            author = tags["by"],
        )
        return lyrics.takeIf { it.lines.isNotEmpty() }
    }

    private fun fromStructured(candidate: LyricoLyrics): Lyrics? {
        if (candidate.original.isEmpty()) return null
        val lyrics = Lyrics()
        lyrics.lines = candidate.original.map { line ->
            LyricLine(
                startMs = line.start,
                text = line.text,
                wordTimestamps = line.words
                    .takeIf { it.size > 1 }
                    ?.map { w -> WordTimestamp(w.text, w.start, (w.end - w.start).coerceAtLeast(0)) },
            )
        }.toMutableList()
        // 翻译/罗马音按时间轴并入各行（与 Lyrico 一致：同刻配对）
        candidate.translated?.let { mergeByTime(lyrics.lines, it, isTranslation = true) }
        candidate.romanization?.let { mergeByTime(lyrics.lines, it, isTranslation = false) }
        return lyrics
    }

    private fun fromRawLrc(candidate: LyricoLyrics): Lyrics? {
        val text = when (candidate.payloadType) {
            LyricoPayloadType.RAW_ENHANCED_LRC,
            LyricoPayloadType.RAW_MULTI_PERSON_ENHANCED_LRC,
            -> candidate.rawEnhancedLrc.ifBlank { candidate.rawMultiPersonEnhancedLrc }
                .ifBlank { candidate.rawVerbatimLrc }
                .ifBlank { candidate.rawPlainLrc }
            LyricoPayloadType.RAW_VERBATIM_LRC -> candidate.rawVerbatimLrc.ifBlank { candidate.rawPlainLrc }
            else -> candidate.rawPlainLrc
        }
        if (text.isBlank()) return null
        return LrcParser.parse(text)
    }

    private fun fromRawTtml(candidate: LyricoLyrics): Lyrics? {
        if (candidate.rawTtml.isBlank()) return null
        return TtmlParser.parse(candidate.rawTtml)
    }

    /**
     * 按起始时间把副流行并入主行。
     *
     * 直接精确匹配会丢数据：插件两路时间戳常有几毫秒偏差。这里用 200ms 容差，
     * 与 App 侧 LRC 合并策略一致。
     */
    private fun mergeByTime(
        main: List<LyricLine>,
        secondary: List<LyricoLine>,
        isTranslation: Boolean,
    ) {
        if (secondary.isEmpty()) return
        val toleranceMs = 200L
        for (line in secondary) {
            val text = line.text
            if (text.isBlank()) continue
            val target = main.firstOrNull { kotlin.math.abs(it.startMs - line.start) <= toleranceMs }
                ?: continue
            if (isTranslation) {
                if (target.translation.isNullOrBlank()) target.translation = text
            } else {
                if (target.roma.isNullOrBlank()) target.roma = text
            }
        }
    }
}
