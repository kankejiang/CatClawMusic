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

    /// <summary>
    /// 静态标记：上次扫描后库内容已变更，发现页等页面需要重新加载。
    /// 页面在 OnAppearing 时检查并消费此标记。
    /// </summary>
    public static bool NeedsReload { get; set; }

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
    /// 进度按阶段分配权重，每个阶段内部报告线性渐进进度，避免进度条卡顿跳跃。
    /// </summary>
    public async Task<int> ScanAsync(
        IProgress<(int done, int total, string status)>? progress = null,
        CancellationToken cancellationToken = default,
        bool useMediaStore = false,
        bool useSafScan = false)
    {
        var allSongs = new HashSet<Song>(new SongPathComparer());
        int totalImported = 0;

        // 进度权重分配：扫描阶段共占 0-90，导入占 90-95，清理占 95-100
        // 各扫描阶段在 0-90 范围内按总步骤数均分
        try
        {
            var safUris = new List<string>();
#if ANDROID
            safUris = Platforms.Android.FolderPicker.GetSavedFolderUris();
#endif
            var customFolders = GetCustomFolders();
            var hasCustomFolders = customFolders.Count > 0;
            var hasSafFolders = safUris.Count > 0;

            // 统计扫描阶段总数
            var totalSteps = 0;
            if (useMediaStore) totalSteps++;
            if (useSafScan && hasSafFolders) totalSteps++;
            if (hasCustomFolders) totalSteps++;
            if (totalSteps == 0) totalSteps = 1;

            var currentStep = 0;
            // 扫描阶段占 0-90%，每个步骤的宽度
            var stepWidth = 90.0 / totalSteps;

            // 辅助：报告当前阶段内某进度（0~1）对应的全局进度
            void ReportStepProgress(int step, double localRatio, string status)
            {
                var globalStart = step * stepWidth;
                var globalPct = (int)(globalStart + stepWidth * localRatio);
                progress?.Report((globalPct, 100, status));
            }

            // 1. MediaStore 扫描
            if (useMediaStore)
            {
                ReportStepProgress(currentStep, 0, $"[{currentStep + 1}/{totalSteps}] 正在通过系统媒体库扫描...");
#if ANDROID
                try
                {
                    var mediaStoreSongs = await Task.Run(() =>
                        Platforms.Android.AndroidMediaScanner.ScanFromMediaStore(), cancellationToken);
                    foreach (var s in mediaStoreSongs)
                        allSongs.Add(s);
                    ReportStepProgress(currentStep, 1, $"[{currentStep + 1}/{totalSteps}] 媒体库扫描完成，发现 {mediaStoreSongs.Count} 首歌曲");
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[LocalScan] MediaStore error: {ex.Message}");
                }
#endif
                currentStep++;
            }

            // 2. SAF 文件夹扫描
            if (useSafScan && hasSafFolders)
            {
                ReportStepProgress(currentStep, 0, $"[{currentStep + 1}/{totalSteps}] 正在通过 SAF 扫描已选文件夹...");
#if ANDROID
                try
                {
                    var existingModTimes = await GetExistingPathModTimesAsync();
                    var safSongs = new List<Song>();
                    var safTotal = 0;
                    await Platforms.Android.SafeContentScanner.ScanSavedFoldersAsync(
                        async batch =>
                        {
                            lock (safSongs) { safSongs.AddRange(batch); }
                            await Task.CompletedTask;
                        },
                        new Progress<(int done, int total, string s)>(p =>
                        {
                            // 将 SAF 内部 (done, total) 映射到当前阶段的全局进度
                            safTotal = p.total;
                            var localRatio = p.total > 0 ? (double)p.done / p.total : 0;
                            ReportStepProgress(currentStep, localRatio, $"[{currentStep + 1}/{totalSteps}] {p.s} (已发现 {safSongs.Count} 首)");
                        }),
                        existingModTimes,
                        null
                    );
                    foreach (var s in safSongs)
                        allSongs.Add(s);
                    ReportStepProgress(currentStep, 1, $"[{currentStep + 1}/{totalSteps}] SAF 扫描完成，发现 {safSongs.Count} 首歌曲");
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[LocalScan] SAF error: {ex.Message}");
                }
#endif
                currentStep++;
            }

            // 3. 自定义文件夹扫描（逐文件读取元数据，内部报告渐进进度）
            if (hasCustomFolders)
            {
                ReportStepProgress(currentStep, 0, $"[{currentStep + 1}/{totalSteps}] 正在扫描自定义文件夹...");
                try
                {
                    // 先收集所有音频文件路径，再逐个读取，以便报告线性进度
                    var allFilePaths = new List<string>();
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
                                if (seenPaths.Add(path))
                                    allFilePaths.Add(path);
                            }
                        }
                        catch (Exception ex)
                        {
                            System.Diagnostics.Debug.WriteLine($"[LocalScan] Scan folder error: {folder}, {ex.Message}");
                        }
                    }

                    var totalFiles = allFilePaths.Count;
                    var customSongs = new List<Song>();
                    var processed = 0;

                    await Task.Run(() =>
                    {
                        foreach (var path in allFilePaths)
                        {
                            cancellationToken.ThrowIfCancellationRequested();
                            try
                            {
                                var song = TagReader.ReadSongInfo(path);
                                if (song != null)
                                {
                                    song.Source = SongSource.Local;
                                    customSongs.Add(song);
                                }
                            }
                            catch (Exception ex)
                            {
                                System.Diagnostics.Debug.WriteLine($"[LocalScan] ReadSongInfo error: {path}, {ex.Message}");
                            }
                            processed++;
                            // 每 5 个文件或最后一个文件报告一次进度，避免过于频繁
                            if (processed % 5 == 0 || processed == totalFiles)
                            {
                                var localRatio = totalFiles > 0 ? (double)processed / totalFiles : 0;
                                ReportStepProgress(currentStep, localRatio, $"[{currentStep + 1}/{totalSteps}] 读取元数据 {processed}/{totalFiles} (已发现 {customSongs.Count} 首)");
                            }
                        }
                    }, cancellationToken);

                    foreach (var s in customSongs)
                        allSongs.Add(s);
                    ReportStepProgress(currentStep, 1, $"[{currentStep + 1}/{totalSteps}] 自定义文件夹扫描完成，发现 {customSongs.Count} 首歌曲");
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[LocalScan] Custom folders error: {ex.Message}");
                }
                currentStep++;
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

        // 标记库已变更，通知发现页等需要重新加载
        if (totalImported > 0)
            NeedsReload = true;

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

    private static List<string> GetCustomFolders() => CustomFolderStore.GetFolders();

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
