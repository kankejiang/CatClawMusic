package com.catclaw.music.kt.lyrico.search

import com.catclaw.music.kt.lyrico.lyrics.LyricoLyrics
import com.catclaw.music.kt.lyrico.lyrics.LyricoPayloadType

/** 搜索目标：来自当前歌曲，插件与 LRCLIB 都按这套字段匹配。 */
data class LyricsTarget(
    val title: String,
    val artist: String,
    val album: String = "",
    val durationMs: Long = 0L,
)

/** 一个搜索到的歌词候选。 */
data class LyricsCandidate(
    /** 来源标识：`lrclib` 或插件 id */
    val sourceId: String,
    /** 来源显示名，界面徽标用 */
    val sourceName: String,
    val trackTitle: String = "",
    val artist: String = "",
    val album: String = "",
    val durationMs: Long = 0L,
    val lyrics: LyricoLyrics,
    /** 排序分（越高越靠前），由 [LyricsRanking] 计算 */
    val score: Double = 0.0,
) {
    val isSynced: Boolean
        get() = lyrics.payloadType == LyricoPayloadType.STRUCTURED ||
            lyrics.rawPlainLrc.contains(Regex("""\[\d+:\d+""")) ||
            lyrics.rawEnhancedLrc.isNotBlank()
}

/**
 * 候选排序规则（对齐 Lyrico 的匹配策略）。
 *
 * 三条硬经验：
 *  1. **时长差超过 15s 且歌名不是完全一致 → 直接丢弃**。同名不同版本（现场/remix）
 *     时长差异明显，放进列表就是误写。
 *  2. 歌名权重最高，艺人次之，专辑再次。
 *  3. 同步歌词优先于纯文本：能逐字/滚动显示，纯文本只能按 5s 一行推进。
 */
object LyricsRanking {

    private const val DURATION_TOLERANCE_MS = 15_000L

    /** 对候选打分并排序（返回新列表，不修改入参）。 */
    fun rank(target: LyricsTarget, candidates: List<LyricsCandidate>): List<LyricsCandidate> {
        return candidates
            .mapNotNull { c ->
                val score = score(target, c) ?: return@mapNotNull null
                c.copy(score = score)
            }
            .sortedWith(
                compareByDescending<LyricsCandidate> { it.score }
                    .thenByDescending { it.isSynced }
                    .thenBy { it.sourceName },
            )
    }

    /** @return 分数；null 表示被硬规则淘汰 */
    fun score(target: LyricsTarget, candidate: LyricsCandidate): Double? {
        val titleExact = normalize(target.title) == normalize(candidate.trackTitle)
        val titleScore = when {
            titleExact -> 1.0
            normalize(target.title).isNotEmpty() &&
                normalize(candidate.trackTitle).contains(normalize(target.title)) -> 0.7
            normalize(candidate.trackTitle).contains(normalize(target.title).take(4)) &&
                normalize(target.title).length >= 4 -> 0.4
            else -> 0.0
        }
        // 歌名完全对不上、且时长也对不上，基本不是同一首
        val durationDiff = if (target.durationMs > 0 && candidate.durationMs > 0) {
            kotlin.math.abs(target.durationMs - candidate.durationMs)
        } else {
            -1L
        }
        if (durationDiff >= 0 && durationDiff > DURATION_TOLERANCE_MS && !titleExact) return null
        if (titleScore == 0.0 && durationDiff != 0L && (durationDiff < 0 || durationDiff > DURATION_TOLERANCE_MS)) {
            return null
        }

        val artistScore = when {
            target.artist.isBlank() -> 0.5
            normalize(target.artist) == normalize(candidate.artist) -> 1.0
            normalize(candidate.artist).contains(normalize(target.artist)) ||
                normalize(target.artist).contains(normalize(candidate.artist)) -> 0.7
            else -> 0.0
        }
        val albumScore = when {
            target.album.isBlank() || candidate.album.isBlank() -> 0.5
            normalize(target.album) == normalize(candidate.album) -> 1.0
            else -> 0.0
        }
        val durationScore = when {
            durationDiff < 0 -> 0.5
            durationDiff <= 1000 -> 1.0
            durationDiff >= DURATION_TOLERANCE_MS -> 0.0
            else -> 1.0 - durationDiff.toDouble() / DURATION_TOLERANCE_MS
        }
        val syncedBonus = when {
            candidate.lyrics.isWordByWord -> 0.25
            candidate.isSynced -> 0.12
            else -> 0.0
        }
        return titleScore * 0.45 +
            artistScore * 0.25 +
            albumScore * 0.10 +
            durationScore * 0.20 +
            syncedBonus
    }

    private fun normalize(text: String): String = text.lowercase()
        .replace(Regex("""[\s\-_()（）\[\]【】「」,.，。!！?？'"“”‘’]"""), "")
        .trim()
}
