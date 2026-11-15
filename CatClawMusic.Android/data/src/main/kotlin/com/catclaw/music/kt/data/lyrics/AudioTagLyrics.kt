package com.catclaw.music.kt.data.lyrics

import java.io.RandomAccessFile

/**
 * 从音频标签里读内嵌歌词。候选顺序逐字对齐 C# `TagReader.ExtractLyricsFromFile`：
 * ID3v2 `USLT`（第一个非空）→ Xiph `LYRICS` → `UNSYNCEDLYRICS` → APEv2 `LYRICS`
 * → `UNSYNCED LYRICS` → MP4 `©lyr`。
 *
 * C# 靠 TagLibSharp；安卓侧没有对位的纯 Java 库，所以按容器自己解（各一个文件），
 * 且只读标签所在的那段窗口，不把整首歌读进内存。
 * SYLT 同步歌词帧 C# 从不解析，这里同样不做。任何损坏都只返回 null，不抛。
 */
object AudioTagLyrics {

    fun read(path: String): String? {
        val file = java.io.File(path)
        if (!file.isFile || file.length() <= 0) return null
        return runCatching { RandomAccessFile(file, "r").use { extract(it) } }.getOrNull()
    }

    /**
     * 从**已取回的字节窗口**里读内嵌歌词，供远程曲用。
     *
     * 与 [read] 的差别：远程文件没有可 seek 的句柄，只有 Range 取回的一段字节。
     * 所以这里按「窗口内能覆盖的部分」解：FLAC 的 VORBIS_COMMENT 与 Ogg 的注释包都在
     * 头部几 KB 内（实测 FLAC 的 `LYRICS` 就在前 256 KB 窗口里）；ID3v2 整表也常在窗口内，
     * 若 USLT 帧被巨大 APIC 挤到窗口之外则只能解到 null —— 那属于可接受的漏读，
     * 比为了歌词多取一次 2 MB 更划算。
     */
    fun readWindow(bytes: ByteArray): String? {
        if (bytes.size < 8) return null
        return runCatching {
            when {
                // ID3v2：帧表若整体落在窗口内就能解；`Id3v2Tag.lyrics` 吃的是 ByteArray 版本
                TagBytes.startsWith(bytes, "ID3") -> Id3v2Tag.lyrics(bytes)
                TagBytes.startsWith(bytes, "fLaC") -> flacLyrics(bytes)
                TagBytes.startsWith(bytes, "OggS") -> OggTag.lyrics(bytes)
                else -> null
            }
        }.getOrNull()
    }

    /**
     * FLAC：走 VORBIS_COMMENT（type=4）块。
     *
     * 不能用 [FlacTag.lyrics]：那个要 `RandomAccessFile`（按块头跳读）。
     * 窗口里的块遍历本身就是 [AudioHeadTags] 用过的那套，这里只取注释块。
     */
    private fun flacLyrics(head: ByteArray): String? {
        var cursor = 4
        while (cursor + 4 <= head.size) {
            val header = head[cursor].toInt() and 0xFF
            val last = header and 0x80 != 0
            val type = header and 0x7F
            val size = ((head[cursor + 1].toInt() and 0xFF) shl 16) or
                ((head[cursor + 2].toInt() and 0xFF) shl 8) or (head[cursor + 3].toInt() and 0xFF)
            cursor += 4
            if (size < 0 || cursor + size > head.size) return null
            if (type == 4) return VorbisTag.lyrics(head.copyOfRange(cursor, cursor + size))
            if (last) return null
            cursor += size
        }
        return null
    }

    private fun extract(raf: RandomAccessFile): String? {
        val length = raf.length()
        val head = TagBytes.readAt(raf, 0, minOf(length, 64L * 1024).toInt())
        // ID3 走**流式**读：歌词帧常排在内嵌封面之后，64 KB 的头窗口装不下整个帧表
        if (TagBytes.startsWith(head, "ID3")) Id3v2Tag.lyrics(raf, length)?.let { return it }
        if (TagBytes.startsWith(head, "fLaC")) FlacTag.lyrics(raf, length)?.let { return it }
        if (TagBytes.startsWith(head, "OggS")) OggTag.lyrics(head)?.let { return it }
        if (Mp4Tag.looksLikeMp4(head)) Mp4Tag.lyrics(raf, length)?.let { return it }
        Apev2Tag.lyrics(raf, length)?.let { return it }
        return null
    }
}
