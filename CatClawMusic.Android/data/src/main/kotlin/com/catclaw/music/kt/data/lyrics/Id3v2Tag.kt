package com.catclaw.music.kt.data.lyrics

import java.nio.charset.StandardCharsets
import com.catclaw.music.kt.data.lyrics.TagBytes.be32
import com.catclaw.music.kt.data.lyrics.TagBytes.indexOfAscii
import com.catclaw.music.kt.data.lyrics.TagBytes.synchsafe

/** ID3v2.2/2.3/2.4：按 C# 的取法只认 USLT，取第一个非空 */
internal object Id3v2Tag {

    fun lyrics(bytes: ByteArray): String? {
        if (bytes.size < 10) return null
        val version = bytes[3].toInt()
        if (version < 2 || version > 4) return null
        val flags = bytes[5].toInt()
        val tagEnd = (10 + synchsafe(bytes, 6)).coerceAtMost(bytes.size)
        var cursor = 10
        if (version >= 3 && flags and 0x40 != 0) cursor += extendedHeaderSize(bytes, cursor, version)
        val idSize = if (version == 2) 3 else 4
        val sizeField = if (version == 2) 3 else 4
        val unsyncTag = flags and 0x80 != 0
        val found = ArrayList<String>()

        while (cursor + idSize + sizeField <= tagEnd) {
            val id = String(bytes, cursor, idSize, StandardCharsets.ISO_8859_1)
            if (id.isBlank() || id.contains(TagBytes.NUL)) break
            val size = when {
                version == 2 -> ((bytes[cursor + 3].toInt() and 0xFF) shl 16) or
                    ((bytes[cursor + 4].toInt() and 0xFF) shl 8) or (bytes[cursor + 5].toInt() and 0xFF)
                version == 4 -> synchsafe(bytes, cursor + 4)
                else -> be32(bytes, cursor + 4)
            }
            if (size <= 0 || cursor + idSize + sizeField + size > bytes.size) break
            val headerEnd = cursor + idSize + sizeField + (if (version == 2) 0 else 2)
            val raw = bytes.copyOfRange(headerEnd, headerEnd + size)
            val frameFlags = if (version >= 3) TagBytes.be16(bytes, cursor + idSize + sizeField) else 0
            val value = if (unsyncTag || (version == 4 && frameFlags and 0x0002 != 0)) desync(raw) else raw
            if (id == "USLT" || id == "ULT") usltText(value)?.let { found += it }
            cursor = headerEnd + size
        }
        return found.firstOrNull { it.isNotBlank() }
    }

    /** USLT 值 = 编码字节 + 语言(3) + 内容描述(按编码空终止) + 歌词正文 */
    private fun usltText(value: ByteArray): String? {
        if (value.size < 5) return null
        val encoding = value[0].toInt() and 0xFF
        var i = 4
        if (encoding == 1 || encoding == 2) {
            while (i + 1 < value.size && !(value[i] == 0.toByte() && value[i + 1] == 0.toByte())) i += 2
            i += 2
        } else {
            while (i < value.size && value[i] != 0.toByte()) i++
            i += 1 // 跳过描述符的终止 NUL
        }
        if (i >= value.size) return null
        return TagBytes.id3Text(value, i, value.size, encoding).takeIf { it.isNotEmpty() }
    }

    /** 扩展头长度：v2.3 是「含自身的 4 字节大端」，v2.4 是 synchsafe 且不含自身 */
    private fun extendedHeaderSize(bytes: ByteArray, at: Int, version: Int): Int =
        if (version == 3) be32(bytes, at) + 6 else synchsafe(bytes, at)

    /** 去 unsynchronisation：0xFF 0x00 → 0xFF */
    private fun desync(raw: ByteArray): ByteArray {
        val out = java.io.ByteArrayOutputStream(raw.size)
        var i = 0
        while (i < raw.size) {
            out.write(raw[i].toInt())
            if (raw[i] == 0xFF.toByte() && i + 1 < raw.size && raw[i + 1] == 0.toByte()) i++
            i++
        }
        return out.toByteArray()
    }
}
