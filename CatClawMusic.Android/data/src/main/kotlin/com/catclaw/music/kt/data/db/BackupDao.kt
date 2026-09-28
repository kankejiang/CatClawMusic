package com.catclaw.music.kt.data.db

import androidx.room.Dao
import androidx.room.Insert
import androidx.room.Query
import com.catclaw.music.kt.data.db.entity.ChatMessageEntity

/**
 * 备份/恢复专用的读写查询。单独一个 DAO 而不是往三个既有 DAO 里各塞几条：
 * 这批 SQL 只在备份时跑，集中一处好读，也好让真 SQLite 测到（见 `BackupDaoSqlTest`）。
 */
@Dao
interface BackupDao {
    @Query(SQL_BACKUP_PLAYLISTS)
    suspend fun backupPlaylists(): List<PlaylistRow>

    @Query(SQL_BACKUP_PLAYLIST_SONGS)
    suspend fun backupPlaylistSongs(): List<PlaylistSongRow>

    @Query(SQL_BACKUP_HISTORY)
    suspend fun backupPlayHistory(): List<HistoryRow>

    @Query(SQL_BACKUP_FAVORITES)
    suspend fun backupFavorites(): List<FavoriteRow>

    @Query(SQL_BACKUP_ARTISTS)
    suspend fun backupArtists(): List<ArtistRow>

    @Query(SQL_BACKUP_SONG_REFS)
    suspend fun songRefs(): List<SongRefRow>

    /** 建歌单要拿回自增 id，所以走裸 INSERT 而不是 Upsert 实体 */
    @Query(SQL_INSERT_PLAYLIST)
    suspend fun insertPlaylist(name: String, now: Long): Long

    @Query(SQL_INSERT_PLAYLIST_SONG)
    suspend fun insertPlaylistSong(playlistId: Int, songId: Int, position: Int)

    /** 现有条目：恢复时既用来跳过重复，也用来把 Position 续在末尾而不是从头覆盖 */
    @Query(SQL_EXISTING_SONG_IDS)
    suspend fun existingPlaylistSongIds(playlistId: Int): List<Int>

    @Query(SQL_INSERT_HISTORY)
    suspend fun insertHistory(songId: Int, playedAt: Long, count: Int)

    @Query(SQL_INSERT_FAVORITE)
    suspend fun insertFavorite(songId: Int, addedAt: Long)

    /** 只补空字段：`COALESCE(NULLIF(列,''), :新值)`，用户自己填过的绝不覆盖 */
    @Query(SQL_FILL_ARTIST)
    suspend fun fillArtistFields(name: String, gender: String?, birthday: String?, region: String?, description: String?)

    @Query(SQL_SET_ARTIST_COVER)
    suspend fun setArtistCover(name: String, path: String)

    @Insert
    suspend fun insertChat(message: ChatMessageEntity): Long

    @Query("DELETE FROM ChatMessageRecord")
    suspend fun clearChat()

    @Query("SELECT * FROM ChatMessageRecord ORDER BY Id ASC")
    suspend fun allChat(): List<ChatMessageEntity>

    @Query("SELECT Name FROM Artists ORDER BY Name")
    suspend fun artistNames(): List<String>
}

data class PlaylistRow(val id: Int, val name: String, val createdAt: Long, val updatedAt: Long, val songCount: Int, val isSystem: Boolean, val sortOrder: Int)
data class PlaylistSongRow(val playlistId: Int, val songId: Int, val songTitle: String?, val songArtist: String?, val position: Int)
data class HistoryRow(val songId: Int, val songTitle: String?, val songArtist: String?, val playCount: Int, val playedAt: Long)
data class FavoriteRow(val songId: Int, val songTitle: String?, val songArtist: String?, val addedAt: Long)
data class ArtistRow(val name: String, val gender: String?, val birthday: String?, val region: String?, val description: String?)
data class SongRefRow(val id: Int, val title: String, val artist: String)

internal const val SQL_BACKUP_PLAYLISTS =
    "SELECT Id AS id, Name AS name, CreatedAt AS createdAt, UpdatedAt AS updatedAt, " +
        "SongCount AS songCount, IsSystem AS isSystem, SortOrder AS sortOrder FROM Playlists ORDER BY Id"

internal const val SQL_BACKUP_PLAYLIST_SONGS =
    "SELECT ps.PlaylistId AS playlistId, ps.SongId AS songId, s.Title AS songTitle, " +
        "IFNULL(a.Name, '') AS songArtist, ps.Position AS position " +
        "FROM PlaylistSongs ps JOIN Songs s ON s.Id = ps.SongId " +
        "LEFT JOIN Artists a ON a.Id = s.ArtistId ORDER BY ps.PlaylistId ASC, ps.Position ASC"

internal const val SQL_BACKUP_HISTORY =
    "SELECT h.SongId AS songId, s.Title AS songTitle, IFNULL(a.Name, '') AS songArtist, " +
        "h.PlayCount AS playCount, h.PlayedAt AS playedAt " +
        "FROM PlayHistory h JOIN Songs s ON s.Id = h.SongId LEFT JOIN Artists a ON a.Id = s.ArtistId"

internal const val SQL_BACKUP_FAVORITES =
    "SELECT f.SongId AS songId, s.Title AS songTitle, IFNULL(a.Name, '') AS songArtist, f.AddedAt AS addedAt " +
        "FROM Favorites f JOIN Songs s ON s.Id = f.SongId LEFT JOIN Artists a ON a.Id = s.ArtistId"

internal const val SQL_BACKUP_ARTISTS =
    "SELECT Name AS name, Gender AS gender, Birthday AS birthday, Region AS region, Description AS description " +
        "FROM Artists ORDER BY Name"

/** 恢复时的对歌索引：本地全量歌的 id/标题/歌手，备份里的 id 对不上就靠 标题+歌手 */
internal const val SQL_BACKUP_SONG_REFS =
    "SELECT s.Id AS id, s.Title AS title, IFNULL(a.Name, '') AS artist FROM Songs s " +
        "LEFT JOIN Artists a ON a.Id = s.ArtistId"

internal const val SQL_INSERT_PLAYLIST =
    "INSERT INTO Playlists(Name, CreatedAt, UpdatedAt, SongCount, IsSystem, SortOrder) " +
        "VALUES(:name, :now, :now, 0, 0, 0)"

/**
 * OR IGNORE 是有意义的：线上库带 `ux_playlist_songs_playlist_song`（Room 实体里同一组列也声明了
 * `unique = true`），恢复同一个歌单两次不会把列表翻倍。这条不是"顺手写的幂等"，删了会真出重复条目。
 */
internal const val SQL_INSERT_PLAYLIST_SONG =
    "INSERT OR IGNORE INTO PlaylistSongs(PlaylistId, SongId, Position) VALUES(:playlistId, :songId, :position)"

internal const val SQL_EXISTING_SONG_IDS = "SELECT SongId FROM PlaylistSongs WHERE PlaylistId = :playlistId"

internal const val SQL_INSERT_HISTORY =
    "INSERT INTO PlayHistory(SongId, PlayedAt, PlayCount) VALUES(:songId, :playedAt, :count)"

internal const val SQL_INSERT_FAVORITE =
    "INSERT OR IGNORE INTO Favorites(SongId, AddedAt) VALUES(:songId, :addedAt)"

internal const val SQL_FILL_ARTIST =
    "UPDATE Artists SET Gender = COALESCE(NULLIF(Gender, ''), :gender), " +
        "Birthday = COALESCE(NULLIF(Birthday, ''), :birthday), " +
        "Region = COALESCE(NULLIF(Region, ''), :region), " +
        "Description = COALESCE(NULLIF(Description, ''), :description) WHERE Name = :name"

internal const val SQL_SET_ARTIST_COVER = "UPDATE Artists SET Cover = :path WHERE Name = :name"
