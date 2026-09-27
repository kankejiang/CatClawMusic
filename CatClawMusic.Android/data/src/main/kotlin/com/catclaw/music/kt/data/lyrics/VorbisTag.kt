package com.catclaw.music.kt.data.lyrics

import java.io.RandomAccessFile

/**
 * Xiph 注释块（FLAC 的 VORBIS_COMMENT 与 OGG 的 comment 头包共用）。
 * 布局：vendor 长度(u32le) + vendor + 条数(u32le) + 每条 [长度(u32le) + "KEY=value"]。
 * 键按 C# 的取法：先 LYRICS，再 UNSYNCEDLYRICS。
 */
internal object VorbisTag {

    fun lyrics(packet: ByteArray): String? {
        if (packet.size < 8) return null
        var cursor = TagBytes.le32(packet, 0) + 4
        if (cursor + 4 > packet.size) return null
        val count = TagBytes.le32(packet, cursor)
        cursor += 4
        var lyrics: String? = null
        var unsynced: String? = null
        repeat(count.coerceAtMost(4096)) {
            if (cursor + 4 > packet.size) return@repeat
            val size = TagBytes.le32(packet, cursor)
            cursor += 4
            if (size < 0 || cursor + size > packet.size) return@repeat
            val entry = String(packet, cursor, size, java.nio.charset.StandardCharsets.UTF_8)
            cursor += size
            val eq = entry.indexOf('=')
            if (eq <= 0) return@repeat
            val key = entry.substring(0, eq).uppercase()
            val value = entry.substring(eq + 1)
            when (key) {
                "LYRICS" -> if (lyrics == null) lyrics = value
                "UNSYNCEDLYRICS" -> if (unsynced == null) unsynced = value
            }
        }
        return (lyrics ?: unsynced)?.trim()?.takeIf { it.isNotEmpty() }
    }
}
