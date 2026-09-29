package com.catclaw.music.kt.data.media

import com.catclaw.music.kt.core.library.AudioTags
import com.catclaw.music.kt.data.lyrics.Id3v2Tag
import com.catclaw.music.kt.data.lyrics.OggTag
import com.catclaw.music.kt.data.lyrics.TagBytes
import com.catclaw.music.kt.data.lyrics.VorbisTag
import java.nio.charset.StandardCharsets
import java.util.regex.Pattern

/**
 * 从「远程文件头部字节」解出标签与时长 —— 远程补元数据的解析层。
 *
 * 为什么不是直接读整首歌：WebDAV/SMB 上一次目录只拿得到文件名与大小，
 * 艺术家、专辑、时长都在文件里。C# 侧靠 TagLib 读头部（256 KB，FLAC 这类再试 2 MB），
 * 安卓没有对位的纯 Java 库，所以按容器自己解 —— 与内嵌歌词那套（[AudioTagLyrics]）同一个思路，
 * 只是这里要的不止歌词。
 *
 * 覆盖范围按「NAS 上远程库的常见格式」定：MP3（ID3v2 + Xing/CBR）、FLAC（Vorbis 注释 + STREAMINFO）、
 * OGG（Vorbis 注释；时长要读最后一包的 granule，头部窗口里没有 ⇒ 留 0）。
 * M4A/AAC 的标签与时长都在 moov 里，非 faststart 的文件还得再 Range 一次文件尾 ——
 * 这一支本端还没接，解不出就一切留空，那首歌会继续留在待补名单里，不会被写坏。
 *
 * 任何一段解坏都只让那个字段留空，不抛异常：这是一次批量回填，一首坏文件不该拖垮整轮。
 */
object AudioHeadTags {

    fun parse(head: ByteArray, fileSize: Long = 0L): AudioTags {
        if (head.size < 4) return AudioTags()
        return when {
            TagBytes.startsWith(head, "ID3") -> fromId3(head, fileSize)
            TagBytes.startsWith(head, "fLaC") -> fromFlac(head)
            TagBytes.startsWith(head, "OggS") -> fromOgg(head)
            else -> fromBareMpeg(head, 0, fileSize)
        }
    }

    /** ID3v2 打在 MP3 上最常见，所以解完文本帧还要接着找第一帧音频拿时长与码率 */
    private fun fromId3(head: ByteArray, fileSize: Long): AudioTags {
        val frames = Id3v2Tag.textFrames(head)
        val base = fromBareMpeg(head, Id3v2Tag.tagSize(head), fileSize)
        return base.copy(
            title = frames["TIT2"].orEmpty(),
            artist = frames["TPE1"],
            album = frames["TALB"].orEmpty(),
            genre = frames["TCON"],
            year = fourDigits(frames["TDRC"] ?: frames["TYER"]),
            trackNumber = frames["TRCK"]?.substringBefore('/')?.trim()?.toIntOrNull() ?: 0,
        )
    }

    private fun fromFlac(head: ByteArray): AudioTags {
        var durationMs = 0L
        var fields: Map<String, String> = emptyMap()
        var cursor = 4
        while (cursor + 4 <= head.size) {
            val last = head[cursor].toInt() and 0x80 != 0
            val type = head[cursor].toInt() and 0x7F
            val size = (head[cursor + 1].toInt() and 0xFF) shl 16 or
                ((head[cursor + 2].toInt() and 0xFF) shl 8) or (head[cursor + 3].toInt() and 0xFF)
            cursor += 4
            if (size < 0 || cursor + size > head.size) break
            when (type) {
                // STREAMINFO：采样率在 bit 80..99，总样本数在 bit 108..143（34 字节定长）
                0 -> if (size >= 34) durationMs = flacDurationMs(head, cursor)
                4 -> fields = VorbisTag.fields(head.copyOfRange(cursor, cursor + size))
            }
            if (last) break
            cursor += size
        }
        return AudioTags(
            title = fields["TITLE"].orEmpty(),
            artist = fields["ARTIST"],
            album = fields["ALBUM"].orEmpty(),
            genre = fields["GENRE"],
            durationMs = durationMs,
            trackNumber = fields["TRACKNUMBER"]?.substringBefore('/')?.trim()?.toIntOrNull() ?: 0,
            year = fourDigits(fields["DATE"]),
        )
    }

    private fun fromOgg(head: ByteArray): AudioTags {
        val packet = OggTag.commentPacket(head) ?: return AudioTags()
        val fields = VorbisTag.fields(packet)
        return AudioTags(
            title = fields["TITLE"].orEmpty(),
            artist = fields["ARTIST"],
            album = fields["ALBUM"].orEmpty(),
            genre = fields["GENRE"],
            // 时长要读最后一页的 granule position，头部窗口里没有 ⇒ 保持 0，这仍是要补的行
            durationMs = 0L,
            trackNumber = fields["TRACKNUMBER"]?.substringBefore('/')?.trim()?.toIntOrNull() ?: 0,
            year = fourDigits(fields["DATE"]),
        )
    }

    /** 裸 MP3（没有 ID3v2）或 ID3v2 之后：从第一帧音频拿时长与码率 */
    private fun fromBareMpeg(head: ByteArray, at: Int, fileSize: Long): AudioTags {
        val frame = MpegAudio.findFrame(head, at) ?: return AudioTags()
        return AudioTags(
            durationMs = MpegAudio.durationMs(head, frame, fileSize, at),
            bitrateKbps = frame.bitrateKbps,
        )
    }

    /** 年份在三种标签里写法都不同（`2005`、`2005-11-01`、`(2005)`），统一取第一段 4 位数字 */
    private val YEAR = Pattern.compile("(\\d{4})")

    private fun fourDigits(raw: String?): Int {
        if (raw.isNullOrBlank()) return 0
        val matcher = YEAR.matcher(raw.trim())
        return if (matcher.find()) matcher.group(1)?.toIntOrNull() ?: 0 else 0
    }

    private fun flacDurationMs(head: ByteArray, blockAt: Int): Long {
        val sampleRate = readBits(head, blockAt, 80, 20)
        val totalSamples = readBits(head, blockAt, 108, 36)
        if (sampleRate <= 0L || totalSamples <= 0L) return 0L
        return totalSamples * 1000L / sampleRate
    }

    /** 大端按位读：FLAC 的 STREAMINFO 把字段横跨字节边界存放 */
    private fun readBits(bytes: ByteArray, blockAt: Int, bitOffset: Int, bitCount: Int): Long {
        var value = 0L
        for (i in 0 until bitCount) {
            val bit = bitOffset + i
            val byteIndex = blockAt + (bit shr 3)
            if (byteIndex >= bytes.size) return 0L
            val bitValue = (bytes[byteIndex].toLong() shr (7 - (bit and 7))) and 1L
            value = (value shl 1) or bitValue
        }
        return value
    }

    /**
     * MPEG Layer-III 帧头与 Xing/CBR 时长。
     *
     * 表都是规范里的固定值（ISO 11172-3 / 13818-3），写错一位就是时长与码率一起偏，
     * 所以每条都有对应的字节级测试。
     */
    private object MpegAudio {

        data class Frame(
            val at: Int,
            val mpeg1: Boolean,
            val mono: Boolean,
            val bitrateKbps: Int,
            val sampleRate: Int,
        ) {
            val samplesPerFrame: Int get() = if (mpeg1) 1152 else 576
        }

        private val BITRATE_V1_L3 = intArrayOf(0, 32, 40, 48, 56, 64, 80, 96, 112, 128, 160, 192, 224, 256, 320, -1)
        private val BITRATE_V2_L3 = intArrayOf(0, 8, 16, 24, 32, 40, 48, 56, 64, 80, 96, 112, 128, 144, 160, -1)
        private val SAMPLE_RATE = arrayOf(intArrayOf(44100, 48000, 32000), intArrayOf(22050, 24000, 16000), intArrayOf(11025, 12000, 8000))

        /** 从 at 起最多扫 64 KB 找一个合法帧头；找不到回 null（不是 MP3 或头太短） */
        fun findFrame(head: ByteArray, at: Int): Frame? {
            val limit = minOf(head.size - 4, at + 64 * 1024)
            var i = maxOf(0, at)
            while (i < limit) {
                val frame = frameAt(head, i)
                if (frame != null) return frame
                i++
            }
            return null
        }

        private fun frameAt(head: ByteArray, i: Int): Frame? {
            if (i + 4 > head.size) return null
            val b0 = head[i].toInt() and 0xFF
            val b1 = head[i + 1].toInt() and 0xFF
            val b2 = head[i + 2].toInt() and 0xFF
            val b3 = head[i + 3].toInt() and 0xFF
            if (b0 != 0xFF || b1 and 0xE0 != 0xE0) return null
            // 11 位同步之后依次是：版本 2 位、层 2 位、CRC 1 位 —— 别按整字节切，
            // 这条写错过一次，表现是所有 MP3 都解不出时长
            val versionBits = (b1 shr 3) and 0x03
            if (versionBits == 0x01) return null // 保留值
            val mpeg1 = versionBits == 0x03
            if (((b1 shr 1) and 0x03) != 0x01) return null // 只要 Layer III
            val bitrateIndex = (b2 shr 4) and 0x0F
            val sampleIndex = (b2 shr 2) and 0x03
            if (bitrateIndex == 0x0F || bitrateIndex == 0 || sampleIndex == 0x03) return null
            val rateTable = if (mpeg1) BITRATE_V1_L3 else BITRATE_V2_L3
            val kbps = rateTable[bitrateIndex]
            if (kbps <= 0) return null
            val rates = SAMPLE_RATE[if (mpeg1) 0 else if (versionBits == 0x02) 1 else 2]
            return Frame(
                at = i,
                mpeg1 = mpeg1,
                mono = ((b3 shr 6) and 0x03) == 0x03,
                bitrateKbps = kbps,
                sampleRate = rates[sampleIndex],
            )
        }

        /**
         * 时长优先看 Xing/Info（VBR 的唯一正解），其次用文件字节数按码率推（CBR）。
         * 两者都不成立就回 0 —— 0 会让这首歌继续留在待补名单里，比写一个假时长好。
         *
         * Xing 的字段是**按 flags 依次排列**的：只置了 bytes 位时它就在第 8 字节。
         * 写死偏移会在「只有字节数」的文件上读到 0，这条被测试抓出来过。
         */
        fun durationMs(head: ByteArray, frame: Frame, fileSize: Long, audioBytesStart: Int): Long {
            val xing = xingAt(head, frame)
            if (xing >= 0) {
                val flags = TagBytes.be32(head, xing + 4)
                var cursor = xing + 8
                var frames = -1L
                var bytesField = -1L
                if (flags and 0x01 != 0 && cursor + 4 <= head.size) {
                    frames = TagBytes.be32(head, cursor).toLong()
                    cursor += 4
                }
                if (flags and 0x02 != 0 && cursor + 4 <= head.size) {
                    bytesField = TagBytes.be32(head, cursor).toLong()
                }
                if (frames > 0) return frames * frame.samplesPerFrame * 1000L / frame.sampleRate
                if (bytesField > 0) return millisFromBytes(bytesField, frame)
            }
            if (fileSize <= 0) return 0L
            val audioBytes = (fileSize - maxOf(frame.at, audioBytesStart)).coerceAtLeast(0L)
            if (audioBytes <= 0) return 0L
            return millisFromBytes(audioBytes, frame)
        }

        /** Xing/Info 的位置 = 帧头 4 字节 + side information（版本与声道数决定长度） */
        private fun xingAt(head: ByteArray, frame: Frame): Int {
            val sideInfo = if (frame.mpeg1) { if (frame.mono) 17 else 32 } else { if (frame.mono) 9 else 17 }
            val at = frame.at + 4 + sideInfo
            if (at + 8 > head.size) return -1
            val marker = String(head, at, 4, StandardCharsets.ISO_8859_1)
            return if (marker == "Xing" || marker == "Info") at else -1
        }

        private fun millisFromBytes(bytes: Long, frame: Frame): Long =
            bytes * 8L / frame.bitrateKbps // kbps 的 k 是 1000，与 bit/s 相除正好抵掉
    }
}
