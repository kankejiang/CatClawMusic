package com.catclaw.music.kt.core.lyrics

/**
 * 歌词数据模型，字段对齐 C# `Core/Models/Lyrics.cs`。
 * 时间统一用**毫秒 Long**（C# 用 TimeSpan）。
 */
data class WordTimestamp(val word: String, val startMs: Long, val durationMs: Long)

enum class LineAlignment { Left, Center, Right }

data class LyricLine(
    val startMs: Long,
    var text: String = "",
    var translation: String? = null,
    var roma: String? = null,
    var wordTimestamps: List<WordTimestamp>? = null,
    /** 0 左 / 1 中 / 2 右，由 TTML/AMLL 的 role 决定；LRC 默认居中 */
    var alignment: Int = 1,
    var secondaryText: String? = null,
    var secondaryAlignment: Int = 1,
    var isBackingVocal: Boolean = false,
    var role: String? = null,
    var singerName: String? = null,
)

data class LyricsMetadata(
    var title: String? = null,
    var artist: String? = null,
    var album: String? = null,
    var author: String? = null,
    var maker: String? = null,
    var version: String? = null,
)

class Lyrics {
    var metadata: LyricsMetadata = LyricsMetadata()
    var lines: MutableList<LyricLine> = mutableListOf()

    /** 外部流（网易云三流歌词的 romalrc / tlyric），解析后按时间戳并入各行 */
    var romaLines: List<LyricLine>? = null
    var translationLines: List<LyricLine>? = null

    /** true 时 UI 用每行 alignment，false 时用全局对齐设置 */
    var hasPerLineAlignment: Boolean = false
}

/** 逐字填充进度计算，对齐 C# LyricFillCalculator */
object LyricFillCalculator {

    fun computeFillProgress(
        line: LyricLine,
        lineIndex: Int,
        allLines: List<LyricLine>,
        positionMs: Long,
        lineMode: Boolean,
    ): Double {
        if (lineMode) return 1.0

        val lineStart = line.startMs
        val lineEnd = if (lineIndex + 1 < allLines.size) allLines[lineIndex + 1].startMs else lineStart + 5_000

        if (positionMs <= lineStart) return 0.0
        if (positionMs >= lineEnd) return 1.0

        line.wordTimestamps?.takeIf { it.isNotEmpty() }?.let {
            return calculateSyllableProgress(it, positionMs)
        }

        val totalMs = (lineEnd - lineStart).toDouble()
        val elapsedMs = (positionMs - lineStart).toDouble()
        return if (totalMs > 0) (elapsedMs / totalMs).coerceIn(0.0, 1.0) else 1.0
    }

    /**
     * 已唱完的音节贡献其字符比例，当前音节按时间进度部分贡献；
     * 词内再把这个音节的时长**均分给每个字符**，所以空格/符号也逐字推进，
     * 不会整词同时着色。
     */
    fun calculateSyllableProgress(syllables: List<WordTimestamp>, positionMs: Long): Double {
        val totalChars = syllables.sumOf { it.word.length }
        if (totalChars == 0) return 0.0

        var filledChars = 0.0
        for (syl in syllables) {
            if (syl.word.isEmpty()) continue
            val len = syl.word.length
            val start = syl.startMs
            val duration = if (syl.durationMs <= 0) 300L else syl.durationMs
            val end = start + duration

            when {
                positionMs >= end -> filledChars += len
                positionMs > start -> {
                    val charDurMs = duration.toDouble() / len
                    if (charDurMs <= 0.0) {
                        filledChars += len
                    } else {
                        val charFrac = (positionMs - start) / charDurMs
                        filledChars += charFrac.coerceAtMost(len.toDouble())
                    }
                }
            }
        }
        return (filledChars / totalChars).coerceIn(0.0, 1.0)
    }
}

/** 书写系统判定，用于「同时间戳两行是否构成原文+译文对」 */
object TextScripts {
    val METADATA_PREFIXES = listOf(
        "作词", "作詞", "作曲", "编曲", "編曲", "填词", "填詞", "谱曲", "譜曲",
        "出品", "发行", "發行", "制作", "製作", "混音", "母带", "母帶",
        "监制", "監制", "统筹", "統籌", "和声", "和聲", "吉他", "贝斯", "貝斯",
        "OP", "SP", "OP:", "SP:", "Lyrics", "Composer", "Arranger", "Producer",
    )

    private val PUNCT_OR_SYMBOL: Set<Int> = setOf(
        Character.DASH_PUNCTUATION, Character.START_PUNCTUATION, Character.END_PUNCTUATION,
        Character.CONNECTOR_PUNCTUATION, Character.OTHER_PUNCTUATION,
        Character.INITIAL_QUOTE_PUNCTUATION, Character.FINAL_QUOTE_PUNCTUATION,
        Character.MATH_SYMBOL, Character.CURRENCY_SYMBOL,
        Character.MODIFIER_SYMBOL, Character.OTHER_SYMBOL,
    ).map { it.toInt() }.toSet()

    private fun isIgnorable(ch: Char): Boolean =
        Character.isWhitespace(ch) || Character.isDigit(ch) ||
            Character.getType(ch).toInt() in PUNCT_OR_SYMBOL

    fun of(text: String?): Set<String> {
        val set = LinkedHashSet<String>()
        if (text.isNullOrBlank()) return set
        for (ch in text) {
            if (isIgnorable(ch)) continue
            val code = ch.code
            when {
                code in 0xAC00..0xD7AF || code in 0x1100..0x11FF || code in 0x3130..0x318F -> set += "Hangul"
                code in 0x3040..0x30FF || code in 0x31F0..0x31FF -> set += "Kana"
                code in 0x4E00..0x9FFF || code in 0x3400..0x4DBF || code in 0xF900..0xFAFF -> set += "Han"
                code in 0x0400..0x04FF -> set += "Cyrillic"
                code in 0x0041..0x007A || code in 0x00C0..0x024F -> set += "Latin"
                else -> set += "Other"
            }
        }
        return set
    }
}

/** 署名/元数据行（作词/作曲/OP…）；缩写要求后跟冒号或空格，避免误伤以 Op 开头的外文正文 */
fun isLyricsMetadataLine(text: String?): Boolean {
    if (text.isNullOrBlank()) return false
    val t = text.trimStart()
    for (prefix in TextScripts.METADATA_PREFIXES) {
        if (!t.startsWith(prefix, ignoreCase = true)) continue
        if (prefix in listOf("OP", "SP", "Lyrics", "Composer", "Arranger", "Producer")) {
            val rest = t.substring(prefix.length)
            if (rest.isNotEmpty() && !rest.startsWith("：") && !rest.startsWith(":") &&
                !rest.startsWith(" ") && !rest.startsWith("　")
            ) continue
        }
        return true
    }
    return false
}

/**
 * 候选行必须引入主行没有的书写系统才算译文；
 * 纯中文↔纯中文的同刻两行是快歌堆叠的连续句，配对会把正文吞成"译文"。
 * 例外：主行含假名（日语混排）时，纯汉字候选行视为中文译文。
 */
fun isTranslationPair(main: String, candidate: String): Boolean {
    val ms = TextScripts.of(main)
    if (ms.isEmpty()) return false
    val cs = TextScripts.of(candidate)
    if (cs.isEmpty()) return false
    if (!ms.containsAll(cs)) return true
    if (ms.contains("Kana")) {
        val candNoHan = cs.toMutableSet().apply { remove("Han") }
        if (candNoHan.isEmpty()) return true
    }
    return false
}
