package com.catclaw.music.kt.core.model

/**
 * 对应 C# Core/Models/Song.cs 的可移植子集。
 * Duration 契约与 MAUI 一致为「秒」；FilePath 对 SAF 歌曲存 content:// URI 字符串。
 */
data class Song(
    val id: Int,
    val title: String,
    val artist: String = "",
    val album: String = "",
    val durationSeconds: Int = 0,
    val filePath: String,
    val fileSizeBytes: Long = 0,
    val bitrateKbps: Int = 0,
    val trackNumber: Int = 0,
    val year: Int = 0,
    val genre: String? = null,
    val coverArtPath: String? = null,
    val lyricsPath: String? = null,
    val source: SongSource = SongSource.Local,
    val protocol: ProtocolType = ProtocolType.WebDAV,
    val remoteId: String? = null,
    val remoteCoverPath: String? = null,
    val mediaStoreId: Long = 0,
) {
    val isRemote: Boolean get() = source != SongSource.Local || filePath.startsWith("http", ignoreCase = true)
}

/**
 * 顺序（ordinal）就是 DB 里 Source 列的整数，必须与 C# `SongSource` 逐位对齐：
 * Navidrome/Subsonic 曲目在 C# 侧也记成 WebDAV，靠 [ProtocolType] 区分，所以这里不能自己加值。
 * 由 SourceOrdinalParityTest 守住。
 */
enum class SongSource { Local, WebDAV, SMB, Cache }

/** 与 C# `ProtocolType` 对齐：远程协议，用于区分 WebDAV 与 Navidrome */
enum class ProtocolType { WebDAV, Navidrome, SMB }
