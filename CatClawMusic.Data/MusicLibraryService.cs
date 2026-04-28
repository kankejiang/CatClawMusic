using CatClawMusic.Core.Interfaces;
using CatClawMusic.Core.Models;
using CatClawMusic.Core.Services;

namespace CatClawMusic.Data;

/// <summary>
/// 音乐库管理服务实现（使用 TagReader + SQLite）
/// </summary>
public class MusicLibraryService : IMusicLibraryService
{
    private readonly MusicDatabase _db;

    public MusicLibraryService(MusicDatabase db)
    {
        _db = db;
    }

    public async Task<List<Song>> ScanLocalAsync()
    {
        // 扫描 Android 常用音乐目录
        var musicDirs = new[]
        {
            "/storage/emulated/0/Music",
            "/storage/emulated/0/Download",
            "/storage/emulated/0/Android/media",
            "/storage/emulated/0/方糖音乐"
        };

        var allSongs = new List<Song>();
        foreach (var dir in musicDirs)
        {
            if (Directory.Exists(dir))
            {
                var songs = TagReader.ScanDirectory(dir, recursive: true);
                allSongs.AddRange(songs);
            }
        }

        return allSongs;
    }

    public async Task<List<Song>> ScanNetworkAsync(ConnectionProfile profile)
    {
        // TODO: 实现 WebDAV 网络扫描
        return new List<Song>();
    }

    public async Task<List<Song>> SearchAsync(string keyword)
    {
        var allSongs = await GetAllSongsAsync();
        if (string.IsNullOrWhiteSpace(keyword)) return allSongs;

        var kw = keyword.ToLowerInvariant();
        return allSongs
            .Where(s =>
                s.Title.ToLowerInvariant().Contains(kw) ||
                s.Artist.ToLowerInvariant().Contains(kw) ||
                s.Album.ToLowerInvariant().Contains(kw))
            .ToList();
    }

    public async Task<Stream?> GetAlbumCoverAsync(Song song)
    {
        // 优先从音频文件提取嵌入封面
        var coverBytes = TagReader.ExtractCoverArt(song.FilePath);
        if (coverBytes != null)
            return new MemoryStream(coverBytes);

        // 尝试已缓存的封面文件
        if (!string.IsNullOrEmpty(song.CoverArtPath) && File.Exists(song.CoverArtPath))
        {
            return File.OpenRead(song.CoverArtPath);
        }

        return null;
    }

    public async Task<List<Song>> GetAllSongsAsync()
    {
        await _db.EnsureInitializedAsync();
        return await _db.GetSongsAsync();
    }

    public async Task<List<Song>> GetSongsByArtistAsync(string artist)
    {
        await _db.EnsureInitializedAsync();
        var allSongs = await _db.GetSongsAsync();
        return allSongs.Where(s => s.Artist == artist).ToList();
    }

    public async Task<List<Song>> GetSongsByAlbumAsync(string album)
    {
        await _db.EnsureInitializedAsync();
        var allSongs = await _db.GetSongsAsync();
        return allSongs.Where(s => s.Album == album).ToList();
    }

    public async Task<List<Album>> GetAllAlbumsAsync()
    {
        await _db.EnsureInitializedAsync();
        var allSongs = await _db.GetSongsAsync();

        return allSongs
            .GroupBy(s => s.Album)
            .Select(g => new Album
            {
                Name = g.Key,
                Artist = g.First().Artist,
                SongCount = g.Count()
            })
            .ToList();
    }
}
