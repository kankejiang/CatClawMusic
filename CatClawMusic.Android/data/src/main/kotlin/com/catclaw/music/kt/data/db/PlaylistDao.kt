package com.catclaw.music.kt.data.db

import androidx.room.Dao
import androidx.room.Query
import androidx.room.Transaction
import androidx.room.Upsert
import com.catclaw.music.kt.data.db.entity.PlaylistEntity
import kotlinx.coroutines.flow.Flow

/**
 * 歌单与歌单条目。
 *
 * 内置单（`IsSystem = 1`）只能被清空不能被删除，这条保护写在 SQL 里而不是调用方；
 * 条目去重靠参考 schema 里的唯一索引 `ux_playlist_songs_playlist_song(PlaylistId, SongId)`
 * 配合 `INSERT OR IGNORE`，所以顺序完全由 `Position` 决定。
 */
@Dao
interface PlaylistDao {

    @Query("SELECT * FROM Playlists ORDER BY IsSystem DESC, SortOrder ASC, CreatedAt ASC")
    fun observePlaylists(): Flow<List<PlaylistEntity>>

    @Query("SELECT * FROM Playlists WHERE Id = :id")
    suspend fun byId(id: Int): PlaylistEntity?

    @Query("SELECT * FROM Playlists WHERE Name = :name LIMIT 1")
    suspend fun byName(name: String): PlaylistEntity?

    @Upsert
    suspend fun upsert(playlist: PlaylistEntity): Long

    @Query(SQL_DELETE_PLAYLIST)
    suspend fun delete(id: Int)

    /** 重命名。返回被改的行数：0 表示那是内置单或 id 不存在，调用方据此决定要不要关编辑框 */
    @Query(SQL_RENAME_PLAYLIST)
    suspend fun rename(id: Int, name: String, now: Long): Int

    @Query("DELETE FROM PlaylistSongs WHERE PlaylistId = :id")
    suspend fun clearItems(id: Int)

    @Query(SQL_INSERT_ITEM)
    suspend fun insertItem(playlistId: Int, songId: Int, position: Int)

    @Query("DELETE FROM PlaylistSongs WHERE PlaylistId = :playlistId AND SongId = :songId")
    suspend fun removeItem(playlistId: Int, songId: Int)

    @Query(SQL_SONG_IDS_IN_ORDER)
    suspend fun songIdsInOrder(playlistId: Int): List<Int>

    @Query(
        """
        SELECT s.Id AS id, s.Title AS title, IFNULL(a.Name, '') AS artist,
               IFNULL(al.Title, '') AS album, s.Duration AS durationSeconds,
               s.FilePath AS filePath, s.CoverArtPath AS coverArtPath, s.MediaStoreId AS mediaStoreId,
               s.Source AS source, s.Protocol AS protocol
        FROM PlaylistSongs ps
        JOIN Songs s ON s.Id = ps.SongId
        LEFT JOIN Artists a ON a.Id = s.ArtistId
        LEFT JOIN Albums al ON al.Id = s.AlbumId
        WHERE ps.PlaylistId = :playlistId
        ORDER BY ps.Position ASC
        """,
    )
    fun observeItems(playlistId: Int): Flow<List<SongListItem>>

    @Query("UPDATE Playlists SET UpdatedAt = :now, SongCount = :count WHERE Id = :id")
    suspend fun touch(id: Int, count: Int, now: Long)

    /** 拖拽排序：整表按新顺序重写 Position，一个事务内完成 */
    @Transaction
    suspend fun reorder(playlistId: Int, orderedSongIds: List<Int>) {
        orderedSongIds.forEachIndexed { index, songId ->
            updatePosition(playlistId, songId, index)
        }
        touch(playlistId, orderedSongIds.size, System.currentTimeMillis())
    }

    @Query(SQL_UPDATE_POSITION)
    suspend fun updatePosition(playlistId: Int, songId: Int, position: Int)

    /** 追加到末尾：位置从当前最大值续起，重复条目由唯一索引忽略 */
    @Transaction
    suspend fun appendAll(playlistId: Int, songIds: List<Int>) {
        var position = (maxPosition(playlistId) ?: -1) + 1
        songIds.forEach {
            insertItem(playlistId, it, position++)
        }
        touch(playlistId, songIdsInOrder(playlistId).size, System.currentTimeMillis())
    }

    @Query("SELECT MAX(Position) FROM PlaylistSongs WHERE PlaylistId = :playlistId")
    suspend fun maxPosition(playlistId: Int): Int?
}

/**
 * 这几条 SQL 的行为（内置单保护、唯一索引去重、按 Position 排序）此前只存在于 Room 注解里一份，
 * JVM 假 DAO 测不到、真机仪器化测试又依赖设备 —— 所以提成常量，让**同一份字符串**能被真 SQLite 执行到。
 * 改这里等于同时改 DAO 与测试的靶子，不会出现"测试验的是另一份拷贝"。
 */
internal const val SQL_DELETE_PLAYLIST = "DELETE FROM Playlists WHERE Id = :id AND IsSystem = 0"

/**
 * 重命名。和删除同一条保护：**内置单不许改名**（"收藏"、"最近播放"是虚拟入口的名字，
 * 改了它们和页面上那两个 EntryCard 就对不上了）。`IsSystem = 0` 放在 WHERE 里而不是靠调用方自觉，
 * 因为调用方是 UI，UI 忘了判就是静默改坏数据。
 */
internal const val SQL_RENAME_PLAYLIST =
    "UPDATE Playlists SET Name = :name, UpdatedAt = :now WHERE Id = :id AND IsSystem = 0"
internal const val SQL_INSERT_ITEM =
    "INSERT OR IGNORE INTO PlaylistSongs (PlaylistId, SongId, Position) VALUES (:playlistId, :songId, :position)"
internal const val SQL_SONG_IDS_IN_ORDER =
    "SELECT SongId FROM PlaylistSongs WHERE PlaylistId = :playlistId ORDER BY Position ASC"
internal const val SQL_UPDATE_POSITION =
    "UPDATE PlaylistSongs SET Position = :position WHERE PlaylistId = :playlistId AND SongId = :songId"
