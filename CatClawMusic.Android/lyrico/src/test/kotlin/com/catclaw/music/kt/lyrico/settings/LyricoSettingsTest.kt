package com.catclaw.music.kt.lyrico.settings

import com.catclaw.music.kt.core.lyrics.LyricLine
import com.catclaw.music.kt.core.lyrics.Lyrics
import com.catclaw.music.kt.core.lyrics.LyricsMetadata
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Test

/** 设置项本身与应用逻辑的单测。 */
class LyricoSettingsTest {

    // ─────────────────────── 编解码 ───────────────────────

    @Test
    fun settingsRoundTripAllFields() {
        val settings = LyricoSettings(
            editFields = EditFieldVisibility(
                albumArtist = false, genre = true, date = false, trackNumber = true,
                discNumber = true, composer = true, lyricist = true, comment = true,
            ),
            cleanup = LyricsCleanupRules(
                removeCreditLines = true, removeEmptyLines = false, trimLines = true,
                removeMetadataTags = true, dedupeTimestamps = false,
            ),
            artistSplit = ArtistSplitConfig(
                enabled = true, separators = listOf("/", "&"), joinSeparator = "、",
            ),
            characterMapping = CharacterMapping(enabled = true, pairs = mapOf("臺" to "台")),
            defaultLyricsFormat = "ENHANCED",
            defaultOverwrite = true,
        )
        val decoded = LyricoSettingsCodec.decode(LyricoSettingsCodec.encode(settings))
        assertEquals(settings, decoded)
    }

    @Test
    fun settingsDefaultsOnGarbage() {
        val defaults = LyricoSettings()
        assertEquals(defaults, LyricoSettingsCodec.decode(""))
        assertEquals(defaults, LyricoSettingsCodec.decode("not json"))
        assertEquals(defaults, LyricoSettingsCodec.decode("{}"))
        // 部分字段缺失时：缺的用默认，有的用给的值
        val partial = LyricoSettingsCodec.decode("""{"defaultOverwrite":true}""")
        assertTrue(partial.defaultOverwrite)
        assertEquals(defaults.cleanup, partial.cleanup)
    }

    @Test
    fun escapesSpecialCharactersInPairs() {
        val settings = LyricoSettings(
            characterMapping = CharacterMapping(enabled = true, pairs = mapOf("\"" to "“")),
        )
        val decoded = LyricoSettingsCodec.decode(LyricoSettingsCodec.encode(settings))
        assertEquals("“", decoded.characterMapping.pairs["\""])
    }

    // ─────────────────────── 清理规则 ───────────────────────

    private fun lyrics(vararg lines: Pair<Long, String>): Lyrics = Lyrics().apply {
        metadata = LyricsMetadata(title = "T", artist = "A")
        this.lines = lines.map { (start, text) -> LyricLine(startMs = start, text = text) }.toMutableList()
    }

    @Test
    fun removesCreditLines() {
        val input = lyrics(
            0L to "作词：怀袖",
            1000L to "故事的小黄花",
            2000L to "作曲：银临",
        )
        val out = LyricsRuleEngine.clean(
            input,
            LyricsCleanupRules(removeCreditLines = true, removeEmptyLines = false, trimLines = false,
                removeMetadataTags = false, dedupeTimestamps = false),
        )
        assertEquals(listOf("故事的小黄花"), out.lines.map { it.text })
    }

    @Test
    fun keepsCreditLinesWhenRuleOff() {
        val input = lyrics(0L to "作词：怀袖", 1000L to "正文")
        val out = LyricsRuleEngine.clean(input, LyricsCleanupRules(removeCreditLines = false))
        assertEquals(2, out.lines.size)
    }

    @Test
    fun removesEmptyLinesAndTrims() {
        val input = lyrics(
            0L to "  第一行  ",
            1000L to "   ",
            2000L to "第二行",
        )
        val out = LyricsRuleEngine.clean(
            input,
            LyricsCleanupRules(removeEmptyLines = true, trimLines = true, removeCreditLines = false,
                removeMetadataTags = false, dedupeTimestamps = false),
        )
        assertEquals(listOf("第一行", "第二行"), out.lines.map { it.text })
    }

    @Test
    fun trimsTranslationToo() {
        val input = Lyrics().apply {
            lines = mutableListOf(
                LyricLine(startMs = 0, text = " a ", translation = "  b  ", roma = " c "),
            )
        }
        val out = LyricsRuleEngine.clean(
            input,
            LyricsCleanupRules(trimLines = true, removeEmptyLines = false, removeCreditLines = false,
                removeMetadataTags = false, dedupeTimestamps = false),
        )
        assertEquals("a", out.lines[0].text)
        assertEquals("b", out.lines[0].translation)
        assertEquals("c", out.lines[0].roma)
    }

    @Test
    fun metadataPreservedThroughCleaning() {
        val input = lyrics(0L to "x")
        val out = LyricsRuleEngine.clean(input, LyricsCleanupRules(removeEmptyLines = true))
        assertEquals("T", out.metadata.title)
        assertEquals("A", out.metadata.artist)
    }

    // ─────────────────────── 艺人拆分 ───────────────────────

    @Test
    fun splitsArtistsByConfiguredSeparators() {
        val config = ArtistSplitConfig(enabled = true, separators = listOf("/", "&"), joinSeparator = "/")
        assertEquals(listOf("A", "B"), LyricsRuleEngine.splitArtistNames("A/B", config))
        assertEquals(listOf("A", "B", "C"), LyricsRuleEngine.splitArtistNames("A & B & C", config))
        // 去重
        assertEquals(listOf("A", "B"), LyricsRuleEngine.splitArtistNames("A/B/A", config))
    }

    @Test
    fun featSeparatorDoesNotLeakIntoName() {
        val config = ArtistSplitConfig(enabled = true, separators = listOf("feat.", "/"), joinSeparator = "/")
        val parts = LyricsRuleEngine.splitArtistNames("周杰伦 feat. 某人", config)
        assertEquals(listOf("周杰伦", "某人"), parts)
    }

    @Test
    fun splitIsCaseInsensitive() {
        val config = ArtistSplitConfig(enabled = true, separators = listOf("feat."), joinSeparator = "/")
        assertEquals(listOf("A", "B"), LyricsRuleEngine.splitArtistNames("A FEAT. B", config))
    }

    @Test
    fun singleArtistUnchanged() {
        val config = ArtistSplitConfig(enabled = true, separators = listOf("/"))
        assertEquals(listOf("周杰伦"), LyricsRuleEngine.splitArtistNames("周杰伦", config))
        assertTrue(LyricsRuleEngine.splitArtistNames("", config).isEmpty())
    }

    // ─────────────────────── 字符映射 ───────────────────────

    @Test
    fun characterMappingReplacesByLongestKeyFirst() {
        // 「臺」与「臺灣」都在表里时，长的要先替换，否则短的会把它拆坏
        val mapping = CharacterMapping(
            enabled = true,
            pairs = linkedMapOf("臺灣" to "台湾", "臺" to "台"),
        )
        assertEquals("台湾的台", mapping.convert("臺灣的臺"))
    }

    @Test
    fun disabledMappingIsNoOp() {
        val mapping = CharacterMapping(enabled = false, pairs = mapOf("a" to "b"))
        assertEquals("aaa", mapping.convert("aaa"))
    }

    @Test
    fun mappingAppliesToFullLyrics() {
        val input = Lyrics().apply {
            metadata = LyricsMetadata(title = "臺北", artist = "臺")
            lines = mutableListOf(LyricLine(startMs = 0, text = "臺"))
        }
        val out = LyricsRuleEngine.applyTo(
            input,
            LyricoSettings(characterMapping = CharacterMapping(true, mapOf("臺" to "台"))),
        )
        assertEquals("台北", out.metadata.title)
        assertEquals("台", out.metadata.artist)
        assertEquals("台", out.lines[0].text)
    }

    // ─────────────────────── 字段可见性 ───────────────────────

    @Test
    fun requiredFieldsAlwaysVisible() {
        val hidden = EditFieldVisibility(
            albumArtist = false, genre = false, date = false, trackNumber = false,
            discNumber = false, composer = false, lyricist = false, comment = false,
        )
        // 标题/歌手/专辑不受配置控制
        assertTrue(hidden.isVisible("title"))
        assertTrue(hidden.isVisible("artist"))
        assertTrue(hidden.isVisible("album"))
        assertFalse(hidden.isVisible("genre"))
    }

    @Test
    fun toggleOnlyAffectsTargetField() {
        val base = EditFieldVisibility()
        val toggled = base.toggle("composer", true)
        assertTrue(toggled.composer)
        assertEquals(base.genre, toggled.genre)
        // 未知 key 不变
        assertEquals(base, base.toggle("nonexistent", true))
    }
}
