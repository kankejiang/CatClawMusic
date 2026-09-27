package com.catclaw.music.kt.data.lyrics

import java.io.RandomAccessFile
import java.nio.charset.StandardCharsets

/** MP4/M4A：只认 C# 用的那个 ©lyr atom（C# 的 M4aMetadataReader 也只做这一个） */
internal object Mp4Tag {

    private val CONTAINERS = setOf("moov", "udta", "ilst", "meta")

    fun looksLikeMp4(head: ByteArray): Boolean =
        TagBytes.indexOfAscii(head, "ftyp", minOf(head.size, 64)) >= 0

    fun lyrics(raf: RandomAccessFile, length: Long): String? {
        val head = TagBytes.readAt(raf, 0, minOf(length, 64).toInt())
        // atom 布局是 [4 字节长度]["ftyp"]，所以魔数在偏移 4
        if (TagBytes.indexOfAscii(head, "ftyp") != 4) return null
        return walk(raf, 0, length, 0)
    }

    private fun walk(raf: RandomAccessFile, from: Long, until: Long, depth: Int): String? {
        if (depth > 6) return null
        var cursor = from
        while (cursor + 8 <= until) {
            val header = TagBytes.readAt(raf, cursor, 8)
            if (header.size < 8) return null
            var size = (TagBytes.be32(header, 0).toLong() and 0xFFFFFFFFL)
            val type = String(header, 4, 4, StandardCharsets.ISO_8859_1)
            var bodyAt = 8L
            if (size == 1L) {
                val wide = TagBytes.readAt(raf, cursor + 8, 8)
                if (wide.size < 8) return null
                size = TagBytes.be64(wide, 0)
                bodyAt = 16L
            }
            if (size < bodyAt || cursor + size > until) return null
            val bodyStart = cursor + bodyAt
            val bodyEnd = cursor + size
            when {
                type in CONTAINERS -> {
                    // meta 头后面还跟着 4 字节 version/flags
                    val offset = if (type == "meta") 4L else 0L
                    walk(raf, bodyStart + offset, bodyEnd, depth + 1)?.let { return it }
                }
                type == "©lyr" || type == "lyr " -> lyricsFromItem(raf, bodyStart, bodyEnd)?.let { return it }
            }
            cursor = bodyEnd
        }
        return null
    }

    /** item 里套一个 data atom：size + "data" + 4 版本/标志 + 4 保留 + 文本 */
    private fun lyricsFromItem(raf: RandomAccessFile, from: Long, until: Long): String? {
        var cursor = from
        while (cursor + 8 <= until) {
            val header = TagBytes.readAt(raf, cursor, 8)
            if (header.size < 8) return null
            val size = TagBytes.be32(header, 0)
            val type = String(header, 4, 4, StandardCharsets.ISO_8859_1)
            if (size < 8) return null
            if (type == "data") {
                val body = TagBytes.readAt(raf, cursor + 16, size - 16)
                val text = String(body, StandardCharsets.UTF_8).trimEnd(TagBytes.NUL).trim()
                if (text.isNotEmpty()) return text
            }
            cursor += size
        }
        return null
    }
}
