using CatClawMusic.Core.Interfaces;
using CatClawMusic.Core.Models;
using CatClawMusic.Data;

namespace CatClawMusic.Maui.Services;

/// <summary>
/// 本地音乐扫描服务，整合 MediaStore、SAF（存储访问框架）及自定义文件夹三种扫描来源。
/// 扫描结果按文件路径去重后统一导入音乐库。
/// </summary>
public class LocalScanService
{
    private readonly IMusicLibraryService _musicLibrary;
    private readonly MusicDatabase _db;

    /// <summary>支持的音频文件扩展名集合</summary>
    private static readonly HashSet<string> AudioExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mp3", ".flac", ".wav", ".m4a", ".aac", ".ogg", ".wma", ".ape", ".opus",
        ".m4b", ".mp4", ".alac", ".aiff", ".aif", ".wv", ".oga", ".tta", ".mka"
    };

    /// <summary>构造函数</summary>
    /// <param name="musicLibrary">音乐库服务，用于扫描和导入</param>
    /// <param name="db">本地音乐数据库，用于读取已有文件的修改时间</param>
    public LocalScanService(IMusicLibraryService musicLibrary, MusicDatabase db)
    {
        _musicLibrary = musicLibrary;
        _db = db;
    }

    /// <summary>
    /// 异步执行本地音乐扫描。
    /// 根据 useMediaStore / useSafScan / 自定义文件夹配置按顺序扫描，
    /// 合并去重后导入数据库。
    /// </summary>
    /// <param name="progress">进度回调（已完成数、总数、状态文本）；可为空</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <param name="useMediaStore">是否使用系统 MediaStore 扫描</param>
    /// <param name="useSafScan">是否使用 SAF 扫描已选文件夹</param>
    /// <returns>实际导入数据库的歌曲数量</returns>
    public async Task<int> ScanAsync(
        IProgress<(int done, int total, string status)>? progress = null,
        CancellationToken cancellationToken = default,
        bool useMediaStore = false,
        bool useSafScan = false)
    {
        var allSongs = new HashSet<Song>(new SongPathComparer());
        int totalImported = 0;

        try
        {
            var safUris = new List<string>();
#if ANDROID
            safUris = Platforms.Android.FolderPicker.GetSavedFolderUris();
#endif
            var customFolders = GetCustomFolders();
            var hasCustomFolders = customFolders.Count > 0;

            if (!useMediaStore && !useSafScan && !hasCustomFolders)
            {
                useMediaStore = true;
            }

            var totalSteps = 0;
            if (useMediaStore) totalSteps++;
            if (useSafScan && safUris.Count > 0) totalSteps++;
            if (hasCustomFolders) totalSteps++;
            if (totalSteps == 0) totalSteps = 1;

            var currentStep = 0;

            if (useMediaStore)
            {
                currentStep++;
                progress?.Report((0, 100, $"[{currentStep}/{totalSteps}] 正在通过系统媒体库扫描..."));
#if ANDROID
                try
                {
                    var mediaStoreSongs = await Task.Run(() =>
                        Platforms.Android.AndroidMediaScanner.ScanFromMediaStore(), cancellationToken);
                    foreach (var s in mediaStoreSongs)
                        allSongs.Add(s);
                    progress?.Report((currentStep * 100 / totalSteps, 100, $"媒体库扫描完成，发现 {mediaStoreSongs.Count} 首歌曲"));
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[LocalScan] MediaStore error: {ex.Message}");
                }
#endif
            }

            if (useSafScan && safUris.Count > 0)
            {
                currentStep++;
                progress?.Report((0, 100, $"[{currentStep}/{totalSteps}] 正在通过 SAF 扫描已选文件夹..."));
#if ANDROID
                try
                {
                    var existingModTimes = await GetExistingPathModTimesAsync();
                    var safSongs = new List<Song>();
                    await Platforms.Android.SafeContentScanner.ScanSavedFoldersAsync(
                        async batch =>
                        {
                            lock (safSongs) { safSongs.AddRange(batch); }
                            await Task.CompletedTask;
                        },
                        null,
                        existingModTimes,
                        null
                    );
                    foreach (var s in safSongs)
                        allSongs.Add(s);
                    progress?.Report((currentStep * 100 / totalSteps, 100, $"SAF扫描完成，发现 {safSongs.Count} 首歌曲"));
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[LocalScan] SAF error: {ex.Message}");
                }
#endif
            }

            if (hasCustomFolders)
            {
                currentStep++;
                progress?.Report((0, 100, $"[{currentStep}/{totalSteps}] 正在扫描自定义文件夹..."));
                try
                {
                    var customSongs = new List<Song>();
                    foreach (var folder in customFolders)
                    {
                        if (Directory.Exists(folder))
                        {
                            var dirSongs = await _musicLibrary.ScanLocalAsync(new List<string> { folder });
                            customSongs.AddRange(dirSongs);
                        }
                    }
                    foreach (var s in customSongs)
                        allSongs.Add(s);
                    progress?.Report((currentStep * 100 / totalSteps, 100, $"自定义文件夹扫描完成，发现 {customSongs.Count} 首歌曲"));
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[LocalScan] Custom folders error: {ex.Message}");
                }
            }

            var songList = allSongs.ToList();
            progress?.Report((90, 100, $"合并后共 {songList.Count} 首歌曲，正在导入..."));

            var importedList = await _musicLibrary.ImportSongsAsync(songList);
            totalImported = importedList.Count;
            progress?.Report((100, 100, $"扫描完成，共导入 {totalImported} 首歌曲"));
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

    private static List<string> GetCustomFolders()
    {
        try
        {
            var json = Preferences.Get("custom_music_folders", "");
            if (string.IsNullOrEmpty(json)) return new List<string>();
            return System.Text.Json.JsonSerializer.Deserialize<List<string>>(json) ?? new List<string>();
        }
        catch { return new List<string>(); }
    }

    private class SongPathComparer : IEqualityComparer<Song>
    {
        public bool Equals(Song? x, Song? y)
        {
            if (x == null || y == null) return false;
            if (!string.IsNullOrEmpty(x.FilePath) && !string.IsNullOrEmpty(y.FilePath))
                return string.Equals(x.FilePath, y.FilePath, StringComparison.OrdinalIgnoreCase);
            return x.Id == y.Id && x.Id > 0;
        }

        public int GetHashCode(Song obj)
        {
            if (!string.IsNullOrEmpty(obj.FilePath))
                return obj.FilePath.ToLowerInvariant().GetHashCode();
            return obj.Id.GetHashCode();
        }
    }
}
