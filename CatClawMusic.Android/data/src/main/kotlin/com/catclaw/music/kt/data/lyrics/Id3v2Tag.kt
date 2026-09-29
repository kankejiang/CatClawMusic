package com.catclaw.music.kt.data.lyrics

import java.nio.charset.StandardCharsets
import com.catclaw.music.kt.data.lyrics.TagBytes.be32
import com.catclaw.music.kt.data.lyrics.TagBytes.synchsafe

/**
 * ID3v2.2/2.3/2.4 的帧表。
 *
 * 歌词那一支按 C# 的取法只认 USLT；文本帧（TIT2/TPE1/…）是给远程补元数据用的，
 * 两者共用同一个走帧循环 —— 三种版本的帧头长度、synchsafe、扩展头、整体去同步
 * 都只在这里写一遍，改一处不会让另一处悄悄漂掉。
 */
internal object Id3v2Tag {

    /** 歌词帧：v2.2 的 3 字节 ID 是 ULT */
    private val LYRIC_IDS = setOf("USLT", "ULT")

    /**
     * 文本帧：4 字节 ID（v2.3/2.4）与 3 字节 ID（v2.2）都收进来，
     * key 用规范里的 4 字节写法，v2.2 的老 ID 在读取时映射过来。
     */
    private val TEXT_FRAME_IDS = setOf("TIT2", "TPE1", "TALB", "TCON", "TYER", "TDRC", "TRCK")
    private val V22_ALIASES = mapOf("TT2" to "TIT2", "TP1" to "TPE1", "TAL" to "TALB", "TCO" to "TCON", "TYE" to "TYER", "TRK" to "TRCK")

    fun lyrics(bytes: ByteArray): String? {
        val found = ArrayList<String>()
        walkFrames(bytes) { id, value ->
            if (id in LYRIC_IDS) usltText(value)?.let { found += it }
        }
        return found.firstOrNull { it.isNotBlank() }
    }

    /** 解出所有文本帧，键是规范里的 4 字节 ID；重复帧保留第一个 */
    fun textFrames(bytes: ByteArray): Map<String, String> {
        val out = LinkedHashMap<String, String>()
        walkFrames(bytes) { id, value ->
            val canonical = V22_ALIASES[id] ?: id
            if (canonical in TEXT_FRAME_IDS) {
                val text = textValue(value)
                if (text.isNotEmpty()) out.putIfAbsent(canonical, text)
            }
        }
        return out
    }

    /**
     * 走一遍帧表。参数是 (帧 ID, 已去同步的帧值)。
     * 损坏（长度越界、ID 含 NUL、版本不认识）一律停在当前位置，不抛。
     */
    private fun walkFrames(bytes: ByteArray, onFrame: (id: String, value: ByteArray) -> Unit) {
        if (bytes.size < 10) return
        val version = bytes[3].toInt()
        if (version < 2 || version > 4) return
        val flags = bytes[5].toInt()
        val tagEnd = (10 + synchsafe(bytes, 6)).coerceAtMost(bytes.size)
        var cursor = 10
        if (version >= 3 && flags and 0x40 != 0) cursor += extendedHeaderSize(bytes, cursor, version)
        val idSize = if (version == 2) 3 else 4
        val sizeField = if (version == 2) 3 else 4
        val unsyncTag = flags and 0x80 != 0

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
            onFrame(id, value)
            cursor = headerEnd + size
        }
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

    /**
     * 文本帧值 = 编码字节 + 正文。v2.4 允许一帧里塞多个值（用终止符隔开），取第一个非空的；
     * 解成 String 之后，UTF-16 的终止符也只是一个 [TagBytes.NUL] 字符，所以两种编码同一句切法。
     * ID3v1 的数字流派形式 `(17)` 原样留着 —— 那是另一张 148 项的表，本端和 C# 都没查。
     */
    private fun textValue(value: ByteArray): String {
        if (value.isEmpty()) return ""
        val encoding = value[0].toInt() and 0xFF
        val text = TagBytes.id3Text(value, 1, value.size, encoding)
        return text.split(TagBytes.NUL).firstOrNull { it.isNotBlank() }.orEmpty().trim()
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
            if (raw[i] == 0xFF.toByte() && i + 1 < raw.size && raw[i + 1] == 0x00.toByte()) i++
            i++
        }
        return out.toByteArray()
    }

    /** 标签在文件里占的字节数（含 10 字节头）；解完标签要从这里继续找第一帧音频 */
    fun tagSize(bytes: ByteArray): Int {
        if (!TagBytes.startsWith(bytes, "ID3") || bytes.size < 10) return 0
        return (10 + synchsafe(bytes, 6)).coerceAtMost(bytes.size)
    }
}
