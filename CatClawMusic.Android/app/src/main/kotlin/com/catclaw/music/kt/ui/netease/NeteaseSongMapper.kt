package com.catclaw.music.kt.ui.netease

import com.catclaw.music.kt.core.model.ProtocolType
import com.catclaw.music.kt.core.model.Song
import com.catclaw.music.kt.core.model.SongSource
import com.catclaw.music.kt.data.netease.OnlineSong

/**
 * 在线曲 → 播放队列 Song 的映射（网易云页与发现页的私人 FM 共用同一份）。
 *
 * id 用负数：本地库 SongId 是自增正数，负数不会撞上；
 * 数字 id 直接取负再减一，非数字 id 用哈希兜底；-1 是「未分配」语义，一律避开。
 * `remoteId = netease:<id>` 是在线歌词 / 打卡 / 续播放找到原曲的唯一依据，不能省。
 */
internal fun OnlineSong.toQueueSong(position: Int, filePath: String): Song {
    val numeric = id.toLongOrNull()
    val songId = when {
        numeric != null && numeric in 1..Int.MAX_VALUE -> -(numeric.toInt() + 1)
        else -> -(kotlin.math.abs(id.hashCode().toLong()).mod(1_000_000_000L) + position + 2).toInt()
    }
    return Song(
        id = if (songId >= -1) -2 else songId,
        title = title,
        artist = artist,
        album = album,
        durationSeconds = durationSeconds,
        filePath = filePath,
        coverArtPath = coverUrl,
        source = SongSource.WebDAV,
        protocol = ProtocolType.WebDAV,
        remoteId = "netease:$id",
    )
}
