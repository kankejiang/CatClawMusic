package com.catclaw.music.kt.core.lyrics

/**
 * 标准 LRC 解析，行为逐条对齐 C# `LyricsService.Lrc.cs`：
 * - `[ti:][ar:]` 等元数据标签 → metadata
 * - `[mm:ss(.xxx)]` 一个或多个 → 歌词行
 * - 一行内多个时间戳且时间戳之间夹着文本 → **逐字方括号格式**，整行合成一行并写 wordTimestamps
 *   （早期实现拆成 N 个空文本行，导致《起风了》这类逐字词文件整页空白）
 * - 行内 `<mm:ss.xxx>` → 逐字时间戳
 * - 无时间戳行忽略；纯音乐/占位文本清空
 * - 同时间戳（容差 300 ms）两行按书写系统配对成原文/译文；署名行不参与
 */
object LrcParser {

    const val NO_LYRICS_PLACEHOLDER = "还没有歌词"
    private const val MERGE_TOLERANCE_MS = 300L

    private val TIME = Regex("""\[(\d+):(\d+)(?:\.(\d+))?\]""")
    private val WORD_TIME = Regex("""<(\d+):(\d+)(?:\.(\d+))?>""")
    private val TAG = Regex("""\[(ti|ar|al|by|re|ve):(.+)\]""", RegexOption.IGNORE_CASE)

    fun parse(content: String?): Lyrics? {
        if (content.isNullOrEmpty()) return null

        val lyrics = Lyrics()
        val normalized = content.replace("\r\n", "\n").replace("\r", "\n")

        for (rawLine in normalized.split("\n")) {
            val line = rawLine.trim()
            if (line.isEmpty() || line.startsWith("//")) continue

            val tagMatch = TAG.find(line)?.takeIf { it.range.first == 0 }
            if (tagMatch != null) {
                val tag = tagMatch.groupValues[1].lowercase()
                val value = tagMatch.groupValues[2].trim()
                when (tag) {
                    "ti" -> lyrics.metadata.title = value
                    "ar" -> lyrics.metadata.artist = value
                    "al" -> lyrics.metadata.album = value
                    "by" -> lyrics.metadata.author = value
                    "re" -> lyrics.metadata.maker = value
                    "ve" -> lyrics.metadata.version = value
                }
                continue
            }

            val timeMatches = TIME.findAll(line).toList()
            if (timeMatches.isEmpty()) continue

            if (timeMatches.size >= 2 && hasInterstitialText(line, timeMatches)) {
                val words = buildInterstitialWords(line, timeMatches)
                if (words.isNotEmpty()) {
                    lyrics.lines += LyricLine(
                        startMs = words.first().startMs,
                        text = words.joinToString("") { it.word },
                        wordTimestamps = words,
                    )
                    continue
                }
            }

            var text = line.substringAfterLast(']', "")
            if (line.lastIndexOf(']') < 0) text = ""
            text = text.trim()
            if (text.contains("纯音乐") || text.contains("暂无歌词") || text.contains(NO_LYRICS_PLACEHOLDER)) {
                text = ""
            }

            val inlineWords = parseWordTimestamps(text)
            val lineText = inlineWords?.joinToString("") { it.word } ?: text

            for (match in timeMatches) {
                lyrics.lines += LyricLine(startMs = millisOf(match), text = lineText, wordTimestamps = inlineWords)
            }
        }

        if (lyrics.lines.isEmpty()) return null

        lyrics.lines = lyrics.lines.sortedBy { it.startMs }.toMutableList()
        mergeTranslationLines(lyrics)
        return lyrics
    }

    /** 把译文流/罗马音流按时间戳并入主行（容差 300 ms） */
    fun mergeExtendedStreams(lyrics: Lyrics) {
        lyrics.translationLines?.forEach { t ->
            findLineAt(lyrics, t.startMs)?.takeIf { it.translation.isNullOrEmpty() }?.translation = t.text
        }
        lyrics.romaLines?.forEach { r ->
            findLineAt(lyrics, r.startMs)?.takeIf { it.roma.isNullOrEmpty() }?.roma = r.text
        }
    }

    private fun findLineAt(lyrics: Lyrics, ts: Long): LyricLine? =
        lyrics.lines.firstOrNull { kotlin.math.abs(it.startMs - ts) < MERGE_TOLERANCE_MS }

    /** 同时间戳组内配对：见 [LyricMerging.mergeTranslationLines] */
    internal fun mergeTranslationLines(lyrics: Lyrics) = LyricMerging.mergeTranslationLines(lyrics)

    /**
     * 行内 `<mm:ss.xxx]词` 逐字标记。
     * 与 C# 的一处**有意分歧**：C# 对每个字词做 Trim，词间空格会被吞掉（"hello world" 变 "helloworld"）；
     * 这里保留原始片段，只跳过空片段，逐字着色时长句才和原文对齐。
     */
    internal fun parseWordTimestamps(text: String?): List<WordTimestamp>? {
        if (text.isNullOrEmpty()) return null
        val matches = WORD_TIME.findAll(text).toList()
        if (matches.isEmpty()) return null

        val result = mutableListOf<WordTimestamp>()
        for ((index, m) in matches.withIndex()) {
            val wordStart = m.range.last + 1
            val nextTagIndex = if (index + 1 < matches.size) matches[index + 1].range.first else text.length
            val word = text.substring(wordStart, nextTagIndex)
            if (word.isEmpty()) continue

            val start = millisOf(m)
            val duration = if (index + 1 < matches.size) {
                millisOf(matches[index + 1]) - start
            } else {
                500L
            }
            result += WordTimestamp(word, start, duration)
        }
        return result.ifEmpty { null }
    }

    private fun hasInterstitialText(line: String, matches: List<MatchResult>): Boolean {
        for (m in 0 until matches.size - 1) {
            val segStart = matches[m].range.last + 1
            val segEnd = matches[m + 1].range.first
            if (segEnd > segStart && line.substring(segStart, segEnd).isNotBlank()) return true
        }
        return false
    }

    private fun buildInterstitialWords(line: String, matches: List<MatchResult>): List<WordTimestamp> {
        val words = mutableListOf<WordTimestamp>()
        for ((m, mt) in matches.withIndex()) {
            val start = millisOf(mt)
            val wordStart = mt.range.last + 1
            val wordEnd = if (m + 1 < matches.size) matches[m + 1].range.first else line.length
            val word = line.substring(wordStart, wordEnd)
            // 行尾锚点时间戳（如 [00:03.600]）无文本，跳过
            if (word.isEmpty()) continue
            val duration = if (m + 1 < matches.size) millisOf(matches[m + 1]) - start else 500L
            words += WordTimestamp(word, start, duration)
        }
        return words
    }

    /** 毫秒字段按 C# 的 PadRight(3,'0').Substring(0,3) 语义补齐：45 → 450，5 → 500 */
    private fun millisOf(match: MatchResult): Long {
        val minutes = match.groupValues[1].toLongOrNull() ?: 0L
        val seconds = match.groupValues[2].toLongOrNull() ?: 0L
        val fraction = match.groupValues[3]
        val millis = if (fraction.isEmpty()) 0L else fraction.padEnd(3, '0').take(3).toLongOrNull() ?: 0L
        return minutes * 60_000 + seconds * 1_000 + millis
    }
}
