package com.catclaw.music.kt.data.lyrics

import com.catclaw.music.kt.data.lyrics.TagFixtures.be32
import com.catclaw.music.kt.data.lyrics.TagFixtures.flac
import com.catclaw.music.kt.data.lyrics.TagFixtures.id3v2
import com.catclaw.music.kt.data.lyrics.TagFixtures.apev2
import com.catclaw.music.kt.data.lyrics.TagFixtures.mp4
import com.catclaw.music.kt.data.lyrics.TagFixtures.ogg
import com.catclaw.music.kt.data.lyrics.TagFixtures.usltValue
import com.catclaw.music.kt.data.lyrics.TagFixtures.vorbisComment
import java.io.File
import java.nio.charset.StandardCharsets
import org.junit.Assert.assertEquals
import org.junit.Assert.assertNull
import org.junit.Test

/**
 * 内嵌标签歌词：候选顺序、四种文本编码、容器块跳过、损坏输入。
 * 标签字节由 TagFixtures 按规范独立拼出，不依赖被测解析器。
 */
class AudioTagLyricsTest {

    private val lyrics = "[00:00.100]第一行\n[00:03.250]第二行"

    @Test
    fun `ID3v23 USLT 四种文本编码都读得出中文`() {
        for (encoding in 0..3) {
            val text = if (encoding == 0) "[00:00.100]Latin only" else lyrics
            val tag = id3v2(3, listOf("USLT" to usltValue(encoding, text, "desc")))
            val parsed = Id3v2Tag.lyrics(tag + ByteArray(64))
            assertEquals("encoding=$encoding", text.lineSequence().first(), parsed?.lineSequence()?.first())
        }
    }

    @Test
    fun `ID3v24 帧长度是 synchsafe 且跳过扩展头`() {
        val frame = "USLT" to usltValue(3, lyrics)
        val ext = byteArrayOf(0, 0, 0, 6, 0, 0)
        val tag = id3v2(4, listOf("TIT2" to byteArrayOf(3) + "标题".toByteArray(StandardCharsets.UTF_8), frame), extHeader = ext)
        assertEquals(lyrics, Id3v2Tag.lyrics(tag))
    }

    @Test
    fun `取第一个非空 USLT，空的跳过`() {
        val tag = id3v2(3, listOf("USLT" to usltValue(3, ""), "COMM" to byteArrayOf(3), "USLT" to usltValue(3, lyrics)))
        assertEquals(lyrics, Id3v2Tag.lyrics(tag))
    }

    @Test
    fun `标签被 unsynchronisation 处理过时也能解出`() {
        // 正文里刻意放 0xFF 0x00，写侧同步化后读侧要还原回原样
        val raw = byteArrayOf(0xFF.toByte(), 0x00, 'A'.code.toByte())
        val value = byteArrayOf(0) + "eng".toByteArray() + byteArrayOf(0) + raw
        val tag = id3v2(3, listOf("USLT" to value), unsync = true)
        val parsed = Id3v2Tag.lyrics(tag)
        assertEquals(3, parsed?.toByteArray(StandardCharsets.ISO_8859_1)?.size)
    }

    @Test
    fun `FLAC 跳过 padding 块取 VORBIS_COMMENT`() {
        val comment = vorbisComment(listOf("ARTIST" to "x", "LYRICS" to lyrics))
        assertEquals(lyrics, VorbisTag.lyrics(comment))
        val file = tempFile("flac", flac(comment))
        assertEquals(lyrics, AudioTagLyrics.read(file.absolutePath))
    }

    @Test
    fun `FLAC 没有 LYRICS 时退到 UNSYNCEDLYRICS`() {
        val comment = vorbisComment(listOf("UNSYNCEDLYRICS" to "plain text"))
        val file = tempFile("flac2", flac(comment, withPaddingFirst = false))
        assertEquals("plain text", AudioTagLyrics.read(file.absolutePath))
    }

    @Test
    fun `OGG 从 comment 包里取歌词`() {
        val file = tempFile("ogg", ogg(vorbisComment(listOf("LYRICS" to lyrics))))
        assertEquals(lyrics, AudioTagLyrics.read(file.absolutePath))
    }

    @Test
    fun `MP4 走 moov udta meta ilst lyr 取 UTF8 文本`() {
        val file = tempFile("m4a", mp4(lyrics))
        assertEquals(lyrics, AudioTagLyrics.read(file.absolutePath))
    }

    @Test
    fun `APEv2 支持 UTF8 与 UTF16LE 两种值编码`() {
        val utf8 = apev2(listOf("Lyrics" to lyrics.toByteArray(StandardCharsets.UTF_8)))
        assertEquals(lyrics, AudioTagLyrics.read(tempFile("ape1", audioBody() + utf8).absolutePath))

        val value = lyrics.toByteArray(StandardCharsets.UTF_16LE)
        val items = apev2Utf16(value)
        assertEquals(lyrics, AudioTagLyrics.read(tempFile("ape2", audioBody() + items).absolutePath))
    }

    @Test
    fun `同时有 ID3 与 APE 时按 C# 顺序取 ID3`() {
        val id3 = id3v2(3, listOf("USLT" to usltValue(3, "from id3")))
        val ape = apev2(listOf("Lyrics" to "from ape".toByteArray()))
        val file = tempFile("both", id3 + audioBody() + ape)
        assertEquals("from id3", AudioTagLyrics.read(file.absolutePath))
    }

    @Test
    fun `没有标签 只有音频体 返回 null 而不抛`() {
        val file = tempFile("bare", audioBody())
        assertNull(AudioTagLyrics.read(file.absolutePath))
    }

    @Test
    fun `标签长度被写坏时安全返回 null`() {
        val broken = byteArrayOf('I'.code.toByte(), 'D'.code.toByte(), 3, 3, 0, 0, 0x7F.toByte(), 0x7F.toByte(), 0x7F.toByte(), 0x7F.toByte()) + ByteArray(16)
        val file = tempFile("broken", broken + audioBody())
        assertNull(AudioTagLyrics.read(file.absolutePath))
    }

    @Test
    fun `空文件与不存在的文件都返回 null`() {
        assertNull(AudioTagLyrics.read(tempFile("empty", ByteArray(0)).absolutePath))
        assertNull(AudioTagLyrics.read(File(tempDir(), "nope.mp3").absolutePath))
    }

    private fun audioBody(): ByteArray = byteArrayOf(0xFF.toByte(), 0xFB.toByte(), 0x90.toByte()) + ByteArray(200)

    private fun apev2Utf16(value: ByteArray): ByteArray {
        // flags bit29-30 = 1 表示 UTF-16LE：0x20000000
        return TagFixtures.apev2WithFlags(listOf("Lyrics" to value), 0x20000000)
    }

    private fun tempDir(): File = File(System.getProperty("java.io.tmpdir"), "catclaw-tags").apply { mkdirs() }

    private fun tempFile(name: String, bytes: ByteArray): File =
        File(tempDir(), "$name.bin").also { it.writeBytes(bytes) }
}
