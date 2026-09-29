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
import com.catclaw.music.kt.core.stats.PlayStatRules
import com.catclaw.music.kt.core.diagnostic.AppLog
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
    /** epoch 秒，0 = 没有这个信息。全部歌曲页的「入库时间」排序读它（列表 SQL 里用 IFNULL 兜 NULL） */
    val dateAdded: Long = 0,
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
               s.Source AS source, s.Protocol AS protocol, IFNULL(s.DateAdded, 0) AS dateAdded
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

    /** 猫爪服务器同步按 C# 的做法用 FilePath 去重（`GetLocalSongPathModTimesAsync` 的键集） */
    @Query("SELECT FilePath FROM Songs")
    suspend fun songPaths(): List<String>

    @Query("SELECT COUNT(*) FROM Songs")
    suspend fun songCount(): Int

    /**
     * 本地曲库占用的字节数（`Source = 0` 才是本地文件；远程行的 FileSize 是远端大小，
     * 算进来会让"存储占用"变成一个谁也对不上的数）。
     */
    @Query("SELECT IFNULL(SUM(FileSize), 0) FROM Songs WHERE Source = 0")
    suspend fun localBytesTotal(): Long

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
               s.Source AS source, s.Protocol AS protocol, IFNULL(s.DateAdded, 0) AS dateAdded
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

    /**
     * 待补元数据的远程曲 —— 时长没填**或**艺术家没建关联。
     * 判据本身在 `MetadataBackfill.needsBackfill`（对着 C# 的同名查询写的），这里只是把它落进 SQL。
     * 按 Id 排序让每一轮跑的顺序一致，中断后重跑不会换一批。
     */
    @Query("SELECT * FROM Songs WHERE Source = :source AND (Duration <= 0 OR ArtistId = 0) ORDER BY Id")
    suspend fun remoteMissingMetadata(source: Int): List<SongEntity>

    /** 远程曲整表：带歌手名/专辑名，直接可播 */
    @Query(
        """
        SELECT s.Id AS id, s.Title AS title, COALESCE(a.Name, '') AS artist,
               COALESCE(al.Title, al.Name, '') AS album, s.Duration AS durationSeconds,
               s.FilePath AS filePath, s.CoverArtPath AS coverArtPath, s.MediaStoreId AS mediaStoreId,
               s.Source AS source, s.Protocol AS protocol, IFNULL(s.DateAdded, 0) AS dateAdded
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

    /** 下载管理页「删除（含文件）」：只撤这一首的记账，别的缓存不动 */
    @Query("DELETE FROM CachedSongs WHERE SongId = :songId")
    suspend fun removeCached(songId: Int)

    @Insert(onConflict = OnConflictStrategy.IGNORE)
    suspend fun insertArtist(artist: ArtistEntity): Long

    @Insert(onConflict = OnConflictStrategy.IGNORE)
    suspend fun insertAlbum(album: AlbumEntity): Long

    @Query("SELECT Id FROM Artists WHERE Name = :name LIMIT 1")
    suspend fun artistIdByName(name: String): Int?

    @Query("SELECT Id FROM Albums WHERE Title = :title AND ArtistId = :artistId LIMIT 1")
    suspend fun albumIdByTitle(title: String, artistId: Int): Int?

    @Query("UPDATE Songs SET LyricsPath = :path WHERE Id = :id")
    suspend fun setLyricsPath(id: Int, path: String?)

    /** 文件已消失的歌曲：整行删掉并清掉关联，与 C# RemoveStaleSongs 同语义 */
    @Query("DELETE FROM Songs WHERE FilePath IN (:paths)")
    suspend fun deleteSongsByPath(paths: List<String>)

    @Query("DELETE FROM SongArtists WHERE SongId NOT IN (SELECT Id FROM Songs)")
    suspend fun pruneSongArtists()

    /**
     * 清掉一首歌的合作歌手关联。`SongArtists.Id` 是自增主键、没有 (SongId, ArtistId) 唯一索引，
     * 所以重建关联必须**先删再插**，不然每跑一轮补全就多一行，下钻页会出现重复行。
     */
    @Query("DELETE FROM SongArtists WHERE SongId = :songId")
    suspend fun clearSongArtistLinks(songId: Int)

    /**
     * 远程补元数据的落库口子：一整批歌曲行 + 歌手关联，一个事务。
     *
     * 之所以要显式批处理，是让 `RemoteMetadataBackfiller` 只依赖 DAO 而不是整个数据库 ——
     * 那样它的取字节、合并、建歌手专辑这几步能在 JVM 里用假 DAO 测到底（Room 的库对象不行）。
     */
    @Transaction
    suspend fun writeBackfill(songs: List<SongEntity>, links: List<SongArtistEntity>) {
        if (songs.isNotEmpty()) upsertSongs(songs)
        if (links.isNotEmpty()) {
            links.map { it.songId }.distinct().forEach { clearSongArtistLinks(it) }
            upsertSongArtists(links)
        }
    }

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
               s.Source AS source, s.Protocol AS protocol, IFNULL(s.DateAdded, 0) AS dateAdded
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

    @Query("SELECT * FROM PlayHistory WHERE SongId = :songId LIMIT 1")
    suspend fun historyOf(songId: Int): PlayHistoryEntity?

    @Query(SQL_TOUCH_HISTORY)
    suspend fun touchHistory(id: Int, playedAt: Long, playCount: Int)

    @Query(SQL_TRIM_HISTORY)
    suspend fun trimHistory(keep: Int)

    /** 歌曲详情页的一行；歌被删了就回 null，让页面自己退回 */
    @Query(SQL_SONG_DETAIL)
    suspend fun songDetail(id: Int): SongDetailRow?

    /** 发现页「统计」那一格要的真实聚合 */
    @Query(SQL_PLAY_STATS)
    suspend fun playStats(): PlayStats

    // ── 听歌统计页读路径 ──

    /** 范围内的按天汇总；[start]/[end] 传空串表示不限（"全部时间"） */
    @Query(SQL_DAILY_STATS_RANGE)
    suspend fun dailyStatsRange(start: String, end: String): List<DailyStatRow>

    /** 范围内的时段分布，已经折成至多 24 根柱子 */
    @Query(SQL_HOURLY_HOURS_RANGE)
    suspend fun hourlyHoursRange(start: String, end: String): List<HourBucketRow>

    /** 范围内听过几首歌 */
    @Query(SQL_SONGS_HEARD_RANGE)
    suspend fun songsHeardRange(start: String, end: String): Int

    /** 常听歌手榜（全部时间，按播放量） */
    @Query(SQL_TOP_ARTISTS)
    suspend fun topArtists(limit: Int): List<ArtistPlayRow>

    @Query(SQL_SESSION_COUNT)
    suspend fun playSessionCount(): Int

    @Query(SQL_SUMMARY_PLAY_TOTAL)
    suspend fun summaryPlayTotal(): Int

    /** 深夜判据按参数传进来，规则本体只存在于 [PlayStatRules] 一处 */
    @Query(SQL_REBUILD_DAILY)
    suspend fun insertRebuiltDaily(nightStart: Int, nightEnd: Int)

    @Query(SQL_REBUILD_HOURLY)
    suspend fun insertRebuiltHourly()

    @Query(SQL_REBUILD_SONG)
    suspend fun insertRebuiltSong()

    /**
     * 老库升级兼容：汇总三表只在"汇总总次数落后于会话条数"时才从 `PlaySession` 重建。
     *
     * 判据照 C# 同一条，且顺序不能反 —— 先清表再判就等于每次都把干净的数据毁一遍。
     * @return 是否真的重建了（统计页据此决定要不要重跑一次查询）
     */
    @Transaction
    suspend fun rebuildStatsFromSessionsIfNeeded(): Boolean {
        val sessions = playSessionCount()
        if (sessions == 0) return false
        if (summaryPlayTotal() >= sessions) return false
        AppLog.i("CatClawStats", "rebuild summaries from $sessions sessions")
        clearDailyStats()
        clearHourlyStats()
        clearSongStats()
        insertRebuiltDaily(PlayStatRules.NIGHT_START_HOUR, PlayStatRules.NIGHT_END_HOUR)
        insertRebuiltHourly()
        insertRebuiltSong()
        return true
    }

    @Query("DELETE FROM DailyPlayStat")
    suspend fun clearDailyStats()

    @Query("DELETE FROM HourlyPlayStat")
    suspend fun clearHourlyStats()

    @Query("DELETE FROM DailySongStat")
    suspend fun clearSongStats()

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

    @Insert
    suspend fun insertSession(entry: PlaySessionEntity): Long

    @Query(SQL_SESSION_BY_ID)
    suspend fun sessionById(id: Int): PlaySessionEntity?

    @Query(SQL_FLUSH_SESSION)
    suspend fun updateSession(id: Int, durationMs: Long, playedAt: Long)

    @Query(SQL_BUMP_DAILY)
    suspend fun bumpDaily(date: String, plays: Int, ms: Long, nightPlays: Int)

    @Query(SQL_BUMP_HOURLY)
    suspend fun bumpHourly(dateHour: String, date: String, hour: Int, plays: Int, ms: Long)

    @Query(SQL_BUMP_SONG)
    suspend fun bumpSong(dateSong: String, date: String, songId: Int, plays: Int, ms: Long)

    /**
     * 一次聆听开始：建会话行 + 三张汇总表各 +1 次（时长此刻记 0，之后由 [flushListenSession] 累加）。
     * 对应 C# `LogListenSessionAsync`。返回自增行 id，调用方要留着它做 flush。
     */
    @Transaction
    suspend fun logListenSession(songId: Int, playedAt: Long, keys: PlayStatRules.Keys): Long {
        val id = insertSession(PlaySessionEntity(songId = songId, playedAt = playedAt))
        bumpDaily(keys.date, 1, 0, if (keys.night) 1 else 0)
        bumpHourly(keys.dateHour, keys.date, keys.hour, 1, 0)
        bumpSong(keys.dateSong, keys.date, songId, 1, 0)
        trimSessions(MAX_SESSIONS_KEPT)
        return id
    }

    /**
     * 把一次聆听的累计时长写回**同一行**，并按增量更新汇总表。
     * 每 30 秒 flush 一次却不新增行，就是为了别把「总播放次数」「听歌趋势」放大
     * （C# 同注释）。
     *
     * 增量的日期键取自该行**上一次**的 PlayedAt：跨午夜的那次聆听归到开始那天，
     * 而不是被最后一次的落库时刻带到第二天去。
     *
     * @param totalMs 本次聆听到此为止的总时长（毫秒），不是增量
     */
    @Transaction
    suspend fun flushListenSession(sessionId: Int, totalMs: Long, nowSeconds: Long): Long {
        if (sessionId <= 0) return 0
        val row = sessionById(sessionId) ?: return 0
        val next = totalMs.coerceAtLeast(0)
        val delta = next - row.durationMs.coerceAtLeast(0)
        updateSession(sessionId, next, nowSeconds)
        if (delta != 0L) {
            val keys = PlayStatRules.keysFor(row.playedAt, row.songId)
            bumpDaily(keys.date, 0, delta, 0)
            bumpHourly(keys.dateHour, keys.date, keys.hour, 0, delta)
            bumpSong(keys.dateSong, keys.date, row.songId, 0, delta)
        }
        return delta
    }

    /** 收藏列表（AI 工具 get_favorite_songs 用），投影与 [observeLocalSongs] 同一套 JOIN */
    @Query(
        """
        SELECT s.Id AS id, s.Title AS title, COALESCE(a.Name, '') AS artist,
               COALESCE(al.Title, al.Name, '') AS album, s.Duration AS durationSeconds,
               s.FilePath AS filePath, s.CoverArtPath AS coverArtPath, s.MediaStoreId AS mediaStoreId,
               s.Source AS source, s.Protocol AS protocol, IFNULL(s.DateAdded, 0) AS dateAdded
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
               s.Source AS source, s.Protocol AS protocol, IFNULL(s.DateAdded, 0) AS dateAdded
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
               s.Source AS source, s.Protocol AS protocol, IFNULL(s.DateAdded, 0) AS dateAdded
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

    companion object {
        /** 会话只保留最近 N 条（与 C# `TrimPlaySessionAsync(2000)` 同值） */
        const val MAX_SESSIONS_KEPT = 2000
    }
}

/** 歌曲详情：基本信息 + 文件与音质 + 播放次数，字段口径与 C# 详情页一致 */
data class SongDetailRow(
    val id: Int,
    val title: String,
    val artist: String,
    val album: String,
    val genre: String?,
    val year: Int,
    val trackNumber: Int,
    val durationSeconds: Int,
    val bitrate: Int,
    val fileSize: Long,
    val filePath: String,
    val coverArtPath: String?,
    val lyricsPath: String?,
    val source: Int,
    val playCount: Int,
    /** epoch 秒；0 = 没这个信息（详情页画占位而不是 1970） */
    val dateAdded: Long,
    val dateModified: Long,
    /** 主歌手 / 所属专辑的 id；0 表示这首歌没有归属（详情菜单据此决定画不画「查看歌手」） */
    val artistId: Int,
    val albumId: Int,
)

/** 播放统计：播过的歌数 + 总播放次数 */
data class PlayStats(val songsPlayed: Int, val totalPlays: Int)

/** 统计页的一天：`yyyy-MM-dd` + 当天次数 / 时长 / 深夜次数 */
data class DailyStatRow(
    val date: String,
    val plays: Int,
    val ms: Long,
    val nightPlays: Int,
)

/** 统计页的一根时段柱：某个钟点在范围内的合计 */
data class HourBucketRow(val hour: Int, val plays: Int, val ms: Long)

/** 常听歌手榜的一行。[name] 为空串表示这首歌没登记歌手，由 UI 决定显示什么 */
data class ArtistPlayRow(val name: String, val plays: Int)

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
