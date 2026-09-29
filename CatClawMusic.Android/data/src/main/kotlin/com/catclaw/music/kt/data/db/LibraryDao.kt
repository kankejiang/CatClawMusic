package com.catclaw.music.kt.data.db

import androidx.room.Dao
import androidx.room.Insert
import androidx.room.OnConflictStrategy
import androidx.room.Query
import androidx.room.Transaction
import androidx.room.Upsert
import com.catclaw.music.kt.data.db.entity.AlbumEntity
import com.catclaw.music.kt.data.db.entity.ArtistEntity
import com.catclaw.music.kt.data.db.entity.CachedSongEntity
import com.catclaw.music.kt.data.db.entity.DailyPlayStatEntity
import com.catclaw.music.kt.data.db.entity.FavoriteEntity
import com.catclaw.music.kt.data.db.entity.LyricsEntity
import com.catclaw.music.kt.data.db.entity.PlayHistoryEntity
import com.catclaw.music.kt.data.db.entity.PlaySessionEntity
import com.catclaw.music.kt.data.db.entity.SongArtistEntity
import com.catclaw.music.kt.data.db.entity.SongEntity
import kotlinx.coroutines.flow.Flow

/** 列表展示用的联表结果：歌手名 / 专辑名不在 Songs 表里，靠 ArtistId / AlbumId 关联 */
data class SongListItem(
    val id: Int,
    val title: String,
    val artist: String,
    val album: String,
    val durationSeconds: Int,
    val filePath: String,
    val coverArtPath: String?,
    val mediaStoreId: Long,
    /** 远程曲必须带着真实来源与协议回播放层，否则 isRemote / 预缓冲判断会把它当本地文件 */
    val source: Int,
    val protocol: Int,
)

/** 常听排行的一行 */
data class PlayCountRow(
    val id: Int,
    val title: String,
    val artist: String,
    val playCount: Int,
)

/** 远程曲去重用的最小投影：RemoteId 才是服务器侧的稳定主键，DateAdded 要在重扫时原样保留 */
data class RemoteSongKey(val id: Int, val remoteId: String, val dateAdded: Long)

@Dao
interface LibraryDao {

    @Query(
        """
        SELECT s.Id AS id, s.Title AS title, IFNULL(a.Name, '') AS artist,
               IFNULL(al.Title, '') AS album, s.Duration AS durationSeconds,
               s.FilePath AS filePath, s.CoverArtPath AS coverArtPath, s.MediaStoreId AS mediaStoreId,
               s.Source AS source, s.Protocol AS protocol
        FROM Songs s
        LEFT JOIN Artists a ON a.Id = s.ArtistId
        LEFT JOIN Albums al ON al.Id = s.AlbumId
        WHERE s.Source = 0
        ORDER BY s.Title COLLATE NOCASE ASC
        """,
    )
    fun observeLocalSongs(): Flow<List<SongListItem>>

    @Query("SELECT * FROM Songs WHERE Id = :id")
    suspend fun songById(id: Int): SongEntity?

    @Query("SELECT * FROM Songs WHERE FilePath = :path LIMIT 1")
    suspend fun songByPath(path: String): SongEntity?

    /** 猫爪服务器同步按 C# 的做法用 FilePath 去重（`GetLocalSongPathModTimesAsync` 的键集） */
    @Query("SELECT FilePath FROM Songs")
    suspend fun songPaths(): List<String>

    @Query("SELECT COUNT(*) FROM Songs")
    suspend fun songCount(): Int

    /** FilePath 上有唯一索引，重复入库走替换 */
    @Upsert
    suspend fun upsertSong(song: SongEntity): Long

    /** 批量版本：首扫 600+ 曲时逐条提交会把主线程/事务开销堆到十几秒 */
    @Upsert
    suspend fun upsertSongs(songs: List<SongEntity>)

    @Insert
    suspend fun insertSongs(songs: List<SongEntity>): List<Long>

    @Insert
    suspend fun insertArtists(artists: List<ArtistEntity>): List<Long>

    @Insert
    suspend fun insertAlbums(albums: List<AlbumEntity>): List<Long>

    @Upsert
    suspend fun upsertSongArtists(links: List<SongArtistEntity>)

    @Query("SELECT Id AS id, Name AS name FROM Artists")
    suspend fun artistNames(): List<NameId>

    @Query("SELECT Id AS id, Title AS title, ArtistId AS artistId FROM Albums")
    suspend fun albumKeys(): List<AlbumKey>

    @Query("SELECT * FROM Songs WHERE Source = 0")
    suspend fun allLocalSongs(): List<SongEntity>

    /** 本地曲的列表投影：AI 工具要在整库内定位某首歌并一次性起播，所以是 suspend 一次性读而不是 Flow */
    @Query(
        """
        SELECT s.Id AS id, s.Title AS title, COALESCE(a.Name, '') AS artist,
               COALESCE(al.Title, al.Name, '') AS album, s.Duration AS durationSeconds,
               s.FilePath AS filePath, s.CoverArtPath AS coverArtPath, s.MediaStoreId AS mediaStoreId,
               s.Source AS source, s.Protocol AS protocol
        FROM Songs s
        LEFT JOIN Artists a ON a.Id = s.ArtistId
        LEFT JOIN Albums al ON al.Id = s.AlbumId
        WHERE s.Source = 0
        ORDER BY s.Title COLLATE NOCASE ASC
        """,
    )
    suspend fun localSongRows(): List<SongListItem>

    /** 已入库的远程曲（Source<>0），按 RemoteId 去重用 */
    @Query("SELECT Id AS id, RemoteId AS remoteId, DateAdded AS dateAdded FROM Songs WHERE Source <> 0 AND RemoteId IS NOT NULL")
    suspend fun remoteKeys(): List<RemoteSongKey>

    /** 远程曲整表：带歌手名/专辑名，直接可播 */
    @Query(
        """
        SELECT s.Id AS id, s.Title AS title, COALESCE(a.Name, '') AS artist,
               COALESCE(al.Title, al.Name, '') AS album, s.Duration AS durationSeconds,
               s.FilePath AS filePath, s.CoverArtPath AS coverArtPath, s.MediaStoreId AS mediaStoreId,
               s.Source AS source, s.Protocol AS protocol
        FROM Songs s
        LEFT JOIN Artists a ON a.Id = s.ArtistId
        LEFT JOIN Albums al ON al.Id = s.AlbumId
        WHERE s.Source <> 0
        ORDER BY artist, album, s.TrackNumber, s.Title
        """,
    )
    fun observeRemoteSongs(): Flow<List<SongListItem>>

    // ── 网络曲本地缓存映射（CachedSongs）──

    @Upsert
    suspend fun upsertCached(entry: CachedSongEntity): Long

    @Query("SELECT * FROM CachedSongs ORDER BY CachedAt DESC")
    suspend fun cachedSongs(): List<CachedSongEntity>

    @Query("SELECT SongId FROM CachedSongs")
    fun observeCachedSongIds(): Flow<List<Int>>

    @Query(SQL_CACHED_BYTES)
    suspend fun cachedBytes(): Long

    @Query("DELETE FROM CachedSongs")
    suspend fun clearCached()

    @Insert(onConflict = OnConflictStrategy.IGNORE)
    suspend fun insertArtist(artist: ArtistEntity): Long

    @Insert(onConflict = OnConflictStrategy.IGNORE)
    suspend fun insertAlbum(album: AlbumEntity): Long

    @Upsert
    suspend fun upsertSongArtist(link: SongArtistEntity)

    @Query("SELECT Id FROM Artists WHERE Name = :name LIMIT 1")
    suspend fun artistIdByName(name: String): Int?

    @Query("SELECT Id FROM Albums WHERE Title = :title AND ArtistId = :artistId LIMIT 1")
    suspend fun albumIdByTitle(title: String, artistId: Int): Int?

    @Query("UPDATE Songs SET CoverArtPath = :path WHERE Id = :id")
    suspend fun setCoverPath(id: Int, path: String?)

    @Query("UPDATE Songs SET LyricsPath = :path WHERE Id = :id")
    suspend fun setLyricsPath(id: Int, path: String?)

    /** 文件已消失的歌曲：整行删掉并清掉关联，与 C# RemoveStaleSongs 同语义 */
    @Query("DELETE FROM Songs WHERE FilePath IN (:paths)")
    suspend fun deleteSongsByPath(paths: List<String>)

    @Query("DELETE FROM SongArtists WHERE SongId NOT IN (SELECT Id FROM Songs)")
    suspend fun pruneSongArtists()

    @Query("SELECT Id AS id, FilePath AS filePath FROM Songs WHERE Source = 0")
    suspend fun localPaths(): List<SongPath>

    // ── 收藏 ──

    @Upsert
    suspend fun upsertFavorite(favorite: FavoriteEntity)

    @Query("DELETE FROM Favorites WHERE SongId = :songId")
    suspend fun removeFavorite(songId: Int)

    @Query("SELECT EXISTS(SELECT 1 FROM Favorites WHERE SongId = :songId)")
    suspend fun isFavorite(songId: Int): Boolean

    @Query(
        """
        SELECT s.Id AS id, s.Title AS title, IFNULL(a.Name, '') AS artist,
               IFNULL(al.Title, '') AS album, s.Duration AS durationSeconds,
               s.FilePath AS filePath, s.CoverArtPath AS coverArtPath, s.MediaStoreId AS mediaStoreId,
               s.Source AS source, s.Protocol AS protocol
        FROM Favorites f JOIN Songs s ON s.Id = f.SongId
        LEFT JOIN Artists a ON a.Id = s.ArtistId
        LEFT JOIN Albums al ON al.Id = s.AlbumId
        ORDER BY f.AddedAt DESC
        """,
    )
    fun observeFavorites(): Flow<List<SongListItem>>

    // ── 歌词 ──

    @Upsert
    suspend fun upsertLyrics(lyrics: LyricsEntity)

    @Query("SELECT Content FROM Lyrics WHERE SongId = :songId")
    suspend fun lyricsContent(songId: Int): String?

    // ── 播放历史与统计 ──

    @Insert
    suspend fun insertHistory(entry: PlayHistoryEntity): Long

    @Insert
    suspend fun insertSession(entry: PlaySessionEntity): Long

    @Upsert
    suspend fun upsertDailyPlayStat(entry: DailyPlayStatEntity)

    @Query("SELECT * FROM PlayHistory ORDER BY PlayedAt DESC LIMIT :limit")
    suspend fun recentHistory(limit: Int): List<PlayHistoryEntity>

    @Query("SELECT * FROM PlayHistory WHERE SongId = :songId LIMIT 1")
    suspend fun historyOf(songId: Int): PlayHistoryEntity?

    @Query(SQL_TOUCH_HISTORY)
    suspend fun touchHistory(id: Int, playedAt: Long, playCount: Int)

    @Query(SQL_TRIM_HISTORY)
    suspend fun trimHistory(keep: Int)

    /**
     * 记一次聆听 —— 语义照 C# `RecordPlayAsync`：一首歌只留一行，
     * 有行就刷新 PlayedAt 并把 PlayCount +1，没行就建一条，然后把整表裁到最近 200 行。
     *
     * ⚠ "一行一歌"没有数据库约束兜底（PlayHistory 上只有普通索引），全靠这段读-改-写维持，
     * 所以必须整体跑在事务里 —— 这就是 C# 那把 `SemaphoreSlim` 在本端的对应物。
     */
    @Transaction
    suspend fun recordPlay(songId: Int, nowSeconds: Long) {
        val existing = historyOf(songId)
        if (existing == null) {
            insertHistory(PlayHistoryEntity(songId = songId, playedAt = nowSeconds, playCount = 1))
        } else {
            touchHistory(existing.id, nowSeconds, existing.playCount + 1)
        }
        trimHistory(200)
    }

    /** 收藏列表（AI 工具 get_favorite_songs 用），投影与 [observeLocalSongs] 同一套 JOIN */
    @Query(
        """
        SELECT s.Id AS id, s.Title AS title, COALESCE(a.Name, '') AS artist,
               COALESCE(al.Title, al.Name, '') AS album, s.Duration AS durationSeconds,
               s.FilePath AS filePath, s.CoverArtPath AS coverArtPath, s.MediaStoreId AS mediaStoreId,
               s.Source AS source, s.Protocol AS protocol
        FROM Favorites f
        JOIN Songs s ON s.Id = f.SongId
        LEFT JOIN Artists a ON a.Id = s.ArtistId
        LEFT JOIN Albums al ON al.Id = s.AlbumId
        ORDER BY f.AddedAt DESC
        LIMIT :limit
        """,
    )
    suspend fun favoriteSongs(limit: Int): List<SongListItem>

    /** 最近播放过的歌：按 PlayHistory 时间对歌去重后取前 N 首 */
    @Query(SQL_RECENT_SONGS)
    suspend fun recentSongs(limit: Int): List<SongListItem>

    /** 同一张列表的响应式版本：PlayHistory 一变就重查，所以"最近播放"入口的计数不会停在进页那一刻 */
    @Query(SQL_RECENT_SONGS)
    fun observeRecentSongs(limit: Int): Flow<List<SongListItem>>

    // ── 发现页（MAUI 的 SearchPage）用的三条查询 ──

    /** 搜索：标题 / 歌手名 / 专辑名任一命中即可，本地曲优先 */
    @Query(SQL_SEARCH_SONGS)
    suspend fun searchSongs(query: String): List<SongListItem>

    /** 常听：按 PlayHistory 的累计次数排序 */
    @Query(SQL_FREQUENT_SONGS)
    suspend fun frequentSongs(limit: Int): List<SongListItem>

    /** 排行用最小投影：C# `GetTopPlayedSongsAsync` 需要把 PlayCount 一起带回去 */
    @Query(
        """
        SELECT s.Id AS id, s.Title AS title, COALESCE(a.Name, '') AS artist,
               SUM(h.PlayCount) AS playCount
        FROM Songs s
        JOIN PlayHistory h ON h.SongId = s.Id
        LEFT JOIN Artists a ON a.Id = s.ArtistId
        GROUP BY s.Id
        ORDER BY SUM(h.PlayCount) DESC, MAX(h.PlayedAt) DESC
        LIMIT :limit
        """,
    )
    suspend fun topPlayed(limit: Int): List<PlayCountRow>

    /** 新曲：按入库时间倒序 */
    @Query(
        """
        SELECT s.Id AS id, s.Title AS title, COALESCE(a.Name, '') AS artist,
               COALESCE(al.Title, al.Name, '') AS album, s.Duration AS durationSeconds,
               s.FilePath AS filePath, s.CoverArtPath AS coverArtPath, s.MediaStoreId AS mediaStoreId,
               s.Source AS source, s.Protocol AS protocol
        FROM Songs s
        LEFT JOIN Artists a ON a.Id = s.ArtistId
        LEFT JOIN Albums al ON al.Id = s.AlbumId
        WHERE s.Source = 0
        ORDER BY s.DateAdded DESC, s.Id DESC
        LIMIT :limit
        """,
    )
    suspend fun newArrivals(limit: Int): List<SongListItem>

    /** 每日推荐：C# 侧是「随机抽一批」，这里同样随机，只是限定本地曲且限量 */
    @Query(
        """
        SELECT s.Id AS id, s.Title AS title, COALESCE(a.Name, '') AS artist,
               COALESCE(al.Title, al.Name, '') AS album, s.Duration AS durationSeconds,
               s.FilePath AS filePath, s.CoverArtPath AS coverArtPath, s.MediaStoreId AS mediaStoreId,
               s.Source AS source, s.Protocol AS protocol
        FROM Songs s
        LEFT JOIN Artists a ON a.Id = s.ArtistId
        LEFT JOIN Albums al ON al.Id = s.AlbumId
        WHERE s.Source = 0
        ORDER BY RANDOM()
        LIMIT :limit
        """,
    )
    suspend fun dailyRecommendation(limit: Int): List<SongListItem>

    /** PlaySession 只保留最近 N 条（C# 侧为 2000） */
    @Query(SQL_TRIM_SESSIONS)
    suspend fun trimSessions(keep: Int)

    // ── 歌手 / 专辑浏览 ──

    @Query(
        """
        SELECT a.Id AS id, a.Name AS name, a.Cover AS cover,
               (SELECT COUNT(*) FROM Songs s WHERE s.ArtistId = a.Id) AS songCount
        FROM Artists a ORDER BY a.Name COLLATE NOCASE ASC
        """,
    )
    fun observeArtists(): Flow<List<ArtistListItem>>

    @Query(SQL_ALBUMS)
    fun observeAlbums(): Flow<List<AlbumListItem>>

    /** 艺人下钻页的歌（主歌手 + 合作歌手） */
    @Query(SQL_SONGS_OF_ARTIST)
    suspend fun songsOfArtist(artistId: Int): List<SongListItem>

    /** 专辑下钻页的歌，按曲目号排 */
    @Query(SQL_SONGS_OF_ALBUM)
    suspend fun songsOfAlbum(albumId: Int): List<SongListItem>

    @Transaction
    suspend fun upsertSongArtistPair(songId: Int, artistId: Int) {
        upsertSongArtist(SongArtistEntity(songId = songId, artistId = artistId))
    }
}

data class SongPath(val id: Int, val filePath: String)

data class NameId(val id: Int, val name: String)

data class AlbumKey(val id: Int, val title: String, val artistId: Int)

data class ArtistListItem(val id: Int, val name: String, val cover: String?, val songCount: Int)

data class AlbumListItem(
    val id: Int,
    val title: String,
    val artist: String,
    val coverArtPath: String?,
    val songCount: Int,
    /** 代表曲的文件路径：专辑级封面是空的，卡片靠它去读那首歌的内嵌图 */
    val sampleFilePath: String? = null,
)
