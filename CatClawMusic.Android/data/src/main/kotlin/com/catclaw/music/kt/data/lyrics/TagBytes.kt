package com.catclaw.music.kt.data.lyrics

import java.io.RandomAccessFile
import java.nio.charset.StandardCharsets

/** 标签解析共用的字节小工具：大小端、synchsafe、窗口读取、按 ID3 文本编码解字符串 */
internal object TagBytes {

    const val MAX_WINDOW = 4 * 1024 * 1024
    const val NUL = '\u0000'

    fun readAt(raf: RandomAccessFile, at: Long, size: Int): ByteArray {
        if (at < 0 || size <= 0 || at >= raf.length()) return ByteArray(0)
        val len = minOf(size.toLong(), raf.length() - at, MAX_WINDOW.toLong()).toInt()
        return runCatching {
            synchronized(raf) {
                raf.seek(at)
                ByteArray(len).also { raf.readFully(it) }
            }
        }.getOrDefault(ByteArray(0))
    }

    fun startsWith(bytes: ByteArray, text: String): Boolean {
        if (bytes.size < text.length) return false
        for (i in text.indices) if (bytes[i] != text[i].code.toByte()) return false
        return true
    }

    fun indexOfAscii(b: ByteArray, text: String, limit: Int = b.size): Int {
        val end = minOf(limit, b.size) - text.length
        var i = 0
        while (i <= end) {
            var hit = true
            for (j in text.indices) if (b[i + j] != text[j].code.toByte()) { hit = false; break }
            if (hit) return i
            i++
        }
        return -1
    }

    /**
     * 每一段都得自己先算完再 `or` —— Kotlin 的 `shl` 与 `or` 是同级中缀、**从左往右**结合，
     * 少了括号就变成 `((a shl 21 or b) shl 14 or c) shl 7 or d`，读出来的数完全不是那个数。
     * 只有一位非零时两种算法碰巧相等，所以小标签一直没暴露这个问题。
     */
    fun synchsafe(b: ByteArray, at: Int): Int =
        ((b[at].toInt() and 0x7F) shl 21) or ((b[at + 1].toInt() and 0x7F) shl 14) or
            ((b[at + 2].toInt() and 0x7F) shl 7) or (b[at + 3].toInt() and 0x7F)

    fun be32(b: ByteArray, at: Int): Int =
        ((b[at].toInt() and 0xFF) shl 24) or ((b[at + 1].toInt() and 0xFF) shl 16) or
            ((b[at + 2].toInt() and 0xFF) shl 8) or (b[at + 3].toInt() and 0xFF)

    fun be64(b: ByteArray, at: Int): Long {
        var v = 0L
        for (i in 0 until 8) v = (v shl 8) or (b[at + i].toLong() and 0xFF)
        return v
    }

    fun be16(b: ByteArray, at: Int): Int =
        (b[at].toInt() and 0xFF) shl 8 or (b[at + 1].toInt() and 0xFF)

    fun le32(b: ByteArray, at: Int): Int =
        b[at].toInt() and 0xFF or ((b[at + 1].toInt() and 0xFF) shl 8) or
            ((b[at + 2].toInt() and 0xFF) shl 16) or ((b[at + 3].toInt() and 0xFF) shl 24)

    /** ID3v2 文本编码：0=ISO-8859-1、1=UTF-16 带 BOM、2=UTF-16BE、3=UTF-8 */
    fun id3Text(b: ByteArray, from: Int, to: Int, encoding: Int): String {
        if (from < 0 || from >= to || to > b.size) return ""
        val slice = b.copyOfRange(from, to)
        val text = when (encoding) {
            1 -> when {
                slice.size >= 2 && slice[0] == 0xFF.toByte() && slice[1] == 0xFE.toByte() ->
                    String(slice, 2, slice.size - 2, StandardCharsets.UTF_16LE)
                slice.size >= 2 && slice[0] == 0xFE.toByte() && slice[1] == 0xFF.toByte() ->
                    String(slice, 2, slice.size - 2, StandardCharsets.UTF_16BE)
                else -> String(slice, StandardCharsets.UTF_16LE)
            }
            2 -> String(slice, StandardCharsets.UTF_16BE)
            3 -> String(slice, StandardCharsets.UTF_8)
            else -> String(slice, StandardCharsets.ISO_8859_1)
        }
        // 标签里常见补 NUL 与 BOM 残留；trim() 不认 U+0000，得显式去掉
        return text.trim { it == NUL || it.isWhitespace() }
    }
}
