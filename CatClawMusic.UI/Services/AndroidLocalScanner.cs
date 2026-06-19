using CatClawMusic.Core.Models;
using CatClawMusic.Core.Services;
using CatClawMusic.UI.Platforms.Android;
using System.Diagnostics;

namespace CatClawMusic.UI.Services;

/// <summary>
/// Android 平台本地音乐扫描器。
/// <para>
/// 根据当前设备权限和用户配置，自动选择最优的扫描策略来发现本地音乐文件。
/// 扫描策略按优先级依次为：</para>
/// <list type="number">
///   <item>SAF（Storage Access Framework）— 用户通过系统文件选择器授权的文件夹</item>
///   <item>MediaStore — 读取 Android 系统媒体库（仅当启用时）</item>
///   <item>TagLib — 直接读取本地文件标签（避免 Android 媒体服务的 binder IPC 开销）</item>
/// </list>
/// <para>如果以上条件均不满足，则提示用户在设置中添加文件夹。</para>
/// </summary>
public class AndroidLocalScanner
{
    /// <summary>
    /// 异步扫描 Android 设备上的本地音乐文件。
    /// <para>
    /// 该方法会根据设备权限状态和传入参数，自动选择合适的扫描策略（SAF / MediaStore / TagLib），
    /// 并在扫描过程中通过 <paramref name="progress"/> 报告进度，通过 <paramref name="songCallback"/> 实时回调已发现的新歌曲。</para>
    /// </summary>
    /// <param name="customFolders">
    /// 用户自定义的扫描路径列表。可包含：
    /// <list type="bullet">
    ///   <item>SAF 内容 URI（以 "content://" 开头）— 触发 SAF 扫描策略</item>
    ///   <item>本地文件系统路径 — 使用 TagLib 扫描这些目录</item>
    /// </list>
    /// 若为 null 或空列表，则仅使用默认扫描目录。
    /// </param>
    /// <param name="progress">进度报告回调</param>
    /// <param name="songCallback">歌曲发现回调。每当一批新歌曲被扫描到后触发</param>
    /// <param name="existingPathModTimes">数据库中已有本地歌曲的路径与最后修改时间映射，用于增量扫描跳过未变更文件</param>
    /// <param name="onPathDiscovered">任意文件（含未变更）被发现时回调，用于统计所有扫描到的路径</param>
    /// <returns>扫描发现的所有新歌曲列表（已去重、已过滤短音频）。</returns>
    public static async Task<List<Song>> ScanAsync(
        List<string>? customFolders = null,
        IProgress<(int done, int total, string status)>? progress = null,
        Func<List<Song>, Task>? songCallback = null,
        Dictionary<string, long>? existingPathModTimes = null,
        Action<string>? onPathDiscovered = null)
    {
        var sw = Stopwatch.StartNew();
        var allSongs = new List<Song>();
        var existingPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var locker = new object();
        long skippedCount = 0;

        // ── 读取扫描配置（ScanSettings） ──────────────────────────────
        bool useMediaStore = ScanSettings.UseMediaStore;
        bool filterShort = ScanSettings.FilterShortAudio;
        int minDuration = ScanSettings.MinDurationSec;

        // ── 合并自定义文件夹：参数传入 + ScanSettings 本地文件夹路径 ──
        var localFolderPaths = ScanSettings.GetLocalFolderPaths();
        if (localFolderPaths.Count > 0)
        {
            customFolders = customFolders != null
                ? customFolders.Concat(localFolderPaths).ToList()
                : localFolderPaths;
        }

        // ── 检测设备权限状态 ──────────────────────────────────────────
        bool hasManageStorage = global::Android.OS.Build.VERSION.SdkInt >= global::Android.OS.BuildVersionCodes.R
            && global::Android.OS.Environment.IsExternalStorageManager;

        // ── 判断是否走 SAF 扫描策略 ──────────────────────────────────
        bool hasSafFolders = customFolders != null && customFolders.Count > 0
            && customFolders.Any(f => f.StartsWith("content://", StringComparison.OrdinalIgnoreCase));

        // ════════════════════════════════════════════════════════════
        // 策略一：SAF（Storage Access Framework）扫描
        // ════════════════════════════════════════════════════════════
        if (hasSafFolders)
        {
            progress?.Report((0, 1, "扫描选中的文件夹..."));
            try
            {
                await SafeContentScanner.ScanSavedFolderAsync(async (songs) =>
                {
                    var filtered = filterShort ? songs.Where(s => s.Duration >= minDuration) : songs;
                    var newSongs = filtered.Where(s => existingPaths.Add(s.FilePath)).ToList();
                    if (newSongs.Count == 0) return;
                    lock (locker) allSongs.AddRange(newSongs);
                    if (songCallback != null)
                        await songCallback(newSongs);
                }, progress, existingPathModTimes, onPathDiscovered);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[CatClaw] SAF scan error: {ex.Message}");
            }
        }

        // ════════════════════════════════════════════════════════════
        // 策略二：MediaStore 扫描（仅当启用时）
        // ════════════════════════════════════════════════════════════
        if (useMediaStore && !hasSafFolders)
        {
            progress?.Report((0, 2, "扫描 Android 媒体库..."));
            try
            {
                var mediaSongs = await Task.Run(() => AndroidMediaScanner.ScanFromMediaStore()).ConfigureAwait(false);
                foreach (var s in mediaSongs)
                {
                    onPathDiscovered?.Invoke(s.FilePath);
                    if (filterShort && s.Duration < minDuration) continue;
                    if (existingPaths.Add(s.FilePath))
                        allSongs.Add(s);
                }
            }
            catch { }

            if (allSongs.Count > 0 && songCallback != null)
                await songCallback(allSongs.ToList());

            progress?.Report((2, 2, $"扫描完成，共 {allSongs.Count} 首"));
        }

        // ════════════════════════════════════════════════════════════
        // 策略三：TagLib 扫描本地文件夹
        // ════════════════════════════════════════════════════════════
        var customLocalFolders = customFolders?.Where(f =>
            !string.IsNullOrWhiteSpace(f) &&
            !f.StartsWith("content://") &&
            Directory.Exists(f)).ToList();

        if (customLocalFolders?.Count > 0 || (hasManageStorage && useMediaStore))
        {
            progress?.Report((0, 1, "扫描本地文件夹..."));

            var scanDirs = new List<string>();

            if (customLocalFolders?.Count > 0)
            {
                scanDirs.AddRange(customLocalFolders);
            }

            if (hasManageStorage && useMediaStore)
            {
                if (Directory.Exists("/storage/emulated/0/Music"))
                    scanDirs.Add("/storage/emulated/0/Music");
                if (Directory.Exists("/storage/emulated/0/Download"))
                    scanDirs.Add("/storage/emulated/0/Download");
            }

            var allScanPaths = new List<string>();
            foreach (var dir in scanDirs)
            {
                try { allScanPaths.AddRange(MusicUtility.ScanFolderRecursive(dir)); }
                catch { }
            }

            // 过滤掉其他策略已收录的路径
            var newPaths = allScanPaths.Where(p => !existingPaths.Contains(p)).ToList();

            // 使用并行处理 + TagLib 读取新增或变更文件的标签信息
            if (newPaths.Count > 0)
            {
                var results = new List<Song>();
                var resultsLock = new object();
                var processedCount = 0;
                var totalToProcess = newPaths.Count;
                var readSw = Stopwatch.StartNew();

                Parallel.ForEach(newPaths, new ParallelOptions
                {
                    MaxDegreeOfParallelism = Math.Min(Environment.ProcessorCount, 4)
                },
                () => new List<Song>(),
                (path, state, localSongs) =>
                {
                    onPathDiscovered?.Invoke(path);

                    try
                    {
                        // 增量扫描：文件未修改则跳过标签读取
                        if (existingPathModTimes != null)
                        {
                            var lastModified = GetFileUnixModifiedTime(path);
                            if (existingPathModTimes.TryGetValue(path, out var dbMod) && dbMod == lastModified)
                            {
                                Interlocked.Increment(ref skippedCount);
                                return localSongs;
                            }
                        }

                        var song = TagReader.ReadSongInfo(path);
                        if (song != null)
                        {
                            if (filterShort && song.Duration < minDuration) return localSongs;
                            localSongs.Add(song);
                            lock (existingPaths) { existingPaths.Add(path); }
                        }
                    }
                    catch { }

                    var count = Interlocked.Increment(ref processedCount);
                    if (count % 50 == 0 || count == totalToProcess)
                    {
                        progress?.Report((count, totalToProcess, $"正在读取标签 {count}/{totalToProcess}..."));
                    }
                    return localSongs;
                },
                localSongs =>
                {
                    if (localSongs.Count > 0)
                    {
                        lock (resultsLock) { results.AddRange(localSongs); }
                    }
                });
                readSw.Stop();

                lock (locker) allSongs.AddRange(results);

                if (results.Count > 0 && songCallback != null)
                {
                    const int batchSize = 30;
                    for (int i = 0; i < results.Count; i += batchSize)
                    {
                        var chunk = results.Skip(i).Take(batchSize).ToList();
                        await songCallback(chunk);
                    }
                }

                System.Diagnostics.Debug.WriteLine($"[CatClaw] TagLib 扫描：总计 {totalToProcess}，跳过 {skippedCount}，读取 {results.Count}，耗时 {readSw.ElapsedMilliseconds}ms");
            }

            progress?.Report((1, 1, $"扫描完成，共 {allSongs.Count} 首"));
        }
        else if (!useMediaStore)
        {
            progress?.Report((0, 1, "请先在设置中添加音乐文件夹"));
        }

        sw.Stop();
        System.Diagnostics.Debug.WriteLine($"[CatClaw] AndroidLocalScanner 扫描完成：新歌曲 {allSongs.Count}，跳过未变更 {skippedCount}，总耗时 {sw.ElapsedMilliseconds}ms");
        return allSongs;
    }

    private static long GetFileUnixModifiedTime(string path)
    {
        try
        {
            return new DateTimeOffset(File.GetLastWriteTimeUtc(path)).ToUnixTimeSeconds();
        }
        catch
        {
            return 0;
        }
    }
}
