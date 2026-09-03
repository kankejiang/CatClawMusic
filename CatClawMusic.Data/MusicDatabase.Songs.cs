using CatClawMusic.Core.Models;
using CatClawMusic.Core.Services;
using SQLite;
using CatClawMusic.Core.Interfaces;

namespace CatClawMusic.Data;

/// <summary>SQLite 数据库操作层 —— partial 分域文件之一。</summary>
public partial class MusicDatabase
{
    public async Task<HashSet<ProtocolType>> GetEnabledProtocolsAsync()
    {
        await EnsureMaintenanceCompletedAsync();
        var profiles = await _database.Table<ConnectionProfile>().ToListAsync();
        var enabled = new HashSet<ProtocolType>();
        foreach (var p in profiles)
        {
            if (p.IsEnabled)
                enabled.Add(p.Protocol);
        }
        return enabled;
    }

    /// <summary>
    /// 过滤歌曲列表，移除来自已关闭协议的歌曲
    /// </summary>
    public List<Song> FilterByEnabledProtocols(List<Song> songs, HashSet<ProtocolType> enabledProtocols)
    {
        return songs.Where(s =>
        {
            if (s.Source == SongSource.Local) return true;
            if (s.Source == SongSource.Cache) return true;
            return enabledProtocols.Contains(s.Protocol);
        }).ToList();
    }

    /// <summary>
    /// 获取所有本地歌曲（含艺术家和专辑详情）
    /// </summary>
    /// <returns>本地歌曲列表</returns>
    /// <summary>
    /// 获取本地歌曲列表（轻量版）：仅加载基础信息和艺术家/专辑名称，
    /// 跳过 PlayHistory 聚合和多艺术家关联（这两项是性能瓶颈）。
    /// 用于列表页面展示，播放详情页再用 GetSongByIdAsync 加载完整信息。
    /// </summary>
    public async Task<List<Song>> GetSongsAsync()
    {
        await EnsureMaintenanceCompletedAsync().ConfigureAwait(false);
        var songs = await _database.Table<Song>().Where(s => s.Source == SongSource.Local).ToListAsync().ConfigureAwait(false);
        var artists = await _database.Table<Artist>().ToListAsync().ConfigureAwait(false);
        var albums = await _database.Table<Album>().ToListAsync().ConfigureAwait(false);
        var artistDict = SafeToDict(artists, a => a.Id, a => a.Name);
        var albumDict = SafeToDict(albums, a => a.Id, a => a.Title);
        foreach (var s in songs)
        {
            s.Artist = artistDict.TryGetValue(s.ArtistId, out var an) ? an : "未知艺术家";
            s.Album = albumDict.TryGetValue(s.AlbumId, out var al) ? al : "未知专辑";
        }
        return songs;
    }

    /// <summary>
    /// 获取本地歌曲总数
    /// </summary>
    /// <returns>本地歌曲数量</returns>
    public async Task<int> GetLocalSongCountAsync()
    {
        await EnsureMaintenanceCompletedAsync();
        return await _database.Table<Song>().Where(s => s.Source == SongSource.Local).CountAsync();
    }

    /// <summary>获取缺失时长的本地歌曲（扫描时基于性能跳过读 duration 导致 Duration=0，供后台回填）</summary>
    public async Task<List<Song>> GetLocalSongsMissingDurationAsync()
    {
        await EnsureMaintenanceCompletedAsync();
        return await _database.Table<Song>()
            .Where(s => s.Source == SongSource.Local && s.Duration <= 0)
            .ToListAsync();
    }

    /// <summary>批量回填歌曲时长（单事务，符合批量写约定）</summary>
    public Task UpdateSongDurationsBatchAsync(IReadOnlyDictionary<int, int> durations)
    {
        if (durations == null || durations.Count == 0) return Task.CompletedTask;
        return _database.RunInTransactionAsync(tran =>
        {
            foreach (var kv in durations)
            {
                if (kv.Key > 0 && kv.Value > 0)
                    tran.Execute("UPDATE Song SET Duration = ? WHERE Id = ?", kv.Value, kv.Key);
            }
        });
    }

    /// <summary>回填歌曲时长（扫描时跳过 duration 提速，播放时由播放器拿到真实值后补写）</summary>
    public Task UpdateSongDurationAsync(int songId, int durationSeconds)
    {
        if (songId <= 0 || durationSeconds <= 0) return Task.CompletedTask;
        return _database.ExecuteAsync("UPDATE Song SET Duration = ? WHERE Id = ?", durationSeconds, songId);
    }

    /// <summary>
    /// 获取网络歌曲总数（WebDAV + SMB + 缓存的网络歌曲）
    /// </summary>
    /// <returns>网络歌曲数量</returns>
    public async Task<int> GetNetworkSongCountAsync()
    {
        await EnsureMaintenanceCompletedAsync();
        return await _database.Table<Song>()
            .Where(s => s.Source == SongSource.WebDAV || s.Source == SongSource.SMB || s.Source == SongSource.Cache)
            .CountAsync();
    }

    /// <summary>
    /// 获取去重后的歌曲总数（仅统计本地歌曲，与本地音乐标签页一致）
    /// </summary>
    /// <returns>本地歌曲数量</returns>
    public async Task<int> GetMergedDedupedCountAsync()
    {
        await EnsureMaintenanceCompletedAsync();
        // 只统计本地歌曲数量，与本地音乐标签页一致
        return await _database.Table<Song>()
            .Where(s => s.Source == SongSource.Local)
            .CountAsync();
    }

    /// <summary>
    /// 获取收藏歌曲总数
    /// </summary>
    /// <returns>收藏记录数量</returns>
    public async Task<int> GetFavoriteCountAsync()
    {
        await EnsureMaintenanceCompletedAsync();
        return await _database.Table<Favorite>().CountAsync();
    }

    /// <summary>
    /// 获取最近播放歌曲数量（仅统计 Songs 表中仍存在的歌曲）
    /// </summary>
    /// <returns>最近播放且未删除的歌曲数量</returns>
    public async Task<int> GetRecentPlayCountAsync()
    {
        await EnsureMaintenanceCompletedAsync();
        var history = await _database.Table<PlayHistory>().OrderByDescending(h => h.PlayedAt).Take(200).ToListAsync();
        if (history.Count == 0) return 0;
        var songIds = history.Select(h => h.SongId).ToHashSet();
        // 只统计 Songs 表中仍存在的歌曲数量（IN 查询，而非整表加载后内存过滤）
        return await _database.Table<Song>().CountAsync(s => songIds.Contains(s.Id));
    }

    /// <summary>
    /// 获取"全部音乐"中第一首歌曲的 ID（优先返回本地歌曲）
    /// </summary>
    /// <returns>歌曲 ID，无歌曲时返回 0</returns>
    public async Task<int> GetFirstSongIdForAllAsync()
    {
        await EnsureMaintenanceCompletedAsync();
        var song = await _database.Table<Song>().Where(s => s.Source == SongSource.Local).FirstOrDefaultAsync();
        if (song != null) return song.Id;
        song = await _database.Table<Song>().FirstOrDefaultAsync();
        return song?.Id ?? 0;
    }

    /// <summary>
    /// 获取最近收藏的第一首歌曲 ID
    /// </summary>
    /// <returns>歌曲 ID，无收藏时返回 0</returns>
    public async Task<int> GetFirstFavoriteSongIdAsync()
    {
        await EnsureMaintenanceCompletedAsync();
        var fav = await _database.Table<Favorite>().OrderByDescending(f => f.AddedAt).FirstOrDefaultAsync();
        return fav?.SongId ?? 0;
    }

    /// <summary>
    /// 获取最近一次播放的歌曲 ID
    /// </summary>
    /// <returns>歌曲 ID，无播放历史时返回 0</returns>
    public async Task<int> GetFirstRecentSongIdAsync()
    {
        await EnsureMaintenanceCompletedAsync();
        var history = await _database.Table<PlayHistory>().OrderByDescending(h => h.PlayedAt).FirstOrDefaultAsync();
        return history?.SongId ?? 0;
    }

    /// <summary>
    /// 获取所有本地歌曲，并预加载艺术家和专辑名称
    /// </summary>
    /// <returns>包含艺术家和专辑信息的歌曲列表</returns>
    public async Task<List<Song>> GetSongsWithDetailsAsync()
    {
        var songs = await _database.Table<Song>().Where(s => s.Source == SongSource.Local).ToListAsync();
        return await FillSongDetailsAsync(songs);
    }

    /// <summary>
    /// 获取所有歌曲（本地 + 网络缓存），并预加载艺术家和专辑名称。
    /// 用于 Library 总览等需要同时统计本地与网络歌曲的场景。
    /// </summary>
    /// <returns>包含艺术家和专辑信息的所有歌曲列表</returns>
    public async Task<List<Song>> GetAllSongsWithDetailsAsync()
    {
        var songs = await _database.Table<Song>().ToListAsync();
        return await FillSongDetailsAsync(songs);
    }

    /// <summary>
    /// 为歌曲列表统一填充艺术家、专辑、播放次数及多艺术家信息。
    /// 性能：播放计数走 SQL GROUP BY 聚合（复用 GetPlayCountTotalsAsync），
    /// 艺术家/专辑只加载目标歌曲引用的 ID——旧版全表物化 Artists/Albums/PlayHistory 三张表，
    /// 列表页/总览页每次刷新都产生 O(全库) 的传输与对象分配。
    /// </summary>
    private async Task<List<Song>> FillSongDetailsAsync(List<Song> songs)
    {
        if (songs.Count == 0) return songs;

        // 批量预加载播放次数（SQL 端 SUM + GROUP BY，同一歌曲可能存在多条历史记录）
        var playHistoryDict = await GetPlayCountTotalsAsync();

        // 批量预加载艺术家和专辑：只查目标歌曲引用的 ID（IN 分块，避免超出 SQLite 变量上限）
        var neededArtistIds = songs.Select(s => s.ArtistId).Where(id => id > 0).Distinct().ToList();
        var neededAlbumIds = songs.Select(s => s.AlbumId).Where(id => id > 0).Distinct().ToList();
        var artists = await QueryIdsInChunksAsync(neededArtistIds,
            chunk => _database.Table<Artist>().Where(a => chunk.Contains(a.Id)).ToListAsync());
        var albums = await QueryIdsInChunksAsync(neededAlbumIds,
            chunk => _database.Table<Album>().Where(al => chunk.Contains(al.Id)).ToListAsync());
        var artistDict = SafeToDict(artists, a => a.Id, a => a.Name);
        var albumDict = SafeToDict(albums, a => a.Id, a => a.Title);

        // 批量加载多艺术家关联
        var songIds = songs.Select(s => s.Id).ToList();
        var allArtistsDict = await GetAllArtistsForSongsAsync(songIds);

        foreach (var s in songs)
        {
            s.Artist = artistDict.TryGetValue(s.ArtistId, out var an) ? an : "未知艺术家";
            s.Album = albumDict.TryGetValue(s.AlbumId, out var al) ? al : "未知专辑";
            s.AllArtists = allArtistsDict.TryGetValue(s.Id, out var aa) ? aa : s.Artist;
            s.PlayCount = playHistoryDict.TryGetValue(s.Id, out var pc) ? pc : 0;
        }
        return songs;
    }

    /// <summary>按 ID 集合分块执行 IN 查询（默认 500 一块，规避 SQLite 变量数上限）</summary>
    private static async Task<List<T>> QueryIdsInChunksAsync<T>(
        List<int> ids, Func<List<int>, Task<List<T>>> query, int chunkSize = 500)
    {
        var result = new List<T>();
        for (int i = 0; i < ids.Count; i += chunkSize)
            result.AddRange(await query(ids.Skip(i).Take(chunkSize).ToList()));
        return result;
    }

    /// <summary>
    /// 获取所有本地歌曲的文件路径与最后修改时间映射，用于增量扫描时跳过未变更文件
    /// </summary>
    public async Task<Dictionary<string, long>> GetLocalSongPathModTimesAsync()
    {
        await EnsureMaintenanceCompletedAsync();
        var songs = await _database.Table<Song>()
            .Where(s => s.Source == SongSource.Local)
            .ToListAsync();
        var dict = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        foreach (var s in songs)
        {
            if (!string.IsNullOrEmpty(s.FilePath))
                dict[s.FilePath] = s.DateModified;
        }
        return dict;
    }

    /// <summary>
    /// 根据 ID 获取单首歌曲
    /// </summary>
    /// <param name="id">歌曲 ID</param>
    /// <returns>歌曲对象，未找到时返回 null</returns>
    public Task<Song?> GetSongByIdAsync(int id) =>
        _database.Table<Song>().Where(s => s.Id == id).FirstOrDefaultAsync();

    /// <summary>获取指定歌曲的累计播放次数（聚合 PlayHistory 表中同一 SongId 的所有记录）</summary>
    /// <param name="songId">歌曲 ID</param>
    /// <returns>累计播放次数；无记录时返回 0</returns>
    public async Task<int> GetPlayCountForSongAsync(int songId)
    {
        try
        {
            var rows = await _database.Table<PlayHistory>().Where(h => h.SongId == songId).ToListAsync();
            return rows.Sum(h => h.PlayCount);
        }
        catch { return 0; }
    }

    /// <summary>聚合所有歌曲的累计播放次数（SQL GROUP BY，避免客户端拉取万级历史行后 GroupBy 造成的对象分配与 UI 阻塞）。</summary>
    /// <returns>Key=SongId, Value=累计 PlayCount 的字典；异常时返回空字典。</returns>
    public async Task<Dictionary<int, int>> GetPlayCountTotalsAsync()
    {
        try
        {
            await EnsureInitializedAsync();
            var rows = await _database.QueryAsync<PlayCountTotal>(
                "SELECT SongId, SUM(PlayCount) AS Total FROM PlayHistory GROUP BY SongId");
            var dict = new Dictionary<int, int>(rows.Count);
            foreach (var r in rows)
                dict[r.SongId] = r.Total;
            return dict;
        }
        catch { return new Dictionary<int, int>(); }
    }

    private sealed class PlayCountTotal
    {
        public int SongId { get; set; }
        public int Total { get; set; }
    }

    /// <summary>根据 ID 查找艺术家</summary>
    public Task<Artist?> FindArtistByIdAsync(int id) =>
        _database.Table<Artist>().Where(a => a.Id == id).FirstOrDefaultAsync();

    /// <summary>根据 ID 查找专辑</summary>
    public Task<Album?> FindAlbumByIdAsync(int id) =>
        _database.Table<Album>().Where(a => a.Id == id).FirstOrDefaultAsync();

    /// <summary>数据库层面搜索歌曲（JOIN Artist/Album/SongArtists 表，避免全部加载到内存）</summary>
    /// <param name="keyword">搜索关键词</param>
    /// <returns>匹配的歌曲列表</returns>
    public async Task<List<Song>> SearchSongsAsync(string keyword)
    {
        await EnsureMaintenanceCompletedAsync();
        var kw = $"%{keyword}%";
        // 使用 SQL JOIN 在数据库层面完成搜索 + Artist/Album 关联，支持多艺术家搜索
        var sql = @"
            SELECT DISTINCT s.*, COALESCE(a.Name, '未知艺术家') as Artist, COALESCE(al.Title, '未知专辑') as Album
            FROM Songs s
            LEFT JOIN Artists a ON s.ArtistId = a.Id
            LEFT JOIN Albums al ON s.AlbumId = al.Id
            LEFT JOIN SongArtists sa ON s.Id = sa.SongId
            LEFT JOIN Artists a2 ON sa.ArtistId = a2.Id
            WHERE s.Title LIKE ? OR a.Name LIKE ? OR al.Title LIKE ? OR a2.Name LIKE ?
        ";
        var songs = await _database.QueryAsync<Song>(sql, kw, kw, kw, kw);
        await PopulateAllArtistsAsync(songs);
        return songs;
    }

    /// <summary>按艺术家获取歌曲（支持多艺术家关联）</summary>
    /// <param name="artist">艺术家名称</param>
    /// <returns>歌曲列表</returns>
    public async Task<List<Song>> GetSongsByArtistAsync(string artist)
    {
        await EnsureMaintenanceCompletedAsync();
        var sql = @"
            SELECT DISTINCT s.*, COALESCE(a.Name, '未知艺术家') as Artist, COALESCE(al.Title, '未知专辑') as Album
            FROM Songs s
            LEFT JOIN Artists a ON s.ArtistId = a.Id
            LEFT JOIN Albums al ON s.AlbumId = al.Id
            LEFT JOIN SongArtists sa ON s.Id = sa.SongId
            LEFT JOIN Artists a2 ON sa.ArtistId = a2.Id
            WHERE a.Name = ? OR a2.Name = ?
        ";
        var songs = await _database.QueryAsync<Song>(sql, artist, artist);
        await PopulateAllArtistsAsync(songs);
        return songs;
    }

    /// <summary>按专辑获取歌曲（数据库层面过滤）</summary>
    /// <param name="album">专辑名称</param>
    /// <returns>歌曲列表</returns>
    public async Task<List<Song>> GetSongsByAlbumAsync(string album)
    {
        await EnsureMaintenanceCompletedAsync();
        var sql = @"
            SELECT s.*, COALESCE(a.Name, '未知艺术家') as Artist, al.Title as Album
            FROM Songs s
            LEFT JOIN Artists a ON s.ArtistId = a.Id
            JOIN Albums al ON s.AlbumId = al.Id
            WHERE al.Title = ?
        ";
        var songs = await _database.QueryAsync<Song>(sql, album);
        await PopulateAllArtistsAsync(songs);
        return songs;
    }

    /// <summary>批量填充歌曲的 AllArtists 字段</summary>
    private async Task PopulateAllArtistsAsync(List<Song> songs)
    {
        if (songs.Count == 0) return;
        var songIds = songs.Select(s => s.Id).ToList();
        var allArtistsDict = await GetAllArtistsForSongsAsync(songIds);
        foreach (var s in songs)
        {
            s.AllArtists = allArtistsDict.TryGetValue(s.Id, out var aa) ? aa : s.Artist;
        }
    }

    /// <summary>
    /// 保存或更新歌曲（基于 FilePath 去重）
    /// </summary>
    /// <param name="song">歌曲对象</param>
    /// <returns>受影响的行数</returns>
    public async Task<int> SaveSongAsync(Song song)
    {
        await EnsureMaintenanceCompletedAsync();
        if (song.Id != 0) return await _database.UpdateAsync(song);

        var existing = await _database.Table<Song>()
            .Where(s => s.FilePath == song.FilePath)
            .FirstOrDefaultAsync();
        if (existing != null)
        {
            song.Id = existing.Id;
            return await _database.UpdateAsync(song);
        }
        return await _database.InsertAsync(song);
    }

    /// <summary>
    /// 删除指定歌曲
    /// </summary>
    /// <param name="song">要删除的歌曲对象</param>
    /// <returns>受影响的行数</returns>
    public Task<int> DeleteSongAsync(Song song)
        => EnsureMaintenanceCompletedAsync().ContinueWith(_ => _database.DeleteAsync(song)).Unwrap();

    /// <summary>清空所有本地歌曲（SAF 权限失效时清理旧缓存）</summary>
    public async Task ClearLocalSongsAsync()
    {
        await EnsureMaintenanceCompletedAsync();
        try { await _database.ExecuteAsync("DELETE FROM SongArtists"); } catch { }
        try { await _database.ExecuteAsync("DELETE FROM Songs WHERE Source = ?", (int)SongSource.Local); } catch { }
        try { await _database.ExecuteAsync("DELETE FROM Artists"); } catch { }
        try { await _database.ExecuteAsync("DELETE FROM Albums"); } catch { }
        // 级联清理孤立记录
        await CleanupOrphanedPlayHistoryAndFavoritesAsync();
    }

    /// <summary>清空所有缓存的网络歌曲</summary>
    public async Task ClearCachedNetworkSongsAsync()
    {
        await EnsureMaintenanceCompletedAsync();
        try { await _database.ExecuteAsync("DELETE FROM SongArtists WHERE SongId IN (SELECT Id FROM Songs WHERE Source != ?)", (int)SongSource.Local); } catch { }
        try { await _database.ExecuteAsync("DELETE FROM Songs WHERE Source != ?", (int)SongSource.Local); } catch { }
        try { await _database.ExecuteAsync("DELETE FROM CachedSongs"); } catch { }
        // 级联清理孤立记录
        await CleanupOrphanedPlayHistoryAndFavoritesAsync();
    }

    /// <summary>删除指定来源中不在保留路径集合内的歌曲，并清理孤立艺术家/专辑。
    /// 性能：只投影判定所需字段（旧版把该来源全部 Song 实体拉回内存），
    /// 关联表删除改为分块 IN 集合 SQL（旧版每首歌 4 条 DELETE）。</summary>
    /// <param name="source">歌曲来源类型</param>
    /// <param name="retainPaths">需要保留的本地文件路径集合（目录前缀语义）</param>
    /// <param name="retainRemoteIds">需要保留的远程 ID 集合</param>
    /// <returns>删除的歌曲数量</returns>
    public async Task<int> RemoveStaleSongsAsync(SongSource source, HashSet<string> retainPaths, HashSet<string>? retainRemoteIds = null)
    {
        await EnsureMaintenanceCompletedAsync();
        var candidates = await _database.QueryAsync<SongStaleRow>(
            "SELECT Id, FilePath, RemoteId FROM Songs WHERE Source = ?", (int)source);

        var toDeleteIds = new List<int>();
        foreach (var s in candidates)
        {
            bool keep = source == SongSource.Local
                ? retainPaths.Any(p => s.FilePath.StartsWith(p, StringComparison.OrdinalIgnoreCase))
                : (retainRemoteIds != null && !string.IsNullOrEmpty(s.RemoteId) && retainRemoteIds.Contains(s.RemoteId));
            if (!keep) toDeleteIds.Add(s.Id);
        }

        if (toDeleteIds.Count == 0) return 0;

        await _database.RunInTransactionAsync(tran =>
        {
            for (int i = 0; i < toDeleteIds.Count; i += 500)
            {
                var inList = string.Join(",", toDeleteIds.Skip(i).Take(500));
                tran.Execute($"DELETE FROM PlayHistory WHERE SongId IN ({inList})");
                tran.Execute($"DELETE FROM Favorites WHERE SongId IN ({inList})");
                tran.Execute($"DELETE FROM SongArtists WHERE SongId IN ({inList})");
                tran.Execute($"DELETE FROM Songs WHERE Id IN ({inList})");
            }
        });

        await CleanupOrphanedArtistsAndAlbumsAsync();
        return toDeleteIds.Count;
    }

    private sealed class SongStaleRow
    {
        public int Id { get; set; }
        public string FilePath { get; set; } = "";
        public string? RemoteId { get; set; }
    }

    /// <summary>删除 Source=Local 且文件路径不在保留集合中的歌曲，并清理关联的播放历史/收藏/艺术家关联</summary>
    /// <param name="retainPaths">本次扫描到的所有本地歌曲文件路径集合（精确匹配，大小写不敏感）</param>
    /// <returns>删除的歌曲数量</returns>
    public async Task<int> RemoveLocalSongsNotInPathsAsync(HashSet<string> retainPaths)
    {
        await EnsureMaintenanceCompletedAsync();
        var localSongs = await _database.Table<Song>().Where(s => s.Source == SongSource.Local).ToListAsync();
        var toDeleteIds = new List<int>();
        foreach (var s in localSongs)
        {
            if (string.IsNullOrEmpty(s.FilePath))
            {
                toDeleteIds.Add(s.Id);
                continue;
            }
            if (!retainPaths.Contains(s.FilePath))
            {
                toDeleteIds.Add(s.Id);
            }
        }

        if (toDeleteIds.Count == 0) return 0;

        await _database.RunInTransactionAsync(tran =>
        {
            foreach (var id in toDeleteIds)
            {
                try { tran.Delete<Song>(id); } catch { }
                try { tran.Execute("DELETE FROM PlayHistory WHERE SongId = ?", id); } catch { }
                try { tran.Execute("DELETE FROM Favorites WHERE SongId = ?", id); } catch { }
                try { tran.Execute("DELETE FROM SongArtists WHERE SongId = ?", id); } catch { }
            }
        });

        await CleanupOrphanedArtistsAndAlbumsAsync();
        return toDeleteIds.Count;
    }

    /// <summary>清理没有关联歌曲的孤立艺术家和专辑</summary>
    public async Task CleanupOrphanedArtistsAndAlbumsAsync()
    {
        await EnsureMaintenanceCompletedAsync();
        try
        {
            // 先清理 SongArtists 中引用已删除歌曲的孤立记录
            await _database.ExecuteAsync(
                "DELETE FROM SongArtists WHERE SongId NOT IN (SELECT Id FROM Songs)");
        }
        catch { }
        try
        {
            await _database.ExecuteAsync(
                "DELETE FROM Artists WHERE Id NOT IN (SELECT DISTINCT ArtistId FROM Songs WHERE ArtistId != 0)"
                + " AND Id NOT IN (SELECT DISTINCT ArtistId FROM SongArtists)");
        }
        catch { }
        try
        {
            await _database.ExecuteAsync(
                "DELETE FROM Albums WHERE Id NOT IN (SELECT DISTINCT AlbumId FROM Songs WHERE AlbumId != 0)");
        }
        catch { }
    }

    /// <summary>清理 PlayHistory 和 Favorites 中引用了已删除歌曲的孤立记录</summary>
    public async Task CleanupOrphanedPlayHistoryAndFavoritesAsync()
    {
        // 注意：此方法可能从 EnsureInitializedAsync 内部调用，不能再调 EnsureInitializedAsync 以避免信号量死锁
        try
        {
            await _database.ExecuteAsync(
                "DELETE FROM PlayHistory WHERE SongId NOT IN (SELECT Id FROM Songs)");
        }
        catch { }
        try
        {
            await _database.ExecuteAsync(
                "DELETE FROM Favorites WHERE SongId NOT IN (SELECT Id FROM Songs)");
        }
        catch { }
        try
        {
            await _database.ExecuteAsync(
                "DELETE FROM SongArtists WHERE SongId NOT IN (SELECT Id FROM Songs)");
        }
        catch { }
    }

    // ═══════════ Artist / Album ═══════════

    /// <summary>
    /// 根据名称查找或创建艺术家，返回艺术家 ID
    /// </summary>
    /// <param name="name">艺术家名称</param>
}
