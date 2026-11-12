package com.catclaw.music.kt.data.netease

import com.catclaw.music.kt.core.model.ProtocolType
import com.catclaw.music.kt.core.model.Song
import com.catclaw.music.kt.core.model.SongSource

/**
 * 在线曲 → 本地播放模型的映射。抽出来是因为私人漫游控制器与页面 ViewModel 都要用：
 * 两处各写一份 id 规则，迟早会出现"同一首歌两个队列 id"。
 *
 * id 用**负数**（本地库 SongId 是自增正数，不会撞）；`-1` 在列表 key / 队列恢复里有
 * "未分配"语义，所以数字 id 一律偏移到 ≤ -2，非数字 id 用哈希兜底。
 */
object NeteaseSongMapper {

    /** 带显式播放地址的映射（在线取链后替换 outer URL 用） */
    fun toQueueSong(online: OnlineSong, position: Int, filePath: String): Song =
        toSong(online, position).copy(filePath = filePath)

    fun toSong(online: OnlineSong, position: Int = 0): Song {
        val numeric = online.id.toLongOrNull()
        val songId = when {
            numeric != null && numeric in 1..Int.MAX_VALUE -> -(numeric.toInt() + 1)
            else -> -(kotlin.math.abs(online.id.hashCode().toLong()).mod(1_000_000_000L) + position + 2).toInt()
        }
        return Song(
            id = if (songId >= -1) -2 else songId,
            title = online.title,
            artist = online.artist,
            album = online.album,
            durationSeconds = online.durationSeconds,
            filePath = "https://music.163.com/song/media/outer/url?id=${online.id}.mp3",
            coverArtPath = online.coverUrl,
            source = SongSource.WebDAV,
            protocol = ProtocolType.WebDAV,
            remoteId = "${NeteasePlaylistClient.REMOTE_PREFIX}${online.id}",
        )
    }
}
