using CatClawMusic.Core.Interfaces;
using CatClawMusic.Core.Models;
using CatClawMusic.Core.Services;

namespace CatClawMusic.Data;

public class MusicLibraryService : IMusicLibraryService
{
    private readonly MusicDatabase _db;
    private readonly INetworkMusicService? _networkMusic;

    public MusicLibraryService(MusicDatabase db, INetworkMusicService? networkMusic = null)
    {
        _db = db;
        _networkMusic = networkMusic;
    }

    public async Task<List<Song>> ScanLocalAsync(List<string>? customFolders = null)
    {
        await _db.EnsureInitializedAsync();

        var scanDirs = new List<string> { "/storage/emulated/0/Music", "/storage/emulated/0/Download" };
        if (customFolders != null)
        {
            foreach (var f in customFolders)
                if (!string.IsNullOrWhiteSpace(f) && Directory.Exists(f) && !scanDirs.Contains(f))
                    scanDirs.Add(f);
        }

        var result = await Task.Run(() =>
        {
            var allSongs = new List<Song>();
            var seenPaths = new HashSet<string>();

            foreach (var dir in scanDirs)
            {
                if (Directory.Exists(dir))
                {
                    try
                    {
                        var scanPaths = MusicUtility.ScanFolderRecursive(dir);
                        foreach (var path in scanPaths)
                        {
                            if (seenPaths.Add(path))
                            {
                                var song = TagReader.ReadSongInfo(path);
                                if (song != null) allSongs.Add(song);
                            }
                        }
                    }
                    catch { }
                }
            }
            return allSongs;
        });

        return await SaveAndDeduplicateAsync(result);
    }

    /// <summary>接受预扫描的歌曲列表，去重后入库</summary>
    public async Task<List<Song>> ImportSongsAsync(List<Song> songs)
    {
        await _db.EnsureInitializedAsync();
        return await SaveAndDeduplicateAsync(songs);
    }

    private async Task<List<Song>> SaveAndDeduplicateAsync(List<Song> allSongs)
    {
        var distinct = allSongs
            .GroupBy(s => s.FilePath)
            .Select(g => g.First())
            .ToList();

        foreach (var song in distinct)
        {
            try
            {
                // 填充 artist_id / album_id
                song.ArtistId = await _db.EnsureArtistAsync(song.Artist);
                song.AlbumId = await _db.EnsureAlbumAsync(song.Album, song.ArtistId);
                song.DateAdded = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                await _db.SaveSongAsync(song);
            }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[CatClaw] 保存歌曲失败: {song.FilePath}, {ex.Message}"); }
        }
        System.Diagnostics.Debug.WriteLine($"[CatClaw] 扫描完成: {distinct.Count} 首入库");
        return distinct;
    }

    public async Task<List<Song>> ScanNetworkAsync(ConnectionProfile profile)
    {
        return new List<Song>();
    }

    public async Task<List<Song>> SearchAsync(string keyword)
    {
        await _db.EnsureInitializedAsync();
        if (string.IsNullOrWhiteSpace(keyword))
            return await GetAllSongsAsync();

        // 本地数据库搜索
        var localResults = await _db.SearchSongsAsync(keyword);

        // 网络搜索（Navidrome/Subsonic）
        var networkResults = new List<Song>();
        if (_networkMusic != null)
        {
            try
            {
                var profiles = await _networkMusic.GetProfilesAsync();
                var enabledProfiles = profiles.Where(p => p.IsEnabled).ToList();
                foreach (var profile in enabledProfiles)
                {
                    try
                    {
                        var results = await _networkMusic.SearchAsync(keyword, profile);
                        networkResults.AddRange(results);
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"[CatClaw] 网络({profile.Name})搜索失败: {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[CatClaw] 获取网络配置失败: {ex.Message}");
            }
        }

        // 合并结果：本地 + 网络，按标题+艺术家去重（本地优先）
        if (networkResults.Count == 0) return localResults;

        var allResults = new List<Song>(localResults);
        var localKeys = new HashSet<string>(
            localResults.Select(s => ((s.Title ?? "").Trim() + "|" + (s.Artist ?? "").Trim()).ToLowerInvariant()),
            StringComparer.OrdinalIgnoreCase);

        foreach (var ns in networkResults)
        {
            var key = ((ns.Title ?? "").Trim() + "|" + (ns.Artist ?? "").Trim()).ToLowerInvariant();
            if (localKeys.Add(key))
                allResults.Add(ns);
        }

        return allResults;
    }

    public async Task<Stream?> GetAlbumCoverAsync(Song song)
    {
        if (song.FilePath.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
            song.FilePath.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            return null;
        var coverBytes = TagReader.ExtractCoverArt(song.FilePath);
        if (coverBytes != null) return new MemoryStream(coverBytes);
        if (!string.IsNullOrEmpty(song.CoverArtPath) && File.Exists(song.CoverArtPath))
            return File.OpenRead(song.CoverArtPath);
        return null;
    }

    public async Task<List<Song>> GetAllSongsAsync()
    {
        await _db.EnsureInitializedAsync();
        return await _db.GetSongsAsync();
    }

    /// <summary>
    /// 获取去重合并的全部歌曲：本地 + 网络歌曲按标题+艺术家去重，本地优先
    /// </summary>
    public async Task<List<Song>> GetMergedSongsAsync()
    {
        var allSongs = await GetAllSongsAsync();
        var networkSongs = await _db.GetCachedNetworkSongsAsync();
        allSongs.AddRange(networkSongs);

        var deduped = allSongs
            .GroupBy(s => (s.Title?.Trim() ?? "").ToLowerInvariant() + "|" + (s.Artist?.Trim() ?? "").ToLowerInvariant())
            .Select(g =>
            {
                var local = g.FirstOrDefault(s => s.Source == SongSource.Local);
                if (local != null) return local;
                var webdav = g.FirstOrDefault(s => s.Source == SongSource.WebDAV);
                if (webdav != null) return webdav;
                return g.First();
            })
            .OrderBy(s => s.Title)
            .ToList();
        return deduped;
    }

    public async Task<List<Song>> GetSongsByArtistAsync(string artist)
    {
        await _db.EnsureInitializedAsync();
        // 使用 SQL JOIN 在数据库层面过滤
        return await _db.GetSongsByArtistAsync(artist);
    }

    public async Task<List<Song>> GetSongsByAlbumAsync(string album)
    {
        await _db.EnsureInitializedAsync();
        // 使用 SQL JOIN 在数据库层面过滤
        return await _db.GetSongsByAlbumAsync(album);
    }

    public async Task<List<Album>> GetAllAlbumsAsync()
    {
        await _db.EnsureInitializedAsync();
        var all = await _db.GetSongsAsync();
        return all.GroupBy(s => s.Album)
            .Select(g => new Album { Name = g.Key, Artist = g.First().Artist, SongCount = g.Count() })
            .ToList();
    }

    public Task<int> EnsureArtistAsync(string name) => _db.EnsureArtistAsync(name);
    public Task<int> EnsureAlbumAsync(string title, int artistId) => _db.EnsureAlbumAsync(title, artistId);
    public async Task<int> SaveSongAsync(Song song) { await _db.EnsureInitializedAsync(); return await _db.SaveSongAsync(song); }
    public async Task<List<Song>> GetFavoriteSongsAsync() { await _db.EnsureInitializedAsync(); return await _db.GetFavoriteSongsAsync(); }
    public async Task<List<Song>> GetRecentSongsAsync() { await _db.EnsureInitializedAsync(); return await _db.GetRecentSongsAsync(); }

    // ── Playlist CRUD ──

    public async Task<List<Playlist>> GetAllPlaylistsAsync() { await _db.EnsureInitializedAsync(); return await _db.GetAllPlaylistsAsync(); }
    public async Task<Playlist?> GetPlaylistByIdAsync(int id) { await _db.EnsureInitializedAsync(); return await _db.GetPlaylistByIdAsync(id); }
    public async Task<int> CreatePlaylistAsync(string name) => await _db.CreatePlaylistAsync(name);
    public async Task UpdatePlaylistAsync(Playlist playlist) => await _db.UpdatePlaylistAsync(playlist);
    public async Task DeletePlaylistAsync(int playlistId) => await _db.DeletePlaylistAsync(playlistId);
    public async Task AddSongToPlaylistAsync(int playlistId, int songId) => await _db.AddSongToPlaylistAsync(playlistId, songId);
    public async Task RemoveSongFromPlaylistAsync(int playlistId, int songId) => await _db.RemoveSongFromPlaylistAsync(playlistId, songId);
    public async Task<List<Song>> GetPlaylistSongsAsync(int playlistId) => await _db.GetPlaylistSongsAsync(playlistId);
    public async Task UpdateSongPositionAsync(int playlistId, int songId, int newPosition) => await _db.UpdateSongPositionAsync(playlistId, songId, newPosition);
    public async Task<int> GetPlaylistSongCountAsync(int playlistId) => await _db.GetPlaylistSongCountAsync(playlistId);

    // ── CachedSong CRUD ──

    public async Task SaveCachedSongAsync(CachedSong cachedSong) => await _db.SaveCachedSongAsync(cachedSong);
    public async Task<List<CachedSong>> GetCachedSongsAsync() { await _db.EnsureInitializedAsync(); return await _db.GetCachedSongsAsync(); }
    public async Task<CachedSong?> GetCachedSongAsync(int songId) { await _db.EnsureInitializedAsync(); return await _db.GetCachedSongAsync(songId); }
    public async Task DeleteCachedSongAsync(int songId) => await _db.DeleteCachedSongAsync(songId);
}
