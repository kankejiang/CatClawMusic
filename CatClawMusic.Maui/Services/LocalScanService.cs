using CatClawMusic.Core.Interfaces;
using CatClawMusic.Core.Models;
using CatClawMusic.Core.Services;
using CatClawMusic.Data;

namespace CatClawMusic.Maui.Services;

/// <summary>
/// 本地音乐扫描服务，整合 MediaStore、SAF（存储访问框架）及自定义文件夹三种扫描来源。
/// 扫描结果按文件路径去重后统一导入音乐库。
/// 
/// 扫描策略：
/// - MediaStore：扫描设备上所有音频文件（全设备覆盖）
/// - SAF：递归遍历用户通过 SAF 选择的文件夹（content:// URI）
/// - 自定义文件夹：递归遍历用户通过自研文件管理器选择的本地文件夹
/// 多个来源取并集后去重。
/// 
/// 清理策略：
/// - 启用 MediaStore 时不清理（MediaStore 覆盖全设备，所有本地歌曲都应被发现）
/// - 仅启用 SAF 和/或自定义文件夹时，清理不在本次扫描路径中的旧歌曲
/// - 无任何来源时，清空所有本地歌曲（用户主动清空）
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
    public LocalScanService(IMusicLibraryService musicLibrary, MusicDatabase db)
    {
        _musicLibrary = musicLibrary;
        _db = db;
    }

    /// <summary>
    /// 异步执行本地音乐扫描。
    /// 根据 useMediaStore / useSafScan / 自定义文件夹配置按顺序扫描，
    /// 合并去重后导入数据库，并清理已删除文件夹中的歌曲。
    /// </summary>
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
            var hasSafFolders = safUris.Count > 0;

            var totalSteps = 0;
            if (useMediaStore) totalSteps++;
            if (useSafScan && hasSafFolders) totalSteps++;
            if (hasCustomFolders) totalSteps++;
            if (totalSteps == 0) totalSteps = 1;

            var currentStep = 0;

            // 1. MediaStore 扫描
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

            // 2. SAF 文件夹扫描
            if (useSafScan && hasSafFolders)
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

            // 3. 自定义文件夹扫描（直接扫描文件，不经由 ScanLocalAsync 以避免重复入库）
            if (hasCustomFolders)
            {
                currentStep++;
                progress?.Report((0, 100, $"[{currentStep}/{totalSteps}] 正在扫描自定义文件夹..."));
                try
                {
                    var customSongs = await Task.Run(() =>
                    {
                        var songs = new List<Song>();
                        var seenPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                        foreach (var folder in customFolders)
                        {
                            System.Diagnostics.Debug.WriteLine($"[LocalScan] 自定义文件夹: '{folder}', Directory.Exists={Directory.Exists(folder)}");
                            if (!Directory.Exists(folder)) continue;
                            try
                            {
                                var filePaths = MusicUtility.ScanFolderRecursive(folder);
                                System.Diagnostics.Debug.WriteLine($"[LocalScan] 文件夹 '{folder}' 递归发现音频文件数: {filePaths.Count}");
                                foreach (var path in filePaths)
                                {
                                    if (!seenPaths.Add(path)) continue;
                                    try
                                    {
                                        var song = TagReader.ReadSongInfo(path);
                                        if (song != null)
                                        {
                                            song.Source = SongSource.Local;
                                            songs.Add(song);
                                        }
                                    }
                                    catch (Exception ex)
                                    {
                                        System.Diagnostics.Debug.WriteLine($"[LocalScan] ReadSongInfo error: {path}, {ex.Message}");
                                    }
                                }
                            }
                            catch (Exception ex)
                            {
                                System.Diagnostics.Debug.WriteLine($"[LocalScan] Scan folder error: {folder}, {ex.Message}");
                            }
                        }
                        return songs;
                    }, cancellationToken);
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

            // 无任何扫描来源：清空本地歌曲
            if (songList.Count == 0 && !useMediaStore)
            {
                progress?.Report((90, 100, "未配置任何扫描来源，正在清空本地音乐库..."));
            }
            else
            {
                progress?.Report((90, 100, $"合并后共 {songList.Count} 首歌曲，正在导入..."));
            }

            var importedList = await _musicLibrary.ImportSongsAsync(songList);
            totalImported = importedList.Count;

            // 清理过时本地歌曲：
            // - MediaStore 覆盖全设备，启用时不清理（避免误删设备上其他位置的歌曲）
            // - 仅 SAF/自定义文件夹/无来源时，清理不在本次扫描结果中的本地歌曲
            //   无来源时 scannedPaths 为空，会清空所有本地歌曲
            if (!useMediaStore)
            {
                progress?.Report((95, 100, "正在清理已删除文件夹的歌曲..."));
                try
                {
                    var scannedPaths = new HashSet<string>(
                        songList.Where(s => s.Source == SongSource.Local && !string.IsNullOrEmpty(s.FilePath))
                                .Select(s => s.FilePath),
                        StringComparer.OrdinalIgnoreCase);
                    var removedCount = await _db.RemoveLocalSongsNotInPathsAsync(scannedPaths);
                    if (removedCount > 0)
                    {
                        System.Diagnostics.Debug.WriteLine($"[LocalScan] 清理了 {removedCount} 首过时本地歌曲");
                    }
                }
                catch (Exception cex)
                {
                    System.Diagnostics.Debug.WriteLine($"[LocalScan] Cleanup stale songs error: {cex.Message}");
                }
            }

            if (songList.Count == 0 && !useMediaStore)
            {
                progress?.Report((100, 100, "本地音乐库已清空"));
            }
            else
            {
                progress?.Report((100, 100, $"扫描完成，共导入 {totalImported} 首歌曲"));
            }
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
