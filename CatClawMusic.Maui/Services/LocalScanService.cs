using CatClawMusic.Core.Interfaces;
using CatClawMusic.Core.Models;
using CatClawMusic.Data;

namespace CatClawMusic.Maui.Services;

/// <summary>本地音乐扫描服务 — 协调 SAF / MediaStore 扫描策略，导入歌曲到数据库</summary>
public class LocalScanService
{
    private readonly IMusicLibraryService _musicLibrary;
    private readonly MusicDatabase _db;

    public LocalScanService(IMusicLibraryService musicLibrary, MusicDatabase db)
    {
        _musicLibrary = musicLibrary;
        _db = db;
    }

    /// <summary>执行本地音乐扫描</summary>
    /// <param name="progress">进度回调: (done, total, status)</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>导入的歌曲总数</returns>
    public async Task<int> ScanAsync(
        IProgress<(int done, int total, string status)>? progress = null,
        CancellationToken cancellationToken = default)
    {
        int totalImported = 0;

        try
        {
#if ANDROID
            // 1. 获取已保存的 SAF 文件夹
            var safUris = Platforms.Android.FolderPicker.GetSavedFolderUris();

            if (safUris.Count > 0)
            {
                // 有 SAF 文件夹 → 使用 SAF 扫描器
                progress?.Report((0, 100, "正在通过 SAF 扫描已选文件夹..."));

                var existingModTimes = await GetExistingPathModTimesAsync();

                var allSongs = new List<Song>();
                await Platforms.Android.SafeContentScanner.ScanSavedFoldersAsync(
                    async batch =>
                    {
                        lock (allSongs) { allSongs.AddRange(batch); }
                        await Task.CompletedTask;
                    },
                    progress,
                    existingModTimes,
                    path => { /* 不逐文件通知 */ }
                );

                progress?.Report((80, 100, $"发现 {allSongs.Count} 首歌曲，正在导入..."));

                // 导入到数据库
                var importedList = await _musicLibrary.ImportSongsAsync(allSongs);
                totalImported = importedList.Count;
                progress?.Report((100, 100, $"扫描完成，共导入 {totalImported} 首歌曲"));
                return totalImported;
            }
#endif

            // 2. 无 SAF 文件夹 → 尝试 MediaStore 扫描
#if ANDROID
            progress?.Report((0, 100, "正在通过系统媒体库扫描..."));

            var mediaStoreSongs = await Task.Run(() =>
                Platforms.Android.AndroidMediaScanner.ScanFromMediaStore(), cancellationToken);

            if (mediaStoreSongs.Count > 0)
            {
                progress?.Report((50, 100, $"发现 {mediaStoreSongs.Count} 首歌曲，正在导入..."));

                var importedList = await _musicLibrary.ImportSongsAsync(mediaStoreSongs);
                totalImported = importedList.Count;
                progress?.Report((100, 100, $"扫描完成，共导入 {totalImported} 首歌曲"));
                return totalImported;
            }
#endif

            // 3. 无可用策略 → 尝试文件系统扫描
            progress?.Report((0, 100, "正在扫描本地文件系统..."));
            var defaultDirs = GetDefaultMusicDirectories();
            var songs = await _musicLibrary.ScanLocalAsync(defaultDirs);
            totalImported = songs.Count;
            progress?.Report((100, 100, $"扫描完成，共找到 {totalImported} 首歌曲"));
        }
        catch (OperationCanceledException)
        {
            progress?.Report((0, 100, "扫描已取消"));
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[LocalScan] Error: {ex}");
            progress?.Report((0, 100, $"扫描失败: {ex.Message}"));
        }

        return totalImported;
    }

    /// <summary>获取数据库中已有本地歌曲的路径→修改时间映射（用于增量扫描）</summary>
    private async Task<Dictionary<string, long>> GetExistingPathModTimesAsync()
    {
        try
        {
            return await _db.GetLocalSongPathModTimesAsync();
        }
        catch
        {
            return new Dictionary<string, long>();
        }
    }

    /// <summary>获取默认音乐目录列表</summary>
    private static List<string> GetDefaultMusicDirectories()
    {
        var dirs = new List<string>();
#if ANDROID
        var storage = "/storage/emulated/0";
        if (Directory.Exists(storage + "/Music")) dirs.Add(storage + "/Music");
        if (Directory.Exists(storage + "/Download")) dirs.Add(storage + "/Download");
#else
        var music = Environment.GetFolderPath(Environment.SpecialFolder.MyMusic);
        if (Directory.Exists(music)) dirs.Add(music);
#endif
        return dirs;
    }
}
