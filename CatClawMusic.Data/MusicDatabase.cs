using CatClawMusic.Core.Models;
using SQLite;

namespace CatClawMusic.Data;

/// <summary>
/// SQLite 数据库操作层，管理歌曲、艺术家、专辑、播放列表、收藏等数据的持久化
/// </summary>
public class MusicDatabase
{
    /// <summary>
    /// SQLite 异步数据库连接
    /// </summary>
    private readonly SQLiteAsyncConnection _database;

    /// <summary>
    /// 数据库是否已完成初始化
    /// </summary>
    private bool _isInitialized;

    /// <summary>
    /// 初始化信号量，确保并发安全
    /// </summary>
    private readonly SemaphoreSlim _initSemaphore = new(1, 1);

    /// <summary>
    /// 使用指定的数据库路径创建 MusicDatabase 实例
    /// </summary>
    public MusicDatabase(string dbPath)
    {
        _database = new SQLiteAsyncConnection(dbPath);
    }

    /// <summary>
    /// 确保数据库表已创建并完成迁移，多次调用安全
    /// </summary>
    public async Task EnsureInitializedAsync()
    {
        if (_isInitialized) return;

        await _initSemaphore.WaitAsync();
        try
        {
            if (_isInitialized) return;

            await _database.EnableWriteAheadLoggingAsync();

            await _database.CreateTableAsync<Artist>();
            await _database.CreateTableAsync<Album>();
            await _database.CreateTableAsync<Song>();
            await _database.CreateTableAsync<Playlist>();
            await _database.CreateTableAsync<PlaylistSong>();

            await _database.CreateTableAsync<PlayHistory>();
            await _database.CreateTableAsync<Favorite>();
            await _database.CreateTableAsync<Lyric>();
            await _database.CreateTableAsync<ConnectionProfile>();
            await _database.CreateTableAsync<CachedSong>();

            await CreateIndexesAsync();

            await MigratePlaylistsTableAsync();
            await MigratePlaylistSongsTableAsync();

            _isInitialized = true;
        }
        finally
        {
            _initSemaphore.Release();
        }
    }

    /// <summary>
    /// 创建数据库查询索引以提升搜索和关联查询性能
    /// </summary>
    private async Task CreateIndexesAsync()
    {
        try { await _database.ExecuteAsync("CREATE INDEX IF NOT EXISTS idx_songs_artist ON Songs(ArtistId)"); } catch { }
        try { await _database.ExecuteAsync("CREATE INDEX IF NOT EXISTS idx_songs_album ON Songs(AlbumId)"); } catch { }
        try { await _database.ExecuteAsync("CREATE INDEX IF NOT EXISTS idx_songs_title ON Songs(Title)"); } catch { }
        try { await _database.ExecuteAsync("CREATE INDEX IF NOT EXISTS idx_songs_source ON Songs(Source)"); } catch { }
        try { await _database.ExecuteAsync("CREATE INDEX IF NOT EXISTS idx_songs_protocol ON Songs(Protocol)"); } catch { }
        try { await _database.ExecuteAsync("CREATE INDEX IF NOT EXISTS idx_songs_source_protocol ON Songs(Source, Protocol)"); } catch { }
        try { await _database.ExecuteAsync("CREATE INDEX IF NOT EXISTS idx_albums_artist ON Albums(ArtistId)"); } catch { }
        try { await _database.ExecuteAsync("CREATE INDEX IF NOT EXISTS idx_play_history_time ON PlayHistory(PlayedAt DESC)"); } catch { }
    }

    // ═══════════ Song CRUD ═══════════

    /// <summary>
    /// 获取所有已启用协议的 ProtocolType 集合（用于过滤歌曲）
    /// </summary>
    public async Task<HashSet<ProtocolType>> GetEnabledProtocolsAsync()
    {
        await EnsureInitializedAsync();
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
    public Task<List<Song>> GetSongsAsync() => GetSongsWithDetailsAsync();

    public async Task<int> GetLocalSongCountAsync()
    {
        await EnsureInitializedAsync();
        return await _database.Table<Song>().Where(s => s.Source == SongSource.Local).CountAsync();
    }

    public async Task<int> GetNetworkSongCountAsync()
    {
        await EnsureInitializedAsync();
        return await _database.Table<Song>()
            .Where(s => s.Source == SongSource.WebDAV || s.Source == SongSource.SMB)
            .CountAsync();
    }

    public async Task<int> GetMergedDedupedCountAsync()
    {
        await EnsureInitializedAsync();
        var songs = await _database.Table<Song>()
            .OrderBy(s => s.Title)
            .ToListAsync();
        var enabledProtocols = await GetEnabledProtocolsAsync();
        var filtered = FilterByEnabledProtocols(songs, enabledProtocols);
        return filtered
            .GroupBy(s => (s.Title?.Trim() ?? "").ToLowerInvariant() + "|" + (s.Artist?.Trim() ?? "").ToLowerInvariant())
            .Count();
    }

    public async Task<int> GetFavoriteCountAsync()
    {
        await EnsureInitializedAsync();
        return await _database.Table<Favorite>().CountAsync();
    }

    public async Task<int> GetRecentPlayCountAsync()
    {
        await EnsureInitializedAsync();
        return await _database.Table<PlayHistory>().CountAsync();
    }

    public async Task<int> GetFirstSongIdForAllAsync()
    {
        await EnsureInitializedAsync();
        var song = await _database.Table<Song>().Where(s => s.Source == SongSource.Local).FirstOrDefaultAsync();
        if (song != null) return song.Id;
        song = await _database.Table<Song>().FirstOrDefaultAsync();
        return song?.Id ?? 0;
    }

    public async Task<int> GetFirstFavoriteSongIdAsync()
    {
        await EnsureInitializedAsync();
        var fav = await _database.Table<Favorite>().OrderByDescending(f => f.AddedAt).FirstOrDefaultAsync();
        return fav?.SongId ?? 0;
    }

    public async Task<int> GetFirstRecentSongIdAsync()
    {
        await EnsureInitializedAsync();
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
        // 批量预加载艺术家和专辑，避免 N+1 查询
        var artists = await _database.Table<Artist>().ToListAsync();
        var albums = await _database.Table<Album>().ToListAsync();
        var artistDict = artists.ToDictionary(a => a.Id, a => a.Name);
        var albumDict = albums.ToDictionary(a => a.Id, a => a.Title);
        foreach (var s in songs)
        {
            s.Artist = artistDict.TryGetValue(s.ArtistId, out var an) ? an : "未知艺术家";
            s.Album = albumDict.TryGetValue(s.AlbumId, out var al) ? al : "未知专辑";
        }
        return songs;
    }

    /// <summary>
    /// 根据 ID 获取单首歌曲
    /// </summary>
    /// <param name="id">歌曲 ID</param>
    /// <returns>歌曲对象，未找到时返回 null</returns>
    public Task<Song?> GetSongByIdAsync(int id) =>
        _database.Table<Song>().Where(s => s.Id == id).FirstOrDefaultAsync();

    /// <summary>数据库层面搜索歌曲（JOIN Artist/Album 表，避免全部加载到内存）</summary>
    /// <param name="keyword">搜索关键词</param>
    /// <returns>匹配的歌曲列表</returns>
    public async Task<List<Song>> SearchSongsAsync(string keyword)
    {
        await EnsureInitializedAsync();
        var kw = $"%{keyword}%";
        // 使用 SQL JOIN 在数据库层面完成搜索 + Artist/Album 关联
        var sql = @"
            SELECT s.*, COALESCE(a.Name, '未知艺术家') as Artist, COALESCE(al.Title, '未知专辑') as Album
            FROM Songs s
            LEFT JOIN Artists a ON s.ArtistId = a.Id
            LEFT JOIN Albums al ON s.AlbumId = al.Id
            WHERE s.Title LIKE ? OR a.Name LIKE ? OR al.Title LIKE ?
        ";
        return await _database.QueryAsync<Song>(sql, kw, kw, kw);
    }

    /// <summary>按艺术家获取歌曲（数据库层面过滤）</summary>
    /// <param name="artist">艺术家名称</param>
    /// <returns>歌曲列表</returns>
    public async Task<List<Song>> GetSongsByArtistAsync(string artist)
    {
        await EnsureInitializedAsync();
        var sql = @"
            SELECT s.*, a.Name as Artist, COALESCE(al.Title, '未知专辑') as Album
            FROM Songs s
            JOIN Artists a ON s.ArtistId = a.Id
            LEFT JOIN Albums al ON s.AlbumId = al.Id
            WHERE a.Name = ?
        ";
        return await _database.QueryAsync<Song>(sql, artist);
    }

    /// <summary>按专辑获取歌曲（数据库层面过滤）</summary>
    /// <param name="album">专辑名称</param>
    /// <returns>歌曲列表</returns>
    public async Task<List<Song>> GetSongsByAlbumAsync(string album)
    {
        await EnsureInitializedAsync();
        var sql = @"
            SELECT s.*, COALESCE(a.Name, '未知艺术家') as Artist, al.Title as Album
            FROM Songs s
            LEFT JOIN Artists a ON s.ArtistId = a.Id
            JOIN Albums al ON s.AlbumId = al.Id
            WHERE al.Title = ?
        ";
        return await _database.QueryAsync<Song>(sql, album);
    }

    /// <summary>
    /// 保存或更新歌曲（基于 FilePath 去重）
    /// </summary>
    /// <param name="song">歌曲对象</param>
    /// <returns>受影响的行数</returns>
    public async Task<int> SaveSongAsync(Song song)
    {
        await EnsureInitializedAsync();
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
        => EnsureInitializedAsync().ContinueWith(_ => _database.DeleteAsync(song)).Unwrap();

    /// <summary>清空所有本地歌曲（SAF 权限失效时清理旧缓存）</summary>
    public async Task ClearLocalSongsAsync()
    {
        await EnsureInitializedAsync();
        try { await _database.ExecuteAsync("DELETE FROM Songs WHERE Source = ?", (int)SongSource.Local); } catch { }
        try { await _database.ExecuteAsync("DELETE FROM Artists"); } catch { }
        try { await _database.ExecuteAsync("DELETE FROM Albums"); } catch { }
    }

    /// <summary>清空所有缓存的网络歌曲</summary>
    public async Task ClearCachedNetworkSongsAsync()
    {
        await EnsureInitializedAsync();
        try { await _database.ExecuteAsync("DELETE FROM Songs WHERE Source != ?", (int)SongSource.Local); } catch { }
        try { await _database.ExecuteAsync("DELETE FROM CachedSongs"); } catch { }
    }

    /// <summary>删除指定来源中不在保留路径集合内的歌曲，并清理孤立艺术家/专辑</summary>
    /// <param name="source">歌曲来源类型</param>
    /// <param name="retainPaths">需要保留的本地文件路径集合</param>
    /// <param name="retainRemoteIds">需要保留的远程 ID 集合</param>
    /// <returns>删除的歌曲数量</returns>
    public async Task<int> RemoveStaleSongsAsync(SongSource source, HashSet<string> retainPaths, HashSet<string>? retainRemoteIds = null)
    {
        await EnsureInitializedAsync();
        var all = await _database.Table<Song>().Where(s => s.Source == source).ToListAsync();
        var toDeleteIds = new List<int>();
        foreach (var s in all)
        {
            bool keep = source == SongSource.Local
                ? retainPaths.Contains(s.FilePath)
                : (retainRemoteIds != null && !string.IsNullOrEmpty(s.RemoteId) && retainRemoteIds.Contains(s.RemoteId));
            if (!keep) toDeleteIds.Add(s.Id);
        }

        if (toDeleteIds.Count == 0) return 0;

        await _database.RunInTransactionAsync(tran =>
        {
            foreach (var id in toDeleteIds)
            {
                try { tran.Delete<Song>(id); } catch { }
                try { tran.Execute("DELETE FROM PlayHistory WHERE SongId = ?", id); } catch { }
                try { tran.Execute("DELETE FROM Favorites WHERE SongId = ?", id); } catch { }
            }
        });

        await CleanupOrphanedArtistsAndAlbumsAsync();
        return toDeleteIds.Count;
    }

    /// <summary>清理没有关联歌曲的孤立艺术家和专辑</summary>
    public async Task CleanupOrphanedArtistsAndAlbumsAsync()
    {
        await EnsureInitializedAsync();
        try
        {
            await _database.ExecuteAsync(
                "DELETE FROM Artists WHERE Id NOT IN (SELECT DISTINCT ArtistId FROM Songs WHERE ArtistId != 0)");
        }
        catch { }
        try
        {
            await _database.ExecuteAsync(
                "DELETE FROM Albums WHERE Id NOT IN (SELECT DISTINCT AlbumId FROM Songs WHERE AlbumId != 0)");
        }
        catch { }
    }

    // ═══════════ Artist / Album ═══════════

    /// <summary>
    /// 根据名称查找或创建艺术家，返回艺术家 ID
    /// </summary>
    /// <param name="name">艺术家名称</param>
    /// <returns>艺术家 ID，名称为空时返回 0</returns>
    public async Task<int> EnsureArtistAsync(string name)
    {
        await EnsureInitializedAsync();
        if (string.IsNullOrEmpty(name)) return 0;
        var a = await _database.Table<Artist>().Where(x => x.Name == name).FirstOrDefaultAsync();
        if (a != null) return a.Id;
        var newArtist = new Artist { Name = name };
        await _database.InsertAsync(newArtist);
        return newArtist.Id;
    }

    public async Task<List<Artist>> GetAllArtistsAsync()
    {
        await EnsureInitializedAsync();
        return await _database.Table<Artist>().ToListAsync();
    }

    public async Task<List<Album>> GetAllAlbumsAsync()
    {
        await EnsureInitializedAsync();
        return await _database.Table<Album>().ToListAsync();
    }

    /// <summary>
    /// 根据标题和艺术家 ID 查找或创建专辑，返回专辑 ID
    /// </summary>
    /// <param name="title">专辑标题</param>
    /// <param name="artistId">艺术家 ID</param>
    /// <returns>专辑 ID，标题为空时返回 0</returns>
    public async Task<int> EnsureAlbumAsync(string title, int artistId)
    {
        await EnsureInitializedAsync();
        if (string.IsNullOrEmpty(title)) return 0;
        var a = await _database.Table<Album>().Where(x => x.Title == title && x.ArtistId == artistId).FirstOrDefaultAsync();
        if (a != null) return a.Id;
        var newAlbum = new Album { Title = title, ArtistId = artistId };
        await _database.InsertAsync(newAlbum);
        return newAlbum.Id;
    }

    /// <summary>
    // ═══════════ Play History ═══════════

    /// <summary>
    /// 记录播放历史，已存在的记录会更新播放时间和次数
    /// </summary>
    /// <param name="songId">歌曲 ID</param>
    public async Task RecordPlayAsync(int songId)
    {
        await EnsureInitializedAsync();
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var existing = await _database.Table<PlayHistory>()
            .Where(h => h.SongId == songId)
            .FirstOrDefaultAsync();
        if (existing != null)
        {
            existing.PlayedAt = now;
            existing.PlayCount++;
            await _database.UpdateAsync(existing);
        }
        else
        {
            await _database.InsertAsync(new PlayHistory { SongId = songId, PlayedAt = now });
        }
        // 只保留最近 200 条历史
        await TrimHistoryAsync(200);
    }

    /// <summary>
    /// 裁剪播放历史，仅保留指定数量记录
    /// </summary>
    private async Task TrimHistoryAsync(int keepCount)
    {
        try
        {
            var count = await _database.Table<PlayHistory>().CountAsync();
            if (count <= keepCount) return;
            await _database.ExecuteAsync(
                "DELETE FROM PlayHistory WHERE SongId IN (SELECT SongId FROM PlayHistory ORDER BY PlayedAt ASC LIMIT ?)",
                count - keepCount);
        }
        catch { }
    }

    /// <summary>
    /// 获取最近的播放历史记录
    /// </summary>
    /// <param name="limit">最大返回数量</param>
    /// <returns>播放历史列表</returns>
    public Task<List<PlayHistory>> GetRecentPlaysAsync(int limit = 200) =>
        _database.Table<PlayHistory>().OrderByDescending(h => h.PlayedAt).Take(limit).ToListAsync();

    /// <summary>获取最近播放的歌曲（含艺术家/专辑名）</summary>
    /// <returns>按播放时间降序排列的歌曲列表</returns>
    public async Task<List<Song>> GetRecentSongsAsync()
    {
        await EnsureInitializedAsync();
        var history = await _database.Table<PlayHistory>().OrderByDescending(h => h.PlayedAt).Take(200).ToListAsync();
        if (history.Count == 0) return new List<Song>();

        var songIds = history.Select(h => h.SongId).ToList();
        var songs = await _database.Table<Song>().Where(s => songIds.Contains(s.Id)).ToListAsync();
        if (songs.Count == 0) return new List<Song>();

        var artists = await _database.Table<Artist>().ToListAsync();
        var albums = await _database.Table<Album>().ToListAsync();
        var artistDict = artists.ToDictionary(a => a.Id, a => a.Name);
        var albumDict = albums.ToDictionary(a => a.Id, a => a.Title);

        foreach (var s in songs)
        {
            s.Artist = artistDict.TryGetValue(s.ArtistId, out var an) ? an : "未知艺术家";
            s.Album = albumDict.TryGetValue(s.AlbumId, out var al) ? al : "未知专辑";
            s.PlayCount = history.FirstOrDefault(h => h.SongId == s.Id)?.PlayCount ?? 0;
        }

        var playTimeDict = history.ToDictionary(h => h.SongId, h => h.PlayedAt);
        return songs.OrderByDescending(s => playTimeDict.TryGetValue(s.Id, out var t) ? t : 0).ToList();
    }

    /// <summary>获取播放次数最多的歌曲（含艺术家/专辑名和播放计数）</summary>
    /// <param name="limit">最大返回数量</param>
    /// <returns>按播放次数降序排列的歌曲列表</returns>
    public async Task<List<Song>> GetTopPlayedSongsAsync(int limit = 50)
    {
        await EnsureInitializedAsync();
        var history = await _database.Table<PlayHistory>().OrderByDescending(h => h.PlayCount).Take(limit).ToListAsync();
        if (history.Count == 0) return new List<Song>();

        var songIds = history.Select(h => h.SongId).ToList();
        var songs = await _database.Table<Song>().Where(s => songIds.Contains(s.Id)).ToListAsync();
        if (songs.Count == 0) return new List<Song>();

        var artists = await _database.Table<Artist>().ToListAsync();
        var albums = await _database.Table<Album>().ToListAsync();
        var artistDict = artists.ToDictionary(a => a.Id, a => a.Name);
        var albumDict = albums.ToDictionary(a => a.Id, a => a.Title);

        foreach (var s in songs)
        {
            s.Artist = artistDict.TryGetValue(s.ArtistId, out var an) ? an : "未知艺术家";
            s.Album = albumDict.TryGetValue(s.AlbumId, out var al) ? al : "未知专辑";
            s.PlayCount = history.FirstOrDefault(h => h.SongId == s.Id)?.PlayCount ?? 0;
        }

        var playCountDict = history.ToDictionary(h => h.SongId, h => h.PlayCount);
        return songs.OrderByDescending(s => playCountDict.TryGetValue(s.Id, out var c) ? c : 0).ToList();
    }

    // ═══════════ Favorites ═══════════

    /// <summary>
    /// 设置或取消收藏指定歌曲
    /// </summary>
    /// <param name="songId">歌曲 ID</param>
    /// <param name="isFav">是否收藏</param>
    public async Task SetFavoriteAsync(int songId, bool isFav)
    {
        await EnsureInitializedAsync();
        var fav = await _database.Table<Favorite>().Where(f => f.SongId == songId).FirstOrDefaultAsync();
        if (isFav && fav == null)
            await _database.InsertAsync(new Favorite { SongId = songId, AddedAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds() });
        else if (!isFav && fav != null)
            await _database.DeleteAsync(fav);
    }

    /// <summary>
    /// 检查歌曲是否已收藏
    /// </summary>
    /// <param name="songId">歌曲 ID</param>
    /// <returns>是否已收藏</returns>
    public async Task<bool> IsFavoriteAsync(int songId)
    {
        await EnsureInitializedAsync();
        return await _database.Table<Favorite>().Where(f => f.SongId == songId).CountAsync() > 0;
    }

    /// <summary>
    /// 获取所有收藏记录
    /// </summary>
    /// <returns>收藏记录列表</returns>
    public Task<List<Favorite>> GetFavoritesAsync()
        => _database.Table<Favorite>().ToListAsync();

    /// <summary>获取收藏歌曲完整信息（含艺术家/专辑名）</summary>
    /// <returns>按收藏时间降序排列的歌曲列表</returns>
    public async Task<List<Song>> GetFavoriteSongsAsync()
    {
        await EnsureInitializedAsync();
        var favs = await _database.Table<Favorite>().ToListAsync();
        if (favs.Count == 0) return new List<Song>();

        var allFavIds = favs.Select(f => f.SongId).ToList();
        var favSongs = new List<Song>();
        foreach (var id in allFavIds)
        {
            var song = await _database.Table<Song>().Where(s => s.Id == id).FirstOrDefaultAsync();
            if (song != null) favSongs.Add(song);
        }
        if (favSongs.Count == 0) return new List<Song>();

        var neededArtistIds = favSongs.Select(s => s.ArtistId).Distinct().ToList();
        var neededAlbumIds = favSongs.Select(s => s.AlbumId).Distinct().ToList();
        var artists = await _database.Table<Artist>().Where(a => neededArtistIds.Contains(a.Id)).ToListAsync();
        var albums = await _database.Table<Album>().Where(a => neededAlbumIds.Contains(a.Id)).ToListAsync();
        var artistDict = artists.ToDictionary(a => a.Id, a => a.Name);
        var albumDict = albums.ToDictionary(a => a.Id, a => a.Title);

        foreach (var s in favSongs)
        {
            s.Artist = artistDict.TryGetValue(s.ArtistId, out var an) ? an : "未知艺术家";
            s.Album = albumDict.TryGetValue(s.AlbumId, out var al) ? al : "未知专辑";
        }

        var favDict = favs.ToDictionary(f => f.SongId, f => f.AddedAt);
        return favSongs.OrderByDescending(s => favDict.TryGetValue(s.Id, out var t) ? t : 0).ToList();
    }

    // ═══════════ Lyric ═══════════

    /// <summary>
    /// 保存或更新歌词信息
    /// </summary>
    /// <param name="songId">歌曲 ID</param>
    /// <param name="lrcPath">LRC 文件路径</param>
    /// <param name="content">歌词内容</param>
    public async Task SaveLyricAsync(int songId, string? lrcPath, string? content)
    {
        await EnsureInitializedAsync();
        var l = await _database.Table<Lyric>().Where(x => x.SongId == songId).FirstOrDefaultAsync();
        if (l != null) { l.LrcPath = lrcPath; l.Content = content; await _database.UpdateAsync(l); }
        else await _database.InsertAsync(new Lyric { SongId = songId, LrcPath = lrcPath, Content = content });
    }

    /// <summary>
    /// 获取指定歌曲的歌词信息
    /// </summary>
    /// <param name="songId">歌曲 ID</param>
    /// <returns>歌词信息，未找到时返回 null</returns>
    public Task<Lyric?> GetLyricAsync(int songId) =>
        _database.Table<Lyric>().Where(x => x.SongId == songId).FirstOrDefaultAsync();

    // ═══════════ Connection Profile ═══════════

    /// <summary>
    /// 保存或更新连接配置
    /// </summary>
    /// <param name="profile">连接配置对象</param>
    /// <returns>受影响的行数</returns>
    public async Task<int> SaveConnectionProfileAsync(ConnectionProfile profile)
    {
        await EnsureInitializedAsync();
        if (profile.Id != 0) return await _database.UpdateAsync(profile);
        return await _database.InsertAsync(profile);
    }

    /// <summary>
    /// 获取所有连接配置
    /// </summary>
    /// <returns>连接配置列表</returns>
    public Task<List<ConnectionProfile>> GetConnectionProfilesAsync()
        => _database.Table<ConnectionProfile>().ToListAsync();

    // ═══════════ Playlist CRUD ═══════════

    /// <summary>
    /// 获取所有播放列表
    /// </summary>
    /// <returns>播放列表列表</returns>
    public Task<List<Playlist>> GetAllPlaylistsAsync()
    {
        return _database.Table<Playlist>().ToListAsync();
    }

    /// <summary>
    /// 根据 ID 获取播放列表
    /// </summary>
    /// <param name="id">播放列表 ID</param>
    /// <returns>播放列表对象，未找到时返回 null</returns>
    public Task<Playlist?> GetPlaylistByIdAsync(int id)
    {
        return _database.Table<Playlist>().Where(p => p.Id == id).FirstOrDefaultAsync();
    }

    /// <summary>
    /// 创建新的播放列表
    /// </summary>
    /// <param name="name">播放列表名称</param>
    /// <returns>新播放列表的 ID</returns>
    public async Task<int> CreatePlaylistAsync(string name)
    {
        await EnsureInitializedAsync();
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var playlist = new Playlist { Name = name, CreatedAt = now, UpdatedAt = now };
        return await _database.InsertAsync(playlist);
    }

    /// <summary>
    /// 更新播放列表信息（自动刷新更新时间）
    /// </summary>
    /// <param name="playlist">播放列表对象</param>
    public async Task UpdatePlaylistAsync(Playlist playlist)
    {
        await EnsureInitializedAsync();
        playlist.UpdatedAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        await _database.UpdateAsync(playlist);
    }

    /// <summary>
    /// 删除播放列表及其所有关联歌曲
    /// </summary>
    /// <param name="playlistId">播放列表 ID</param>
    public async Task DeletePlaylistAsync(int playlistId)
    {
        await EnsureInitializedAsync();
        await _database.ExecuteAsync("DELETE FROM PlaylistSongs WHERE PlaylistId = ?", playlistId);
        await _database.DeleteAsync<Playlist>(playlistId);
    }

    /// <summary>
    /// 将歌曲添加到播放列表末尾（重复则忽略）
    /// </summary>
    /// <param name="playlistId">播放列表 ID</param>
    /// <param name="songId">歌曲 ID</param>
    public async Task AddSongToPlaylistAsync(int playlistId, int songId)
    {
        await EnsureInitializedAsync();
        var existing = await _database.Table<PlaylistSong>()
            .Where(ps => ps.PlaylistId == playlistId && ps.SongId == songId)
            .FirstOrDefaultAsync();
        if (existing != null) return;

        var maxPos = await _database.Table<PlaylistSong>()
            .Where(ps => ps.PlaylistId == playlistId)
            .CountAsync();
        await _database.InsertAsync(new PlaylistSong
        {
            PlaylistId = playlistId,
            SongId = songId,
            Position = maxPos
        });

        var playlist = await GetPlaylistByIdAsync(playlistId);
        if (playlist != null)
        {
            playlist.SongCount = maxPos + 1;
            await UpdatePlaylistAsync(playlist);
        }
    }

    /// <summary>
    /// 从播放列表中移除歌曲并重新调整位置
    /// </summary>
    /// <param name="playlistId">播放列表 ID</param>
    /// <param name="songId">歌曲 ID</param>
    public async Task RemoveSongFromPlaylistAsync(int playlistId, int songId)
    {
        await EnsureInitializedAsync();
        var entry = await _database.Table<PlaylistSong>()
            .Where(ps => ps.PlaylistId == playlistId && ps.SongId == songId)
            .FirstOrDefaultAsync();
        if (entry == null) return;

        await _database.DeleteAsync(entry);

        var remaining = await _database.Table<PlaylistSong>()
            .Where(ps => ps.PlaylistId == playlistId)
            .OrderBy(ps => ps.Position)
            .ToListAsync();
        for (int i = 0; i < remaining.Count; i++)
        {
            remaining[i].Position = i;
            await _database.UpdateAsync(remaining[i]);
        }

        var playlist = await GetPlaylistByIdAsync(playlistId);
        if (playlist != null)
        {
            playlist.SongCount = remaining.Count;
            await UpdatePlaylistAsync(playlist);
        }
    }

    /// <summary>
    /// 获取播放列表中的所有歌曲（按位置排序，含艺术家和专辑信息）
    /// </summary>
    /// <param name="playlistId">播放列表 ID</param>
    /// <returns>歌曲列表</returns>
    public async Task<List<Song>> GetPlaylistSongsAsync(int playlistId)
    {
        await EnsureInitializedAsync();
        var entries = await _database.Table<PlaylistSong>()
            .Where(ps => ps.PlaylistId == playlistId)
            .OrderBy(ps => ps.Position)
            .ToListAsync();
        if (entries.Count == 0) return new List<Song>();

        var songIds = entries.Select(e => e.SongId).ToList();
        var songs = await _database.Table<Song>().Where(s => songIds.Contains(s.Id)).ToListAsync();
        var artists = await _database.Table<Artist>().ToListAsync();
        var albums = await _database.Table<Album>().ToListAsync();
        var artistDict = artists.ToDictionary(a => a.Id, a => a.Name);
        var albumDict = albums.ToDictionary(a => a.Id, a => a.Title);

        var songMap = songs.ToDictionary(s => s.Id);

        var sorted = new List<Song>(entries.Count);
        foreach (var entry in entries)
        {
            if (songMap.TryGetValue(entry.SongId, out var song))
            {
                song.Artist = artistDict.TryGetValue(song.ArtistId, out var an) ? an : "未知艺术家";
                song.Album = albumDict.TryGetValue(song.AlbumId, out var al) ? al : "未知专辑";
                sorted.Add(song);
            }
        }
        return sorted;
    }

    /// <summary>
    /// 更新播放列表中歌曲的位置
    /// </summary>
    /// <param name="playlistId">播放列表 ID</param>
    /// <param name="songId">歌曲 ID</param>
    /// <param name="newPosition">新位置索引</param>
    public async Task UpdateSongPositionAsync(int playlistId, int songId, int newPosition)
    {
        await EnsureInitializedAsync();
        var entry = await _database.Table<PlaylistSong>()
            .Where(ps => ps.PlaylistId == playlistId && ps.SongId == songId)
            .FirstOrDefaultAsync();
        if (entry == null) return;

        entry.Position = newPosition;
        await _database.UpdateAsync(entry);
    }

    /// <summary>
    /// 批量更新播放列表中所有歌曲的顺序位置
    /// </summary>
    /// <param name="playlistId">播放列表 ID</param>
    /// <param name="orderedSongIds">排序后的歌曲 ID 列表</param>
    public async Task UpdatePlaylistOrderAsync(int playlistId, List<int> orderedSongIds)
    {
        await EnsureInitializedAsync();

        var allEntries = await _database.Table<PlaylistSong>()
            .Where(ps => ps.PlaylistId == playlistId)
            .ToListAsync();

        System.Diagnostics.Debug.WriteLine($"[DB] UpdatePlaylistOrderAsync: playlistId={playlistId}, orderedSongIds.Count={orderedSongIds.Count}, existingEntries.Count={allEntries.Count}");

        var entryDict = allEntries.ToDictionary(e => e.SongId);

        int updatedCount = 0;
        int missingCount = 0;

        for (int i = 0; i < orderedSongIds.Count; i++)
        {
            var songId = orderedSongIds[i];
            if (entryDict.TryGetValue(songId, out var entry))
            {
                entry.Position = i;
                await _database.UpdateAsync(entry);
                updatedCount++;
            }
            else
            {
                missingCount++;
                System.Diagnostics.Debug.WriteLine($"[DB] UpdatePlaylistOrderAsync: SongId={songId} not found in PlaylistSong for playlist {playlistId}");
            }
        }

        System.Diagnostics.Debug.WriteLine($"[DB] UpdatePlaylistOrderAsync completed: {updatedCount} updated, {missingCount} missing");
    }

    /// <summary>
    /// 获取播放列表中的歌曲数量
    /// </summary>
    /// <param name="playlistId">播放列表 ID</param>
    /// <returns>歌曲数量</returns>
    public async Task<int> GetPlaylistSongCountAsync(int playlistId)
    {
        return await _database.Table<PlaylistSong>()
            .Where(ps => ps.PlaylistId == playlistId)
            .CountAsync();
    }

    /// <summary>
    /// 获取播放列表中的第一首歌曲
    /// </summary>
    /// <param name="playlistId">播放列表 ID</param>
    /// <returns>歌曲对象，播放列表为空时返回 null</returns>
    public async Task<Song?> GetFirstSongInPlaylistAsync(int playlistId)
    {
        await EnsureInitializedAsync();
        var entry = await _database.Table<PlaylistSong>()
            .Where(ps => ps.PlaylistId == playlistId)
            .OrderBy(ps => ps.Position)
            .FirstOrDefaultAsync();
        if (entry == null) return null;

        var song = await _database.Table<Song>().Where(s => s.Id == entry.SongId).FirstOrDefaultAsync();
        if (song == null) return null;

        var artist = await _database.Table<Artist>().Where(a => a.Id == song.ArtistId).FirstOrDefaultAsync();
        var album = await _database.Table<Album>().Where(a => a.Id == song.AlbumId).FirstOrDefaultAsync();
        song.Artist = artist?.Name ?? "未知艺术家";
        song.Album = album?.Title ?? "未知专辑";
        return song;
    }

    // ═══════════ CachedSong CRUD ═══════════

    /// <summary>
    /// 保存或更新缓存歌曲信息
    /// </summary>
    /// <param name="cachedSong">缓存歌曲对象</param>
    public async Task SaveCachedSongAsync(CachedSong cachedSong)
    {
        await EnsureInitializedAsync();
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
        await EnsureInitializedAsync();
        var cached = await _database.Table<CachedSong>().Where(c => c.SongId == songId).FirstOrDefaultAsync();
        if (cached != null)
            await _database.DeleteAsync(cached);
    }

    // ═══════════ Network Song Cache ═══════════

    /// <summary>替换所有网络缓存歌曲（先清除旧的，再批量写入新的）</summary>
    /// <param name="songs">新歌曲列表</param>
    public async Task ReplaceNetworkSongsAsync(List<Song> songs)
    {
        await EnsureInitializedAsync();
        try { await _database.ExecuteAsync("DELETE FROM Songs WHERE Source = ?", (int)SongSource.WebDAV); }
        catch { }

        if (songs.Count == 0) return;

        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        foreach (var s in songs) s.DateAdded = now;

        // Phase 1: 批量处理 Artist
        var allArtists = await _database.Table<Artist>().ToListAsync();
        var artistNameToId = allArtists.ToDictionary(a => a.Name, a => a.Id);
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
        var albumKeyToId = allAlbums.ToDictionary(a => (a.Title ?? "", a.ArtistId), a => a.Id);
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
    }

    /// <summary>获取缓存的网络歌曲</summary>
    /// <returns>去重后的网络歌曲列表（SMB 优先于 WebDAV）</returns>
    public async Task<List<Song>> GetCachedNetworkSongsAsync()
    {
        await EnsureInitializedAsync();
        var songs = await _database.Table<Song>()
            .Where(s => s.Source == SongSource.WebDAV || s.Source == SongSource.SMB)
            .ToListAsync();
        // 填充 Artist/Album 名称
        var artists = await _database.Table<Artist>().ToListAsync();
        var albums = await _database.Table<Album>().ToListAsync();
        var artistDict = artists.ToDictionary(a => a.Id, a => a.Name);
        var albumDict = albums.ToDictionary(a => a.Id, a => a.Title);
        foreach (var s in songs)
        {
            s.Artist = artistDict.TryGetValue(s.ArtistId, out var an) ? an : "未知艺术家";
            s.Album = albumDict.TryGetValue(s.AlbumId, out var al) ? al : "未知专辑";
        }
        // 同一标题+艺术家的歌曲去重，SMB 优先于 WebDAV
        return songs
            .GroupBy(s => (s.Title?.Trim() ?? "").ToLowerInvariant() + "|" + (s.Artist?.Trim() ?? "").ToLowerInvariant())
            .Select(g => g.FirstOrDefault(s => s.Source == SongSource.SMB) ?? g.First())
            .ToList();
    }

    /// <summary>缓存网络歌曲数量</summary>
    /// <returns>网络歌曲总数</returns>
    public async Task<int> GetCachedNetworkSongCountAsync()
        => await _database.Table<Song>().Where(s => s.Source == SongSource.WebDAV || s.Source == SongSource.SMB).CountAsync();

    public async Task ReplaceNetworkSongsBeginAsync()
    {
        await EnsureInitializedAsync();
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
        await EnsureInitializedAsync();

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

    /// <summary>插入单首歌曲（用于增量入库），网络歌曲基于 RemoteId 去重</summary>
    /// <param name="song">歌曲对象</param>
    public async Task InsertSongAsync(Song song)
    {
        await EnsureInitializedAsync();
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
            System.Diagnostics.Debug.WriteLine($"[CatClaw] InsertSong 失败: {song.Title} - {ex.Message}");
        }
    }

    /// <summary>
    /// 迁移旧版 Playlist 表到新版 Playlists 表
    /// </summary>
    private async Task MigratePlaylistsTableAsync()
    {
        try
        {
            var hasOldTable = await TableExistsAsync("Playlist");
            var hasNewTable = await TableExistsAsync("Playlists");

            if (hasOldTable)
            {
                if (hasNewTable)
                    await _database.ExecuteAsync("DROP TABLE Playlists");
                await _database.ExecuteAsync("ALTER TABLE Playlist RENAME TO Playlists");
                hasNewTable = true;
            }

            if (!hasNewTable) return;

            var cols = await _database.QueryAsync<TableColumn>("PRAGMA table_info(Playlists)");
            if (cols.Any(c => c.pk > 0)) return;

            await _database.ExecuteAsync("ALTER TABLE Playlists RENAME TO Playlists_old");
            await _database.CreateTableAsync<Playlist>();
            await _database.ExecuteAsync(
                "INSERT INTO Playlists(Id, Name, CreatedAt, UpdatedAt, SongCount, IsSystem) " +
                "SELECT Id, Name, CreatedAt, UpdatedAt, SongCount, IsSystem FROM Playlists_old");
            await _database.ExecuteAsync("DROP TABLE Playlists_old");
        }
        catch { }
    }

    /// <summary>
    /// 迁移 PlaylistSongs 表结构，确保包含自增主键
    /// </summary>
    private async Task MigratePlaylistSongsTableAsync()
    {
        try
        {
            var exists = await TableExistsAsync("PlaylistSongs");
            if (!exists) return;

            var columns = await _database.QueryAsync<TableColumn>("PRAGMA table_info(PlaylistSongs)");
            if (columns.Any(c => c.pk > 0)) return;

            await _database.ExecuteAsync("ALTER TABLE PlaylistSongs RENAME TO PlaylistSongs_old");
            await _database.CreateTableAsync<PlaylistSong>();
            await _database.ExecuteAsync(
                "INSERT INTO PlaylistSongs(PlaylistId, SongId, Position) " +
                "SELECT PlaylistId, SongId, Position FROM PlaylistSongs_old");
            await _database.ExecuteAsync("DROP TABLE PlaylistSongs_old");
        }
        catch { }
    }

    /// <summary>
    /// 检查指定表是否存在于数据库中
    /// </summary>
    private async Task<bool> TableExistsAsync(string tableName)
    {
        var count = await _database.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name=?", tableName);
        return count > 0;
    }

    /// <summary>
    /// SQLite PRAGMA table_info 返回的列信息
    /// </summary>
    private class TableColumn
    {
        /// <summary>
        /// 列序号
        /// </summary>
        public int cid { get; set; }

        /// <summary>
        /// 列名
        /// </summary>
        public string name { get; set; } = string.Empty;

        /// <summary>
        /// 列类型
        /// </summary>
        public string type { get; set; } = string.Empty;

        /// <summary>
        /// 是否非空约束
        /// </summary>
        public int notnull { get; set; }

        /// <summary>
        /// 默认值
        /// </summary>
        public string dflt_value { get; set; } = string.Empty;

        /// <summary>
        /// 是否主键
        /// </summary>
        public int pk { get; set; }
    }
}
