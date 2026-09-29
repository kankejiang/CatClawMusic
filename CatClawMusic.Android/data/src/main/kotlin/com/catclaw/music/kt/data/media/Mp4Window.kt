package com.catclaw.music.kt.data.media

import com.catclaw.music.kt.data.lyrics.TagBytes
import java.nio.charset.StandardCharsets

/** 一次扫描的同时拿到文本字段与时长 */
internal data class Mp4Scan(val fields: Map<String, String>, val durationMs: Long) {
    companion object {
        val EMPTY = Mp4Scan(emptyMap(), 0L)
    }
}

/**
 * 在一段**字节窗口**里走 MP4/M4A 的 atom 树。
 *
 * 与 `Mp4Tag` 的区别：那一个是对本地文件用 RandomAccessFile 按需读块，
 * 这一个吃的是远程取回来的窗口 —— 没有文件可 seek，偏移全部相对窗口。
 *
 * 两个入口对应 moov 的两种位置：
 * - [scanWindow]：faststart（moov 在头部），整段窗口就当 atom 序列走；
 * - [scanTail]：非 faststart 的 moov 在**文件末尾**，尾部窗口是从别的 atom 中间开始的，
 *   所以先定位 `moov` 魔数、倒退 4 字节取原子头，再往里钻；它声明的长度常常超出窗口，
 *   这时按窗口截断而不是放弃 —— C# 那边是 Range 8 MB 尾再手动解 moov。
 *
 * 只认三样：`mvhd`（时长）、`ilst` 里的文本项。任何一段读坏都只让那一块空着，不抛。
 */
internal object Mp4Window {

    /** 只有这些容器值得往里钻；封面与音频数据块一律跳过 */
    private val CONTAINERS = setOf("moov", "udta", "meta", "ilst", "trak", "mdia", "minf", "stbl")

    /** ilst 里的文本项：四项标签 + 年份与曲目号（iTunes 的曲目号是二进制不是文本） */
    private val TEXT_KEYS = mapOf(
        "©nam" to "title",
        "©ART" to "artist",
        "©alb" to "album",
        "©gen" to "genre",
        "©day" to "date",
        "trkn" to "track",
    )

    fun looksLikeMp4(head: ByteArray): Boolean = TagBytes.indexOfAscii(head, "ftyp", minOf(head.size, 64)) >= 0

    fun scanWindow(bytes: ByteArray): Mp4Scan {
        val acc = Accumulator()
        walk(bytes, 0, bytes.size, 0, "", acc)
        return acc.toResult()
    }

    fun scanTail(tail: ByteArray): Mp4Scan {
        val at = TagBytes.indexOfAscii(tail, "moov")
        if (at < 4) return Mp4Scan.EMPTY
        val header = at - 4
        val declared = TagBytes.be32(tail, header).toLong() and 0xFFFFFFFFL
        val bodyStart = header + 8
        // 声明长度超出窗口的部分是"被截掉的尾部 atom"，按窗口截断照样能读到已经拿到手的孩子原子
        val bodyEnd = if (declared in 8 until Long.MAX_VALUE) {
            minOf(header + declared, tail.size.toLong()).toInt()
        } else {
            tail.size
        }
        if (bodyStart >= bodyEnd) return Mp4Scan.EMPTY
        val acc = Accumulator()
        walk(tail, bodyStart, bodyEnd, 1, "moov", acc)
        return acc.toResult()
    }

    private class Accumulator {
        val fields = LinkedHashMap<String, String>()
        var durationMs = 0L

        fun toResult(): Mp4Scan = Mp4Scan(fields, durationMs)
    }

    private fun walk(
        bytes: ByteArray,
        from: Int,
        until: Int,
        depth: Int,
        parentPath: String,
        acc: Accumulator,
    ) {
        if (depth > 8) return
        var cursor = from
        while (cursor + 8 <= until) {
            var size = TagBytes.be32(bytes, cursor).toLong() and 0xFFFFFFFFL
            val type = String(bytes, cursor + 4, 4, StandardCharsets.ISO_8859_1)
            var bodyAt = 8L
            if (size == 1L) {
                if (cursor + 16 > until) return
                size = TagBytes.be64(bytes, cursor + 8)
                bodyAt = 16L
            }
            if (size < bodyAt || cursor + size > until) return
            // 窗口本身不超过几 MB，过了上面那道边界检查就能安全收成 Int
            val bodyStart = (cursor + bodyAt).toInt()
            val bodyEnd = (cursor + size).toInt()
            if (type in CONTAINERS) {
                // meta 是「准容器」：头后面还跟着 4 字节 version/flags，少跳这 4 字节整棵树都会错开
                val offset = if (type == "meta") 4 else 0
                val path = if (parentPath.isEmpty()) type else "$parentPath/$type"
                walk(bytes, bodyStart + offset, bodyEnd, depth + 1, path, acc)
            } else {
                consume(bytes, type, bodyStart, bodyEnd, parentPath, acc)
            }
            cursor = bodyEnd
        }
    }

    private fun consume(
        bytes: ByteArray,
        type: String,
        bodyFrom: Int,
        bodyTo: Int,
        parentPath: String,
        acc: Accumulator,
    ) {
        if (type == "mvhd" && acc.durationMs == 0L && parentPath.endsWith("moov")) {
            acc.durationMs = readMvhd(bytes, bodyFrom, bodyTo)
            return
        }
        if (!parentPath.endsWith("ilst")) return
        val key = TEXT_KEYS[type] ?: return
        val value = if (type == "trkn") binaryTrack(bytes, bodyFrom, bodyTo) else textItem(bytes, bodyFrom, bodyTo)
        if (!value.isNullOrEmpty()) acc.fields.putIfAbsent(key, value)
    }

    /** mvhd：v0 的 timescale/duration 在 body 的 12/16，v1 是 64 位 creation+modification 之后的 20/24 */
    private fun readMvhd(head: ByteArray, from: Int, to: Int): Long {
        if (to - from < 20) return 0L
        val version = head[from].toInt() and 0xFF
        return if (version == 1) {
            // v1 要读到 body 的第 32 字节：timescale 在 20，64 位 duration 在 24
            if (to - from < 32) return 0L
            millisOf(TagBytes.be64(head, from + 24).toDouble(), TagBytes.be32(head, from + 20).toDouble())
        } else {
            millisOf(
                (TagBytes.be32(head, from + 16).toLong() and 0xFFFFFFFFL).toDouble(),
                TagBytes.be32(head, from + 12).toDouble(),
            )
        }
    }

    private fun millisOf(duration: Double, timescale: Double): Long =
        if (timescale <= 0.0 || duration <= 0.0) 0L else (duration * 1000.0 / timescale).toLong()

    /** item 里套一个 data atom：size + "data" + 4 版本/标志 + 4 保留 + 正文 */
    private fun textItem(head: ByteArray, from: Int, to: Int): String? {
        var cursor = from
        while (cursor + 8 <= to) {
            val size = TagBytes.be32(head, cursor)
            val type = String(head, cursor + 4, 4, StandardCharsets.ISO_8859_1)
            if (size < 8 || cursor + size > to) return null
            if (type == "data") {
                // version/flags 是 data 体自己的前 4 字节（原子头的 size/type 之后）
                val flags = TagBytes.be32(head, cursor + 8) and 0x00FF_FFFF
                // 标志低字节是内容类型：1=UTF-8，其余（MAC7/UTF-16）按拉丁字符兜底解
                val charset = if (flags and 0xFF == 1) StandardCharsets.UTF_8 else StandardCharsets.ISO_8859_1
                val text = String(head, cursor + 16, size - 16, charset).trim { it == TagBytes.NUL || it.isWhitespace() }
                if (text.isNotEmpty()) return text
            }
            cursor += size
        }
        return null
    }

    /** trkn 的正文是二进制：8 字节里第 3、4 位才是本张序号 */
    private fun binaryTrack(head: ByteArray, from: Int, to: Int): String? {
        var cursor = from
        while (cursor + 8 <= to) {
            val size = TagBytes.be32(head, cursor)
            val type = String(head, cursor + 4, 4, StandardCharsets.ISO_8859_1)
            if (size < 8 || cursor + size > to) return null
            if (type == "data" && size >= 22) {
                val track = TagBytes.be16(head, cursor + 16 + 2)
                if (track > 0) return track.toString()
            }
            cursor += size
        }
        return null
    }
}
