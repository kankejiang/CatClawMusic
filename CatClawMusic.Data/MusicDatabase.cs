using CatClawMusic.Core.Models;
using CatClawMusic.Core.Services;
using SQLite;

namespace CatClawMusic.Data;

/// <summary>
/// SQLite 数据库操作层，管理歌曲、艺术家、专辑、播放列表、收藏等数据的持久化
/// </summary>
public class MusicDatabase
{
    /// <summary>
    /// 安全的 ToDictionary：遇到重复键时保留第一个，避免异常
    /// </summary>
    private static Dictionary<TKey, TValue> SafeToDict<TSource, TKey, TValue>(
        IEnumerable<TSource> source, Func<TSource, TKey> keySelector, Func<TSource, TValue> valueSelector)
        where TKey : notnull
    {
        var dict = new Dictionary<TKey, TValue>();
        foreach (var item in source)
        {
            var key = keySelector(item);
            if (!dict.ContainsKey(key))
                dict[key] = valueSelector(item);
        }
        return dict;
    }
    /// <summary>
    /// SQLite 异步数据库连接
    /// </summary>
    private readonly SQLiteAsyncConnection _database;

    /// <summary>
    /// 数据库是否已完成初始化
    /// </summary>
    private bool _isInitialized;

    /// <summary>
    /// 从文件路径提取艺术家名称的回调（由 UI 层设置，用于修复 ArtistId=0 的歌曲）
    /// </summary>
    public Func<string, string?>? ExtractArtistNameCallback { get; set; }

    /// <summary>
    /// 初始化信号量，确保并发安全
    /// </summary>
    private readonly SemaphoreSlim _initSemaphore = new(1, 1);

    /// <summary>
    /// 后台维护任务信号量，确保维护任务与依赖维护完成的查询串行
    /// </summary>
    private readonly SemaphoreSlim _maintenanceSemaphore = new(1, 1);

    /// <summary>
    /// 后台维护任务（拆分合并艺术家、修复专辑关联），在基础初始化完成后启动
    /// </summary>
    private Task? _maintenanceTask;

    /// <summary>
    /// 后台维护是否已完成
    /// </summary>
    private bool _maintenanceCompleted;

    /// <summary>
    /// 使用指定的数据库路径创建 MusicDatabase 实例
    /// </summary>
    public MusicDatabase(string dbPath)
    {
        _database = new SQLiteAsyncConnection(dbPath);
    }

    /// <summary>
    /// 确保数据库表已创建并完成必要迁移，多次调用安全。
    /// 合并艺术家拆分、专辑关联修复等耗时维护任务在后台执行，不阻塞启动。
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

            // PlayHistory 迁移必须在 CreateTableAsync 之前，否则旧表缺少主键列会导致 "Cannot add a PRIMARY KEY column"
            await MigratePlayHistoryTableAsync();
            await _database.CreateTableAsync<PlayHistory>();
            await _database.CreateTableAsync<Favorite>();
            await _database.CreateTableAsync<Lyric>();
            await _database.CreateTableAsync<ConnectionProfile>();
            await _database.CreateTableAsync<CachedSong>();
            await _database.CreateTableAsync<SongArtist>();

            await CreateIndexesAsync();

            await MigratePlaylistsTableAsync();
            await MigratePlaylistSongsTableAsync();
            await MigrateArtistsTableAsync();
            await RecoverArtistsTableAsync();

            // 迁移现有单艺术家数据到多对多 SongArtists 表
            await MigrateToMultiArtistAsync();

            _isInitialized = true;

            // 耗时维护任务放到后台，避免阻塞启动页跳转
            _maintenanceTask = Task.Run(RunMaintenanceAsync);
        }
        finally
        {
            _initSemaphore.Release();
        }
    }

    /// <summary>
    /// 等待后台维护任务完成。读取/写入歌曲、艺术家、专辑相关数据前调用，确保数据修复已完成。
    /// </summary>
    public async Task EnsureMaintenanceCompletedAsync()
    {
        await EnsureInitializedAsync();
        var task = _maintenanceTask;
        if (task != null)
            await task;
    }

    /// <summary>
    /// 后台维护：执行耗时的历史数据修复任务
    /// </summary>
    private async Task RunMaintenanceAsync()
    {
        await _maintenanceSemaphore.WaitAsync();
        try
        {
            if (_maintenanceCompleted) return;

            // 将历史合并艺术家名（如 "国风堂/哦漏"）拆分为独立艺术家
            await SplitCombinedArtistsAsync();

            // 修复早期版本中 ArtistId=0 导致 AlbumId 关联错误的问题
            await RepairAlbumAssociationsAsync();

            _maintenanceCompleted = true;
        }
        finally
        {
            _maintenanceSemaphore.Release();
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
        try { await _database.ExecuteAsync("CREATE INDEX IF NOT EXISTS idx_song_artists_song ON SongArtists(SongId)"); } catch { }
        try { await _database.ExecuteAsync("CREATE INDEX IF NOT EXISTS idx_song_artists_artist ON SongArtists(ArtistId)"); } catch { }
    }

    // ═══════════ Song CRUD ═══════════

    /// <summary>
    /// 获取所有已启用协议的 ProtocolType 集合（用于过滤歌曲）
    /// </summary>
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
    public Task<List<Song>> GetSongsAsync() => GetSongsWithDetailsAsync();

    public async Task<int> GetLocalSongCountAsync()
    {
        await EnsureMaintenanceCompletedAsync();
        return await _database.Table<Song>().Where(s => s.Source == SongSource.Local).CountAsync();
    }

    public async Task<int> GetNetworkSongCountAsync()
    {
        await EnsureMaintenanceCompletedAsync();
        return await _database.Table<Song>()
            .Where(s => s.Source == SongSource.WebDAV || s.Source == SongSource.SMB)
            .CountAsync();
    }

    public async Task<int> GetMergedDedupedCountAsync()
    {
        await EnsureMaintenanceCompletedAsync();
        // 只统计本地歌曲数量，与本地音乐标签页一致
        return await _database.Table<Song>()
            .Where(s => s.Source == SongSource.Local)
            .CountAsync();
    }

    public async Task<int> GetFavoriteCountAsync()
    {
        await EnsureMaintenanceCompletedAsync();
        return await _database.Table<Favorite>().CountAsync();
    }

    public async Task<int> GetRecentPlayCountAsync()
    {
        await EnsureMaintenanceCompletedAsync();
        var history = await _database.Table<PlayHistory>().OrderByDescending(h => h.PlayedAt).Take(200).ToListAsync();
        if (history.Count == 0) return 0;
        var songIds = history.Select(h => h.SongId).ToHashSet();
        // 只统计 Songs 表中仍存在的歌曲数量
        var allSongs = await _database.Table<Song>().ToListAsync();
        return allSongs.Count(s => songIds.Contains(s.Id));
    }

    public async Task<int> GetFirstSongIdForAllAsync()
    {
        await EnsureMaintenanceCompletedAsync();
        var song = await _database.Table<Song>().Where(s => s.Source == SongSource.Local).FirstOrDefaultAsync();
        if (song != null) return song.Id;
        song = await _database.Table<Song>().FirstOrDefaultAsync();
        return song?.Id ?? 0;
    }

    public async Task<int> GetFirstFavoriteSongIdAsync()
    {
        await EnsureMaintenanceCompletedAsync();
        var fav = await _database.Table<Favorite>().OrderByDescending(f => f.AddedAt).FirstOrDefaultAsync();
        return fav?.SongId ?? 0;
    }

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
        // 批量预加载艺术家和专辑，避免 N+1 查询
        var artists = await _database.Table<Artist>().ToListAsync();
        var albums = await _database.Table<Album>().ToListAsync();
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
        }
        return songs;
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

    /// <summary>删除指定来源中不在保留路径集合内的歌曲，并清理孤立艺术家/专辑</summary>
    /// <param name="source">歌曲来源类型</param>
    /// <param name="retainPaths">需要保留的本地文件路径集合</param>
    /// <param name="retainRemoteIds">需要保留的远程 ID 集合</param>
    /// <returns>删除的歌曲数量</returns>
    public async Task<int> RemoveStaleSongsAsync(SongSource source, HashSet<string> retainPaths, HashSet<string>? retainRemoteIds = null)
    {
        await EnsureMaintenanceCompletedAsync();
        var all = await _database.Table<Song>().Where(s => s.Source == source).ToListAsync();
        var toDeleteIds = new List<int>();
        foreach (var s in all)
        {
            bool keep = source == SongSource.Local
                ? retainPaths.Any(p => s.FilePath.StartsWith(p, StringComparison.OrdinalIgnoreCase))
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
    /// <returns>艺术家 ID，名称为空时返回 0</returns>
    public async Task<int> EnsureArtistAsync(string name)
    {
        await EnsureMaintenanceCompletedAsync();
        if (string.IsNullOrEmpty(name)) return 0;

        // 防御性拆分：无论调用方是否已拆分，此处统一处理
        // 避免 "国风堂/哦漏" 被直接写入 Artists 表
        var names = MusicUtility.SplitArtistNames(name);
        var firstId = 0;

        foreach (var n in names)
        {
            var a = await _database.Table<Artist>().Where(x => x.Name == n).FirstOrDefaultAsync();
            if (a != null)
            {
                if (firstId == 0) firstId = a.Id;
            }
            else
            {
                var newArtist = new Artist { Name = n };
                await _database.InsertAsync(newArtist);
                if (firstId == 0) firstId = newArtist.Id;
            }
        }

        return firstId > 0 ? firstId : 0;
    }

    public async Task<List<Artist>> GetAllArtistsAsync()
    {
        await EnsureMaintenanceCompletedAsync();
        return await _database.Table<Artist>().ToListAsync();
    }

    public async Task UpdateArtistAsync(Artist artist)
    {
        await EnsureMaintenanceCompletedAsync();
        await _database.UpdateAsync(artist);
    }

    public async Task<List<Album>> GetAllAlbumsAsync()
    {
        await EnsureMaintenanceCompletedAsync();
        return await _database.Table<Album>().ToListAsync();
    }

    /// <summary>
    /// 修复歌曲的 AlbumId 关联：根据歌曲的 Album 名称和 Artist 名称重新匹配正确的专辑 ID。
    /// 解决早期版本中 ArtistId=0 导致 AlbumId 关联错误的问题。
    /// </summary>
    public async Task RepairAlbumAssociationsAsync()
    {
        // 注意：此方法可能从 EnsureInitializedAsync 内部调用，不能再调 EnsureInitializedAsync 以避免信号量死锁
        // Song.Artist 和 Song.Album 是 [Ignore] 字段，不存储在数据库中
        // 需要从文件标签重新读取来修复关联
        try
        {
            var songs = await _database.Table<Song>().ToListAsync();
            var artists = await _database.Table<Artist>().ToListAsync();
            var albums = await _database.Table<Album>().ToListAsync();

            var artistDict = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (var a in artists)
                if (!artistDict.ContainsKey(a.Name))
                    artistDict[a.Name] = a.Id;

            var albumDict = new Dictionary<(string title, int artistId), int>();
            foreach (var a in albums)
                if (!albumDict.ContainsKey((a.Title, a.ArtistId)))
                    albumDict[(a.Title, a.ArtistId)] = a.Id;

            int fixedCount = 0;
            await _database.RunInTransactionAsync(tran =>
            {
                foreach (var song in songs)
                {
                    // 从文件标签重新读取艺术家和专辑名
                    string? artistName = null;
                    string? albumName = null;

                    if (!string.IsNullOrEmpty(song.FilePath) && System.IO.File.Exists(song.FilePath))
                    {
                        try
                        {
                            var tagInfo = TagReader.ReadSongInfo(song.FilePath);
                            if (tagInfo != null)
                            {
                                artistName = tagInfo.Artist;
                                albumName = tagInfo.Album;
                            }
                        }
                        catch { }
                    }

                    // 回退到 ExtractArtistNameCallback
                    if (string.IsNullOrEmpty(artistName) && ExtractArtistNameCallback != null && !string.IsNullOrEmpty(song.FilePath))
                        artistName = ExtractArtistNameCallback(song.FilePath);

                    // 重新计算正确的 ArtistId
                    int correctArtistId = song.ArtistId;
                    if (!string.IsNullOrEmpty(artistName))
                    {
                        if (artistDict.TryGetValue(artistName, out var aid))
                        {
                            correctArtistId = aid;
                        }
                        else
                        {
                            // 艺术家不在数据库中，创建新的
                            var newArtist = new Artist { Name = artistName };
                            tran.Insert(newArtist);
                            correctArtistId = newArtist.Id;
                            artistDict[artistName] = correctArtistId;
                        }
                    }

                    // 重新计算正确的 AlbumId
                    int correctAlbumId = song.AlbumId;
                    if (!string.IsNullOrEmpty(albumName))
                    {
                        if (albumDict.TryGetValue((albumName, correctArtistId), out var albId))
                        {
                            correctAlbumId = albId;
                        }
                        else
                        {
                            // 创建新的专辑
                            var newAlbum = new Album { Title = albumName, ArtistId = correctArtistId };
                            tran.Insert(newAlbum);
                            correctAlbumId = newAlbum.Id;
                            albumDict[(albumName, correctArtistId)] = correctAlbumId;
                        }
                    }

                    // 如果 ArtistId 或 AlbumId 有误，更新
                    if (correctArtistId != song.ArtistId || correctAlbumId != song.AlbumId)
                    {
                        song.ArtistId = correctArtistId;
                        song.AlbumId = correctAlbumId;
                        tran.Update(song);
                        fixedCount++;
                    }
                }
            });
            System.Diagnostics.Debug.WriteLine($"[CatClaw] 专辑关联修复完成，修正 {fixedCount} 首歌曲");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[CatClaw] 专辑关联修复失败: {ex.Message}");
        }
    }

    /// <summary>
    /// 批量确保艺术家存在，返回艺术家名到 ID 的映射。一次性查询 + 批量插入，避免逐条数据库往返。
    /// </summary>
    public async Task<Dictionary<string, int>> EnsureArtistsBatchAsync(List<string> names)
    {
        await EnsureMaintenanceCompletedAsync();
        var result = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        if (names.Count == 0) return result;

        // 统一拆分 "A/B" 等多艺术家名字
        var allNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var name in names)
        {
            if (string.IsNullOrEmpty(name)) continue;
            foreach (var n in MusicUtility.SplitArtistNames(name))
                allNames.Add(n);
        }

        if (allNames.Count == 0) return result;

        // 批量查询已存在的艺术家（分批避免 IN 子句过长）
        var nameList = allNames.ToList();
        const int chunkSize = 500;
        for (int i = 0; i < nameList.Count; i += chunkSize)
        {
            var chunk = nameList.Skip(i).Take(chunkSize).ToList();
            var existing = await _database.Table<Artist>()
                .Where(a => chunk.Contains(a.Name))
                .ToListAsync();
            foreach (var a in existing)
                result[a.Name] = a.Id;
        }

        // 批量插入缺失的艺术家
        var missing = allNames.Where(n => !result.ContainsKey(n)).ToList();
        if (missing.Count > 0)
        {
            await _database.RunInTransactionAsync(tran =>
            {
                foreach (var n in missing)
                {
                    var artist = new Artist { Name = n };
                    tran.Insert(artist);
                    result[n] = artist.Id;
                }
            });
        }

        return result;
    }

    /// <summary>
    /// 批量确保专辑存在，返回 (专辑名, 艺术家ID) 到 ID 的映射。一次性查询 + 批量插入。
    /// </summary>
    public async Task<Dictionary<(string title, int artistId), int>> EnsureAlbumsBatchAsync(List<(string title, int artistId)> albums)
    {
        await EnsureMaintenanceCompletedAsync();
        var result = new Dictionary<(string title, int artistId), int>();
        if (albums.Count == 0) return result;

        var uniqueAlbums = albums
            .Where(a => !string.IsNullOrEmpty(a.title))
            .Distinct()
            .ToList();

        if (uniqueAlbums.Count == 0) return result;

        // 按艺术家 ID 分批查询已存在的专辑
        var artistIds = uniqueAlbums.Select(a => a.artistId).Distinct().ToList();
        const int idChunkSize = 300;
        var existingDict = new Dictionary<(string title, int artistId), Album>();
        for (int i = 0; i < artistIds.Count; i += idChunkSize)
        {
            var chunk = artistIds.Skip(i).Take(idChunkSize).ToList();
            var existing = await _database.Table<Album>()
                .Where(al => chunk.Contains(al.ArtistId))
                .ToListAsync();
            foreach (var al in existing)
            {
                var key = (al.Title, al.ArtistId);
                if (!existingDict.ContainsKey(key))
                    existingDict[key] = al;
            }
        }

        foreach (var key in uniqueAlbums)
        {
            if (existingDict.TryGetValue(key, out var al))
                result[key] = al.Id;
        }

        // 批量插入缺失的专辑
        var missing = uniqueAlbums.Where(k => !result.ContainsKey(k)).ToList();
        if (missing.Count > 0)
        {
            await _database.RunInTransactionAsync(tran =>
            {
                foreach (var k in missing)
                {
                    var album = new Album { Title = k.title, ArtistId = k.artistId };
                    tran.Insert(album);
                    result[k] = album.Id;
                }
            });
        }

        return result;
    }

    /// <summary>
    /// 根据标题和艺术家 ID 查找或创建专辑，返回专辑 ID
    /// </summary>
    /// <param name="title">专辑标题</param>
    /// <param name="artistId">艺术家 ID</param>
    /// <returns>专辑 ID，标题为空时返回 0</returns>
    public async Task<int> EnsureAlbumAsync(string title, int artistId)
    {
        await EnsureMaintenanceCompletedAsync();
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
        try
        {
            await EnsureMaintenanceCompletedAsync();
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
            await TrimHistoryAsync(200);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[CatClaw] RecordPlayAsync 失败: {ex.Message}");
        }
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
            // 按主键 Id 删除最旧的记录，避免按 SongId 误删多条
            await _database.ExecuteAsync(
                "DELETE FROM PlayHistory WHERE Id IN (SELECT Id FROM PlayHistory ORDER BY PlayedAt ASC LIMIT ?)",
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
        await EnsureMaintenanceCompletedAsync();
        var history = await _database.Table<PlayHistory>().OrderByDescending(h => h.PlayedAt).Take(200).ToListAsync();
        if (history.Count == 0) return new List<Song>();

        var songIds = history.Select(h => h.SongId).ToHashSet();
        var allSongs = await _database.Table<Song>().ToListAsync();
        var songs = allSongs.Where(s => songIds.Contains(s.Id)).ToList();
        if (songs.Count == 0) return new List<Song>();

        // 只过滤孤立记录，不删除（歌曲可能因权限过期暂时不可见，重新扫描后可恢复）
        var foundIds = songs.Select(s => s.Id).ToHashSet();
        var validHistory = history.Where(h => foundIds.Contains(h.SongId)).ToList();

        var artists = await _database.Table<Artist>().ToListAsync();
        var albums = await _database.Table<Album>().ToListAsync();
        var artistDict = SafeToDict(artists, a => a.Id, a => a.Name);
        var albumDict = SafeToDict(albums, a => a.Id, a => a.Title);

        foreach (var s in songs)
        {
            s.Artist = artistDict.TryGetValue(s.ArtistId, out var an) ? an : "未知艺术家";
            s.Album = albumDict.TryGetValue(s.AlbumId, out var al) ? al : "未知专辑";
            s.PlayCount = validHistory.FirstOrDefault(h => h.SongId == s.Id)?.PlayCount ?? 0;
        }

        var playTimeDict = validHistory.GroupBy(h => h.SongId).ToDictionary(g => g.Key, g => g.Max(h => h.PlayedAt));

        // 填充多艺术家
        var allArtistsDict2 = await GetAllArtistsForSongsAsync(songs.Select(s => s.Id));
        foreach (var s in songs)
            s.AllArtists = allArtistsDict2.TryGetValue(s.Id, out var aa) ? aa : s.Artist;

        return songs.OrderByDescending(s => playTimeDict.TryGetValue(s.Id, out var t) ? t : 0).ToList();
    }

    /// <summary>获取播放次数最多的歌曲（含艺术家/专辑名和播放计数）</summary>
    /// <param name="limit">最大返回数量</param>
    /// <returns>按播放次数降序排列的歌曲列表</returns>
    public async Task<List<Song>> GetTopPlayedSongsAsync(int limit = 50)
    {
        await EnsureMaintenanceCompletedAsync();
        var history = await _database.Table<PlayHistory>().OrderByDescending(h => h.PlayCount).Take(limit).ToListAsync();
        if (history.Count == 0) return new List<Song>();

        var songIds = history.Select(h => h.SongId).ToHashSet();
        var allSongs = await _database.Table<Song>().ToListAsync();
        var songs = allSongs.Where(s => songIds.Contains(s.Id)).ToList();
        if (songs.Count == 0) return new List<Song>();

        // 只过滤孤立记录，不删除（歌曲可能因权限过期暂时不可见，重新扫描后可恢复）
        var foundIds = songs.Select(s => s.Id).ToHashSet();
        var validHistory = history.Where(h => foundIds.Contains(h.SongId)).ToList();

        var artists = await _database.Table<Artist>().ToListAsync();
        var albums = await _database.Table<Album>().ToListAsync();
        var artistDict = SafeToDict(artists, a => a.Id, a => a.Name);
        var albumDict = SafeToDict(albums, a => a.Id, a => a.Title);

        foreach (var s in songs)
        {
            s.Artist = artistDict.TryGetValue(s.ArtistId, out var an) ? an : "未知艺术家";
            s.Album = albumDict.TryGetValue(s.AlbumId, out var al) ? al : "未知专辑";
            s.PlayCount = validHistory.FirstOrDefault(h => h.SongId == s.Id)?.PlayCount ?? 0;
        }

        var playCountDict = validHistory.GroupBy(h => h.SongId).ToDictionary(g => g.Key, g => g.Max(h => h.PlayCount));

        // 填充多艺术家
        var allArtistsDict3 = await GetAllArtistsForSongsAsync(songs.Select(s => s.Id));
        foreach (var s in songs)
            s.AllArtists = allArtistsDict3.TryGetValue(s.Id, out var aa) ? aa : s.Artist;

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
        await EnsureMaintenanceCompletedAsync();
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
        await EnsureMaintenanceCompletedAsync();
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
        await EnsureMaintenanceCompletedAsync();
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
        var artistDict = SafeToDict(artists, a => a.Id, a => a.Name);
        var albumDict = SafeToDict(albums, a => a.Id, a => a.Title);

        foreach (var s in favSongs)
        {
            s.Artist = artistDict.TryGetValue(s.ArtistId, out var an) ? an : "未知艺术家";
            s.Album = albumDict.TryGetValue(s.AlbumId, out var al) ? al : "未知专辑";
        }

        var favDict = SafeToDict(favs, f => f.SongId, f => f.AddedAt);

        // 填充多艺术家
        var allArtistsDict4 = await GetAllArtistsForSongsAsync(favSongs.Select(s => s.Id));
        foreach (var s in favSongs)
            s.AllArtists = allArtistsDict4.TryGetValue(s.Id, out var aa) ? aa : s.Artist;

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
        await EnsureMaintenanceCompletedAsync();
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
        await EnsureMaintenanceCompletedAsync();
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
        await EnsureMaintenanceCompletedAsync();
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
        await EnsureMaintenanceCompletedAsync();
        playlist.UpdatedAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        await _database.UpdateAsync(playlist);
    }

    /// <summary>
    /// 删除播放列表及其所有关联歌曲
    /// </summary>
    /// <param name="playlistId">播放列表 ID</param>
    public async Task DeletePlaylistAsync(int playlistId)
    {
        await EnsureMaintenanceCompletedAsync();
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
        await EnsureMaintenanceCompletedAsync();
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
        await EnsureMaintenanceCompletedAsync();
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
    /// 从播放列表中批量移除歌曲并重新调整位置
    /// </summary>
    /// <param name="playlistId">播放列表 ID</param>
    /// <param name="songIds">要移除的歌曲 ID 集合</param>
    public async Task RemoveSongsFromPlaylistAsync(int playlistId, IEnumerable<int> songIds)
    {
        await EnsureMaintenanceCompletedAsync();
        var ids = songIds.ToHashSet();
        if (ids.Count == 0) return;

        // 一次性删除目标记录
        await _database.ExecuteAsync(
            "DELETE FROM PlaylistSongs WHERE PlaylistId = ? AND SongId IN (" + string.Join(",", ids) + ")",
            playlistId);

        // 重新整理剩余歌曲的位置
        var remaining = await _database.Table<PlaylistSong>()
            .Where(ps => ps.PlaylistId == playlistId)
            .OrderBy(ps => ps.Position)
            .ToListAsync();
        for (int i = 0; i < remaining.Count; i++)
        {
            if (remaining[i].Position != i)
            {
                remaining[i].Position = i;
                await _database.UpdateAsync(remaining[i]);
            }
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
        await EnsureMaintenanceCompletedAsync();
        var entries = await _database.Table<PlaylistSong>()
            .Where(ps => ps.PlaylistId == playlistId)
            .OrderBy(ps => ps.Position)
            .ToListAsync();
        if (entries.Count == 0) return new List<Song>();

        var songIds = entries.Select(e => e.SongId).ToList();
        var songs = await _database.Table<Song>().Where(s => songIds.Contains(s.Id)).ToListAsync();
        var artists = await _database.Table<Artist>().ToListAsync();
        var albums = await _database.Table<Album>().ToListAsync();
        var artistDict = SafeToDict(artists, a => a.Id, a => a.Name);
        var albumDict = SafeToDict(albums, a => a.Id, a => a.Title);

        var songMap = SafeToDict(songs, s => s.Id, s => s);

        var sorted = new List<Song>(entries.Count);
        var allArtistsDict5 = await GetAllArtistsForSongsAsync(songs.Select(s => s.Id));
        foreach (var entry in entries)
        {
            if (songMap.TryGetValue(entry.SongId, out var song))
            {
                song.Artist = artistDict.TryGetValue(song.ArtistId, out var an) ? an : "未知艺术家";
                song.Album = albumDict.TryGetValue(song.AlbumId, out var al) ? al : "未知专辑";
                song.AllArtists = allArtistsDict5.TryGetValue(song.Id, out var aa) ? aa : song.Artist;
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
        await EnsureMaintenanceCompletedAsync();
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
        await EnsureMaintenanceCompletedAsync();

        var allEntries = await _database.Table<PlaylistSong>()
            .Where(ps => ps.PlaylistId == playlistId)
            .ToListAsync();

        System.Diagnostics.Debug.WriteLine($"[DB] UpdatePlaylistOrderAsync: playlistId={playlistId}, orderedSongIds.Count={orderedSongIds.Count}, existingEntries.Count={allEntries.Count}");

        var entryDict = SafeToDict(allEntries, e => e.SongId, e => e);

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
        await EnsureMaintenanceCompletedAsync();
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

        // 填充多艺术家
        var allArtistsDict = await GetAllArtistsForSongsAsync(new[] { song.Id });
        song.AllArtists = allArtistsDict.TryGetValue(song.Id, out var aa) ? aa : song.Artist;

        return song;
    }

    // ═══════════ CachedSong CRUD ═══════════

    /// <summary>
    /// 保存或更新缓存歌曲信息
    /// </summary>
    /// <param name="cachedSong">缓存歌曲对象</param>
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

    /// <summary>获取缓存的网络歌曲</summary>
    /// <returns>去重后的网络歌曲列表（SMB 优先于 WebDAV）</returns>
    public async Task<List<Song>> GetCachedNetworkSongsAsync()
    {
        await EnsureMaintenanceCompletedAsync();
        var songs = await _database.Table<Song>()
            .Where(s => s.Source == SongSource.WebDAV || s.Source == SongSource.SMB)
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
            System.Diagnostics.Debug.WriteLine($"[CatClaw] InsertSong 失败: {song.Title} - {ex.Message}");
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

    /// <summary>SongArtist JOIN 查询的中间结果行</summary>
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

    private class SongArtistRow
    {
        public int SongId { get; set; }
        public string Name { get; set; } = "";
    }

    /// <summary>
    /// 迁移 Artists 表：添加 Gender/Birthday/Region/Description 列
    /// </summary>
    private async Task MigrateArtistsTableAsync()
    {
        try
        {
            var columns = await _database.QueryAsync<TableColumn>("PRAGMA table_info(Artists)");
            var columnNames = columns.Select(c => c.name).ToHashSet();

            if (!columnNames.Contains("Gender"))
                try { await _database.ExecuteAsync("ALTER TABLE Artists ADD COLUMN Gender TEXT"); } catch { }
            if (!columnNames.Contains("Birthday"))
                try { await _database.ExecuteAsync("ALTER TABLE Artists ADD COLUMN Birthday TEXT"); } catch { }
            if (!columnNames.Contains("Region"))
                try { await _database.ExecuteAsync("ALTER TABLE Artists ADD COLUMN Region TEXT"); } catch { }
            if (!columnNames.Contains("Description"))
                try { await _database.ExecuteAsync("ALTER TABLE Artists ADD COLUMN Description TEXT"); } catch { }
        }
        catch { }
    }

    /// <summary>
    /// 恢复 Artist 表数据：之前 [Table] 属性和 [PrimaryKey,AutoIncrement] 丢失导致：
    /// 1. ORM 创建了错误的 "Artist" 单数表（所有 Id=0）
    /// 2. "Artists" 复数表可能也有 Id=0 的脏数据
    /// 按 Name 合并数据，重建 Artists 表结构
    /// </summary>
    private async Task RecoverArtistsTableAsync()
    {
        try
        {
            // 检查 Artists 表是否有主键（没有说明表结构损坏需要重建）
            var artistsCols = await _database.QueryAsync<TableColumn>("PRAGMA table_info(Artists)");
            var hasPrimaryKey = artistsCols.Any(c => c.pk > 0);

            // 检查是否存在错误的 "Artist" 单数表
            var hasArtistTable = await TableExistsAsync("Artist");

            if (!hasPrimaryKey || hasArtistTable)
            {
                await RebuildArtistsTableAsync(hasArtistTable);
            }
        }
        catch { }
    }

    /// <summary>
    /// 重建 Artists 表：合并 Artist 和 Artists 表数据，按 Name 去重，重建正确表结构
    /// </summary>
    private async Task RebuildArtistsTableAsync(bool hasArtistTable)
    {
        // 读取两个表的所有数据（按 Name 去重合并）
        var mergedArtists = new Dictionary<string, ArtistRecoveryRow>();

        // 1. 从 Artists 表读取
        try
        {
            var artistsData = await _database.QueryAsync<ArtistRecoveryRow>(
                "SELECT Name, Cover, Gender, Birthday, Region, Description FROM Artists");
            foreach (var a in artistsData)
            {
                if (!string.IsNullOrEmpty(a.Name) && !mergedArtists.ContainsKey(a.Name))
                    mergedArtists[a.Name] = a;
            }
        }
        catch { }

        // 2. 从 Artist 表读取（补充 Artists 表没有的）
        if (hasArtistTable)
        {
            try
            {
                var artistData = await _database.QueryAsync<ArtistRecoveryRow>(
                    "SELECT Name, Cover, Gender, Birthday, Region, Description FROM Artist");
                foreach (var a in artistData)
                {
                    if (!string.IsNullOrEmpty(a.Name) && !mergedArtists.ContainsKey(a.Name))
                        mergedArtists[a.Name] = a;
                }
            }
            catch { }
        }

        // 3. 获取 Songs 表中引用的 ArtistId → 需要保留的映射
        // 先读取旧 Artists 表的 Id 映射
        var oldIdToName = new Dictionary<int, string>();
        try
        {
            var idNameRows = await _database.QueryAsync<IdNameRow>("SELECT Id, Name FROM Artists");
            foreach (var r in idNameRows)
                oldIdToName[r.Id] = r.Name;
        }
        catch { }

        // 4. 重建 Artists 表
        await _database.ExecuteAsync("DROP TABLE IF EXISTS Artists");
        if (hasArtistTable)
            await _database.ExecuteAsync("DROP TABLE IF EXISTS Artist");

        await _database.CreateTableAsync<Artist>();

        // 5. 插入合并后的数据，构建 Name → 新Id 映射
        var nameToNewId = new Dictionary<string, int>();
        foreach (var kvp in mergedArtists)
        {
            var a = kvp.Value;
            var artist = new Artist
            {
                Name = a.Name,
                Cover = a.Cover,
                Gender = a.Gender,
                Birthday = a.Birthday,
                Region = a.Region,
                Description = a.Description,
            };
            await _database.InsertAsync(artist);
            nameToNewId[a.Name] = artist.Id;
        }

        // 6. 更新 Songs 表的 ArtistId
        // 对于每个旧 ArtistId，找到对应的 Name，再找到新 Id
        var oldIdToNewId = new Dictionary<int, int>();
        foreach (var kvp in oldIdToName)
        {
            if (nameToNewId.TryGetValue(kvp.Value, out var newId))
                oldIdToNewId[kvp.Key] = newId;
        }

        // 批量更新 Songs.ArtistId
        foreach (var mapping in oldIdToNewId)
        {
            if (mapping.Key != mapping.Value)
            {
                try
                {
                    await _database.ExecuteAsync("UPDATE Songs SET ArtistId = ? WHERE ArtistId = ?",
                        mapping.Value, mapping.Key);
                }
                catch { }
            }
        }

        // 7. 修复 ArtistId=0 的歌曲：尝试从文件元数据重新关联
        await FixOrphanedArtistIdsAsync(nameToNewId, ExtractArtistNameCallback);
    }

    /// <summary>
    /// 修复 ArtistId=0 的歌曲：通过回调提取艺术家名称重新关联
    /// </summary>
    private async Task FixOrphanedArtistIdsAsync(Dictionary<string, int> nameToNewId, Func<string, string?>? extractArtistName = null)
    {
        try
        {
            // 获取所有 ArtistId=0 的歌曲
            var orphanSongs = await _database.Table<Song>().Where(s => s.ArtistId == 0).ToListAsync();
            if (orphanSongs.Count == 0) return;
            if (extractArtistName == null) return;

            // 尝试从文件元数据提取艺术家名称
            foreach (var song in orphanSongs)
            {
                if (string.IsNullOrEmpty(song.FilePath) || !System.IO.File.Exists(song.FilePath))
                    continue;

                try
                {
                    var artistName = extractArtistName(song.FilePath);

                    if (string.IsNullOrEmpty(artistName)) continue;

                    // 在 nameToNewId 中查找或创建
                    if (!nameToNewId.TryGetValue(artistName, out var artistId))
                    {
                        var newArtist = new Artist { Name = artistName };
                        await _database.InsertAsync(newArtist);
                        artistId = newArtist.Id;
                        nameToNewId[artistName] = artistId;
                    }

                    song.ArtistId = artistId;
                    await _database.UpdateAsync(song);
                }
                catch { }
            }
        }
        catch { }
    }

    /// <summary>
    /// 将现有歌曲的单 ArtistId 迁移到多对多 SongArtists 表。
    /// 对于 ArtistId > 0 且 SongArtists 中尚无记录的歌曲，创建一条 SongArtist 记录。
    /// </summary>
    private async Task MigrateToMultiArtistAsync()
    {
        try
        {
            // 检查是否已有 SongArtist 数据（避免重复迁移）
            var existingCount = await _database.Table<SongArtist>().CountAsync();
            if (existingCount > 0) return;

            // 查找所有有艺术家关联的歌曲
            var songs = await _database.Table<Song>().Where(s => s.ArtistId > 0).ToListAsync();
            if (songs.Count == 0) return;

            var entries = songs.Select(s => new SongArtist
            {
                SongId = s.Id,
                ArtistId = s.ArtistId
            }).ToList();

            await _database.InsertAllAsync(entries);
            System.Diagnostics.Debug.WriteLine($"[CatClaw] 多艺术家迁移完成，为 {entries.Count} 首歌曲创建了 SongArtist 关联");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[CatClaw] 多艺术家迁移失败: {ex.Message}");
        }
    }

    /// <summary>
    /// 将历史遗留的合并艺术家名（如 "国风堂/哦漏"）拆分为多个独立艺术家。
    /// 流程：
    /// 1. 查找名称含 " / " 的艺术家
    /// 2. 拆分名称 → 为每个子名称查找或创建独立艺术家
    /// 3. 更新 Song.ArtistId → 指向第一个子艺术家
    /// 4. 更新 SongArtists → 将合并艺术家 ID 替换为各子艺术家 ID
    /// 5. 删除旧的合并艺术家记录
    /// </summary>
    private async Task SplitCombinedArtistsAsync()
    {
        try
        {
            // 查找所有名称含 "/" 或 "／" 的艺术家（历史合并艺术家）
            var allArtists = await _database.Table<Artist>().ToListAsync();
            var combinedArtists = allArtists
                .Where(a => a.Name.Contains(" / ") || a.Name.Contains("/") || a.Name.Contains("／"))
                .ToList();

            if (combinedArtists.Count == 0) return;

            System.Diagnostics.Debug.WriteLine($"[CatClaw] 发现 {combinedArtists.Count} 个需要拆分的合并艺术家");

            await _database.RunInTransactionAsync(tran =>
            {
                foreach (var combined in combinedArtists)
                {
                    try
                    {
                        // 规范化：将全角斜杠替换为半角，便于统一拆分
                        var normalizedName = combined.Name.Replace('／', '/');
                        // 拆分名称：优先用 SplitArtistNames，如果没拆开则按 '/' 强拆
                        var names = CatClawMusic.Core.Services.MusicUtility.SplitArtistNames(normalizedName);
                        if (names.Count <= 1 && normalizedName.Contains('/'))
                        {
                            names = normalizedName.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                                .Where(p => !string.IsNullOrEmpty(p))
                                .ToList();
                        }
                        if (names.Count <= 1) continue;

                        // 为每个子名称查找或创建独立艺术家
                        var individualIds = new List<int>();
                        var allArtistsSnapshot = tran.Query<Artist>("SELECT * FROM Artists").ToList();
                        foreach (var name in names)
                        {
                            var existing = allArtistsSnapshot.FirstOrDefault(a =>
                                string.Equals(a.Name, name, StringComparison.OrdinalIgnoreCase) &&
                                a.Id != combined.Id);

                            if (existing != null)
                            {
                                individualIds.Add(existing.Id);
                            }
                            else
                            {
                                var newArtist = new Artist { Name = name };
                                tran.Insert(newArtist);
                                allArtistsSnapshot.Add(newArtist);
                                individualIds.Add(newArtist.Id);
                            }
                        }

                        // 更新 Song.ArtistId → 指向第一个子艺术家
                        if (individualIds.Count > 0)
                        {
                            tran.Execute("UPDATE Songs SET ArtistId = ? WHERE ArtistId = ?", individualIds[0], combined.Id);
                        }

                        // 更新 SongArtists → 将合并艺术家 ID 替换为子艺术家 ID
                        var songArtistRows = tran.Query<SongArtist>(
                            "SELECT * FROM SongArtists WHERE ArtistId = ?", combined.Id);
                        foreach (var sa in songArtistRows)
                        {
                            tran.Execute("DELETE FROM SongArtists WHERE Id = ?", sa.Id);
                            foreach (var id in individualIds)
                            {
                                try
                                {
                                    tran.Insert(new SongArtist { SongId = sa.SongId, ArtistId = id });
                                }
                                catch { }
                            }
                        }

                        // 删除旧的合并艺术家
                        tran.Execute("DELETE FROM Artists WHERE Id = ?", combined.Id);

                        System.Diagnostics.Debug.WriteLine(
                            $"[CatClaw] 拆分艺术家 \"{combined.Name}\" → [{string.Join(", ", names)}]");
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine(
                            $"[CatClaw] 拆分艺术家 \"{combined.Name}\" 失败: {ex.Message}");
                    }
                }
            });

            // 清理可能产生的孤立 SongArtists 记录
            await CleanupOrphanedPlayHistoryAndFavoritesAsync();

            System.Diagnostics.Debug.WriteLine("[CatClaw] 合并艺术家拆分迁移完成");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[CatClaw] 合并艺术家拆分迁移失败: {ex.Message}");
        }
    }

    /// <summary>用于恢复时读取艺术家行的辅助类</summary>
    private class ArtistRecoveryRow
    {
        public string Name { get; set; } = "";
        public string? Cover { get; set; }
        public string? Gender { get; set; }
        public string? Birthday { get; set; }
        public string? Region { get; set; }
        public string? Description { get; set; }
    }

    /// <summary>用于读取 Id-Name 映射的辅助类</summary>
    private class IdNameRow
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
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
    /// 迁移 PlayHistory 表：添加自增主键 Id 列
    /// </summary>
    private async Task MigratePlayHistoryTableAsync()
    {
        try
        {
            var exists = await TableExistsAsync("PlayHistory");
            if (!exists)
            {
                // PlayHistory 不存在但 PlayHistory_old 可能残留 → 恢复
                var oldExists = await TableExistsAsync("PlayHistory_old");
                if (oldExists)
                {
                    await _database.ExecuteAsync("ALTER TABLE PlayHistory_old RENAME TO PlayHistory");
                }
                return;
            }

            var columns = await _database.QueryAsync<TableColumn>("PRAGMA table_info(PlayHistory)");
            if (columns.Any(c => c.pk > 0))
            {
                // 表结构已正确，清理残留旧表
                try { await _database.ExecuteAsync("DROP TABLE IF EXISTS PlayHistory_old"); } catch { }
                return;
            }

            await _database.ExecuteAsync("ALTER TABLE PlayHistory RENAME TO PlayHistory_old");
            await _database.CreateTableAsync<PlayHistory>();
            await _database.ExecuteAsync(
                "INSERT INTO PlayHistory(SongId, PlayedAt, PlayCount) " +
                "SELECT SongId, PlayedAt, PlayCount FROM PlayHistory_old");
            await _database.ExecuteAsync("DROP TABLE PlayHistory_old");
        }
        catch
        {
            // 迁移失败时尝试恢复旧表数据，而非直接丢弃
            try
            {
                var oldExists = await TableExistsAsync("PlayHistory_old");
                var newExists = await TableExistsAsync("PlayHistory");

                if (oldExists && !newExists)
                {
                    await _database.ExecuteAsync("ALTER TABLE PlayHistory_old RENAME TO PlayHistory");
                }
                else if (oldExists && newExists)
                {
                    try
                    {
                        await _database.ExecuteAsync(
                            "INSERT OR IGNORE INTO PlayHistory(SongId, PlayedAt, PlayCount) " +
                            "SELECT SongId, PlayedAt, PlayCount FROM PlayHistory_old");
                    }
                    catch { }
                    try { await _database.ExecuteAsync("DROP TABLE PlayHistory_old"); } catch { }
                }
            }
            catch { }
        }
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
