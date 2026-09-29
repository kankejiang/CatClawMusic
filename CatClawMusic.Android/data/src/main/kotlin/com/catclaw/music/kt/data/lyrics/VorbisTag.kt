package com.catclaw.music.kt.data.lyrics

import java.io.RandomAccessFile

/**
 * Xiph 注释块（FLAC 的 VORBIS_COMMENT 与 OGG 的 comment 头包共用）。
 * 布局：vendor 长度(u32le) + vendor + 条数(u32le) + 每条 [长度(u32le) + "KEY=value"]。
 *
 * 同一个走条目循环供两处用：歌词按 C# 的取法先 LYRICS 再 UNSYNCEDLYRICS，
 * 文本字段给远程补元数据用（TITLE/ARTIST/ALBUM/GENRE/DATE/TRACKNUMBER）。
 */
internal object VorbisTag {

    fun lyrics(packet: ByteArray): String? {
        var lyrics: String? = null
        var unsynced: String? = null
        walkEntries(packet) { key, value ->
            when (key) {
                "LYRICS" -> if (lyrics == null) lyrics = value
                "UNSYNCEDLYRICS" -> if (unsynced == null) unsynced = value
            }
        }
        return (lyrics ?: unsynced)?.trim()?.takeIf { it.isNotEmpty() }
    }

    /** 一个键可能有多个值（Xiph 允许重复），取第一个 */
    fun fields(packet: ByteArray): Map<String, String> {
        val out = LinkedHashMap<String, String>()
        walkEntries(packet) { key, value ->
            if (KEYS.contains(key) && value.isNotBlank()) out.putIfAbsent(key, value.trim())
        }
        return out
    }

    private val KEYS = setOf("TITLE", "ARTIST", "ALBUM", "GENRE", "DATE", "TRACKNUMBER")

    private fun walkEntries(packet: ByteArray, onEntry: (key: String, value: String) -> Unit) {
        if (packet.size < 8) return
        var cursor = TagBytes.le32(packet, 0) + 4
        if (cursor + 4 > packet.size) return
        val count = TagBytes.le32(packet, cursor)
        cursor += 4
        repeat(count.coerceAtMost(4096)) {
            if (cursor + 4 > packet.size) return@repeat
            val size = TagBytes.le32(packet, cursor)
            cursor += 4
            if (size < 0 || cursor + size > packet.size) return@repeat
            val entry = String(packet, cursor, size, java.nio.charset.StandardCharsets.UTF_8)
            cursor += size
            val eq = entry.indexOf('=')
            if (eq <= 0) return@repeat
            onEntry(entry.substring(0, eq).uppercase(), entry.substring(eq + 1))
        }
    }
}
