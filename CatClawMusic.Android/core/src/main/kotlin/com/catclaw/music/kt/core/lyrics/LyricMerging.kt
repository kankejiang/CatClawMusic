package com.catclaw.music.kt.core.lyrics

/** 对唱/合唱行的合并与角色推断，TTML / AMLL / LRC 共用（对齐 C# 的共享实现） */
object LyricMerging {

    private const val DUET_TOLERANCE_MS = 150L
    private const val TRANSLATION_TOLERANCE_MS = 300L

    data class RoleAlignment(val alignment: Int, val isBackingVocal: Boolean)

    /**
     * 角色 → 对齐/和声判定：
     * 含 backing/harmony/chorus/bgv → 居中+和声；否则取第一段数字（>=1000 和声，1 左，2 右，其余居中）；
     * 再否则按关键字 left|start|male|男=左、right|end|female|女=右。
     */
    fun inferRoleAlignment(role: String?): RoleAlignment {
        if (role.isNullOrBlank()) return RoleAlignment(1, false)
        val lower = role.lowercase()
        if (lower.contains("backing") || lower.contains("harmony") || lower.contains("chorus") || lower.contains("bgv")) {
            return RoleAlignment(1, true)
        }
        Regex("\\d+").find(lower)?.groupValues?.get(0)?.toIntOrNull()?.let { n ->
            return when {
                n >= 1000 -> RoleAlignment(1, true)
                n == 1 -> RoleAlignment(0, false)
                n == 2 -> RoleAlignment(2, false)
                else -> RoleAlignment(1, false)
            }
        }
        return when {
            lower.contains("left") || lower.contains("start") || lower.contains("male") || role.contains("男") ->
                RoleAlignment(0, false)
            lower.contains("right") || lower.contains("end") || lower.contains("female") || role.contains("女") ->
                RoleAlignment(2, false)
            else -> RoleAlignment(1, false)
        }
    }

    /**
     * 同拍对唱合并：只配 0(左)+2(右)，和声行与已带 secondaryText 的行不参与；
     * 主行本身是右对齐时把两条文本交换，保证 Text 落在左、SecondaryText 落在右。
     */
    fun mergeDuetLines(lyrics: Lyrics) {
        if (lyrics.lines.size < 2) return

        val used = BooleanArray(lyrics.lines.size)
        val merged = mutableListOf<LyricLine>()

        for (i in lyrics.lines.indices) {
            if (used[i]) continue
            val current = lyrics.lines[i]
            if (current.isBackingVocal || current.secondaryText != null) {
                merged += current
                continue
            }

            var partnerIndex = -1
            var partnerAlignment = 1
            for (j in i + 1 until lyrics.lines.size) {
                val other = lyrics.lines[j]
                val gap = other.startMs - current.startMs
                if (gap > DUET_TOLERANCE_MS) break
                if (used[j] || other.isBackingVocal || other.secondaryText != null) continue
                val pair = current.alignment to other.alignment
                if (pair == 0 to 2 || pair == 2 to 0) {
                    partnerIndex = j
                    partnerAlignment = other.alignment
                    break
                }
            }

            if (partnerIndex >= 0) {
                val other = lyrics.lines[partnerIndex]
                used[partnerIndex] = true
                if (current.alignment == 2) {
                    current.secondaryText = current.text
                    current.secondaryAlignment = 2
                    current.text = other.text
                    current.alignment = other.alignment
                } else {
                    current.secondaryText = other.text
                    current.secondaryAlignment = partnerAlignment
                }
            }
            merged += current
        }

        lyrics.lines = merged
    }

    /** 同时间戳（容差 300ms）两行按书写系统配对成原文/译文 */
    fun mergeTranslationLines(lyrics: Lyrics) {
        if (lyrics.lines.size < 2) return

        val merged = mutableListOf<LyricLine>()
        var i = 0
        while (i < lyrics.lines.size) {
            val current = lyrics.lines[i]
            val group = mutableListOf(current)
            var j = i + 1
            while (j < lyrics.lines.size &&
                kotlin.math.abs(lyrics.lines[j].startMs - current.startMs) < TRANSLATION_TOLERANCE_MS
            ) {
                group += lyrics.lines[j]
                j++
            }

            var main = group.first()
            group.firstOrNull { !it.wordTimestamps.isNullOrEmpty() }?.let { main = it }

            if (isLyricsMetadataLine(main.text)) {
                // 署名行不吸收任何行（C# 在这里会把同刻正文行一起吞掉，按其注释意图实现）
                merged += group
                i = j
                continue
            }

            if (main.translation.isNullOrEmpty()) {
                for (g in group) {
                    if (g === main) continue
                    if (!g.wordTimestamps.isNullOrEmpty()) continue
                    if (isLyricsMetadataLine(g.text)) continue
                    if (!isTranslationPair(main.text, g.text)) continue
                    if (main.text.trim() != g.text.trim()) {
                        main.translation = g.text
                        break
                    }
                }
            }

            merged += main
            i = j
        }
        lyrics.lines = merged
    }

    /** 解析完的统一收尾顺序：排序 → 对唱合并 → 逐行对齐标记 → 译文合并 */
    fun finalize(lyrics: Lyrics) {
        lyrics.lines = lyrics.lines.sortedBy { it.startMs }.toMutableList()
        mergeDuetLines(lyrics)
        lyrics.hasPerLineAlignment =
            lyrics.lines.any { it.alignment != 1 || it.secondaryText != null }
        mergeTranslationLines(lyrics)
    }
}
