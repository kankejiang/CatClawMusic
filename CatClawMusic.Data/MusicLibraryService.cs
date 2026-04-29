using CatClawMusic.Core.Interfaces;
using CatClawMusic.Core.Models;
using CatClawMusic.Core.Services;

namespace CatClawMusic.Data;

public class MusicLibraryService : IMusicLibraryService
{
    private readonly MusicDatabase _db;

    public MusicLibraryService(MusicDatabase db)
    {
        _db = db;
    }

    /// <summary>
    /// 扫描本地音乐：调用方负责扫描逻辑，本方法负责去重+入库
    /// </summary>
    public async Task<List<Song>> ScanLocalAsync(List<string>? customFolders = null)
    {
        await _db.EnsureInitializedAsync();
        var allSongs = new List<Song>();

        // 文件系统路径扫描（传统方式，MANAGE_EXTERNAL_STORAGE 已启用时可用）
        var scanDirs = new List<string> { "/storage/emulated/0/Music", "/storage/emulated/0/Download" };
        if (customFolders != null)
        {
            foreach (var f in customFolders)
                if (!string.IsNullOrWhiteSpace(f) && Directory.Exists(f) && !scanDirs.Contains(f))
                    scanDirs.Add(f);
        }
        foreach (var dir in scanDirs)
        {
            if (Directory.Exists(dir))
            {
                try
                {
                    var scanPaths = MusicUtility.ScanFolderRecursive(dir);
                    foreach (var path in scanPaths)
                    {
                        if (!allSongs.Any(s => s.FilePath == path))
                        {
                            var song = TagReader.ReadSongInfo(path);
                            if (song != null) allSongs.Add(song);
                        }
                    }
                }
                catch { }
            }
        }

        return await SaveAndDeduplicateAsync(allSongs);
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
            try { await _db.SaveSongAsync(song); }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[CatClaw] 保存歌曲失败: {song.FilePath}, {ex.Message}"); }
        }
        System.Diagnostics.Debug.WriteLine($"[CatClaw] 扫描完成: MediaStore+文件共 {allSongs.Count} 首, 去重后 {distinct.Count} 首, 全部入库");

        return distinct;
    }

    public async Task<List<Song>> ScanNetworkAsync(ConnectionProfile profile)
    {
        return new List<Song>();
    }

    public async Task<List<Song>> SearchAsync(string keyword)
    {
        var all = await GetAllSongsAsync();
        if (string.IsNullOrWhiteSpace(keyword)) return all;
        var kw = keyword.ToLowerInvariant();
        return all.Where(s =>
            (s.Title?.ToLowerInvariant().Contains(kw) ?? false) ||
            (s.Artist?.ToLowerInvariant().Contains(kw) ?? false) ||
            (s.Album?.ToLowerInvariant().Contains(kw) ?? false)
        ).ToList();
    }

    public async Task<Stream?> GetAlbumCoverAsync(Song song)
    {
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
        return all.GroupBy(s => s.Album)
            .Select(g => new Album { Name = g.Key, Artist = g.First().Artist, SongCount = g.Count() })
            .ToList();
    }
}
