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
        foreach (var s in songs)
        {
            var artist = await _database.Table<Artist>().Where(a => a.Id == s.ArtistId).FirstOrDefaultAsync();
            var album = await _database.Table<Album>().Where(a => a.Id == s.AlbumId).FirstOrDefaultAsync();
            s.Artist = artist?.Name ?? "未知艺术家";
            s.Album = album?.Title ?? "未知专辑";
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
            .Where(h => h.SongId == songId && h.PlayedAt > now - 3600)
            .FirstOrDefaultAsync();
        if (existing != null)
        {
            existing.PlayCount++;
            await _database.UpdateAsync(existing);
        }
        else
        {
            await _database.InsertAsync(new PlayHistory { SongId = songId, PlayedAt = now });
        }
    }

    public Task<List<PlayHistory>> GetRecentPlaysAsync(int limit = 50) =>
        _database.Table<PlayHistory>().OrderByDescending(h => h.PlayedAt).Take(limit).ToListAsync();

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
