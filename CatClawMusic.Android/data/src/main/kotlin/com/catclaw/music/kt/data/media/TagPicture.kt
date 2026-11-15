package com.catclaw.music.kt.data.media

import com.catclaw.music.kt.data.lyrics.OggTag
import com.catclaw.music.kt.data.lyrics.TagBytes
import java.nio.charset.StandardCharsets
import java.util.Base64

/**
 * 从音频文件字节里抽出**内嵌封面**。
 *
 * 为什么需要它：远程曲库（WebDAV / SMB）入库时只有目录清单 —— 文件名、大小、路径。
 * 封面既不在目录里，也没有侧车图（实测某台 NAS 的 1227 个文件里图片扩展名为 0），
 * 但**音频文件自己带着图**：实测 1682×1682 / 598 KB 的 JPEG 就在 FLAC 的 `PICTURE` 块里，
 * MP3 的 `APIC` 帧里也有 63–229 KB 的图。本端此前只解文本标签，于是远程曲永远显示首字母 ——
 * 数据一直就在文件里，只是没人读。
 *
 * 覆盖四种常见容器：
 * - **ID3v2**（MP3）：`APIC`（v2.3/2.4）/ `PIC`（v2.2）
 * - **FLAC**：type=6 `PICTURE` 块
 * - **Ogg**：Vorbis 注释里的 `METADATA_BLOCK_PICTURE`（FLAC PICTURE 块的 base64）
 * - **MP4/M4A**：`moov/udta/meta/ilst` 里的 `covr`
 *
 * ## 两段式取值
 *
 * 图片动辄几百 KB，而标签窗口只有 256 KB / 2 MB。所以分两步：
 * 1. [locate] 只吃头部窗口，算出图片在文件里的**绝对偏移与长度**；
 * 2. 若窗口已覆盖 → [extract] 直接切片；否则调用方按 `[offset, offset+length)` 精确补一次 Range，
 *    再交给 [extract]（它接受 head 与紧随其后的 tail 两段）。
 *
 * Ogg 是例外：图以 base64 存在注释包（文件头部）里，`locate` 拿不到文件偏移，
 * 但 `extract` 能从头部窗口直接解出来。MP4 的 `moov` 可能在文件尾，交由 [extractFromMp4Tail]。
 *
 * 任何一段解坏都只返回 null，不抛 —— 这是一次批量回填，一张坏图不该拖垮整轮。
 */
object TagPicture {

    /** 一张内嵌图：MIME 与原始字节（JPEG/PNG，原样写盘即可） */
    class Picture(val mime: String, val bytes: ByteArray) {
        /** 落盘用的扩展名；不认识的 MIME 一律 .img */
        val extension: String
            get() = when (mime.lowercase()) {
                "image/jpeg", "image/jpg" -> "jpg"
                "image/png" -> "png"
                "image/webp" -> "webp"
                "image/gif" -> "gif"
                "image/bmp" -> "bmp"
                else -> "img"
            }
    }

    /** 图片在文件里的绝对区间与类型 */
    class Location(val offset: Long, val length: Long, val mime: String) {
        val end: Long get() = offset + length
    }

    /**
     * 单张封面的上限。超过就不取 —— 12 MB 的图对列表缩略图没有任何意义，
     * 而自动补全时 4 个 worker 各持一份，很容易把进程堆撑爆。
     */
    const val MAX_COVER_BYTES = 12 * 1024 * 1024

    /**
     * 定位用的头部窗口。
     *
     * 256 KB 是实测下的平衡点：FLAC 的 PICTURE 块头落在前 8 KB（VORBIS_COMMENT 之后就轮到它），
     * MP3 的 ID3v2 整表常在 60–235 KB，两者都能在这个窗口里定位到。
     * 极端情况（图前有巨大 APIC 之外的填充）走 Ogg 那条 `extract(head)` 兜底。
     */
    const val HEAD_WINDOW = 256 * 1024

    /** 小于这个长度的"图"几乎肯定是解错了（1×1 占位图也有几百字节，但 0 字节显然是坏的） */
    private const val MIN_COVER_BYTES = 64

    /**
     * 只吃头部窗口，定位图片的绝对区间。
     *
     * @return null = 这个文件没有内嵌图，或格式不支持用 Range 补取（Ogg 走 [extract]）
     */
    fun locate(head: ByteArray): Location? = when {
        TagBytes.startsWith(head, "ID3") -> id3Location(head)
        TagBytes.startsWith(head, "fLaC") -> flacLocation(head)
        Mp4Window.looksLikeMp4(head) -> mp4Location(head)
        else -> null // Ogg 与裸 MPEG 没有可 Range 的图
    }

    /**
     * 从字节里解出图片。`tail` 是紧接 `head` 之后的一段（无补取时传空）。
     *
     * 这个入口对四种容器都能用，只要提供的字节覆盖了整张图。
     */
    fun extract(head: ByteArray, tail: ByteArray = ByteArray(0)): Picture? {
        if (head.isEmpty()) return null
        return when {
            TagBytes.startsWith(head, "ID3") -> id3Picture(joinIfNeeded(head, tail))
            TagBytes.startsWith(head, "fLaC") -> flacPicture(joinIfNeeded(head, tail))
            TagBytes.startsWith(head, "OggS") -> oggPicture(head)
            Mp4Window.looksLikeMp4(head) -> mp4Picture(head)
            else -> null
        }
    }

    /**
     * MP4 的 `moov` 在文件尾时（非 faststart 的 M4A），封面只能用尾部窗口解。
     * 与 [AudioHeadTags.parseTail] 同一个理由、同一段窗口。
     */
    fun extractFromMp4Tail(tail: ByteArray): Picture? = mp4PictureFromTail(tail)

    private fun joinIfNeeded(head: ByteArray, tail: ByteArray): ByteArray =
        if (tail.isEmpty()) head else head + tail

    private fun valid(bytes: ByteArray): Boolean = bytes.size in MIN_COVER_BYTES..MAX_COVER_BYTES

    // ── ID3v2：APIC / PIC ──────────────────────────────────────────────────

    private class Id3Frame(val id: String, val bodyAt: Int, val size: Int)

    /** 走一遍帧表。损坏（长度越界、ID 含 NUL、版本不认识）一律停在当前位置。 */
    private fun id3Frames(bytes: ByteArray): List<Id3Frame> {
        if (bytes.size < 10) return emptyList()
        val version = bytes[3].toInt()
        if (version < 2 || version > 4) return emptyList()
        val idSize = if (version == 2) 3 else 4
        val sizeField = if (version == 2) 3 else 4
        val frameHeader = idSize + sizeField + (if (version == 2) 0 else 2)
        val tagEnd = (10 + TagBytes.synchsafe(bytes, 6)).coerceAtMost(bytes.size)

        var cursor = 10
        if (version >= 3 && bytes[5].toInt() and 0x40 != 0) {
            cursor += if (version == 3) TagBytes.be32(bytes, cursor) + 6 else TagBytes.synchsafe(bytes, cursor)
        }
        val out = ArrayList<Id3Frame>()
        while (cursor + frameHeader <= tagEnd) {
            val id = String(bytes, cursor, idSize, StandardCharsets.ISO_8859_1)
            if (id.isBlank() || id.contains(TagBytes.NUL)) break
            val size = when {
                version == 2 -> ((bytes[cursor + 3].toInt() and 0xFF) shl 16) or
                    ((bytes[cursor + 4].toInt() and 0xFF) shl 8) or (bytes[cursor + 5].toInt() and 0xFF)
                version == 4 -> TagBytes.synchsafe(bytes, cursor + 4)
                else -> TagBytes.be32(bytes, cursor + 4)
            }
            if (size <= 0) break
            val bodyAt = cursor + frameHeader
            out += Id3Frame(if (version == 2) V22[id] ?: id else id, bodyAt, size)
            cursor = bodyAt + size
        }
        return out
    }

    /** v2.2 的 3 字节帧 ID → 规范 4 字节写法 */
    private val V22 = mapOf("PIC" to "APIC")

    private fun id3Location(head: ByteArray): Location? {
        val frame = id3Frames(head).firstOrNull { it.id == "APIC" } ?: return null
        val info = id3PictureInfo(head, frame) ?: return null
        // 帧体必须完整落在标签里，否则说明帧表本身坏了
        if (frame.bodyAt + frame.size > head.size) {
            // 图还在后面：给出真实区间，让调用方补取
            return Location(info.dataOffset.toLong(), info.dataLength.toLong(), info.mime)
        }
        return Location(info.dataOffset.toLong(), info.dataLength.toLong(), info.mime)
    }

    private class PicInfo(val mime: String, val dataOffset: Int, val dataLength: Int)

    /** APIC 体：编码(1) + MIME(ascii NUL) + 图片类型(1) + 描述(按编码 NUL) + 数据 */
    private fun id3PictureInfo(bytes: ByteArray, frame: Id3Frame): PicInfo? {
        val bodyAt = frame.bodyAt
        val limit = minOf(bodyAt + frame.size, bytes.size)
        if (bodyAt + 2 > limit) return null
        val version = bytes[3].toInt()
        val encoding = bytes[bodyAt].toInt() and 0xFF

        var p: Int
        val mime: String
        if (version == 2) {
            if (bodyAt + 5 > limit) return null
            val fmt = String(bytes, bodyAt + 1, 3, StandardCharsets.ISO_8859_1).uppercase()
            mime = when (fmt) {
                "JPG" -> "image/jpeg"
                "PNG" -> "image/png"
                else -> return null
            }
            p = bodyAt + 5
        } else {
            var i = bodyAt + 1
            while (i < limit && bytes[i] != 0.toByte()) i++
            if (i >= limit) return null
            mime = String(bytes, bodyAt + 1, i - bodyAt - 1, StandardCharsets.ISO_8859_1)
                .trim()
                .ifEmpty { return null }
                .let { if (it.equals("image/jpg", true)) "image/jpeg" else it }
            p = i + 1 + 1 // 跳过 MIME 的 NUL 与图片类型字节
        }
        if (p > limit) return null
        // 跳过描述串
        if (encoding == 1 || encoding == 2) {
            while (p + 1 < limit && !(bytes[p] == 0.toByte() && bytes[p + 1] == 0.toByte())) p += 2
            p += 2
        } else {
            while (p < limit && bytes[p] != 0.toByte()) p++
            p += 1
        }
        if (p >= limit) return null
        val dataLength = bodyAt + frame.size - p
        if (dataLength <= 0 || dataLength > MAX_COVER_BYTES) return null
        return PicInfo(mime, p, dataLength)
    }

    private fun id3Picture(bytes: ByteArray): Picture? {
        val frame = id3Frames(bytes).firstOrNull { it.id == "APIC" } ?: return null
        val info = id3PictureInfo(bytes, frame) ?: return null
        val end = info.dataOffset + info.dataLength
        if (end > bytes.size) return null // 窗口没覆盖到图尾
        val data = bytes.copyOfRange(info.dataOffset, end)
        return if (valid(data)) Picture(info.mime, data) else null
    }

    // ── FLAC：PICTURE 块（type=6） ────────────────────────────────────────

    private class FlacBlock(val type: Int, val bodyAt: Int, val size: Int, val truncated: Boolean)

    private fun flacBlocks(bytes: ByteArray): List<FlacBlock> {
        val out = ArrayList<FlacBlock>()
        var cursor = 4
        while (cursor + 4 <= bytes.size) {
            val header = bytes[cursor].toInt() and 0xFF
            val last = header and 0x80 != 0
            val type = header and 0x7F
            val size = ((bytes[cursor + 1].toInt() and 0xFF) shl 16) or
                ((bytes[cursor + 2].toInt() and 0xFF) shl 8) or (bytes[cursor + 3].toInt() and 0xFF)
            cursor += 4
            val truncated = cursor + size > bytes.size
            out += FlacBlock(type, cursor, size, truncated)
            if (last || truncated) break
            cursor += size
        }
        return out
    }

    /** PICTURE 体：类型(4) + MIMElLen(4) + MIME + 描述长(4) + 描述 + 宽高深色(16) + 数据长(4) + 数据 */
    private fun flacPictureInfo(bytes: ByteArray, block: FlacBlock): PicInfo? {
        val bodyAt = block.bodyAt
        val limit = bodyAt + block.size
        val available = minOf(limit, bytes.size)
        if (bodyAt + 8 > available) return null
        val mimeLen = TagBytes.be32(bytes, bodyAt + 4)
        if (mimeLen <= 0 || mimeLen > 256 || bodyAt + 8 + mimeLen > limit) return null
        val mime = String(bytes, bodyAt + 8, mimeLen, StandardCharsets.ISO_8859_1).trim()
        if (mime.isEmpty()) return null
        var p = bodyAt + 8 + mimeLen
        if (p + 4 > limit) return null
        val descLen = TagBytes.be32(bytes, p)
        if (descLen < 0 || p + 4 + descLen + 16 + 4 > limit) return null
        p += 4 + descLen + 16
        val dataLength = TagBytes.be32(bytes, p)
        p += 4
        if (dataLength <= 0 || dataLength > MAX_COVER_BYTES || p + dataLength > limit) return null
        return PicInfo(mime, p, dataLength)
    }

    private fun flacLocation(bytes: ByteArray): Location? {
        val block = flacBlocks(bytes).firstOrNull { it.type == 6 } ?: return null
        val info = flacPictureInfo(bytes, block) ?: return null
        return Location(info.dataOffset.toLong(), info.dataLength.toLong(), info.mime)
    }

    private fun flacPicture(bytes: ByteArray): Picture? {
        val block = flacBlocks(bytes).firstOrNull { it.type == 6 } ?: return null
        val info = flacPictureInfo(bytes, block) ?: return null
        val end = info.dataOffset + info.dataLength
        if (end > bytes.size) return null
        val data = bytes.copyOfRange(info.dataOffset, end)
        return if (valid(data)) Picture(info.mime, data) else null
    }

    // ── Ogg：METADATA_BLOCK_PICTURE（base64 的 FLAC PICTURE 块体） ────────

    private fun oggPicture(head: ByteArray): Picture? {
        val packet = OggTag.commentPacket(head) ?: return null
        val encoded = vorbisValue(packet, "METADATA_BLOCK_PICTURE") ?: return null
        val raw = runCatching { Base64.getDecoder().decode(encoded.trim()) }.getOrNull() ?: return null
        if (raw.size < 32) return null
        val fake = FlacBlock(6, 0, raw.size, truncated = false)
        val info = flacPictureInfo(raw, fake) ?: return null
        val end = info.dataOffset + info.dataLength
        if (end > raw.size) return null
        val data = raw.copyOfRange(info.dataOffset, end)
        return if (valid(data)) Picture(info.mime, data) else null
    }

    private fun vorbisValue(packet: ByteArray, key: String): String? {
        if (packet.size < 8) return null
        var cursor = TagBytes.le32(packet, 0) + 4
        if (cursor + 4 > packet.size) return null
        val count = TagBytes.le32(packet, cursor)
        cursor += 4
        repeat(count.coerceAtMost(4096)) {
            if (cursor + 4 > packet.size) return null
            val size = TagBytes.le32(packet, cursor)
            cursor += 4
            if (size < 0 || cursor + size > packet.size) return null
            val entry = String(packet, cursor, size, StandardCharsets.UTF_8)
            cursor += size
            val eq = entry.indexOf('=')
            if (eq > 0 && entry.substring(0, eq).uppercase() == key) return entry.substring(eq + 1)
        }
        return null
    }

    // ── MP4/M4A：moov/udta/meta/ilst/covr ────────────────────────────────

    /**
     * `covr` 的 `data` atom 头是 size(4) + "data"(4) + 类型标志(4) + 保留(4)，
     * 类型标志低 24 位 13 = JPEG、14 = PNG。图从第 16 字节开始。
     */
    private fun covrData(bytes: ByteArray, from: Int, to: Int): Pair<String, IntRange>? {
        var cursor = from
        while (cursor + 8 <= to) {
            val size = TagBytes.be32(bytes, cursor)
            val type = String(bytes, cursor + 4, 4, StandardCharsets.ISO_8859_1)
            if (size < 8 || cursor + size > to) return null
            if (type == "data" && size > 16) {
                val flags = TagBytes.be32(bytes, cursor + 8) and 0x00FF_FFFF
                val mime = when (flags) {
                    13 -> "image/jpeg"
                    14 -> "image/png"
                    27 -> "image/bmp"
                    else -> "image/jpeg" // 多数写入器用 13；未知时按 JPEG 兜底（写盘后仍能解码）
                }
                return mime to (cursor + 16 until cursor + size)
            }
            cursor += size
        }
        return null
    }

    private fun mp4Picture(head: ByteArray): Picture? = mp4PictureIn(head, 0, head.size, 0)

    private fun mp4PictureFromTail(tail: ByteArray): Picture? {
        val at = TagBytes.indexOfAscii(tail, "moov")
        if (at < 4) return null
        val header = at - 4
        val declared = TagBytes.be32(tail, header).toLong() and 0xFFFFFFFFL
        val bodyStart = header + 8
        val bodyEnd = if (declared in 8 until Long.MAX_VALUE) {
            minOf(header + declared, tail.size.toLong()).toInt()
        } else {
            tail.size
        }
        if (bodyStart >= bodyEnd) return null
        return mp4PictureIn(tail, bodyStart, bodyEnd, 1)
    }

    private fun mp4PictureIn(bytes: ByteArray, from: Int, until: Int, depth: Int): Picture? {
        if (depth > 8) return null
        val containers = setOf("moov", "udta", "meta", "ilst", "trak", "mdia", "minf", "stbl")
        var cursor = from
        while (cursor + 8 <= until) {
            var size = TagBytes.be32(bytes, cursor).toLong() and 0xFFFFFFFFL
            val type = String(bytes, cursor + 4, 4, StandardCharsets.ISO_8859_1)
            var bodyAt = 8L
            if (size == 1L) {
                if (cursor + 16 > until) return null
                size = TagBytes.be64(bytes, cursor + 8)
                bodyAt = 16L
            }
            if (size < bodyAt || cursor + size > until) return null
            val bodyStart = (cursor + bodyAt).toInt()
            val bodyEnd = (cursor + size).toInt()
            if (type == "covr") {
                val found = covrData(bytes, bodyStart, bodyEnd) ?: return null
                val data = bytes.copyOfRange(found.second.first, found.second.last + 1)
                return if (valid(data)) Picture(found.first, data) else null
            }
            if (type in containers) {
                val offset = if (type == "meta") 4 else 0
                mp4PictureIn(bytes, bodyStart + offset, bodyEnd, depth + 1)?.let { return it }
            }
            cursor = bodyEnd
        }
        return null
    }

    private fun mp4Location(head: ByteArray): Location? {
        // covr 的绝对偏移需要走一遍 atom 树；这里复用同一套遍历，只是返回区间
        val found = mp4LocationIn(head, 0, head.size, 0) ?: return null
        return Location(found.first.toLong(), found.second.toLong(), found.third)
    }

    private fun mp4LocationIn(bytes: ByteArray, from: Int, until: Int, depth: Int): Triple<Int, Int, String>? {
        if (depth > 8) return null
        val containers = setOf("moov", "udta", "meta", "ilst", "trak", "mdia", "minf", "stbl")
        var cursor = from
        while (cursor + 8 <= until) {
            var size = TagBytes.be32(bytes, cursor).toLong() and 0xFFFFFFFFL
            val type = String(bytes, cursor + 4, 4, StandardCharsets.ISO_8859_1)
            var bodyAt = 8L
            if (size == 1L) {
                if (cursor + 16 > until) return null
                size = TagBytes.be64(bytes, cursor + 8)
                bodyAt = 16L
            }
            if (size < bodyAt || cursor + size > until) return null
            val bodyStart = (cursor + bodyAt).toInt()
            val bodyEnd = (cursor + size).toInt()
            if (type == "covr") {
                val found = covrData(bytes, bodyStart, bodyEnd) ?: return null
                return Triple(found.second.first, found.second.last + 1 - found.second.first, found.first)
            }
            if (type in containers) {
                val offset = if (type == "meta") 4 else 0
                mp4LocationIn(bytes, bodyStart + offset, bodyEnd, depth + 1)?.let { return it }
            }
            cursor = bodyEnd
        }
        return null
    }
}
