package com.catclaw.music.kt.core.lyrics

import org.junit.Assert.assertEquals
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Test

class LyricsFileLocatorAndEncodingTest {

    @Test
    fun candidateOrderPutsTextBeforeBinary() {
        val c = LyricsFileLocator.candidates("/sdcard/Music/Train - Hey/song.mp3")
        assertEquals(
            listOf(
                "/sdcard/Music/Train - Hey/song.lrc",
                "/sdcard/Music/Train - Hey/song.ttml",
                "/sdcard/Music/Train - Hey/song.xml",
                "/sdcard/Music/Train - Hey/song.krc",
                "/sdcard/Music/Train - Hey/song.qrc",
            ),
            c,
        )
        assertEquals("/sdcard/Music/Train - Hey", LyricsFileLocator.directoryOf("/sdcard/Music/Train - Hey/song.mp3"))
        assertEquals("song", LyricsFileLocator.baseName("/sdcard/Music/song.mp3"))
    }

    @Test
    fun noDirectoryMeansNoCandidates() {
        assertTrue(LyricsFileLocator.candidates("song.mp3").isEmpty())
        assertNull(LyricsFileLocator.directoryOf("content://media/external/audio/media/12"))
    }

    @Test
    fun fuzzyPickPrefersLrcThenTtmlThenXml() {
        val files = listOf("其他.lrc", "晴天.ttml", "晴天.xml", "晴天.lrc")
        assertEquals("晴天.lrc", LyricsFileLocator.fuzzyPick(files, "晴天"))
        assertEquals("晴天.ttml", LyricsFileLocator.fuzzyPick(files - "晴天.lrc", "晴天"))
        assertNull(LyricsFileLocator.fuzzyPick(files, "不存在的歌"))
        assertNull(LyricsFileLocator.fuzzyPick(files, "  "))
    }

    @Test
    fun decodesBomAndUtf16Variants() {
        val text = "[00:01.00]你好"
        assertEquals(text, LyricsEncoding.decode(text.toByteArray(Charsets.UTF_8)))
        assertEquals(text, LyricsEncoding.decode(byteArrayOf(0xEF.toByte(), 0xBB.toByte(), 0xBF.toByte()) + text.toByteArray(Charsets.UTF_8)))
        assertEquals(text, LyricsEncoding.decode(byteArrayOf(0xFF.toByte(), 0xFE.toByte()) + text.toByteArray(Charsets.UTF_16LE)))
        assertEquals(text, LyricsEncoding.decode(byteArrayOf(0xFE.toByte(), 0xFF.toByte()) + text.toByteArray(Charsets.UTF_16BE)))
    }

    @Test
    fun fallsBackToGbkForNonUtf8Bytes() {
        val gbk = "[00:01.00]中文歌词".toByteArray(charset("GBK"))
        val decoded = LyricsEncoding.decode(gbk)!!
        assertTrue(decoded.contains("中文歌词"))
    }

    @Test
    fun oversizedFileIsRejected() {
        assertNull(LyricsEncoding.decode(ByteArray(LyricsEncoding.MAX_LYRICS_FILE_BYTES + 1)))
    }

    @Test
    fun contentSniffingRoutesToTheRightParser() {
        val lrc = LyricsEncoding.parseContent("[00:01.00]一句")!!
        assertEquals("一句", lrc.lines[0].text)

        val amll = LyricsEncoding.parseContent(
            """{"lyrics":{"lines":[{"startTime":1000,"content":"aml行"}]}}""",
        )!!
        assertEquals("aml行", amll.lines[0].text)

        val ttml = LyricsEncoding.parseContent(
            """<?xml version="1.0"?><tt><body><div><p begin="00:02.000">ttml行</p></div></body></tt>""",
        )!!
        assertEquals("ttml行", ttml.lines[0].text)
        assertEquals(2_000L, ttml.lines[0].startMs)

        assertNull(LyricsEncoding.parseContent(null))
        assertNull(LyricsEncoding.parseContent("[ti:只有元数据]"))
    }
}
