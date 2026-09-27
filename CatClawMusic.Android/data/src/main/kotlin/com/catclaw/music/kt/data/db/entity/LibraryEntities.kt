package com.catclaw.music.kt.data.db.entity

import androidx.room.ColumnInfo
import androidx.room.Entity
import androidx.room.Index
import androidx.room.PrimaryKey

/**
 * 列名 / 主键 / 索引逐一对齐 MAUI 线上库的真实 schema
 * （见 `docs-schema/catclaw.reference.sqlite.sql`，由 `%LOCALAPPDATA%\CatClawMusic.Maui\...\catclaw.db` 导出）。
 * `Duration` 单位是**秒**，与 C# `Song.Duration` 契约一致。
 */
@Entity(
    tableName = "Songs",
    indices = [
        Index("FilePath", unique = true),
        Index("ArtistId"),
        Index("AlbumId"),
        Index("Title"),
        Index("Source"),
        Index("Protocol"),
        Index("RemoteId"),
        Index(value = ["Source", "Protocol"]),
    ],
)
data class SongEntity(
    @PrimaryKey(autoGenerate = true) @ColumnInfo(name = "Id") val id: Int = 0,
    @ColumnInfo(name = "Title") val title: String,
    @ColumnInfo(name = "ArtistId") val artistId: Int = 0,
    @ColumnInfo(name = "AlbumId") val albumId: Int = 0,
    @ColumnInfo(name = "Duration") val durationSeconds: Int = 0,
    @ColumnInfo(name = "FilePath") val filePath: String,
    @ColumnInfo(name = "FileSize") val fileSize: Long = 0,
    @ColumnInfo(name = "Bitrate") val bitrate: Int = 0,
    @ColumnInfo(name = "TrackNumber") val trackNumber: Int = 0,
    @ColumnInfo(name = "Year") val year: Int = 0,
    @ColumnInfo(name = "Genre") val genre: String? = null,
    @ColumnInfo(name = "DateAdded") val dateAdded: Long = 0,
    @ColumnInfo(name = "DateModified") val dateModified: Long = 0,
    @ColumnInfo(name = "CoverArtPath") val coverArtPath: String? = null,
    @ColumnInfo(name = "LyricsPath") val lyricsPath: String? = null,
    /** 对应 C# SongSource 枚举的整数值 */
    @ColumnInfo(name = "Source") val source: Int = 0,
    @ColumnInfo(name = "Protocol") val protocol: Int = 0,
    @ColumnInfo(name = "RemoteId") val remoteId: String? = null,
    @ColumnInfo(name = "MediaStoreId") val mediaStoreId: Long = 0,
    @ColumnInfo(name = "RemoteCoverPath") val remoteCoverPath: String? = null,
)

@Entity(tableName = "Artists")
data class ArtistEntity(
    @PrimaryKey(autoGenerate = true) @ColumnInfo(name = "Id") val id: Int = 0,
    @ColumnInfo(name = "Name") val name: String,
    @ColumnInfo(name = "Cover") val cover: String? = null,
    @ColumnInfo(name = "Gender") val gender: String? = null,
    @ColumnInfo(name = "Birthday") val birthday: String? = null,
    @ColumnInfo(name = "Region") val region: String? = null,
    @ColumnInfo(name = "Description") val description: String? = null,
)

/** 注意：Albums 同时存在 Title/Name、Year/ReleaseYear 两组历史列，线上库两者都在，照抄不做合并 */
@Entity(tableName = "Albums", indices = [Index("ArtistId")])
data class AlbumEntity(
    @PrimaryKey(autoGenerate = true) @ColumnInfo(name = "Id") val id: Int = 0,
    @ColumnInfo(name = "Title") val title: String = "",
    @ColumnInfo(name = "Name") val name: String = "",
    @ColumnInfo(name = "Artist") val artist: String = "",
    @ColumnInfo(name = "CoverArtPath") val coverArtPath: String? = null,
    @ColumnInfo(name = "SongCount") val songCount: Int = 0,
    @ColumnInfo(name = "Year") val year: Int = 0,
    @ColumnInfo(name = "ArtistId") val artistId: Int = 0,
    @ColumnInfo(name = "Cover") val cover: String? = null,
    @ColumnInfo(name = "ReleaseYear") val releaseYear: Int = 0,
)

@Entity(
    tableName = "SongArtists",
    indices = [Index("SongId"), Index("ArtistId")],
)
data class SongArtistEntity(
    @PrimaryKey(autoGenerate = true) @ColumnInfo(name = "Id") val id: Int = 0,
    @ColumnInfo(name = "SongId") val songId: Int,
    @ColumnInfo(name = "ArtistId") val artistId: Int,
)
