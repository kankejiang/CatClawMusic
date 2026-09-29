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
