package com.catclaw.music.kt.data.db.entity

import androidx.room.ColumnInfo
import androidx.room.Entity
import androidx.room.Index
import androidx.room.PrimaryKey

@Entity(tableName = "Playlists")
data class PlaylistEntity(
    @PrimaryKey(autoGenerate = true) @ColumnInfo(name = "Id") val id: Int = 0,
    @ColumnInfo(name = "Name") val name: String,
    @ColumnInfo(name = "CreatedAt") val createdAt: Long = 0,
    @ColumnInfo(name = "UpdatedAt") val updatedAt: Long = 0,
    @ColumnInfo(name = "SongCount") val songCount: Int = 0,
    /** 收藏 / 播放历史这类内置歌单标记，删歌单时要跳过 */
    @ColumnInfo(name = "IsSystem") val isSystem: Boolean = false,
    @ColumnInfo(name = "SortOrder") val sortOrder: Int = 0,
)

/** (PlaylistId, SongId) 唯一 + (PlaylistId, Position) 排序索引，与线上库一致 */
@Entity(
    tableName = "PlaylistSongs",
    indices = [
        Index(value = ["PlaylistId", "SongId"], unique = true),
        Index(value = ["PlaylistId", "Position"]),
        Index("SongId"),
    ],
)
data class PlaylistSongEntity(
    @PrimaryKey(autoGenerate = true) @ColumnInfo(name = "Id") val id: Int = 0,
    @ColumnInfo(name = "PlaylistId") val playlistId: Int,
    @ColumnInfo(name = "SongId") val songId: Int,
    @ColumnInfo(name = "Position") val position: Int = 0,
)

/** 收藏表主键就是 SongId（无自增 Id 列） */
@Entity(tableName = "Favorites")
data class FavoriteEntity(
    @PrimaryKey @ColumnInfo(name = "SongId") val songId: Int,
    @ColumnInfo(name = "AddedAt") val addedAt: Long = 0,
)

@Entity(tableName = "Lyrics")
data class LyricsEntity(
    @PrimaryKey @ColumnInfo(name = "SongId") val songId: Int,
    @ColumnInfo(name = "LrcPath") val lrcPath: String? = null,
    @ColumnInfo(name = "Content") val content: String? = null,
)
