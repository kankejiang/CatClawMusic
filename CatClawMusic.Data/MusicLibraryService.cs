using CatClawMusic.Core.Interfaces;
using CatClawMusic.Core.Models;
using CatClawMusic.Core.Services;

namespace CatClawMusic.Data;

/// <summary>
/// 音乐库管理服务实现（TagReader 扫描 + SQLite 存储）
/// </summary>
public class MusicLibraryService : IMusicLibraryService
{
    private readonly MusicDatabase _db;

    public MusicLibraryService(MusicDatabase db)
    {
        _db = db;
    }

    /// <summary>
    /// 扫描本地音乐文件（文件系统遍历 + TagLib 标签读取）
    /// </summary>
    public async Task<List<Song>> ScanLocalAsync()
    {
        await _db.EnsureInitializedAsync();

        var allSongs = new List<Song>();

        // 常见音乐目录
        var musicDirs = new[]
        {
            "/storage/emulated/0/Music",
            "/storage/emulated/0/Download",
            "/storage/emulated/0/方糖音乐",
            "/storage/emulated/0/猫爪音乐"
        };

        foreach (var dir in musicDirs)
        {
            if (Directory.Exists(dir))
            {
                var songs = TagReader.ScanDirectory(dir, recursive: false);
                allSongs.AddRange(songs);
            }
        }

        // 也尝试扫描 SD 卡（如果有）
        var sdCards = GetExternalStoragePaths();
        foreach (var sd in sdCards)
        {
            var musicDir = Path.Combine(sd, "Music");
            if (Directory.Exists(musicDir))
            {
                var songs = TagReader.ScanDirectory(musicDir, recursive: false);
                allSongs.AddRange(songs);
            }
        }

        // 去重（按文件路径）
        var distinct = allSongs
            .GroupBy(s => s.FilePath)
            .Select(g => g.First())
            .ToList();

        // 保存到数据库
        foreach (var song in distinct)
        {
            try { await _db.SaveSongAsync(song); } catch { }
        }

        return distinct;
    }

    /// <summary>
    /// 获取外部存储路径（SD卡等）
    /// </summary>
    private static List<string> GetExternalStoragePaths()
    {
        var paths = new List<string>();
        try
        {
#if ANDROID
            var context = global::Android.App.Application.Context;
            var dirs = context.GetExternalFilesDirs(null);
            foreach (var dir in dirs)
            {
                if (dir != null)
                {
                    var path = dir.AbsolutePath;
                    // 从 /Android/data/... 回退到存储根目录
                    var idx = path.IndexOf("/Android/", StringComparison.Ordinal);
                    if (idx > 0)
                        paths.Add(path.Substring(0, idx));
                }
            }
#endif
        }
        catch { }
        return paths;
    }

    public async Task<List<Song>> ScanNetworkAsync(ConnectionProfile profile)
    {
        // TODO: 网络扫描（WebDAV / Navidrome）
        return new List<Song>();
    }

    public async Task<List<Song>> SearchAsync(string keyword)
    {
        var allSongs = await GetAllSongsAsync();
        if (string.IsNullOrWhiteSpace(keyword)) return allSongs;

        var kw = keyword.ToLowerInvariant();
        return allSongs
            .Where(s =>
                (s.Title?.ToLowerInvariant().Contains(kw) ?? false) ||
                (s.Artist?.ToLowerInvariant().Contains(kw) ?? false) ||
                (s.Album?.ToLowerInvariant().Contains(kw) ?? false))
            .ToList();
    }

    public async Task<Stream?> GetAlbumCoverAsync(Song song)
    {
        var coverBytes = TagReader.ExtractCoverArt(song.FilePath);
        if (coverBytes != null)
            return new MemoryStream(coverBytes);

        if (!string.IsNullOrEmpty(song.CoverArtPath) && File.Exists(song.CoverArtPath))
            return File.OpenRead(song.CoverArtPath);

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
        var all = await _db.GetSongsAsync();
        return all.Where(s => s.Artist == artist).ToList();
    }

    public async Task<List<Song>> GetSongsByAlbumAsync(string album)
    {
        await _db.EnsureInitializedAsync();
        var all = await _db.GetSongsAsync();
        return all.Where(s => s.Album == album).ToList();
    }

    public async Task<List<Album>> GetAllAlbumsAsync()
    {
        await _db.EnsureInitializedAsync();
        var all = await _db.GetSongsAsync();
        return all
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
