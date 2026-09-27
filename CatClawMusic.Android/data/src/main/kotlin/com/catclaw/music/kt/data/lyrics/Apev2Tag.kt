package com.catclaw.music.kt.data.lyrics

import java.io.RandomAccessFile
import java.nio.charset.StandardCharsets

/**
 * APEv2 尾部标签：值编码看条目 flags 的 bit29-30（0=UTF-8、1=UTF-16LE、2=二进制）。
 * 键按 C# 的取法：先 LYRICS，再 UNSYNCED LYRICS（含带空格那种写法）。
 */
internal object Apev2Tag {

    private const val FOOTER = "APETAGEX"

    fun lyrics(raf: RandomAccessFile, length: Long): String? {
        if (length < 32) return null
        val tail = TagBytes.readAt(raf, maxOf(0L, length - 32), 32)
        var footerAt = TagBytes.indexOfAscii(tail, FOOTER)
        if (footerAt < 0) {
            // 有些实现只写 header 不写 footer，往前 32 字节再找一次
            val before = TagBytes.readAt(raf, maxOf(0L, length - 64), 32)
            footerAt = TagBytes.indexOfAscii(before, FOOTER)
            if (footerAt < 0) return null
            return parse(raf, maxOf(0L, length - 64) + footerAt)
        }
        return parse(raf, length - 32 + footerAt)
    }

    private fun parse(raf: RandomAccessFile, footerAt: Long): String? {
        val footer = TagBytes.readAt(raf, footerAt, 32)
        if (footer.size < 32) return null
        val tagSize = TagBytes.le32(footer, 12)
        val count = TagBytes.le32(footer, 16)
        if (tagSize <= 32 || count <= 0 || count > 4096) return null
        var bodyAt = footerAt - (tagSize - 32)
        // 有 header 的写法里，items 区前还有一整块 header：直接看魔数，不去猜 flags 位
        if (TagBytes.startsWith(TagBytes.readAt(raf, bodyAt, 8), FOOTER)) bodyAt += 32
        val items = TagBytes.readAt(raf, maxOf(0L, bodyAt), (footerAt - bodyAt).toInt())
        var cursor = 0
        var lyrics: String? = null
        var alt: String? = null
        repeat(count) {
            if (cursor + 9 > items.size) return@repeat
            val valueLength = TagBytes.le32(items, cursor)
            val flags = TagBytes.le32(items, cursor + 4)
            cursor += 8
            val keyEnd = whileIndex(items, cursor)
            if (keyEnd < 0 || valueLength < 0 || cursor + (keyEnd - cursor) + 1 + valueLength > items.size) return@repeat
            val key = String(items, cursor, keyEnd - cursor, StandardCharsets.UTF_8).uppercase()
            cursor = keyEnd + 1
            val value = items.copyOfRange(cursor, cursor + valueLength)
            cursor += valueLength
            val text = when ((flags ushr 29) and 3) {
                1 -> String(value, StandardCharsets.UTF_16LE)
                2 -> return@repeat
                else -> String(value, StandardCharsets.UTF_8)
            }.trimEnd(TagBytes.NUL).trim()
            if (text.isEmpty()) return@repeat
            when (key) {
                "LYRICS" -> if (lyrics == null) lyrics = text
                "UNSYNCED LYRICS", "UNSYNCEDLYRICS" -> if (alt == null) alt = text
            }
        }
        return lyrics ?: alt
    }

    private fun whileIndex(b: ByteArray, from: Int): Int {
        var i = from
        while (i < b.size) {
            if (b[i] == 0.toByte()) return i
            i++
        }
        return -1
    }
}
