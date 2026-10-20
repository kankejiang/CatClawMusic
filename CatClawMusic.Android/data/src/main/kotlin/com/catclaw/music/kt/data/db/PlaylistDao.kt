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
/**
 * 歌单封面的来源：歌单内**第一首**（Position 最小）那首歌的封面列与音频路径。
 *
 * `coverArtPath` 是本端库里那一列（多半是空的），`filePath` 才是真正出图的入口 ——
 * 本端没有 C# 那台落盘的封面缓存，取图靠 `CoverTile` 现读内嵌封面。
 */
data class PlaylistCover(
    val playlistId: Int,
    val coverArtPath: String?,
    val filePath: String,
)

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

    /**
     * 每个歌单的封面来源。
     *
     * 一条 SQL 出全部歌单，而不是"每个歌单查一次"（C# 那边是 N 次查询 + `Task.WhenAll` 并行兜着，
     * 见 `PlaylistViewModel.cs:101-122`）：歌单一多，省下的是 N 次数据库往返。
     */
    @Query(SQL_PLAYLIST_COVERS)
    fun observePlaylistCovers(): Flow<List<PlaylistCover>>

    /**
     * 拖拽排序后的落库：按目标顺序逐行覆写 `SortOrder`（一个事务内完成）。
     *
     * 传入的必须**只含自建歌单**（`IsSystem = 0`）：本端的列表次序是
     * `IsSystem DESC, SortOrder ASC, CreatedAt ASC`，系统歌单恒在最前 ——
     * 把它们的序号一起重写既改变不了显示顺序，又会把它们的序号搅乱。
     * （C# 的 `UpdatePlaylistsOrderAsync` 收的是全部歌单，那是因为它的排序里没有 IsSystem 优先这一条。）
     */
    @Transaction
    suspend fun reorderPlaylists(orderedIds: List<Int>) {
        orderedIds.forEachIndexed { index, id -> updateSortOrder(id, index + 1) }
    }

    @Query(SQL_UPDATE_SORT_ORDER)
    suspend fun updateSortOrder(id: Int, order: Int)

    @Query("DELETE FROM PlaylistSongs WHERE PlaylistId = :id")
    suspend fun clearItems(id: Int)

    @Query(SQL_INSERT_ITEM)
    suspend fun insertItem(playlistId: Int, songId: Int, position: Int)

    @Query(SQL_REMOVE_ITEM)
    suspend fun removeItem(playlistId: Int, songId: Int)

    /**
     * 从歌单移掉一首并同步 `SongCount`。
     *
     * `SongCount` 是 Playlists 上的**存量列**而不是查询时算出来的，所以删行之外还得有人改它——
     * 这条规则此前写在调用方（AI 工具里就是 `removeItem` + `touch` 两行），
     * 于是页面侧只要少抄一行就是页面上一个错数字。收进 DAO，调用方想漏也漏不掉。
     * 加歌方向同理，走 [appendAll]（它本来就 touch）。
     */
    @Transaction
    suspend fun removeFromPlaylist(playlistId: Int, songId: Int) {
        removeItem(playlistId, songId)
        touch(playlistId, songIdsInOrder(playlistId).size, System.currentTimeMillis())
    }

    @Query(SQL_SONG_IDS_IN_ORDER)
    suspend fun songIdsInOrder(playlistId: Int): List<Int>

    @Query(
        """
        SELECT s.Id AS id, s.Title AS title, IFNULL(a.Name, '') AS artist,
               IFNULL(al.Title, '') AS album, s.Duration AS durationSeconds,
               s.FilePath AS filePath, s.CoverArtPath AS coverArtPath, s.MediaStoreId AS mediaStoreId,
               s.Source AS source, s.Protocol AS protocol, IFNULL(s.DateAdded, 0) AS dateAdded
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
internal const val SQL_REMOVE_ITEM =
    "DELETE FROM PlaylistSongs WHERE PlaylistId = :playlistId AND SongId = :songId"
internal const val SQL_SONG_IDS_IN_ORDER =
    "SELECT SongId FROM PlaylistSongs WHERE PlaylistId = :playlistId ORDER BY Position ASC"
/**
 * 覆写自建歌单的 `SortOrder`。`IsSystem = 0` 与删除 / 改名是同一条保护：
 * 系统歌单（收藏 / 最近播放）的序号不该被拖拽搅动，而调用方是 UI，忘了判就是静默写坏排序。
 */
/**
 * 每个歌单第一首（Position 最小）的封面来源。
 *
 * 用相关子查询取 `MIN(Position)`，而不是 `GROUP BY` + `MIN`：后者拿不到"最小那一行"的其它列
 * ——SQLite 会随便给一行。同一歌单若出现重复 Position（正常不会：`reorder` 写的是密集序列），
 * 这里会各出一行，调用方按歌单 id 归并即可。
 */
internal const val SQL_PLAYLIST_COVERS =
    """
    SELECT ps.PlaylistId AS playlistId, s.CoverArtPath AS coverArtPath, s.FilePath AS filePath
    FROM PlaylistSongs ps
    JOIN Songs s ON s.Id = ps.SongId
    WHERE ps.Position = (
        SELECT MIN(ps2.Position) FROM PlaylistSongs ps2 WHERE ps2.PlaylistId = ps.PlaylistId
    )
    """
internal const val SQL_UPDATE_SORT_ORDER =
    "UPDATE Playlists SET SortOrder = :order WHERE Id = :id AND IsSystem = 0"
internal const val SQL_UPDATE_POSITION =
    "UPDATE PlaylistSongs SET Position = :position WHERE PlaylistId = :playlistId AND SongId = :songId"
