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

    private fun extract(raf: RandomAccessFile): String? {
        val length = raf.length()
        val head = TagBytes.readAt(raf, 0, minOf(length, 64L * 1024).toInt())
        if (TagBytes.startsWith(head, "ID3")) Id3v2Tag.lyrics(head)?.let { return it }
        if (TagBytes.startsWith(head, "fLaC")) FlacTag.lyrics(raf, length)?.let { return it }
        if (TagBytes.startsWith(head, "OggS")) OggTag.lyrics(head)?.let { return it }
        if (Mp4Tag.looksLikeMp4(head)) Mp4Tag.lyrics(raf, length)?.let { return it }
        Apev2Tag.lyrics(raf, length)?.let { return it }
        return null
    }
}
