package com.catclaw.music.kt.lyrico.settings

import com.catclaw.music.kt.core.lyrics.Lyrics
import com.catclaw.music.kt.core.lyrics.isLyricsMetadataLine

/**
 * 把用户设置套用到歌词数据上。
 *
 * 集中在这里而不是散到各处：清理规则如果分散实现，「预览」和「实际写入」就会不一致。
 * 用户在预览里看着干净，写进文件却带着制作人员行。所以预览与写回必须走同一个函数。
 */
object LyricsRuleEngine {

    /** 套用全部规则：字符映射 -> 艺人拆分 -> 清理。 */
    fun applyTo(lyrics: Lyrics, settings: LyricoSettings): Lyrics {
        val mapped = if (settings.characterMapping.enabled) {
            mapCharacters(lyrics, settings.characterMapping)
        } else {
            lyrics
        }
        val split = if (settings.artistSplit.enabled) {
            splitArtists(mapped, settings.artistSplit)
        } else {
            mapped
        }
        return clean(split, settings.cleanup)
    }

    /** 逐行清理。返回新对象，不修改入参（调用方可能还要拿原始数据做对比）。 */
    fun clean(lyrics: Lyrics, rules: LyricsCleanupRules): Lyrics {
        // 所有规则都关掉时直接返回原对象，省一次整篇拷贝
        val nothingToDo = !rules.removeCreditLines && !rules.removeEmptyLines &&
            !rules.trimLines && !rules.removeMetadataTags && !rules.dedupeTimestamps
        if (nothingToDo) return lyrics

        val result = Lyrics()
        result.metadata = lyrics.metadata
        result.hasPerLineAlignment = lyrics.hasPerLineAlignment
        result.lines = lyrics.lines.mapNotNull { line ->
            var text = line.text
            var translation = line.translation
            var roma = line.roma

            if (rules.trimLines) {
                text = text.trim()
                translation = translation?.trim()
                roma = roma?.trim()
            }
            // 制作人员行：整行都是「作词：xxx」这类内容
            if (rules.removeCreditLines && isLyricsMetadataLine(text)) return@mapNotNull null
            // 全局标签行（[ti:]、[ar:] 等），解析时通常已吞掉，这里兜底
            if (rules.removeMetadataTags && METADATA_TAG.matches(text)) return@mapNotNull null
            // 空行只对原文判断：译文为空但原文有内容时要保留这一行
            if (rules.removeEmptyLines && text.isBlank()) return@mapNotNull null

            line.copy(
                text = text,
                translation = translation?.takeIf { it.isNotBlank() },
                roma = roma?.takeIf { it.isNotBlank() },
            )
        }.toMutableList()
        result.translationLines = lyrics.translationLines
        result.romaLines = lyrics.romaLines
        return result
    }

    private val METADATA_TAG = Regex("""^\[(ti|ar|al|by|re|ve|offset):.*]$""", RegexOption.IGNORE_CASE)

    private fun mapCharacters(lyrics: Lyrics, mapping: CharacterMapping): Lyrics {
        val result = Lyrics()
        result.metadata = lyrics.metadata.let { m ->
            m.copy(
                title = m.title?.let(mapping::convert),
                artist = m.artist?.let(mapping::convert),
                album = m.album?.let(mapping::convert),
            )
        }
        result.hasPerLineAlignment = lyrics.hasPerLineAlignment
        result.lines = lyrics.lines.map { line ->
            line.copy(
                text = mapping.convert(line.text),
                translation = line.translation?.let(mapping::convert),
                roma = line.roma?.let(mapping::convert),
            )
        }.toMutableList()
        return result
    }

    private fun splitArtists(lyrics: Lyrics, config: ArtistSplitConfig): Lyrics {
        val artist = lyrics.metadata.artist ?: return lyrics
        val parts = splitArtistNames(artist, config)
        if (parts.size <= 1) return lyrics
        val result = Lyrics()
        result.metadata = lyrics.metadata.copy(artist = parts.joinToString(config.joinSeparator))
        result.hasPerLineAlignment = lyrics.hasPerLineAlignment
        result.lines = lyrics.lines
        result.translationLines = lyrics.translationLines
        result.romaLines = lyrics.romaLines
        return result
    }

    /**
     * 拆分艺人名。
     *
     * `feat.` / `ft.` 这类分隔符后面不该保留分隔符本身：
     * 「A feat. B」拆完是 `[A, B]`，不是 `[A feat., B]`。
     */
    fun splitArtistNames(artist: String, config: ArtistSplitConfig): List<String> {
        if (artist.isBlank()) return emptyList()
        var work = artist
        config.separators.forEach { sep ->
            if (sep.isBlank()) return@forEach
            work = work.replace(Regex(Regex.escape(sep), RegexOption.IGNORE_CASE), MARKER)
        }
        return work.split(MARKER)
            .map { it.trim() }
            .filter { it.isNotEmpty() }
            .distinct()
    }

    /** 供清理规则预览用：只返回文本行，不必构造完整 Lyrics。 */
    fun cleanLineTexts(texts: List<String>, rules: LyricsCleanupRules): List<String> =
        texts.mapNotNull { raw ->
            var text = raw
            if (rules.trimLines) text = text.trim()
            if (rules.removeCreditLines && isLyricsMetadataLine(text)) return@mapNotNull null
            if (rules.removeMetadataTags && METADATA_TAG.matches(text)) return@mapNotNull null
            if (rules.removeEmptyLines && text.isBlank()) return@mapNotNull null
            text
        }

    /** 拆分用的内部标记（正常歌词里不会出现） */
    private const val MARKER = "\u0000"
}
