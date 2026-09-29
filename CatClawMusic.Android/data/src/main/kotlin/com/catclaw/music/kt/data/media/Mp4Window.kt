package com.catclaw.music.kt.data.media

import com.catclaw.music.kt.data.lyrics.TagBytes
import java.nio.charset.StandardCharsets

/**
 * 在一段**字节窗口**里走 MP4/M4A 的 atom 树。
 *
 * 与 `Mp4Tag` 的区别：那一个是对本地文件用 RandomAccessFile 按需读块，
 * 这一个吃的是远程取回来的头部窗口 —— 没有文件可 seek，偏移全部相对窗口。
 *
 * 只认三样东西：`mvhd`（时长）、`ilst` 里的文本项（标题/歌手/专辑/类型/年份/曲目号）。
 * 任何一段读坏都只停止下钻，不抛 —— 这是一次批量回填，一首坏文件不该拖垮整轮。
 */
internal object Mp4Window {

    /** 只有这些容器值得往里钻；封面与音频数据块一律跳过 */
    private val CONTAINERS = setOf("moov", "udta", "meta", "ilst", "trak", "mdia", "minf", "stbl")

    /** ilst 里的文本项：四项标签 + 两种年份/曲目写法（iTunes 历史上有过不带 © 的键） */
    private val TEXT_KEYS = mapOf(
        "©nam" to "title",
        "©ART" to "artist",
        "©alb" to "album",
        "©gen" to "genre",
        "©day" to "date",
        "trkn" to "track",
    )

    fun looksLikeMp4(head: ByteArray): Boolean = TagBytes.indexOfAscii(head, "ftyp", minOf(head.size, 64)) >= 0

    /** @return 键是 [TEXT_KEYS] 的 value，已按规范解好文本；空的解不出来就不给 */
    fun fields(head: ByteArray): Map<String, String> {
        val out = LinkedHashMap<String, String>()
        forEachAtom(head, 0, head.size, 0) { type, bodyFrom, bodyTo, isContainer, parentPath ->
            if (isContainer) return@forEachAtom
            if (!parentPath.endsWith("ilst")) return@forEachAtom
            val key = TEXT_KEYS[type] ?: return@forEachAtom
            if (type == "trkn") {
                binaryTrack(head, bodyFrom, bodyTo)?.let { out.putIfAbsent(key, it) }
            } else {
                textItem(head, bodyFrom, bodyTo)?.let { out.putIfAbsent(key, it) }
            }
        }
        return out
    }

    /** mvhd：v0 的 timescale/duration 在 body 的 12/16 处，v1 是 64 位 creation+modification 后的 20/28 处 */
    fun durationMs(head: ByteArray): Long {
        var result = 0L
        forEachAtom(head, 0, head.size, 0) { type, bodyFrom, bodyTo, isContainer, _ ->
            if (isContainer || type != "mvhd" || result != 0L) return@forEachAtom
            result = readMvhd(head, bodyFrom, bodyTo)
        }
        return result
    }

    private fun readMvhd(head: ByteArray, from: Int, to: Int): Long {
        if (to - from < 20) return 0L
        val version = head[from].toInt() and 0xFF
        return if (version == 1) {
            // v1 要读到 body 的第 32 字节：timescale 在 20，64 位 duration 在 24
            if (to - from < 32) return 0L
            val timescale = TagBytes.be32(head, from + 20).toDouble()
            val duration = TagBytes.be64(head, from + 24).toDouble()
            millisOf(duration, timescale)
        } else {
            val timescale = TagBytes.be32(head, from + 12).toDouble()
            val duration = (TagBytes.be32(head, from + 16).toLong() and 0xFFFFFFFFL).toDouble()
            millisOf(duration, timescale)
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
                // 标志低 16 位是内容类型：1=UTF-8，其余（MAC7/UTF-16）按拉丁字符兜底解
                val charset = if (flags and 0xFF == 1) StandardCharsets.UTF_8 else StandardCharsets.ISO_8859_1
                val text = String(head, cursor + 16, size - 16, charset).trim { it == TagBytes.NUL || it.isWhitespace() }
                if (text.isNotEmpty()) return text
            }
            cursor += size
        }
        return null
    }

    /** trkn 的正文是二进制：8 字节里第 3、4 位是本张的序号 */
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

    /**
     * 深度优先走 atom。`parentPath` 是形如 `moov/udta/meta/ilst` 的路径，
     * 用来判断「这个叶子在不在 ilst 里」—— 因为同名键在别处没有意义。
     */
    private fun forEachAtom(
        bytes: ByteArray,
        from: Int,
        until: Int,
        depth: Int,
        parentPath: String = "",
        onAtom: (type: String, bodyFrom: Int, bodyTo: Int, isContainer: Boolean, parentPath: String) -> Unit,
    ) {
        if (depth > 8) return
        var cursor = from
        while (cursor + 8 <= until) {
            var size = (TagBytes.be32(bytes, cursor).toLong() and 0xFFFFFFFFL)
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
            val isContainer = type in CONTAINERS
            if (isContainer) {
                // meta 是「准容器」：头后面还跟着 4 字节 version/flags，没有它整棵树都会错开
                val offset = if (type == "meta") 4 else 0
                val path = if (parentPath.isEmpty()) type else "$parentPath/$type"
                forEachAtom(bytes, bodyStart + offset, bodyEnd, depth + 1, path, onAtom)
            }
            onAtom(type, bodyStart, bodyEnd, isContainer, parentPath)
            cursor = bodyEnd
        }
    }
}
