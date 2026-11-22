package com.catclaw.music.kt.lyrico.lyrics

import com.catclaw.music.kt.core.lyrics.LyricLine
import com.catclaw.music.kt.core.lyrics.Lyrics
import java.util.Locale

/** 写回标签时可选的目标歌词格式（对应 Lyrico 的四种）。 */
enum class LyricoLyricsFormat(val label: String) {
    /** 每行一个方括号时间戳，翻译/罗马音各占一行（最通用） */
    PLAIN("普通 LRC"),

    /** 行时间戳 + 词用尖括号 `<>`：`[00:12.00]<00:12.00>词<00:12.50>词` */
    ENHANCED("增强 LRC"),

    /** 每个词前都挂方括号：`[00:12.00]词[00:12.50]词`（兼容性最差，多数播放器只认第一段） */
    VERBATIM("逐词 LRC"),

    /** W3C TTML，适合支持苹果/AMLL 生态的播放器 */
    TTML("TTML"),
}

/**
 * 把 [Lyrics] 编码成可写入标签的文本。
 *
 * 四种格式的时间精度统一到毫秒，输出时按 LRC 习惯保留两位小数（10ms 粒度）——
 * 与 App 侧 `LrcParser` 的解析粒度一致，写完再读回来能对上。
 */
object LyricoLyricsEncoder {

    fun encode(lyrics: Lyrics, format: LyricoLyricsFormat): String = when (format) {
        LyricoLyricsFormat.PLAIN -> plain(lyrics)
        LyricoLyricsFormat.ENHANCED -> enhanced(lyrics)
        LyricoLyricsFormat.VERBATIM -> verbatim(lyrics)
        LyricoLyricsFormat.TTML -> ttml(lyrics)
    }

    // ───────────────────────────── 普通 LRC ─────────────────────────────

    private fun plain(lyrics: Lyrics): String {
        val sb = StringBuilder()
        appendLrcTags(sb, lyrics)
        lyrics.lines.forEach { line ->
            sb.append(lineTag(line.startMs)).append(line.text).append('\n')
            // 译文/罗马音按 Lyrico 习惯作为同时间戳的独立行
            line.translation?.takeIf { it.isNotBlank() }?.let {
                sb.append(lineTag(line.startMs)).append(it).append('\n')
            }
            line.roma?.takeIf { it.isNotBlank() }?.let {
                sb.append(lineTag(line.startMs)).append(it).append('\n')
            }
        }
        return sb.toString().trimEnd('\n')
    }

    // ─────────────────────────── 增强 LRC（逐字）───────────────────────────

    private fun enhanced(lyrics: Lyrics): String {
        val sb = StringBuilder()
        appendLrcTags(sb, lyrics)
        lyrics.lines.forEach { line ->
            val words = line.wordTimestamps
            if (words.isNullOrEmpty()) {
                sb.append(lineTag(line.startMs)).append(line.text).append('\n')
            } else {
                sb.append(lineTag(line.startMs))
                words.forEach { w -> sb.append(wordTag(w.startMs)).append(w.word) }
                sb.append('\n')
            }
        }
        return sb.toString().trimEnd('\n')
    }

    // ───────────────────────── Verbatim（每词方括号）─────────────────────────

    private fun verbatim(lyrics: Lyrics): String {
        val sb = StringBuilder()
        appendLrcTags(sb, lyrics)
        lyrics.lines.forEach { line ->
            val words = line.wordTimestamps
            if (words.isNullOrEmpty()) {
                sb.append(lineTag(line.startMs)).append(line.text).append('\n')
            } else {
                words.forEach { w ->
                    sb.append(lineTag(w.startMs)).append(w.word)
                }
                sb.append('\n')
            }
        }
        return sb.toString().trimEnd('\n')
    }

    // ─────────────────────────────── TTML ────────────────────────────────

    private fun ttml(lyrics: Lyrics): String {
        val sb = StringBuilder()
        sb.append("<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n")
        sb.append("<tt xmlns=\"http://www.w3.org/ns/ttml\" ")
        sb.append("xmlns:tts=\"http://www.w3.org/ns/ttml#styling\" ")
        sb.append("xmlns:ttm=\"http://www.w3.org/ns/ttml#metadata\" ")
        sb.append("xmlns:itunes=\"http://music.apple.com/lyric-ttml-internal\">\n")
        sb.append("  <head>\n")
        val meta = lyrics.metadata
        meta.title?.takeIf { it.isNotBlank() }?.let {
            sb.append("    <metadata><ttm:title>").append(escape(it)).append("</ttm:title></metadata>\n")
        }
        sb.append("  </head>\n")
        sb.append("  <body>\n    <div>\n")
        lyrics.lines.forEachIndexed { index, line ->
            val end = lyrics.lines.getOrNull(index + 1)?.startMs
                ?: (line.startMs + estimateDuration(line))
            sb.append("      <p begin=\"").append(ttmlTime(line.startMs))
                .append("\" end=\"").append(ttmlTime(end)).append("\">")
            val words = line.wordTimestamps
            if (words.isNullOrEmpty()) {
                sb.append(escape(line.text))
            } else {
                words.forEach { w ->
                    val we = (w.startMs + w.durationMs).coerceAtLeast(w.startMs + 1)
                    sb.append("<span begin=\"").append(ttmlTime(w.startMs))
                        .append("\" end=\"").append(ttmlTime(we)).append("\">")
                        .append(escape(w.word)).append("</span>")
                }
            }
            sb.append("</p>\n")
        }
        sb.append("    </div>\n  </body>\n</tt>\n")
        return sb.toString()
    }

    private fun estimateDuration(line: LyricLine): Long {
        val words = line.wordTimestamps
        if (!words.isNullOrEmpty()) {
            val last = words.last()
            return (last.startMs + last.durationMs - line.startMs).coerceAtLeast(1000)
        }
        // 无逐字时按每字 120ms 估，夹在 1s..8s 之间
        return (line.text.length * 120L).coerceIn(1000L, 8000L)
    }

    // ─────────────────────────────── 工具 ─────────────────────────────────

    private fun appendLrcTags(sb: StringBuilder, lyrics: Lyrics) {
        val meta = lyrics.metadata
        meta.title?.takeIf { it.isNotBlank() }?.let { sb.append("[ti:").append(it).append("]\n") }
        meta.artist?.takeIf { it.isNotBlank() }?.let { sb.append("[ar:").append(it).append("]\n") }
        meta.album?.takeIf { it.isNotBlank() }?.let { sb.append("[al:").append(it).append("]\n") }
        meta.author?.takeIf { it.isNotBlank() }?.let { sb.append("[by:").append(it).append("]\n") }
    }

    /** `[mm:ss.xx]` */
    private fun lineTag(ms: Long): String {
        val safe = ms.coerceAtLeast(0)
        val minutes = safe / 60_000
        val seconds = (safe % 60_000) / 1000
        val centis = (safe % 1000) / 10
        return String.format(Locale.US, "[%02d:%02d.%02d]", minutes, seconds, centis)
    }

    /** `<mm:ss.xx>` */
    private fun wordTag(ms: Long): String {
        val safe = ms.coerceAtLeast(0)
        val minutes = safe / 60_000
        val seconds = (safe % 60_000) / 1000
        val centis = (safe % 1000) / 10
        return String.format(Locale.US, "<%02d:%02d.%02d>", minutes, seconds, centis)
    }

    /** TTML 时间：`mm:ss.mmm` */
    private fun ttmlTime(ms: Long): String {
        val safe = ms.coerceAtLeast(0)
        val minutes = safe / 60_000
        val seconds = (safe % 60_000) / 1000
        val millis = safe % 1000
        return String.format(Locale.US, "%02d:%02d.%03d", minutes, seconds, millis)
    }

    private fun escape(text: String): String = buildString(text.length) {
        text.forEach { c ->
            when (c) {
                '&' -> append("&amp;")
                '<' -> append("&lt;")
                '>' -> append("&gt;")
                '"' -> append("&quot;")
                '\'' -> append("&apos;")
                else -> append(c)
            }
        }
    }
}
