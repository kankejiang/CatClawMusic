package com.catclaw.music.kt.data.media

import com.catclaw.music.kt.core.library.AudioTags
import com.catclaw.music.kt.data.lyrics.TagFixtures
import org.junit.Assert.assertEquals
import org.junit.Assert.assertTrue
import org.junit.Test
import java.io.ByteArrayOutputStream
import java.nio.charset.StandardCharsets

/**
 * 远程头部字节的标签与时长解析。
 *
 * 所有字节都是测试自己按规范拼的（`TagFixtures` 加本文件的位写入器），
 * 与被测解析器没有共用代码 —— 否则「解对了」就只是自证。
 */
class AudioHeadTagsTest {

    // ---- ID3v2 文本帧 ----

    @Test
    fun id3v23TextFrames() {
        val head = TagFixtures.id3v2(
            3,
            listOf(
                "TIT2" to textFrame(3, "夜曲"),
                "TPE1" to textFrame(3, "周杰伦"),
                "TALB" to textFrame(3, "十一月的萧邦"),
                "TCON" to textFrame(0, "Pop"),
                "TYER" to textFrame(0, "2005"),
                "TRCK" to textFrame(0, "3/12"),
            ),
        ) + mpegFrame(mpeg1 = true, bitrateIndex = 9, sampleIndex = 0, mono = false)
        val tags = AudioHeadTags.parse(head)
        assertEquals("夜曲", tags.title)
        assertEquals("周杰伦", tags.artist)
        assertEquals("十一月的萧邦", tags.album)
        assertEquals("Pop", tags.genre)
        assertEquals(2005, tags.year)
        // TRCK 的形状是「本张第几首/共几首」，只要前半个 —— 与 SAF 扫描同一口径
        assertEquals(3, tags.trackNumber)
    }

    @Test
    fun id3v22UsesThreeLetterIds() {
        val head = TagFixtures.id3v2(
            2,
            listOf(
                "TT2" to textFrame(0, "Title"),
                "TP1" to textFrame(0, "Artist"),
                "TAL" to textFrame(0, "Album"),
                "TCO" to textFrame(0, "Rock"),
                "TYE" to textFrame(0, "1999"),
                "TRK" to textFrame(0, "7"),
            ),
        )
        val tags = AudioHeadTags.parse(head)
        assertEquals("Title", tags.title)
        assertEquals("Artist", tags.artist)
        assertEquals("Album", tags.album)
        assertEquals("Rock", tags.genre)
        assertEquals(1999, tags.year)
        assertEquals(7, tags.trackNumber)
    }

    @Test
    fun utf16FrameWithMultipleValuesTakesTheFirst() {
        // v2.4 允许一帧里用终止符塞多个值
        val head = TagFixtures.id3v2(4, listOf("TPE1" to textFrame(1, "主唱") + byteArrayOf(0, 0) + textFrame(1, "和声")))
        assertEquals("主唱", AudioHeadTags.parse(head).artist)
    }

    @Test
    fun tdrcDateAndParenthesisedYearBothParse() {
        val tdrc = TagFixtures.id3v2(4, listOf("TDRC" to textFrame(0, "2005-11-01T00:00:00")))
        assertEquals(2005, AudioHeadTags.parse(tdrc).year)
        val paren = TagFixtures.id3v2(3, listOf("TYER" to textFrame(0, "(2005)")))
        assertEquals(2005, AudioHeadTags.parse(paren).year)
        val none = TagFixtures.id3v2(3, listOf("TYER" to textFrame(0, "无")))
        assertEquals(0, AudioHeadTags.parse(none).year)
    }

    @Test
    fun textFramesSurviveAHeaderFullOfCoverArt() {
        // 70 KB 的 APIC 之后再接文本帧：帧长度第二个字节非 0，位拼接写错就会在这里断链
        val cover = ByteArray(70_000) { (it % 251).toByte() }
        val head = TagFixtures.id3v2(
            3,
            listOf("APIC" to cover, "TIT2" to textFrame(3, "标题"), "TPE1" to textFrame(3, "歌手")),
        )
        val tags = AudioHeadTags.parse(head)
        assertEquals("标题", tags.title)
        assertEquals("歌手", tags.artist)
    }

    @Test
    fun framesAfterId3TagAreStillFound() {
        val head = TagFixtures.id3v2(3, listOf("TIT2" to textFrame(0, "T"))) +
            ByteArray(6) + // 标签与第一帧之间常见的填充
            mpegFrame(mpeg1 = true, bitrateIndex = 9, sampleIndex = 0, mono = false) +
            ByteArray(200)
        val tags = AudioHeadTags.parse(head, fileSize = 3_200_030L)
        assertEquals("T", tags.title)
        // 128 kbps 是从 ID3 之后那一帧的头里读的，读错偏移就会拿到完全不同的数
        assertEquals(128, tags.bitrateKbps)
        assertTrue(tags.durationMs > 0L)
    }

    // ---- MP3 时长 ----

    @Test
    fun xingFrameCountIsTheVbrTruth() {
        val head = mpegFrame(mpeg1 = true, bitrateIndex = 9, sampleIndex = 0, mono = false) +
            ByteArray(32) + // MPEG1 立体声 side info
            xing(flags = 0x01, frames = 1000, bytes = 0) +
            ByteArray(200)
        // 1000 帧 x 1152 采样 / 44100 Hz = 26.122 s
        assertEquals(26_122L, AudioHeadTags.parse(head).durationMs)
    }

    @Test
    fun xingBytesFieldIsUsedWhenFramesAreAbsent() {
        val head = mpegFrame(mpeg1 = true, bitrateIndex = 9, sampleIndex = 0, mono = false) +
            ByteArray(32) + xing(flags = 0x02, frames = 0, bytes = 3_200_000) + ByteArray(8)
        // 3 200 000 字节 @128kbps = 200 s
        assertEquals(200_000L, AudioHeadTags.parse(head).durationMs)
    }

    @Test
    fun xingFieldsAreReadInFlagOrder() {
        // 两个位都置起来时，帧数在 +8、字节数在 +12；优先帧数（VBR 的字节数推不出真实时长）
        val head = mpegFrame(mpeg1 = true, bitrateIndex = 9, sampleIndex = 0, mono = false) +
            ByteArray(32) + xing(flags = 0x03, frames = 1000, bytes = 9_999_999) + ByteArray(8)
        assertEquals(26_122L, AudioHeadTags.parse(head).durationMs)
    }

    @Test
    fun cbrDurationSubtractsTheTagBytes() {
        val head = mpegFrame(mpeg1 = true, bitrateIndex = 9, sampleIndex = 0, mono = false) + ByteArray(64)
        // 裸帧（无 ID3）：整个 fileSize 都是音频
        assertEquals(200_000L, AudioHeadTags.parse(head, fileSize = 3_200_000L).durationMs)
    }

    @Test
    fun monoSideInfoIsShorterThanStereo() {
        // 单声道的 Xing 在 4+17 处，立体声在 4+32 处；写死一个偏移就会在单声道文件上解不出时长
        val mono = mpegFrame(mpeg1 = true, bitrateIndex = 9, sampleIndex = 0, mono = true) +
            ByteArray(17) + xing(flags = 0x01, frames = 1000, bytes = 0) + ByteArray(8)
        assertEquals(26_122L, AudioHeadTags.parse(mono).durationMs)
        val stereo = mpegFrame(mpeg1 = true, bitrateIndex = 9, sampleIndex = 0, mono = false) +
            ByteArray(32) + xing(flags = 0x01, frames = 1000, bytes = 0) + ByteArray(8)
        assertEquals(26_122L, AudioHeadTags.parse(stereo).durationMs)
    }

    @Test
    fun mpeg25SampleRatesScaleTheDuration() {
        // MPEG2.5 Layer III：576 采样/帧、8000 Hz
        val head = mpegFrame(mpeg1 = false, versionBits = 0, bitrateIndex = 4, sampleIndex = 2, mono = false) +
            ByteArray(17) + xing(flags = 0x01, frames = 100, bytes = 0) + ByteArray(8)
        val tags = AudioHeadTags.parse(head)
        assertEquals(100L * 576 * 1000L / 8000L, tags.durationMs)
    }

    @Test
    fun nonLayerThreeHeadersAreRejected() {
        val layerTwo = byteArrayOf(0xFF.toByte(), (0xE0 or (0x03 shl 3) or (0x02 shl 1) or 1).toByte(), (9 shl 4).toByte(), 0)
        assertEquals(0L, AudioHeadTags.parse(layerTwo + ByteArray(64), fileSize = 3_200_000L).durationMs)
        val reservedVersion = byteArrayOf(0xFF.toByte(), (0xE0 or (0x01 shl 3) or (0x01 shl 1) or 1).toByte(), (9 shl 4).toByte(), 0)
        assertEquals(0L, AudioHeadTags.parse(reservedVersion + ByteArray(64), fileSize = 3_200_000L).durationMs)
        val freeBitrate = byteArrayOf(0xFF.toByte(), (0xE0 or (0x03 shl 3) or (0x01 shl 1) or 1).toByte(), 0.toByte(), 0.toByte())
        assertEquals(0L, AudioHeadTags.parse(freeBitrate + ByteArray(64), fileSize = 3_200_000L).durationMs)
    }

    // ---- FLAC / OGG ----

    @Test
    fun flacCombinesStreaminfoDurationAndVorbisFields() {
        val comment = TagFixtures.vorbisComment(
            listOf(
                "TITLE" to "天空之城",
                "ARTIST" to "久石譲",
                "ALBUM" to "天空の城ラピュタ",
                "GENRE" to "Soundtrack",
                "DATE" to "1986",
                "TRACKNUMBER" to "1/37",
            ),
        )
        val streaminfo = streaminfo(sampleRate = 44_100, totalSamples = 44_100L * 180L)
        val head = flacWith(streaminfo, comment)
        val tags = AudioHeadTags.parse(head)
        assertEquals(180_000L, tags.durationMs)
        assertEquals("天空之城", tags.title)
        assertEquals("久石譲", tags.artist)
        assertEquals("天空の城ラピュタ", tags.album)
        assertEquals("Soundtrack", tags.genre)
        assertEquals(1986, tags.year)
        assertEquals(1, tags.trackNumber)
    }

    @Test
    fun flacWithoutStreaminfoStillGivesFields() {
        val comment = TagFixtures.vorbisComment(listOf("ARTIST" to "Only"))
        val tags = AudioHeadTags.parse(TagFixtures.flac(comment))
        assertEquals("Only", tags.artist)
        assertEquals(0L, tags.durationMs)
    }

    @Test
    fun oggHasFieldsButNoDurationInAHeadWindow() {
        val comment = TagFixtures.vorbisComment(listOf("TITLE" to "Ogg Track", "ARTIST" to "Singer"))
        val tags = AudioHeadTags.parse(TagFixtures.ogg(comment))
        assertEquals("Ogg Track", tags.title)
        assertEquals("Singer", tags.artist)
        // 时长在最后一页的 granule 里，头部窗口拿不到：留 0，让这首歌继续留在待补名单
        assertEquals(0L, tags.durationMs)
    }

    @Test
    fun garbageNeverThrowsAndNeverInventsValues() {
        val tags = AudioHeadTags.parse("not audio at all".toByteArray(StandardCharsets.UTF_8), fileSize = 5_000_000L)
        assertEquals(AudioTags(), tags)
        assertEquals(AudioTags(), AudioHeadTags.parse(ByteArray(2)))
    }

    /** ID3v2 文本帧值 = 编码字节 + 正文（多值时测试自己再拼终止符） */
    private fun textFrame(encoding: Int, text: String): ByteArray =
        byteArrayOf(encoding.toByte()) + TagFixtures.encode(text, encoding)

    /** MPEG 帧头 4 字节：b1 = 111 V V L L P，b2 = B B B B S S P X，b3 = M M … */
    private fun mpegFrame(
        mpeg1: Boolean,
        bitrateIndex: Int = 9,
        sampleIndex: Int = 0,
        mono: Boolean,
        versionBits: Int = if (mpeg1) 0x03 else 0x02,
    ): ByteArray {
        val b1 = 0xE0 or (versionBits shl 3) or (0x01 shl 1) or 1
        val b2 = (bitrateIndex shl 4) or (sampleIndex shl 2)
        val b3 = (if (mono) 0x03 else 0x00) shl 6
        return byteArrayOf(0xFF.toByte(), b1.toByte(), b2.toByte(), b3.toByte())
    }

    /** Xing/Info：魔数 + flags + 可选的帧数与字节数 */
    private fun xing(flags: Int, frames: Int, bytes: Int): ByteArray {
        val out = ByteArrayOutputStream()
        out.write("Xing".toByteArray(StandardCharsets.ISO_8859_1))
        out.write(TagFixtures.be32(flags))
        if (flags and 0x01 != 0) out.write(TagFixtures.be32(frames))
        if (flags and 0x02 != 0) out.write(TagFixtures.be32(bytes))
        return out.toByteArray()
    }

    /** FLAC STREAMINFO 的 34 字节：采样率在 bit 80..99、总样本数在 bit 108..143 */
    private fun streaminfo(sampleRate: Int, totalSamples: Long, channels: Int = 2, bitsPerSample: Int = 16): ByteArray {
        val bytes = ByteArray(34)
        fun put(bitOffset: Int, bitCount: Int, value: Long) {
            for (i in 0 until bitCount) {
                if ((value shr (bitCount - 1 - i)) and 1L == 1L) {
                    val index = (bitOffset + i) / 8
                    bytes[index] = (bytes[index].toInt() or (1 shl (7 - (bitOffset + i) % 8))).toByte()
                }
            }
        }
        put(80, 20, sampleRate.toLong())
        put(100, 3, (channels - 1).toLong())
        put(103, 5, (bitsPerSample - 1).toLong())
        put(108, 36, totalSamples)
        return bytes
    }

    private fun flacWith(streaminfo: ByteArray, comment: ByteArray): ByteArray {
        val out = ByteArrayOutputStream()
        out.write("fLaC".toByteArray(StandardCharsets.ISO_8859_1))
        out.write(byteArrayOf(0, 0, 0, streaminfo.size.toByte())) // type 0，不是最后一块
        out.write(streaminfo)
        out.write(byteArrayOf(0x84.toByte(), (comment.size shr 16).toByte(), (comment.size shr 8).toByte(), comment.size.toByte()))
        out.write(comment)
        return out.toByteArray()
    }
}
