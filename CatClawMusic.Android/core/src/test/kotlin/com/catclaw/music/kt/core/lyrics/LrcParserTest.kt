package com.catclaw.music.kt.core.lyrics

import org.junit.Assert.assertEquals
import org.junit.Assert.assertNotNull
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Test

class LrcParserTest {

    @Test
    fun metadataAndBasicLines() {
        val l = LrcParser.parse(
            """
            [ti:Call Me Maybe]
            [ar:Carly Rae Jepsen]
            [al:Curiosity]
            [by:someone]
            [00:12.00]Look at your eyes
            [00:16.50]and I know what you want
            """.trimIndent(),
        )
        assertNotNull(l)
        assertEquals("Call Me Maybe", l!!.metadata.title)
        assertEquals("Carly Rae Jepsen", l.metadata.artist)
        assertEquals("Curiosity", l.metadata.album)
        assertEquals(2, l.lines.size)
        assertEquals(12_000L, l.lines[0].startMs)
        assertEquals(16_500L, l.lines[1].startMs)
    }

    @Test
    fun millisFieldPaddedLikeCsharp() {
        val l = LrcParser.parse("[00:07.24]a\n[00:08.5]b\n[00:09.123]c\n[00:10]d")!!
        assertEquals(listOf(7_240L, 8_500L, 9_123L, 10_000L), l.lines.map { it.startMs })
    }

    @Test
    fun multipleTimestampsShareOneText() {
        val l = LrcParser.parse("[01:23.45][02:34.56]副歌重复")!!
        assertEquals(2, l.lines.size)
        assertEquals(listOf(83_450L, 154_560L), l.lines.map { it.startMs })
        assertTrue(l.lines.all { it.text == "副歌重复" })
    }

    /** 《起风了》那类逐字方括号：必须合成一行，不能拆成 N 个空行 */
    @Test
    fun interstitialBracketKaraokeBecomesOneLine() {
        val l = LrcParser.parse("[00:00.000]起[00:00.211]风[00:00.422]了[00:00.633]")!!
        assertEquals(1, l.lines.size)
        assertEquals("起风了", l.lines[0].text)
        assertEquals(3, l.lines[0].wordTimestamps!!.size)
        assertEquals(211L, l.lines[0].wordTimestamps!![0].durationMs)
        // 最后一个有文本的字词时长固定 500ms（尾部的 [00:00.633] 是锚点，无文本被跳过）
        assertEquals(211L, l.lines[0].wordTimestamps!![1].durationMs)
    }

    @Test
    fun inlineAngleWordTimestamps() {
        val l = LrcParser.parse("[00:01.00]<00:01.00>hello <00:01.50>world")!!
        assertEquals("hello world", l.lines[0].text)
        assertEquals(2, l.lines[0].wordTimestamps!!.size)
        assertEquals(500L, l.lines[0].wordTimestamps!![0].durationMs)
    }

    /** 走在冷风中 case：署名行与正文首句同时间戳，不能把正文吞成译文 */
    @Test
    fun metadataLineDoesNotAbsorbFirstLyric() {
        val l = LrcParser.parse(
            """
            [00:20.00]作词 择荇
            [00:20.00]走得那么急
            [00:24.00]How can you be so cold
            """.trimIndent(),
        )!!
        assertEquals(3, l.lines.size)
        val first = l.lines.first { it.text == "走得那么急" }
        assertNull(first.translation)
        assertTrue(isLyricsMetadataLine("作词 择荇"))
    }

    /** 天女珞 case：同为中文的同刻两行是连续句，不配成译文 */
    @Test
    fun sameScriptLinesAreNotTranslationPair() {
        val l = LrcParser.parse("[00:10.00]天女珞在云端\n[00:10.00]轻轻唱着歌")!!
        assertEquals(1, l.lines.size)
        assertNull(l.lines[0].translation)
    }

    @Test
    fun hangulThenChineseMergesAsTranslation() {
        val l = LrcParser.parse("[00:07.24]미안해\n[00:07.24]对不起")!!
        assertEquals(1, l.lines.size)
        assertEquals("미안해", l.lines[0].text)
        assertEquals("对不起", l.lines[0].translation)
    }

    @Test
    fun japaneseMainWithChineseOnlyCandidateIsTranslation() {
        val l = LrcParser.parse("[00:05.00]君の歌を\n[00:05.00]唱你的歌")!!
        assertEquals("唱你的歌", l.lines[0].translation)
    }

    @Test
    fun pureMusicAndPlaceholdersAreCleared() {
        val l = LrcParser.parse("[00:01.00]纯音乐，请欣赏\n[00:02.00]暂无歌词\n[00:03.00]还没有歌词哦")!!
        assertTrue(l.lines.all { it.text.isEmpty() })
    }

    @Test
    fun unsortedInputGetsSorted() {
        val l = LrcParser.parse("[00:30.00]b\n[00:10.00]a")!!
        assertEquals(listOf("a", "b"), l.lines.map { it.text })
    }

    @Test
    fun emptyOrNoTimestampReturnsNull() {
        assertNull(LrcParser.parse(""))
        assertNull(LrcParser.parse(null))
        assertNull(LrcParser.parse("[ti:only meta]\nby line without time"))
    }

    @Test
    fun externalRomaAndTranslationStreamsMergeWithinTolerance() {
        val main = LrcParser.parse("[00:10.00]君の声")!!
        main.translationLines = listOf(LyricLine(10_150, "你的声音"))
        main.romaLines = listOf(LyricLine(9_900, "kimi no koe"))
        LrcParser.mergeExtendedStreams(main)
        assertEquals("你的声音", main.lines[0].translation)
        assertEquals("kimi no koe", main.lines[0].roma)
    }

    @Test
    fun fillProgressMatchesCsharpSemantics() {
        val lines = listOf(
            LyricLine(1_000, "a"),
            LyricLine(3_000, "b"),
        )
        assertEquals(1.0, LyricFillCalculator.computeFillProgress(lines[0], 0, lines, 500, lineMode = true), 0.0)
        assertEquals(0.0, LyricFillCalculator.computeFillProgress(lines[0], 0, lines, 500, lineMode = false), 0.0)
        // 线性：1000→3000 之间走了 1000ms = 0.5
        assertEquals(0.5, LyricFillCalculator.computeFillProgress(lines[0], 0, lines, 2_000, false), 0.001)
        assertEquals(1.0, LyricFillCalculator.computeFillProgress(lines[0], 0, lines, 9_000, false), 0.0)

        val syllables = listOf(WordTimestamp("ab", 0, 1_000), WordTimestamp("cd", 1_000, 1_000))
        assertEquals(0.25, LyricFillCalculator.calculateSyllableProgress(syllables, 500), 0.001)
        assertEquals(0.5, LyricFillCalculator.calculateSyllableProgress(syllables, 1_000), 0.001)
        assertEquals(1.0, LyricFillCalculator.calculateSyllableProgress(syllables, 2_000), 0.001)
    }
}
