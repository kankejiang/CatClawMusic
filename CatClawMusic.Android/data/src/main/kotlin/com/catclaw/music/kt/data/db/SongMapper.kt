package com.catclaw.music.kt.data.db

import com.catclaw.music.kt.core.model.ProtocolType
import com.catclaw.music.kt.core.model.Song
import com.catclaw.music.kt.core.model.SongSource
import com.catclaw.music.kt.data.db.entity.SongEntity

/** Room 联表行 → 播放层用的 `:core` Song（Source / Protocol 随行带回，本地行自然是 Local） */
fun SongListItem.toSong(): Song = Song(
    id = id,
    title = title,
    artist = artist,
    album = album,
    durationSeconds = durationSeconds,
    filePath = filePath,
    coverArtPath = coverArtPath,
    source = SongSource.entries.getOrElse(source) { SongSource.Local },
    protocol = ProtocolType.entries.getOrElse(protocol) { ProtocolType.WebDAV },
    mediaStoreId = mediaStoreId,
)

/**
 * 整行 → Song：Source / Protocol / RemoteId 都带上，远程曲才能选对取流方式。
 * 整数按 ordinal 还原，越界一律退回 Local / WebDAV（C# 侧未知值也是这么兜的）。
 */
fun SongEntity.toSong(artistName: String = "", albumTitle: String = ""): Song = Song(
    id = id,
    title = title,
    artist = artistName,
    album = albumTitle,
    durationSeconds = durationSeconds,
    filePath = filePath,
    fileSizeBytes = fileSize,
    bitrateKbps = bitrate,
    trackNumber = trackNumber,
    year = year,
    genre = genre,
    coverArtPath = coverArtPath,
    lyricsPath = lyricsPath,
    source = SongSource.entries.getOrElse(source) { SongSource.Local },
    protocol = ProtocolType.entries.getOrElse(protocol) { ProtocolType.WebDAV },
    remoteId = remoteId,
    remoteCoverPath = remoteCoverPath,
    mediaStoreId = mediaStoreId,
)
