using CatClawMusic.Core.Models;
using SQLite;

namespace CatClawMusic.Data;

public class MusicDatabase
{
    private readonly SQLiteAsyncConnection _database;
    private bool _isInitialized;
    private readonly SemaphoreSlim _initSemaphore = new(1, 1);

    public MusicDatabase(string dbPath)
    {
        _database = new SQLiteAsyncConnection(dbPath);
    }

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

            _isInitialized = true;
        }
        finally
        {
            _initSemaphore.Release();
        }
    }

    private async Task CreateIndexesAsync()
    {
        try { await _database.ExecuteAsync("CREATE INDEX IF NOT EXISTS idx_songs_artist ON Songs(ArtistId)"); } catch { }
        try { await _database.ExecuteAsync("CREATE INDEX IF NOT EXISTS idx_songs_album ON Songs(AlbumId)"); } catch { }
        try { await _database.ExecuteAsync("CREATE INDEX IF NOT EXISTS idx_songs_title ON Songs(Title)"); } catch { }
        try { await _database.ExecuteAsync("CREATE INDEX IF NOT EXISTS idx_albums_artist ON Albums(ArtistId)"); } catch { }
        try { await _database.ExecuteAsync("CREATE INDEX IF NOT EXISTS idx_play_history_time ON PlayHistory(PlayedAt DESC)"); } catch { }
    }

    // ═══════════ Song CRUD ═══════════

    public Task<List<Song>> GetSongsAsync() => GetSongsWithDetailsAsync();

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

    public Task<Song?> GetSongByIdAsync(int id) =>
        _database.Table<Song>().Where(s => s.Id == id).FirstOrDefaultAsync();

    /// <summary>数据库层面搜索歌曲（JOIN Artist/Album 表，避免全部加载到内存）</summary>
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

    // ═══════════ Artist / Album ═══════════

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

    public Task<List<Artist>> GetAllArtistsAsync() => _database.Table<Artist>().ToListAsync();
    public Task<List<Album>> GetAllAlbumsAsync() => _database.Table<Album>().ToListAsync();

    // ═══════════ Play History ═══════════

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
        // 只保留最近 20 条历史
        await TrimHistoryAsync(20);
    }

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

    public Task<List<PlayHistory>> GetRecentPlaysAsync(int limit = 20) =>
        _database.Table<PlayHistory>().OrderByDescending(h => h.PlayedAt).Take(limit).ToListAsync();

    /// <summary>获取最近播放的歌曲（含艺术家/专辑名）</summary>
    public async Task<List<Song>> GetRecentSongsAsync()
    {
        await EnsureInitializedAsync();
        var history = await _database.Table<PlayHistory>().OrderByDescending(h => h.PlayedAt).Take(20).ToListAsync();
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
        }

        var playTimeDict = history.ToDictionary(h => h.SongId, h => h.PlayedAt);
        return songs.OrderByDescending(s => playTimeDict.TryGetValue(s.Id, out var t) ? t : 0).ToList();
    }

    // ═══════════ Favorites ═══════════

    public async Task SetFavoriteAsync(int songId, bool isFav)
    {
        await EnsureInitializedAsync();
        var fav = await _database.Table<Favorite>().Where(f => f.SongId == songId).FirstOrDefaultAsync();
        if (isFav && fav == null)
            await _database.InsertAsync(new Favorite { SongId = songId, AddedAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds() });
        else if (!isFav && fav != null)
            await _database.DeleteAsync(fav);
    }

    public async Task<bool> IsFavoriteAsync(int songId)
    {
        await EnsureInitializedAsync();
        return await _database.Table<Favorite>().Where(f => f.SongId == songId).CountAsync() > 0;
    }

    public Task<List<Favorite>> GetFavoritesAsync()
        => _database.Table<Favorite>().ToListAsync();

    /// <summary>获取收藏歌曲完整信息（含艺术家/专辑名）</summary>
    public async Task<List<Song>> GetFavoriteSongsAsync()
    {
        await EnsureInitializedAsync();
        var favs = await _database.Table<Favorite>().ToListAsync();
        if (favs.Count == 0) return new List<Song>();

        // 批量加载所有歌曲、艺术家、专辑
        var allFavIds = favs.Select(f => f.SongId).ToHashSet();
        var songs = await _database.Table<Song>().ToListAsync();
        var favSongs = songs.Where(s => allFavIds.Contains(s.Id)).ToList();
        if (favSongs.Count == 0) return new List<Song>();

        var artists = await _database.Table<Artist>().ToListAsync();
        var albums = await _database.Table<Album>().ToListAsync();
        var artistDict = artists.ToDictionary(a => a.Id, a => a.Name);
        var albumDict = albums.ToDictionary(a => a.Id, a => a.Title);

        foreach (var s in favSongs)
        {
            s.Artist = artistDict.TryGetValue(s.ArtistId, out var an) ? an : "未知艺术家";
            s.Album = albumDict.TryGetValue(s.AlbumId, out var al) ? al : "未知专辑";
        }

        // 用 Dictionary 替代 O(n) 的 First() 查找，降为 O(1)
        var favDict = favs.ToDictionary(f => f.SongId, f => f.AddedAt);
        return favSongs.OrderByDescending(s => favDict.TryGetValue(s.Id, out var t) ? t : 0).ToList();
    }

    // ═══════════ Lyric ═══════════

    public async Task SaveLyricAsync(int songId, string? lrcPath, string? content)
    {
        await EnsureInitializedAsync();
        var l = await _database.Table<Lyric>().Where(x => x.SongId == songId).FirstOrDefaultAsync();
        if (l != null) { l.LrcPath = lrcPath; l.Content = content; await _database.UpdateAsync(l); }
        else await _database.InsertAsync(new Lyric { SongId = songId, LrcPath = lrcPath, Content = content });
    }

    public Task<Lyric?> GetLyricAsync(int songId) =>
        _database.Table<Lyric>().Where(x => x.SongId == songId).FirstOrDefaultAsync();

    // ═══════════ Connection Profile ═══════════

    public async Task<int> SaveConnectionProfileAsync(ConnectionProfile profile)
    {
        await EnsureInitializedAsync();
        if (profile.Id != 0) return await _database.UpdateAsync(profile);
        return await _database.InsertAsync(profile);
    }

    public Task<List<ConnectionProfile>> GetConnectionProfilesAsync()
        => _database.Table<ConnectionProfile>().ToListAsync();

    // ═══════════ Network Song Cache ═══════════

    /// <summary>替换所有网络缓存歌曲（先清除旧的，再批量写入新的）</summary>
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
    public async Task<List<Song>> GetCachedNetworkSongsAsync()
    {
        await EnsureInitializedAsync();
        var songs = await _database.Table<Song>()
            .Where(s => s.Source == SongSource.WebDAV)
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
        return songs;
    }

    /// <summary>缓存网络歌曲数量</summary>
    public async Task<int> GetCachedNetworkSongCountAsync()
        => await _database.Table<Song>().Where(s => s.Source == SongSource.WebDAV).CountAsync();

    /// <summary>本地歌曲数量</summary>
    public async Task<int> GetLocalSongCountAsync()
        => await _database.Table<Song>().Where(s => s.Source == SongSource.Local).CountAsync();

    /// <summary>开始替换网络歌曲（先清除旧的），配合 InsertSongAsync 逐首插入</summary>
    public async Task ReplaceNetworkSongsBeginAsync()
    {
        await EnsureInitializedAsync();
        try { await SaveNetworkFavoriteRefsAsync(); }
        catch { }
        try { await _database.ExecuteAsync("DELETE FROM Songs WHERE Source = ?", (int)SongSource.WebDAV); }
        catch { /* 表可能为空 */ }
    }

    private readonly Dictionary<string, long> _pendingNetworkFavs = new();

    private async Task SaveNetworkFavoriteRefsAsync()
    {
        _pendingNetworkFavs.Clear();
        var favs = await _database.Table<Favorite>().ToListAsync();
        if (favs.Count == 0) return;
        var favSongIds = favs.Select(f => f.SongId).ToHashSet();
        var networkSongs = await _database.Table<Song>()
            .Where(s => s.Source == SongSource.WebDAV && favSongIds.Contains(s.Id))
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

    public async Task RestoreNetworkFavoritesAsync()
    {
        if (_pendingNetworkFavs.Count == 0) return;
        await EnsureInitializedAsync();

        var newNetworkSongs = await _database.Table<Song>()
            .Where(s => s.Source == SongSource.WebDAV)
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
    public async Task InsertSongAsync(Song song)
    {
        await EnsureInitializedAsync();
        try
        {
            // 网络歌曲基于 RemoteId 去重，本地歌曲基于 FilePath 去重
            Song? existing = null;
            if (song.Source == SongSource.WebDAV && !string.IsNullOrEmpty(song.RemoteId))
            {
                existing = await _database.Table<Song>()
                    .Where(s => s.Source == SongSource.WebDAV && s.RemoteId == song.RemoteId)
                    .FirstOrDefaultAsync();
            }
            else if (!string.IsNullOrEmpty(song.FilePath))
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
                await _database.InsertAsync(song);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[CatClaw] InsertSong 失败: {song.Title} - {ex.Message}");
        }
    }
}
