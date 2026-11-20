package com.catclaw.music.kt.lyrico.lyrics

import com.catclaw.music.kt.core.lyrics.LrcParser
import com.catclaw.music.kt.core.lyrics.Lyrics
import com.catclaw.music.kt.core.lyrics.LyricsMetadata
import com.catclaw.music.kt.core.lyrics.TtmlParser
import com.catclaw.music.kt.core.lyrics.LyricLine
import com.catclaw.music.kt.core.lyrics.WordTimestamp
import org.junit.Assert.assertEquals
import org.junit.Assert.assertNotNull
import org.junit.Assert.assertTrue
import org.junit.Test

/**
 * 编码器的往返测试：**写出去的文本必须能被 App 自己的解析器读回来**。
 * 这是「写标签」功能正确性的最低保证 —— 只验证「字符串看起来对」是不够的。
 */
class LyricoLyricsEncoderTest {

    private fun sampleLyrics(): Lyrics = Lyrics().apply {
        metadata = LyricsMetadata(title = "晴天", artist = "周杰伦", album = "叶惠美")
        lines = mutableListOf(
            LyricLine(
                startMs = 1000,
                text = "故事的小黄花",
                wordTimestamps = listOf(
                    WordTimestamp("故事", 1000, 400),
                    WordTimestamp("的", 1400, 200),
                    WordTimestamp("小黄花", 1600, 900),
                ),
                translation = "The little yellow flower of the story",
                roma = "gu shi de xiao huang hua",
            ),
            LyricLine(startMs = 3000, text = "从出生那年就飘着"),
        )
    }

    @Test
    fun plainRoundTripsThroughAppParser() {
        val text = LyricoLyricsEncoder.encode(sampleLyrics(), LyricoLyricsFormat.PLAIN)
        val parsed = LrcParser.parse(text)
        assertNotNull("普通 LRC 应能被 App 解析器读回", parsed)
        parsed!!
        assertEquals(2, parsed.lines.size)
        assertEquals("故事的小黄花", parsed.lines[0].text)
        assertEquals(1000L, parsed.lines[0].startMs)
        assertEquals("晴天", parsed.metadata.title)
        assertEquals("周杰伦", parsed.metadata.artist)
    }

    @Test
    fun enhancedKeepsWordTimestampsThroughRoundTrip() {
        val text = LyricoLyricsEncoder.encode(sampleLyrics(), LyricoLyricsFormat.ENHANCED)
        val parsed = LrcParser.parse(text)
        assertNotNull(parsed)
        parsed!!
        val first = parsed.lines.first()
        assertEquals("故事的小黄花", first.text)
        val words = first.wordTimestamps
        assertNotNull("增强 LRC 应带回逐字时间戳", words)
        assertEquals(3, words!!.size)
        assertEquals(1000L, words[0].startMs)
        assertEquals("小黄花", words[2].word)
    }

    @Test
    fun verbatimKeepsWordsAndLines() {
        val text = LyricoLyricsEncoder.encode(sampleLyrics(), LyricoLyricsFormat.VERBATIM)
        assertTrue(text.contains("[00:01.00]故事"))
        assertTrue(text.contains("[00:01.60]小黄花"))
        val parsed = LrcParser.parse(text)
        assertNotNull(parsed)
        assertTrue("逐字文本至少要能解析出歌词行", parsed!!.lines.isNotEmpty())
    }

    @Test
    fun ttmlRoundTripsThroughAppParser() {
        val text = LyricoLyricsEncoder.encode(sampleLyrics(), LyricoLyricsFormat.TTML)
        assertTrue("应是合法 TTML 头", text.contains("<tt "))
        val parsed = TtmlParser.parse(text)
        assertNotNull("TTML 应能被 App 解析器读回", parsed)
        parsed!!
        assertEquals(2, parsed.lines.size)
        assertEquals("故事的小黄花", parsed.lines[0].text)
        assertEquals(1000L, parsed.lines[0].startMs)
        // 逐字时间戳应保留
        val words = parsed.lines[0].wordTimestamps
        assertNotNull("TTML span 应带回逐字", words)
        assertEquals(3, words!!.size)
    }

    @Test
    fun escapesXmlSpecialCharacters() {
        val lyrics = Lyrics().apply {
            metadata = LyricsMetadata(title = "A & B")
            lines = mutableListOf(LyricLine(startMs = 0, text = "<tag> & \"quote\""))
        }
        val text = LyricoLyricsEncoder.encode(lyrics, LyricoLyricsFormat.TTML)
        assertTrue(text.contains("&amp;"))
        assertTrue(text.contains("&lt;tag&gt;"))
        val parsed = TtmlParser.parse(text)
        assertNotNull("转义后仍应可解析", parsed)
        assertEquals("<tag> & \"quote\"", parsed!!.lines.first().text)
    }

    @Test
    fun emptyLyricsEncodesToSomethingParsable() {
        val empty = Lyrics()
        val text = LyricoLyricsEncoder.encode(empty, LyricoLyricsFormat.PLAIN)
        assertEquals("", text)
    }
}
