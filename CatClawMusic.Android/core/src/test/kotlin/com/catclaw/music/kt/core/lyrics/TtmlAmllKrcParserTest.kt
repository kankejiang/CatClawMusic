package com.catclaw.music.kt.core.lyrics

import org.junit.Assert.assertEquals
import org.junit.Assert.assertNotNull
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Test

class TtmlAmllKrcParserTest {

    // ── TTML ──────────────────────────────────────────────

    @Test
    fun ttmlTimestampFormats() {
        assertEquals(7_240L, TtmlParser.parseTimestamp("00:07.240"))
        assertEquals(7_240L, TtmlParser.parseTimestamp("00:00:07.24"))   // .24 → 240ms（截断）
        assertEquals(245L, TtmlParser.parseTimestamp("00:00:00.2456"))   // 截断不四舍五入
        assertEquals(3_500L, TtmlParser.parseTimestamp("00:00:03:15"))   // HH:MM:SS:FF, 15 帧 @30fps = 500ms
        assertEquals(2_000L, TtmlParser.parseTimestamp("2s"))
        assertEquals(3_725_000L, TtmlParser.parseTimestamp("PT1H2M5S"))
        assertNull(TtmlParser.parseTimestamp("nonsense"))
        assertNull(TtmlParser.parseTimestamp(null))
    }

    @Test
    fun ttmlParsesParagraphsAndAgents() {
        val xml = """
            <?xml version="1.0" encoding="utf-8"?>
            <tt xmlns:ttm="http://www.w3.org/ns/ttml#metadata" xmlns:itunes="http://music.apple.com/itms">
              <metadata>
                <ttm:agent xml:id="v1" type="person"><ttm:name>女声</ttm:name></ttm:agent>
                <ttm:agent xml:id="v2" type="person"><ttm:name>男声</ttm:name></ttm:agent>
                <ttm:agent xml:id="bgv1" type="person"><ttm:name>和声</ttm:name></ttm:agent>
              </metadata>
              <body>
                <div>
                  <p begin="00:01.000" end="00:04.000" ttm:agent="v1">第一句</p>
                  <p begin="00:01.000" end="00:04.000" ttm:agent="v2">第二句</p>
                  <p begin="00:05.000" ttm:agent="bgv1">和声句</p>
                </div>
              </body>
            </tt>
        """.trimIndent()

        val l = TtmlParser.parse(xml)
        assertNotNull(l)
        assertEquals(2, l!!.lines.size)
        // v1 → 左、v2 → 右，同拍 → 合并成一行双文本
        val merged = l.lines[0]
        assertEquals("第一句", merged.text)
        assertEquals("第二句", merged.secondaryText)
        assertTrue(l.hasPerLineAlignment)
        assertEquals("女声", merged.singerName)
        // 和声行独立保留
        assertEquals("和声句", l.lines[1].text)
        assertTrue(l.lines[1].isBackingVocal)
    }

    @Test
    fun ttmlSpanOffsetsAreConvertedToAbsoluteTime() {
        // 网易云式：span begin/end 是相对行首的偏移（裸数字按"秒"解释，所以写成 mm:ss 形式）
        val xml = """
            <?xml version="1.0"?>
            <tt><body><div>
              <p begin="00:20.000" end="00:23.000"><span begin="00:00.000" end="00:01.000">你</span><span begin="00:01.000" end="00:02.500">好</span></p>
            </div></body></tt>
        """.trimIndent()
        val l = TtmlParser.parse(xml)!!
        val words = l.lines[0].wordTimestamps!!
        assertEquals(20_000L, words[0].startMs)
        assertEquals(1_000L, words[0].durationMs)
        assertEquals(21_000L, words[1].startMs)
        assertEquals("你好", l.lines[0].text)
    }

    @Test
    fun ttmlBrBecomesNewlineAndNoSpanMeansNoWords() {
        val xml = """
            <?xml version="1.0"?>
            <tt><body><div><p begin="00:01.000">A<br/>B</p></div></body></tt>
        """.trimIndent()
        val l = TtmlParser.parse(xml)!!
        assertEquals("A\nB", l.lines[0].text)
        assertNull(l.lines[0].wordTimestamps)
    }

    @Test
    fun ttmlRejectsGarbageAndMissingBody() {
        assertNull(TtmlParser.parse("<tt><div><p begin=\"1s\">x</p></div></tt>"))
        assertNull(TtmlParser.parse("not xml at all"))
        assertNull(TtmlParser.parse(null))
    }

    @Test
    fun ttmlSanitizeKeepsEmoji() {
        // C# 逐 UTF-16 char 过滤会把代理对删掉；这里按码点过滤要保住 emoji
        val xml = """
            <?xml version="1.0"?>
            <tt><body><div><p begin="00:01.000">你好😀世界</p></div></body></tt>
        """.trimIndent()
        assertEquals("你好😀世界", TtmlParser.parse(xml)!!.lines[0].text)
    }

    // ── AMLL ──────────────────────────────────────────────

    @Test
    fun amllWordOffsetsBecomeAbsolute() {
        val json = """
            {"lyrics":{"lines":[
              {"startTime":15000,"content":"起风了","words":[
                 {"word":"起","startTime":0,"endTime":211},
                 {"word":"风","startTime":211,"endTime":422},
                 {"word":"了","startTime":422,"endTime":700}
              ]},
              {"startTime":20000,"content":"","role":"v1000"}
            ]}}
        """.trimIndent()
        assertTrue(AmllParser.looksLike(json))
        val l = AmllParser.parse(json)!!
        assertEquals(2, l.lines.size)
        val words = l.lines[0].wordTimestamps!!
        assertEquals(15_000L, words[0].startMs)
        assertEquals(15_211L, words[1].startMs)
        assertEquals(15_422L, words[2].startMs)
        assertEquals(211L, words[0].durationMs)
        assertTrue(l.lines[1].isBackingVocal)
    }

    @Test
    fun amllDefaultsWordDurationTo200ms() {
        val json = """{"lyrics":{"lines":[{"startTime":1000,"content":"啦","words":[{"word":"啦","startTime":0}]}]}}"""
        val l = AmllParser.parse(json)!!
        assertEquals(200L, l.lines[0].wordTimestamps!![0].durationMs)
        assertEquals(1_000L, l.lines[0].wordTimestamps!![0].startMs)
    }

    @Test
    fun amllRejectsApiErrorPayload() {
        assertTrue(AmllParser.looksLike("""{"code":404,"message":"not found"}"""))
        assertNull(AmllParser.parse("""{"code":404,"message":"not found"}"""))
        assertNull(AmllParser.parse("{not json"))
        assertNull(AmllParser.parse("""{"lyrics":{"lines":[]}}"""))
    }

    // ── KRC ───────────────────────────────────────────────

    @Test
    fun krcTextBlockParsing() {
        val text = "[id:qrc_id]\n[0,500]第一句<20,120>第<140,180>一句\n[500,600]<0,300>只<300,300>有逐字\n"
        val l = KrcParser.parseKrcText(text)!!
        assertEquals(2, l.lines.size)
        assertEquals(0L, l.lines[0].startMs)
        assertEquals("第一句", l.lines[0].text)
        assertEquals(20L, l.lines[0].wordTimestamps!![0].startMs)   // 相对偏移 + 行起始(0)
        assertEquals(500L, l.lines[1].startMs)
        assertEquals(500L, l.lines[1].wordTimestamps!![0].startMs)  // 0 + 500
        assertEquals(800L, l.lines[1].wordTimestamps!![1].startMs)
    }

    @Test
    fun krcDoesNotSortOrMerge() {
        // 照抄 C#：KRC 分支不排序、不配对译文
        val l = KrcParser.parseKrcText("[9000,500]后\n[1000,500]前\n미안해\n")!!
        assertEquals(listOf(9_000L, 1_000L), l.lines.map { it.startMs })
        assertNull(l.lines[0].translation)
    }

    @Test
    fun krcRejectsWrongMagic() {
        assertNull(KrcParser.parse(null))
        assertNull(KrcParser.parse(ByteArray(4)))
        assertNull(KrcParser.parse("YXnotakrcfile".toByteArray()))
    }
}
