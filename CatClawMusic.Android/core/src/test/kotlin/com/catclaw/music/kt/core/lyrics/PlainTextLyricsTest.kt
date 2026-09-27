package com.catclaw.music.kt.core.lyrics

import org.junit.Assert.assertEquals
import org.junit.Assert.assertNotNull
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Test

/**
 * 无时间戳歌词的兜底档，逐条对着 C# `LyricsService.Lrc.cs:424-458` + `:499-509` 钉。
 * 这一档此前本端完全不存在，所以每条都是新行为 —— 尤其"标签行也占一格时间轴"这种
 * 只在 C# 源码里才看得出来的细节。
 */
class PlainTextLyricsTest {

    @Test
    fun blankLinesAreDroppedWithoutConsumingTimelineSlots() {
        val lyrics = plainTextLyrics("第一行\n\n   \n第二行\n")!!
        assertEquals(listOf("第一行", "第二行"), lyrics.lines.map { it.text })
        assertEquals(listOf(0L, 5_000L), lyrics.lines.map { it.startMs })
    }

    @Test
    fun commentLinesAreDropped() {
        val lyrics = plainTextLyrics("// 这是注释\n正文")!!
        assertEquals(listOf("正文"), lyrics.lines.map { it.text })
        assertEquals("注释不占时间轴", listOf(0L), lyrics.lines.map { it.startMs })
    }

    @Test
    fun metadataTagLineStillConsumesASlot() {
        // C# 在标签分支里 `lineIndex++` 之后才 continue —— 所以第一行正文是从 5 秒开始的
        val lyrics = plainTextLyrics("[ti:歌名]\n第一行")!!
        assertEquals(listOf("第一行"), lyrics.lines.map { it.text })
        assertEquals(5_000L, lyrics.lines.single().startMs)
    }

    @Test
    fun placeholderLinesAreFiltered() {
        val lyrics = plainTextLyrics("纯音乐，请欣赏\n真正的一行\n暂无歌词\n还没有歌词哦")!!
        assertEquals(listOf("真正的一行"), lyrics.lines.map { it.text })
    }

    @Test
    fun crlfAndLoneCarriageReturnsAreNormalized() {
        val lyrics = plainTextLyrics("a\r\nb\rc")!!
        assertEquals(listOf("a", "b", "c"), lyrics.lines.map { it.text })
        assertEquals(listOf(0L, 5_000L, 10_000L), lyrics.lines.map { it.startMs })
    }

    @Test
    fun nothingLeftMeansNoLyrics() {
        assertNull(plainTextLyrics(""))
        assertNull(plainTextLyrics("   \n\t\n"))
        assertNull("全是占位行就等于没有歌词" + " ", plainTextLyrics("纯音乐\n暂无歌词"))
    }

    @Test
    fun markupIsRefusedRatherThanShownAsLyrics() {
        assertTrue(looksLikeMarkup("<!DOCTYPE html><html><body>404</body></html>"))
        assertTrue(looksLikeMarkup("{\"code\":401,\"message\":\"unauthorized\"}"))
        assertTrue(looksLikeMarkup("<Error><Message>no such file</Message></Error>"))
        assertTrue(looksLikeMarkup("<html lang=\"zh\"><body>x"))
        assertNull("错误页不该整篇刷成歌词", LyricsEncoding.parseContent("<html><body>404 Not Found</body></html>"))
        assertNull(LyricsEncoding.parseContent("""{"error":"lyrics not found"}"""))
    }

    @Test
    fun binaryJunkThatHappensToDecodeIsStillRefused() {
        val junk = String(byteArrayOf(1, 2, 3, 4, 5, 6, 7, 8), Charsets.ISO_8859_1)
        assertTrue(looksLikeBinaryText(junk))
        assertNull(plainTextLyrics(junk))
        assertNull(LyricsEncoding.parseContent(junk))
    }

    @Test
    fun tabsAndNewlinesDoNotCountAsJunk() {
        val lyrics = plainTextLyrics("第一行\t带制表\n第二行")
        assertNotNull("正常歌词里夹一个制表符不该被二进制闸误杀", lyrics)
        assertEquals(listOf("第一行\t带制表", "第二行"), lyrics!!.lines.map { it.text })
        assertEquals(listOf(0L, 5_000L), lyrics!!.lines.map { it.startMs })
    }

    @Test
    fun timedLrcStillWinsOverThePlainTextBranch() {
        val lyrics = LyricsEncoding.parseContent("[00:30.00]有时间戳")!!
        assertEquals(listOf(30_000L), lyrics.lines.map { it.startMs })
    }

    @Test
    fun brokenTtmlNeverFallsBackToDumpingXmlOnScreen() {
        // C#：「TTML 专用路径：绝不回退到 PlainText」。没有这一道，用户会在歌词页看到 XML 源码。
        val broken = """<tt xmlns="http://www.w3.org/ns/ttml"><body><div>  <p broken</div></body></tt>"""
        assertNull(LyricsEncoding.parseContent(broken))
    }

    @Test
    fun ordinaryProseWithoutAnyMarkupBecomesScrollableLyrics() {
        val lyrics = LyricsEncoding.parseContent("稻香\n对这个世界如果你有太多的抱怨")
        assertEquals(listOf("稻香", "对这个世界如果你有太多的抱怨"), lyrics?.lines?.map { it.text })
    }
}
