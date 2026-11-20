package com.catclaw.music.kt.lyrico.lyrics

import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Test

/** 插件返回值解析的单测（纯 JVM，不需要设备）。 */
class LyricoLyricsParserTest {

    @Test
    fun parsesStructuredWordByWord() {
        // 真实形态：[[行起, 行止, [[词起, 词止, 文本], ...]], ...]
        val payload = """
        [{"type":"structured","tags":{"ti":"晴天","ar":"周杰伦","al":"叶惠美"},
          "original":[
            [1000,3000,[[1000,1500,"故事"],[1500,2000,"的"],[2000,3000,"小黄花"]]],
            [3000,5000,[[3000,5000,"从出生那年就飘着"]]]
          ]}]
        """.trimIndent()

        val list = LyricoLyricsParser.parseCandidates(payload)
        assertEquals(1, list.size)
        val lyric = list.first()
        assertEquals(LyricoPayloadType.STRUCTURED, lyric.payloadType)
        assertEquals("晴天", lyric.tags["ti"])
        assertEquals(2, lyric.original.size)
        assertEquals("故事的小黄花", lyric.original[0].text)
        assertTrue("第一行是逐字", lyric.original[0].isWordByWord)
        assertFalse("第二行不是逐字", lyric.original[1].isWordByWord)
        assertTrue(lyric.hasContent)
        assertTrue(lyric.isWordByWord)
    }

    @Test
    fun parsesTranslatedAndRomanization() {
        val payload = """
        [{"type":"structured","tags":{},
          "original":[[0,1000,[[0,1000,"さくら"]]]],
          "translated":[[0,1000,[[0,1000,"樱花"]]]],
          "romanization":[[0,1000,[[0,1000,"sakura"]]]]}]
        """.trimIndent()
        val lyric = LyricoLyricsParser.parseCandidates(payload).first()
        assertEquals("樱花", lyric.translated?.first()?.text)
        assertEquals("sakura", lyric.romanization?.first()?.text)
    }

    @Test
    fun parsesRawLrcPayloadTypes() {
        val payload = """
        [{"type":"rawPlainLrc","plainLrc":"[00:01.00]第一行\n[00:02.00]第二行"},
         {"type":"rawEnhancedLrc","rawEnhancedLrc":"[00:01.00]<00:01.00>你好"}]
        """.trimIndent()
        val list = LyricoLyricsParser.parseCandidates(payload)
        assertEquals(2, list.size)
        assertEquals(LyricoPayloadType.RAW_PLAIN_LRC, list[0].payloadType)
        assertTrue(list[0].rawPlainLrc.contains("第一行"))
        assertEquals(LyricoPayloadType.RAW_ENHANCED_LRC, list[1].payloadType)
        assertTrue(list[1].rawEnhancedLrc.contains("你好"))
        assertTrue(list[0].hasContent)
    }

    @Test
    fun toleratesObjectShapedLines() {
        val payload = """
        [{"type":"structured","original":[
          {"start":0,"end":1000,"words":[{"start":0,"end":500,"text":"a"},{"start":500,"end":1000,"text":"b"}]}
        ]}]
        """.trimIndent()
        val lyric = LyricoLyricsParser.parseCandidates(payload).first()
        assertEquals("ab", lyric.original.first().text)
    }

    @Test
    fun emptyOrGarbageYieldsNoCandidates() {
        assertTrue(LyricoLyricsParser.parseCandidates(null).isEmpty())
        assertTrue(LyricoLyricsParser.parseCandidates("").isEmpty())
        assertTrue(LyricoLyricsParser.parseCandidates("not-json").isEmpty())
        assertTrue(LyricoLyricsParser.parseCandidates("[]").isEmpty())
    }
}
