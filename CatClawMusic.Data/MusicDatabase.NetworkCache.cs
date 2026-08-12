using CatClawMusic.Core.Models;
using CatClawMusic.Core.Services;
using SQLite;
using CatClawMusic.Core.Interfaces;

namespace CatClawMusic.Data;

/// <summary>SQLite 数据库操作层 —— partial 分域文件之一。</summary>
public partial class MusicDatabase
{
    public async Task SaveCachedSongAsync(CachedSong cachedSong)
    {
        await EnsureMaintenanceCompletedAsync();
        if (cachedSong.Id != 0)
            await _database.UpdateAsync(cachedSong);
        else
            await _database.InsertAsync(cachedSong);
    }

    /// <summary>
    /// 获取所有已缓存的歌曲
    /// </summary>
    /// <returns>缓存歌曲列表</returns>
    public Task<List<CachedSong>> GetCachedSongsAsync()
    {
        return _database.Table<CachedSong>().ToListAsync();
    }

    /// <summary>
    /// 根据歌曲 ID 获取缓存歌曲信息
    /// </summary>
    /// <param name="songId">歌曲 ID</param>
    /// <returns>缓存歌曲信息，未找到时返回 null</returns>
    public Task<CachedSong?> GetCachedSongAsync(int songId)
    {
        return _database.Table<CachedSong>().Where(c => c.SongId == songId).FirstOrDefaultAsync();
    }

    /// <summary>
    /// 删除指定歌曲的缓存记录
    /// </summary>
    /// <param name="songId">歌曲 ID</param>
    public async Task DeleteCachedSongAsync(int songId)
    {
        await EnsureMaintenanceCompletedAsync();
        var cached = await _database.Table<CachedSong>().Where(c => c.SongId == songId).FirstOrDefaultAsync();
        if (cached != null)
            await _database.DeleteAsync(cached);
    }

    // ═══════════ Network Song Cache ═══════════

    /// <summary>替换所有网络缓存歌曲（先清除旧的，再批量写入新的）</summary>
    /// <param name="songs">新歌曲列表</param>
    public async Task ReplaceNetworkSongsAsync(List<Song> songs)
    {
        await EnsureMaintenanceCompletedAsync();
        try { await _database.ExecuteAsync("DELETE FROM Songs WHERE Source = ?", (int)SongSource.WebDAV); }
        catch { }

        if (songs.Count == 0) return;

        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        foreach (var s in songs) s.DateAdded = now;

        // Phase 1: 批量处理 Artist
        var allArtists = await _database.Table<Artist>().ToListAsync();
        var artistNameToId = SafeToDict(allArtists, a => a.Name, a => a.Id);
        var newArtistNames = new HashSet<string>();

        foreach (var s in songs)
        {
            if (!string.IsNullOrEmpty(s.Artist) && !artistNameToId.ContainsKey(s.Artist))
                newArtistNames.Add(s.Artist);
        }

        if (newArtistNames.Count > 0)
        {
            var newArtists = newArtistNames.Select(n => new Artist { Name = n }).ToList();
            await _database.InsertAllAsync(newArtists);
            foreach (var a in newArtists)
                artistNameToId[a.Name] = a.Id;
        }

        // Phase 2: 回填 Song.ArtistId
        foreach (var s in songs)
        {
            if (!string.IsNullOrEmpty(s.Artist))
                s.ArtistId = artistNameToId.TryGetValue(s.Artist, out var aid) ? aid : 0;
        }

        // Phase 3: 批量处理 Album（依赖已解析的 ArtistId）
        var allAlbums = await _database.Table<Album>().ToListAsync();
        var albumKeyToId = SafeToDict(allAlbums, a => (a.Title ?? "", a.ArtistId), a => a.Id);
        var newAlbumKeys = new HashSet<(string Title, int ArtistId)>();
        var newAlbums = new List<Album>();

        foreach (var s in songs)
        {
            if (string.IsNullOrEmpty(s.Album)) continue;
            var key = (s.Album, s.ArtistId);
            if (!albumKeyToId.ContainsKey(key) && newAlbumKeys.Add(key))
                newAlbums.Add(new Album { Title = s.Album, ArtistId = s.ArtistId });
        }

        if (newAlbums.Count > 0)
        {
            await _database.InsertAllAsync(newAlbums);
            foreach (var a in newAlbums)
                albumKeyToId[(a.Title ?? "", a.ArtistId)] = a.Id;
        }

        // Phase 4: 回填 Song.AlbumId
        foreach (var s in songs)
        {
            if (!string.IsNullOrEmpty(s.Album))
            {
                var key = (s.Album, s.ArtistId);
                s.AlbumId = albumKeyToId.TryGetValue(key, out var albId) ? albId : 0;
            }
        }

        // Phase 5: 批量插入所有歌曲
        await _database.InsertAllAsync(songs);

        // Phase 6: 创建 SongArtist 多对多关联（为每首歌的主艺术家建立记录）
        if (songs.Count > 0)
        {
            var songArtistEntries = songs
                .Where(s => s.Id > 0 && s.ArtistId > 0)
                .Select(s => new SongArtist { SongId = s.Id, ArtistId = s.ArtistId })
                .ToList();
            if (songArtistEntries.Count > 0)
            {
                try
                {
                    // 先删除这些歌曲的旧关联，再插入新关联
                    var songIds = songArtistEntries.Select(e => e.SongId).Distinct().ToList();
                    var songIdStr = string.Join(",", songIds);
                    await _database.ExecuteAsync($"DELETE FROM SongArtists WHERE SongId IN ({songIdStr})");
                    await _database.InsertAllAsync(songArtistEntries);
                }
                catch { }
            }
        }
    }

    /// <summary>获取缓存的网络歌曲（WebDAV/SMB/Cache，不去重，保留同歌名不同版本）</summary>
    /// <returns>网络歌曲列表</returns>
    public async Task<List<Song>> GetCachedNetworkSongsAsync()
    {
        await EnsureMaintenanceCompletedAsync();
        var songs = await _database.Table<Song>()
            .Where(s => s.Source == SongSource.WebDAV || s.Source == SongSource.SMB || s.Source == SongSource.Cache)
            .ToListAsync();
        // 填充 Artist/Album 名称
        var artists = await _database.Table<Artist>().ToListAsync();
        var albums = await _database.Table<Album>().ToListAsync();
        var artistDict = SafeToDict(artists, a => a.Id, a => a.Name);
        var albumDict = SafeToDict(albums, a => a.Id, a => a.Title);
        foreach (var s in songs)
        {
            s.Artist = artistDict.TryGetValue(s.ArtistId, out var an) ? an : "未知艺术家";
            s.Album = albumDict.TryGetValue(s.AlbumId, out var al) ? al : "未知专辑";
        }
        // 填充多艺术家
        var allArtistsDict6 = await GetAllArtistsForSongsAsync(songs.Select(s => s.Id));
        foreach (var s in songs)
            s.AllArtists = allArtistsDict6.TryGetValue(s.Id, out var aa) ? aa : s.Artist;

        return songs;
    }

    /// <summary>缓存网络歌曲数量（WebDAV/SMB/Cache）</summary>
    /// <returns>网络歌曲总数</returns>
    public async Task<int> GetCachedNetworkSongCountAsync()
        => await _database.Table<Song>().Where(s => s.Source == SongSource.WebDAV || s.Source == SongSource.SMB || s.Source == SongSource.Cache).CountAsync();

    /// <summary>
    /// 开始替换网络歌曲：先保存现有网络歌曲的收藏引用，再清空 WebDAV 歌曲数据。
    /// 配合 RestoreNetworkFavoritesAsync 使用以保留收藏状态。
    /// </summary>
    public async Task ReplaceNetworkSongsBeginAsync()
    {
        await EnsureMaintenanceCompletedAsync();
        try { await SaveNetworkFavoriteRefsAsync(); }
        catch { }
        try { await _database.ExecuteAsync("DELETE FROM Songs WHERE Source = ?", (int)SongSource.WebDAV); }
        catch { /* 表可能为空 */ }
    }

    /// <summary>
    /// 待恢复的网络歌曲收藏映射（RemoteId -> AddedAt）
    /// </summary>
    private readonly Dictionary<string, long> _pendingNetworkFavs = new();

    /// <summary>
    /// 保存当前网络歌曲的收藏引用，用于后续恢复
    /// </summary>
    private async Task SaveNetworkFavoriteRefsAsync()
    {
        _pendingNetworkFavs.Clear();
        var favs = await _database.Table<Favorite>().ToListAsync();
        if (favs.Count == 0) return;
        var favSongIds = favs.Select(f => f.SongId).ToHashSet();
        var networkSongs = await _database.Table<Song>()
            .Where(s => (s.Source == SongSource.WebDAV || s.Source == SongSource.SMB) && favSongIds.Contains(s.Id))
            .ToListAsync();
        foreach (var ns in networkSongs)
        {
            if (!string.IsNullOrEmpty(ns.RemoteId))
            {
                var fav = favs.First(f => f.SongId == ns.Id);
                _pendingNetworkFavs[ns.RemoteId] = fav.AddedAt;
            }
        }
    }

    /// <summary>
    /// 在重新扫描后恢复网络歌曲的收藏状态
    /// </summary>
    public async Task RestoreNetworkFavoritesAsync()
    {
        if (_pendingNetworkFavs.Count == 0) return;
        await EnsureMaintenanceCompletedAsync();

        var newNetworkSongs = await _database.Table<Song>()
            .Where(s => s.Source == SongSource.WebDAV || s.Source == SongSource.SMB)
            .ToListAsync();

        foreach (var kv in _pendingNetworkFavs)
        {
            var newMatch = newNetworkSongs.FirstOrDefault(s => s.RemoteId == kv.Key);
            if (newMatch != null)
            {
                try
                {
                    var existing = await _database.Table<Favorite>()
                        .Where(f => f.SongId == newMatch.Id).CountAsync();
                    if (existing == 0)
                        await _database.InsertAsync(new Favorite { SongId = newMatch.Id, AddedAt = kv.Value });
                }
                catch { }
            }
        }
        _pendingNetworkFavs.Clear();
    }

    /// <summary>批量插入歌曲（事务 + 内存去重，比逐条 InsertSongAsync 快 10 倍以上）</summary>
    /// <param name="songs">待插入的歌曲列表</param>
    /// <returns>成功插入（非更新）的歌曲列表</returns>
    public async Task<List<Song>> InsertSongsBatchAsync(List<Song> songs)
    {
        await EnsureMaintenanceCompletedAsync();
        if (songs.Count == 0) return songs;

        // 1. 按本批次 FilePath / RemoteId 批量查询已有记录，避免每次加载全表 Songs
        var existingByPath = new Dictionary<string, Song>(StringComparer.OrdinalIgnoreCase);
        var existingByRemoteId = new Dictionary<string, Song>(StringComparer.OrdinalIgnoreCase);

        var filePaths = songs
            .Where(s => !string.IsNullOrEmpty(s.FilePath))
            .Select(s => s.FilePath!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var remoteIds = songs
            .Where(s => !string.IsNullOrEmpty(s.RemoteId)
                && (s.Source == SongSource.WebDAV || s.Source == SongSource.SMB))
            .Select(s => s.RemoteId!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        const int chunkSize = 500;
        for (int i = 0; i < filePaths.Count; i += chunkSize)
        {
            var chunk = filePaths.Skip(i).Take(chunkSize).ToList();
            var existing = await _database.Table<Song>()
                .Where(s => chunk.Contains(s.FilePath))
                .ToListAsync();
            foreach (var s in existing)
            {
                if (!string.IsNullOrEmpty(s.FilePath))
                    existingByPath[s.FilePath] = s;
            }
        }

        for (int i = 0; i < remoteIds.Count; i += chunkSize)
        {
            var chunk = remoteIds.Skip(i).Take(chunkSize).ToList();
            var existing = await _database.Table<Song>()
                .Where(s => chunk.Contains(s.RemoteId))
                .ToListAsync();
            foreach (var s in existing)
            {
                if (!string.IsNullOrEmpty(s.RemoteId))
                    existingByRemoteId[s.RemoteId] = s;
            }
        }

        var inserted = new List<Song>();

        // 2. 在事务中批量处理
        await _database.RunInTransactionAsync(tran =>
        {
            foreach (var song in songs)
            {
                try
                {
                    // 内存去重
                    Song? existing = null;
                    if ((song.Source == SongSource.WebDAV || song.Source == SongSource.SMB)
                        && !string.IsNullOrEmpty(song.RemoteId)
                        && existingByRemoteId.TryGetValue(song.RemoteId, out var byRemote))
                    {
                        existing = byRemote;
                    }
                    if (existing == null && !string.IsNullOrEmpty(song.FilePath)
                        && existingByPath.TryGetValue(song.FilePath, out var byPath))
                    {
                        existing = byPath;
                    }

                    if (existing != null)
                    {
                        song.Id = existing.Id;
                        tran.Update(song);
                        // 更新内存缓存
                        if (!string.IsNullOrEmpty(song.FilePath))
                            existingByPath[song.FilePath] = song;
                    }
                    else
                    {
                        tran.Insert(song);
                        if (song.Id > 0) inserted.Add(song);
                        // 加入内存缓存，防止后续批次重复
                        if (!string.IsNullOrEmpty(song.FilePath))
                            existingByPath[song.FilePath] = song;
                        if (!string.IsNullOrEmpty(song.RemoteId))
                            existingByRemoteId[song.RemoteId] = song;
                    }
                }
                catch (SQLite.SQLiteException ex) when (ex.Result == SQLite3.Result.Constraint)
                {
                    // 并发冲突：按 FilePath 查重更新
                    try
                    {
                        var conflict = tran.FindWithQuery<Song>(
                            "SELECT * FROM Songs WHERE FilePath = ?", song.FilePath);
                        if (conflict != null)
                        {
                            song.Id = conflict.Id;
                            tran.Update(song);
                        }
                    }
                    catch { }
                }
                catch { }
            }
        });

        return inserted;
    }

    /// <summary>插入单首歌曲（用于增量入库），网络歌曲基于 RemoteId 去重</summary>
    /// <param name="song">歌曲对象</param>
    public async Task InsertSongAsync(Song song)
    {
        await EnsureMaintenanceCompletedAsync();
        try
        {
            // 网络歌曲基于 RemoteId 去重，本地歌曲基于 FilePath 去重
            Song? existing = null;
            if ((song.Source == SongSource.WebDAV || song.Source == SongSource.SMB) && !string.IsNullOrEmpty(song.RemoteId))
            {
                existing = await _database.Table<Song>()
                    .Where(s => (s.Source == SongSource.WebDAV || s.Source == SongSource.SMB) && s.RemoteId == song.RemoteId)
                    .FirstOrDefaultAsync();
            }
            
            // RemoteId 没命中时，再按 FilePath 兜底查重
            if (existing == null && !string.IsNullOrEmpty(song.FilePath))
            {
                existing = await _database.Table<Song>()
                    .Where(s => s.FilePath == song.FilePath)
                    .FirstOrDefaultAsync();
            }

            if (existing != null)
            {
                song.Id = existing.Id;
                await _database.UpdateAsync(song);
            }
            else
            {
                try
                {
                    await _database.InsertAsync(song);
                }
                catch (SQLite.SQLiteException ex) when (ex.Result == SQLite.SQLite3.Result.Constraint)
                {
                    // 并发或残留数据导致的 FilePath 冲突，按 FilePath 更新
                    var conflict = await _database.Table<Song>()
                        .Where(s => s.FilePath == song.FilePath)
                        .FirstOrDefaultAsync();
                    if (conflict != null)
                    {
                        song.Id = conflict.Id;
                        await _database.UpdateAsync(song);
                    }
                    else throw;
                }
            }
        }
        catch (Exception ex)
        {
            Log.Debug("MusicDatabase", $"[CatClaw] InsertSong 失败: {song.Title} - {ex.Message}");
        }
    }

    /// <summary>
    /// 批量保存歌曲的多艺术家关联：先删除旧关联，再批量插入新关联。
    /// </summary>
    /// <param name="entries">(songId, artistIds) 列表</param>
    public async Task SaveSongArtistsBatchAsync(List<(int SongId, List<int> ArtistIds)> entries)
    {
        if (entries.Count == 0) return;

        await EnsureMaintenanceCompletedAsync();

        await _database.RunInTransactionAsync(tran =>
        {
            foreach (var (songId, artistIds) in entries)
            {
                // 删除旧关联
                try { tran.Execute("DELETE FROM SongArtists WHERE SongId = ?", songId); } catch { }

                // 插入新关联（跳过 ArtistId=0 的无效记录）
                foreach (var artistId in artistIds)
                {
                    if (artistId <= 0) continue;
                    try
                    {
                        tran.Insert(new SongArtist { SongId = songId, ArtistId = artistId });
                    }
                    catch { }
                }
            }
        });
    }

    /// <summary>
    /// 批量获取指定歌曲的所有艺术家名称（用于填充 Song.AllArtists 字段）。
    /// </summary>
    /// <param name="songIds">歌曲 ID 集合</param>
    /// <returns>songId → "艺术家1 / 艺术家2" 的字典</returns>
    public async Task<Dictionary<int, string>> GetAllArtistsForSongsAsync(IEnumerable<int> songIds)
    {
        var result = new Dictionary<int, string>();
        var idList = songIds.ToList();
        if (idList.Count == 0) return result;

        // 使用 SQL 直接 JOIN 查询，比 ORM 逐条查高效
        var songIdStr = string.Join(",", idList);
        try
        {
            var rows = await _database.QueryAsync<SongArtistRow>(
                $@"SELECT sa.SongId, a.Name
                   FROM SongArtists sa
                   JOIN Artists a ON sa.ArtistId = a.Id
                   WHERE sa.SongId IN ({songIdStr})
                   ORDER BY sa.Id");

            // 按 SongId 分组拼接
            var groups = rows.GroupBy(r => r.SongId);
            foreach (var g in groups)
            {
                result[g.Key] = string.Join(" / ", g.Select(r => r.Name));
            }
        }
        catch { }

        return result;
    }

    /// <summary>
    /// 批量查询指定歌曲的 SongArtist 关联记录（用于计算艺术家歌曲计数）。
    /// </summary>
    /// <param name="songIds">歌曲 ID 集合</param>
    /// <returns>SongArtist 记录列表</returns>
    public async Task<List<SongArtist>> QuerySongArtistsBySongIdsAsync(HashSet<int> songIds)
    {
        if (songIds.Count == 0) return new List<SongArtist>();
        await EnsureMaintenanceCompletedAsync();
        var ids = string.Join(",", songIds);
        return await _database.QueryAsync<SongArtist>(
            $"SELECT * FROM SongArtists WHERE SongId IN ({ids})");
    }

    /// <summary>SongArtist JOIN 查询的中间结果行，用于批量加载歌曲的多艺术家名称</summary>
    private class SongArtistRow
    {
        /// <summary>歌曲 ID</summary>
        public int SongId { get; set; }

        /// <summary>艺术家名称</summary>
        public string Name { get; set; } = "";
    }

    /// <summary>
    /// 迁移 Artists 表：添加 Gender/Birthday/Region/Description 列
}
