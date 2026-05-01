using CatClawMusic.Core.Models;
using SQLite;

namespace CatClawMusic.Data;

public class MusicDatabase
{
    private readonly SQLiteAsyncConnection _database;
    private bool _isInitialized;
    private readonly object _initLock = new();

    public MusicDatabase(string dbPath)
    {
        _database = new SQLiteAsyncConnection(dbPath);
    }

    public async Task EnsureInitializedAsync()
    {
        if (_isInitialized) return;
        lock (_initLock)
        {
            if (_isInitialized) return;
        }

        // WAL 模式提升并发性能
        await _database.EnableWriteAheadLoggingAsync();

        // 核心表
        await _database.CreateTableAsync<Artist>();
        await _database.CreateTableAsync<Album>();
        await _database.CreateTableAsync<Song>();
        await _database.CreateTableAsync<Playlist>();
        await _database.CreateTableAsync<PlaylistSong>();

        // 扩展表
        await _database.CreateTableAsync<PlayHistory>();
        await _database.CreateTableAsync<Favorite>();
        await _database.CreateTableAsync<Lyric>();
        await _database.CreateTableAsync<ConnectionProfile>();
        await _database.CreateTableAsync<CachedSong>();

        // 索引
        await CreateIndexesAsync();

        _isInitialized = true;
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
        var songs = await _database.Table<Song>().ToListAsync();
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

    // ═══════════ Artist / Album ═══════════

    public async Task<int> EnsureArtistAsync(string name)
    {
        await EnsureInitializedAsync();
        if (string.IsNullOrEmpty(name)) return 0;
        var a = await _database.Table<Artist>().Where(x => x.Name == name).FirstOrDefaultAsync();
        if (a != null) return a.Id;
        await _database.InsertAsync(new Artist { Name = name });
        a = await _database.Table<Artist>().Where(x => x.Name == name).FirstOrDefaultAsync();
        return a?.Id ?? 0;
    }

    public async Task<int> EnsureAlbumAsync(string title, int artistId)
    {
        await EnsureInitializedAsync();
        if (string.IsNullOrEmpty(title)) return 0;
        var a = await _database.Table<Album>().Where(x => x.Title == title && x.ArtistId == artistId).FirstOrDefaultAsync();
        if (a != null) return a.Id;
        await _database.InsertAsync(new Album { Title = title, ArtistId = artistId });
        a = await _database.Table<Album>().Where(x => x.Title == title && x.ArtistId == artistId).FirstOrDefaultAsync();
        return a?.Id ?? 0;
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
            var all = await _database.Table<PlayHistory>().OrderByDescending(h => h.PlayedAt).ToListAsync();
            if (all.Count <= keepCount) return;
            var toDelete = all.Skip(keepCount).ToList();
            foreach (var h in toDelete)
                await _database.DeleteAsync(h);
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

        var songIds = history.Select(h => h.SongId).ToHashSet();
        var songs = await _database.Table<Song>().ToListAsync();
        var recentSongs = songs.Where(s => songIds.Contains(s.Id)).ToList();
        if (recentSongs.Count == 0) return new List<Song>();

        var artists = await _database.Table<Artist>().ToListAsync();
        var albums = await _database.Table<Album>().ToListAsync();
        var artistDict = artists.ToDictionary(a => a.Id, a => a.Name);
        var albumDict = albums.ToDictionary(a => a.Id, a => a.Title);

        foreach (var s in recentSongs)
        {
            s.Artist = artistDict.TryGetValue(s.ArtistId, out var an) ? an : "未知艺术家";
            s.Album = albumDict.TryGetValue(s.AlbumId, out var al) ? al : "未知专辑";
        }

        // 按播放时间倒序排列
        var playTimeDict = history.ToDictionary(h => h.SongId, h => h.PlayedAt);
        return recentSongs.OrderByDescending(s => playTimeDict.TryGetValue(s.Id, out var t) ? t : 0).ToList();
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

        return favSongs.OrderByDescending(s => favs.First(f => f.SongId == s.Id).AddedAt).ToList();
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
}
