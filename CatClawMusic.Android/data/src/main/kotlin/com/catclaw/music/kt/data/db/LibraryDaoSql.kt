package com.catclaw.music.kt.data.db

/**
 * 发现页两条排序 SQL 提成常量，好让 `LibraryDaoSqlTest` 能拿**真 SQLite** 执行它们。
 *
 * 值得钉的是这两处"写错了也不报错"的细节：
 * 1. `SQL_RECENT_SONGS` 的 `LIMIT` 在**子查询里**、JOIN 之前。
 *    挪到 JOIN 之后就变成"按历史行数取前 N"—— 同一首歌有 5 条历史就会挤掉 4 个名额，
 *    结果"最近播放"里全是同一批反复听的歌，而且不抛异常、只是数据不对。
 * 2. `SQL_FREQUENT_SONGS` 的次序是 `SUM(PlayCount) DESC, MAX(PlayedAt) DESC`：
 *    累计次数并列时靠"最近一次播放"定序，缺第二个键就会出现每次都不同的不稳定排序。
 */
internal const val SQL_RECENT_SONGS = """
    SELECT s.Id AS id, s.Title AS title, COALESCE(a.Name, '') AS artist,
           COALESCE(al.Title, al.Name, '') AS album, s.Duration AS durationSeconds,
           s.FilePath AS filePath, s.CoverArtPath AS coverArtPath, s.MediaStoreId AS mediaStoreId,
           s.Source AS source, s.Protocol AS protocol
    FROM (
        SELECT SongId, MAX(PlayedAt) AS lastPlayed FROM PlayHistory
        GROUP BY SongId ORDER BY lastPlayed DESC LIMIT :limit
    ) h
    JOIN Songs s ON s.Id = h.SongId
    LEFT JOIN Artists a ON a.Id = s.ArtistId
    LEFT JOIN Albums al ON al.Id = s.AlbumId
    ORDER BY h.lastPlayed DESC
"""

internal const val SQL_FREQUENT_SONGS = """
    SELECT s.Id AS id, s.Title AS title, COALESCE(a.Name, '') AS artist,
           COALESCE(al.Title, al.Name, '') AS album, s.Duration AS durationSeconds,
           s.FilePath AS filePath, s.CoverArtPath AS coverArtPath, s.MediaStoreId AS mediaStoreId,
           s.Source AS source, s.Protocol AS protocol
    FROM Songs s
    JOIN PlayHistory h ON h.SongId = s.Id
    LEFT JOIN Artists a ON a.Id = s.ArtistId
    LEFT JOIN Albums al ON al.Id = s.AlbumId
    GROUP BY s.Id
    ORDER BY SUM(h.PlayCount) DESC, MAX(h.PlayedAt) DESC
    LIMIT :limit
"""

/**
 * 搜索。两处值得钉：
 * 1. 关键词是**拼进 LIKE 的模式**（`'%' || :query || '%'`），所以用户输入里的 `%` 与 `_`
 *    会被 SQLite 当成通配符 —— 这不是注入（参数仍是绑定的），但会让"搜 100%"返回一堆不相干的歌。
 * 2. 排序第一键是 `s.Source ASC`：`Local=0` 排在远程来源之前，这是有意的本地优先，
 *    与 `SongSource` 的 ordinal 契约绑在一起（ordinal 由 `SourceOrdinalParityTest` 钉住）。
 */
internal const val SQL_SEARCH_SONGS = """
    SELECT s.Id AS id, s.Title AS title, COALESCE(a.Name, '') AS artist,
           COALESCE(al.Title, al.Name, '') AS album, s.Duration AS durationSeconds,
           s.FilePath AS filePath, s.CoverArtPath AS coverArtPath, s.MediaStoreId AS mediaStoreId,
           s.Source AS source, s.Protocol AS protocol
    FROM Songs s
    LEFT JOIN Artists a ON a.Id = s.ArtistId
    LEFT JOIN Albums al ON al.Id = s.AlbumId
    WHERE s.Title LIKE '%' || :query || '%' OR a.Name LIKE '%' || :query || '%'
       OR al.Title LIKE '%' || :query || '%' OR al.Name LIKE '%' || :query || '%'
    ORDER BY s.Source ASC, s.Title COLLATE NOCASE ASC
    LIMIT 120
"""

/**
 * 缓存总字节数。`SUM` 在空表（或全 NULL）时返回 **NULL** 而不是 0，
 * 而这条的值直接喂设置页的"已用 X / 上限 N MB"，Room 侧返回类型又是非空 `Long` ——
 * 少了 COALESCE 就是空表时抛异常。
 */
internal const val SQL_CACHED_BYTES = "SELECT COALESCE(SUM(FileSize), 0) FROM CachedSongs"

/**
 * 记一次聆听时对那一行的刷新。
 *
 * 单独拿出来是因为 `PlayHistory` 上**没有** SongId 唯一索引（参考 schema 里只是普通索引），
 * "一首歌一行"没有任何数据库约束兜底，只能靠 `recordPlay` 那段读-改-写维持。
 */
internal const val SQL_TOUCH_HISTORY =
    "UPDATE PlayHistory SET PlayedAt = :playedAt, PlayCount = :playCount WHERE Id = :id"

/** 历史只留最近 keep 行（C# `TrimHistoryAsync(200)`）。次级键 Id DESC 让并列时的取舍确定下来 */
internal const val SQL_TRIM_HISTORY = """
    DELETE FROM PlayHistory WHERE Id NOT IN (
        SELECT Id FROM PlayHistory ORDER BY PlayedAt DESC, Id DESC LIMIT :keep
    )
"""

/**
 * 播放会话裁剪：只保留最近 keep 条。
 *
 * 加了次级键 `Id DESC`，把并列时的顺序从"依赖排序器的偶然行为"变成显式保证
 * （SQL 里 `ORDER BY PlayedAt DESC` 遇到相同时间戳，保留哪几条是未定义的）。
 *
 * ⚠ 但我实测过：**本机的 SQLite 对 `ORDER BY x DESC` 的并列项实际按 rowid 倒序返回，
 * 所以旧写法今天并不会删错** —— 这一条是健壮性改动，不是 bug 修复，
 * 别把它记成"曾经会删掉最新一条"。（`LibraryCacheSqlTest` 里那条测试就是钉这个事实的。）
 */
internal const val SQL_TRIM_SESSIONS = """
    DELETE FROM PlaySession WHERE Id NOT IN (
        SELECT Id FROM PlaySession ORDER BY PlayedAt DESC, Id DESC LIMIT :keep
    )
"""

/**
 * 三张汇总表的增量 UPSERT —— 逐条对齐 C# `IncrementDailyStatsAsync`。
 *
 * 用 `excluded.` 而不是把新值算好再写：加法的唯一事实源是数据库里那一行，
 * 两次并发聆听才不会互相覆盖（本端 recordPlay 也走同一条规矩）。
 * 深夜计数只在"这次算出来是深夜"时才加，且只随**次数**加、不随时长加
 * （时长是 flush 出来的，那时已经不属于"开始那一刻"了）。
 */
internal const val SQL_BUMP_DAILY =
    "INSERT INTO DailyPlayStat(Date, PlayCount, TotalDurationMs, NightPlayCount) " +
        "VALUES (:date, :plays, :ms, :nightPlays) " +
        "ON CONFLICT(Date) DO UPDATE SET PlayCount = PlayCount + excluded.PlayCount, " +
        "TotalDurationMs = TotalDurationMs + excluded.TotalDurationMs, " +
        "NightPlayCount = NightPlayCount + excluded.NightPlayCount"

internal const val SQL_BUMP_HOURLY =
    "INSERT INTO HourlyPlayStat(DateHour, Date, Hour, PlayCount, TotalDurationMs) " +
        "VALUES (:dateHour, :date, :hour, :plays, :ms) " +
        "ON CONFLICT(DateHour) DO UPDATE SET PlayCount = PlayCount + excluded.PlayCount, " +
        "TotalDurationMs = TotalDurationMs + excluded.TotalDurationMs"

internal const val SQL_BUMP_SONG =
    "INSERT INTO DailySongStat(DateSong, Date, SongId, PlayCount, TotalDurationMs) " +
        "VALUES (:dateSong, :date, :songId, :plays, :ms) " +
        "ON CONFLICT(DateSong) DO UPDATE SET PlayCount = PlayCount + excluded.PlayCount, " +
        "TotalDurationMs = TotalDurationMs + excluded.TotalDurationMs"

/** 把一次聆听的累计时长写回同一行；PlayedAt 同步推到当下（行的归属日期用旧值算，见 flushListenSession） */
internal const val SQL_FLUSH_SESSION =
    "UPDATE PlaySession SET DurationMs = :durationMs, PlayedAt = :playedAt WHERE Id = :id"

internal const val SQL_SESSION_BY_ID = "SELECT * FROM PlaySession WHERE Id = :id"

/**
 * 某个艺人的全部歌曲 —— 对应 C# `MusicDatabase.GetSongsByArtistAsync`。
 *
 * 两处与 C# 有意不同，都是往"更准"的方向偏：
 * 1. **按 Id 匹配而不是按 Name**：C# 那句 `WHERE a.Name = ? OR a2.Name = ?` 会把两个同名
 *    不同人的歌手合并成一个人（曲库里 "Jay" 这种艺名并不罕见）。
 * 2. 合作歌手用 `IN (子查询)` 而不是 `LEFT JOIN SongArtists` + DISTINCT：
 *    JOIN 会让一首挂 N 个歌手的歌先膨胀成 N 行再去重，子查询不会。
 *    两者结果集相同，这一点由 `LibraryDaoSqlTest` 钉住。
 */
internal const val SQL_SONGS_OF_ARTIST = """
    SELECT DISTINCT s.Id AS id, s.Title AS title, COALESCE(a.Name, '') AS artist,
           COALESCE(al.Title, '') AS album, s.Duration AS durationSeconds,
           s.FilePath AS filePath, s.CoverArtPath AS coverArtPath, s.MediaStoreId AS mediaStoreId,
           s.Source AS source, s.Protocol AS protocol
    FROM Songs s
    LEFT JOIN Artists a ON a.Id = s.ArtistId
    LEFT JOIN Albums al ON al.Id = s.AlbumId
    WHERE s.ArtistId = :artistId
       OR s.Id IN (SELECT sa.SongId FROM SongArtists sa WHERE sa.ArtistId = :artistId)
    ORDER BY s.Source ASC, s.Title COLLATE NOCASE ASC
"""

/**
 * 某张专辑的全部歌曲 —— 对应 C# `GetSongsByAlbumAsync`（它没有 ORDER BY，等于按 rowid 出）。
 *
 * 这里显式按曲目号排：专辑的常识顺序是 1 轨、2 轨……，而扫描入库的顺序是文件遍历顺序，
 * 两者不一致时"看着像乱序"。TrackNumber 为 0（没标签）的歌排在有号的后面，再按标题。
 */
internal const val SQL_SONGS_OF_ALBUM = """
    SELECT s.Id AS id, s.Title AS title, COALESCE(a.Name, '') AS artist,
           COALESCE(al.Title, '') AS album, s.Duration AS durationSeconds,
           s.FilePath AS filePath, s.CoverArtPath AS coverArtPath, s.MediaStoreId AS mediaStoreId,
           s.Source AS source, s.Protocol AS protocol
    FROM Songs s
    LEFT JOIN Artists a ON a.Id = s.ArtistId
    JOIN Albums al ON al.Id = s.AlbumId
    WHERE s.AlbumId = :albumId
    ORDER BY CASE WHEN s.TrackNumber > 0 THEN 0 ELSE 1 END, s.TrackNumber ASC,
             s.Title COLLATE NOCASE ASC
"""

/**
 * 专辑列表。带一首代表曲的文件路径：本地曲的专辑级 `CoverArtPath` 一直是空的
 * （本端没有 MAUI 那台 CoverHelper 抽图引擎），所以卡片要出图只能借代表曲的内嵌图。
 * 代表曲取「最小曲目号的那首」，与专辑页里第一行的歌一致。
 */
internal const val SQL_ALBUMS = """
    SELECT al.Id AS id, al.Title AS title, IFNULL(a.Name, '') AS artist,
           al.CoverArtPath AS coverArtPath, al.SongCount AS songCount,
           (SELECT s.FilePath FROM Songs s WHERE s.AlbumId = al.Id
             ORDER BY CASE WHEN s.TrackNumber > 0 THEN 0 ELSE 1 END, s.TrackNumber ASC, s.Id ASC
             LIMIT 1) AS sampleFilePath
    FROM Albums al LEFT JOIN Artists a ON a.Id = al.ArtistId
    ORDER BY al.Title COLLATE NOCASE ASC
"""

/**
 * 播放统计的两个真实聚合：播过的歌数与总播放次数。
 *
 * 空表时 `SUM` 返回 NULL 而不是 0（和缓存字节数那条同一个坑），所以 COALESCE 不能省；
 * `COUNT(DISTINCT SongId)` 而不是行数：一行一歌是 recordPlay 维持的不变量，
 * 但那是代码维持的，不是数据库约束，统计口径不该依赖它没被写坏。
 */
internal const val SQL_PLAY_STATS = """
    SELECT COUNT(DISTINCT SongId) AS songsPlayed, COALESCE(SUM(PlayCount), 0) AS totalPlays
    FROM PlayHistory
"""

/**
 * 歌曲详情的一行 —— 对应 C# `SongDetailViewModel` 读的那些字段。
 *
 * 播放次数用子查询而不是 JOIN：`PlayHistory` 是"一行一歌"由代码维持的不变量，
 * 一旦哪天写坏了，JOIN 会把一首歌变成多行详情，子查询最多把次数加总。
 */
internal const val SQL_SONG_DETAIL = """
    SELECT s.Id AS id, s.Title AS title, IFNULL(a.Name, '') AS artist,
           IFNULL(al.Title, '') AS album, s.Genre AS genre, s.Year AS year,
           s.TrackNumber AS trackNumber, s.Duration AS durationSeconds, s.Bitrate AS bitrate,
           s.FileSize AS fileSize, s.FilePath AS filePath, s.CoverArtPath AS coverArtPath,
           s.LyricsPath AS lyricsPath, s.Source AS source,
           IFNULL(s.ArtistId, 0) AS artistId, IFNULL(s.AlbumId, 0) AS albumId,
           (SELECT COALESCE(SUM(h.PlayCount), 0) FROM PlayHistory h WHERE h.SongId = s.Id) AS playCount
    FROM Songs s
    LEFT JOIN Artists a ON a.Id = s.ArtistId
    LEFT JOIN Albums al ON al.Id = s.AlbumId
    WHERE s.Id = :id
"""

/*
 * ── 听歌统计读路径（对应 C# `MusicDatabase.PlayHistory.cs` 的 Get*PlayStatsAsync）──
 *
 * 三个范围查询共用同一个"空串=不限"写法，而不是像 C# 那样分两条查询：
 * 少一条 SQL、也少一个"哪条忘了加 WHERE"的分支。Date 是 `yyyy-MM-dd`，
 * 字符串序即日期序，所以 >= / <= 直接可比 —— 也正因如此，写侧的日期格式是契约的一部分。
 */

/** 按天汇总。空表时 `SUM` 回 NULL 而不是 0，所以每列都 COALESCE。 */
internal const val SQL_DAILY_STATS_RANGE = """
    SELECT Date AS date,
           COALESCE(PlayCount, 0) AS plays,
           COALESCE(TotalDurationMs, 0) AS ms,
           COALESCE(NightPlayCount, 0) AS nightPlays
    FROM DailyPlayStat
    WHERE (:start = '' OR Date >= :start) AND (:end = '' OR Date <= :end)
    ORDER BY Date
"""

/**
 * 时段分布：把范围内每一天的小时行合成 24 根柱子。
 *
 * 聚合放在 SQL 里而不是把每天的每小时行拉到内存 —— 全部时间范围下这是几千行，
 * 而 UI 只需要 24 个格子。
 */
internal const val SQL_HOURLY_HOURS_RANGE = """
    SELECT Hour AS hour,
           COALESCE(SUM(PlayCount), 0) AS plays,
           COALESCE(SUM(TotalDurationMs), 0) AS ms
    FROM HourlyPlayStat
    WHERE (:start = '' OR Date >= :start) AND (:end = '' OR Date <= :end)
    GROUP BY Hour
    ORDER BY Hour
"""

/** 范围内听过几首歌。`COUNT(DISTINCT SongId)` 而不是行数：同一首听两天算一首。 */
internal const val SQL_SONGS_HEARD_RANGE = """
    SELECT COUNT(DISTINCT SongId)
    FROM DailySongStat
    WHERE (:start = '' OR Date >= :start) AND (:end = '' OR Date <= :end)
"""

/**
 * 常听歌手榜 —— 对应 C# `GetTopPlayedArtistsAsync`。
 *
 * C# 是把每首歌的"全部歌手"拼成 `"A / B"` 再按 `"/"` 拆开累加；这里用 `SongArtists`
 * 直接 JOIN，一行就是一个（歌, 歌手），天然就是这个口径，不必再拆字符串。
 * 内层先按 (歌手, 歌) 收一次，保证一首歌对它每个歌手只贡献自己那一份播放量
 * （`SongArtists` 没有 (SongId, ArtistId) 唯一索引，重复行不能让它算两遍）。
 * 没有关联行时退回主歌手；主歌手也空 → 名字留空串，由 UI 决定显示什么，
 * 榜单里出现硬编码的"未知歌手"就再也翻不了译。
 *
 * 两处 `GROUP BY` 用**序号**而不是列名：`artistId` / `name` 在这两条查询里既是输出别名、
 * 又是 `s.ArtistId`/`a.Name` 这样的输入列，SQLite 解析 GROUP BY 时会报
 * `ambiguous column name`（Room 在校验阶段就拦下了）。
 */
internal const val SQL_TOP_ARTISTS = """
    SELECT CASE WHEN a.Name IS NULL OR TRIM(a.Name) = '' THEN '' ELSE TRIM(a.Name) END AS name,
           SUM(t.plays) AS plays
    FROM (
        SELECT CASE WHEN sa.ArtistId IS NULL OR sa.ArtistId = 0 THEN s.ArtistId ELSE sa.ArtistId END AS artistId,
               h.SongId AS songId,
               SUM(h.PlayCount) AS plays
        FROM PlayHistory h
        JOIN Songs s ON s.Id = h.SongId
        LEFT JOIN (SELECT DISTINCT SongId, ArtistId FROM SongArtists) sa ON sa.SongId = s.Id
        GROUP BY 1, 2
    ) t
    LEFT JOIN Artists a ON a.Id = t.artistId
    GROUP BY 1
    ORDER BY plays DESC, name ASC
    LIMIT :limit
"""

/**
 * 汇总表落后于会话日志时，从 `PlaySession` 重建三张汇总表 —— C# `RebuildDailyStatsIfNeededAsync`。
 *
 * 为什么还需要它：安卓端读的是用户既有的库文件，很老的版本只写过 `PlaySession`，
 * 那种库直接进统计页会是"一首都没听过"。而重建只看得见留下来的会话
 * （`PlaySession` 裁到最近 2000 条），所以判据是 C# 同一条：**会话条数 > 汇总总次数** 才动手，
 * 平时一条 SQL 都不发。
 *
 * 日期/小时全按**本地时区**取，和 [com.catclaw.music.kt.core.stats.PlayStatRules] 同一口径；
 * 用 UTC 取会把东八区用户的深夜听歌整段挪到白天。深夜判据走参数，规则只在 Kotlin 里存一份。
 */
internal const val SQL_REBUILD_DAILY = """
    INSERT INTO DailyPlayStat(Date, PlayCount, TotalDurationMs, NightPlayCount)
    SELECT date(PlayedAt, 'unixepoch', 'localtime') AS d,
           COUNT(*),
           COALESCE(SUM(MAX(DurationMs, 0)), 0),
           COALESCE(SUM(CASE WHEN CAST(strftime('%H', PlayedAt, 'unixepoch', 'localtime') AS INTEGER) >= :nightStart
                              OR CAST(strftime('%H', PlayedAt, 'unixepoch', 'localtime') AS INTEGER) < :nightEnd
                             THEN 1 ELSE 0 END), 0)
    FROM PlaySession
    GROUP BY d
"""

/** `DateHour` 的形状是 `"yyyy-MM-dd HH"`（中间一个空格、小时两位），和写侧的键完全一致 */
internal const val SQL_REBUILD_HOURLY = """
    INSERT INTO HourlyPlayStat(DateHour, Date, Hour, PlayCount, TotalDurationMs)
    SELECT strftime('%Y-%m-%d %H', PlayedAt, 'unixepoch', 'localtime') AS dh,
           date(PlayedAt, 'unixepoch', 'localtime'),
           CAST(strftime('%H', PlayedAt, 'unixepoch', 'localtime') AS INTEGER),
           COUNT(*),
           COALESCE(SUM(MAX(DurationMs, 0)), 0)
    FROM PlaySession
    GROUP BY dh
"""

/** `DateSong` 是 `yyyy-MM-dd|songId`（**竖线**）；写成下划线就会和写侧变成两份数据 */
internal const val SQL_REBUILD_SONG = """
    INSERT INTO DailySongStat(DateSong, Date, SongId, PlayCount, TotalDurationMs)
    SELECT date(PlayedAt, 'unixepoch', 'localtime') || '|' || SongId AS ds,
           date(PlayedAt, 'unixepoch', 'localtime'),
           SongId,
           COUNT(*),
           COALESCE(SUM(MAX(DurationMs, 0)), 0)
    FROM PlaySession
    GROUP BY date(PlayedAt, 'unixepoch', 'localtime'), SongId
"""

/** 重建前的判据：会话条数 */
internal const val SQL_SESSION_COUNT = "SELECT COUNT(*) FROM PlaySession"

/** 重建前的判据：汇总表已记录的总次数 */
internal const val SQL_SUMMARY_PLAY_TOTAL = "SELECT COALESCE(SUM(PlayCount), 0) FROM DailyPlayStat"
