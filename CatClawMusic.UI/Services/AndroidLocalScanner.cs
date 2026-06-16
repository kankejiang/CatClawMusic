using CatClawMusic.Core.Models;
using CatClawMusic.Core.Services;
using CatClawMusic.UI.Platforms.Android;
using System.Collections.Concurrent;

namespace CatClawMusic.UI.Services;

/// <summary>
/// Android 平台本地音乐扫描器。
/// <para>
/// 根据当前设备权限和用户配置，自动选择最优的扫描策略来发现本地音乐文件。
/// 扫描策略按优先级依次为：</para>
/// <list type="number">
///   <item>SAF（Storage Access Framework）— 用户通过系统文件选择器授权的文件夹</item>
///   <item>MediaStore — 读取 Android 系统媒体库（仅当启用时）</item>
///   <item>TagLib — 使用 TagReader 直接读取本地文件标签（无论是否启用 MediaStore）</item>
/// </list>
/// <para>如果以上条件均不满足，则提示用户在设置中添加文件夹。</para>
/// </summary>
public class AndroidLocalScanner
{
    /// <summary>
    /// 异步扫描 Android 设备上的本地音乐文件。
    /// <para>
    /// 该方法会根据设备权限状态和传入参数，自动选择合适的扫描策略（SAF / MediaStore / TagLib），
    /// 并在扫描过程中通过 <paramref name="progress"/> 报告进度，通过 <paramref name="songCallback"/> 实时回调已发现的歌曲。</para>
    /// </summary>
    /// <param name="customFolders">
    /// 用户自定义的扫描路径列表。可包含：
    /// <list type="bullet">
    ///   <item>SAF 内容 URI（以 "content://" 开头）— 触发 SAF 扫描策略</item>
    ///   <item>本地文件系统路径 — 使用 TagLib 扫描这些目录</item>
    /// </list>
    /// 若为 null 或空列表，则仅使用默认扫描目录。
    /// </param>
    /// <param name="progress">
    /// 进度报告回调，元组格式为 (已完成数, 总数, 状态描述文本)。
    /// 用于在 UI 层展示扫描进度和当前阶段信息。
    /// </param>
    /// <param name="songCallback">
    /// 歌曲发现回调。每当一批新歌曲被扫描到后触发，调用方可据此实时更新 UI 列表，
    /// 而不必等待整个扫描过程结束。
    /// </param>
    /// <returns>扫描发现的所有歌曲列表（已去重、已过滤短音频）。</returns>
    public static async Task<List<Song>> ScanAsync(
        List<string>? customFolders = null,
        IProgress<(int done, int total, string status)>? progress = null,
        Func<List<Song>, Task>? songCallback = null)
    {
        var allSongs = new List<Song>();
        var existingPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var locker = new object();

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
        // hasManageStorage：Android 11+ 是否拥有"所有文件访问"权限（MANAGE_EXTERNAL_STORAGE）
        bool hasManageStorage = global::Android.OS.Build.VERSION.SdkInt >= global::Android.OS.BuildVersionCodes.R
            && global::Android.OS.Environment.IsExternalStorageManager;

        // ── 判断是否走 SAF 扫描策略 ──────────────────────────────────
        // 当 customFolders 中包含 "content://" 开头的 URI 时，说明用户通过
        // Storage Access Framework 选择了文件夹，优先使用 SAF 策略
        bool hasSafFolders = customFolders != null && customFolders.Count > 0
            && customFolders.Any(f => f.StartsWith("content://", StringComparison.OrdinalIgnoreCase));

        // ════════════════════════════════════════════════════════════
        // 策略一：SAF（Storage Access Framework）扫描
        // ════════════════════════════════════════════════════════════
        // 当用户通过系统文件选择器授权了 SAF 文件夹（content:// URI）时，
        // 使用 SafeContentScanner 读取已持久化的 SAF 目录内容。
        // 此策略不需要"所有文件访问"权限，兼容性最好。
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
                }, progress);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[CatClaw] SAF scan error: {ex.Message}");
            }
        }

        // ════════════════════════════════════════════════════════════
        // 策略二：MediaStore 扫描（仅当启用时）
        // ════════════════════════════════════════════════════════════
        // 当 ScanSettings.UseMediaStore 为 true 且没有 SAF 文件夹时，
        // 通过 Android 系统 MediaStore 数据库查询音乐信息。
        // 若同时有本地自定义文件夹路径，还会用 TagLib 补充扫描这些目录。
        if (useMediaStore && !hasSafFolders)
        {
            progress?.Report((0, 2, "扫描 Android 媒体库..."));
            try
            {
                var mediaSongs = await Task.Run(() => AndroidMediaScanner.ScanFromMediaStore()).ConfigureAwait(false);
                foreach (var s in mediaSongs)
                {
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
        // 无论是否启用 MediaStore，都扫描用户添加的本地文件夹。
        // 当拥有"所有文件访问"权限时，还会扫描默认的 /Music 和 /Download 目录。
        var customLocalFolders = customFolders?.Where(f =>
            !string.IsNullOrWhiteSpace(f) &&
            !f.StartsWith("content://") &&
            Directory.Exists(f)).ToList();

        if (customLocalFolders?.Count > 0 || (hasManageStorage && useMediaStore))
        {
            progress?.Report((0, 1, "扫描本地文件夹..."));

            var scanDirs = new List<string>();
            
            // 添加用户自定义的本地文件夹
            if (customLocalFolders?.Count > 0)
            {
                scanDirs.AddRange(customLocalFolders);
            }

            // 当拥有"所有文件访问"权限且启用了 MediaStore 时，还扫描默认目录
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

            // 过滤掉已收录的路径，只处理新增文件
            var newPaths = allScanPaths.Where(p => !existingPaths.Contains(p)).ToList();

            // 使用并行处理 + TagLib 读取新增文件的标签信息
            if (newPaths.Count > 0)
            {
                var songBag = new ConcurrentBag<Song>();
                var processedCount = 0;
                var totalToProcess = newPaths.Count;

                Parallel.ForEach(newPaths, new ParallelOptions
                {
                    MaxDegreeOfParallelism = hasManageStorage ? Math.Min(Environment.ProcessorCount, 8) : 1
                }, path =>
                {
                    try
                    {
                        var song = TagReader.ReadSongInfo(path);
                        if (song != null)
                        {
                            if (filterShort && song.Duration < minDuration) return;
                            songBag.Add(song);
                            existingPaths.Add(path);
                        }
                    }
                    catch { }

                    var count = Interlocked.Increment(ref processedCount);
                    if (count % 50 == 0 || count == totalToProcess)
                    {
                        progress?.Report((count, totalToProcess, $"正在读取标签 {count}/{totalToProcess}..."));
                    }
                });

                var batch = songBag.ToList();
                lock (locker) allSongs.AddRange(batch);

                if (batch.Count > 0 && songCallback != null)
                {
                    const int batchSize = 30;
                    for (int i = 0; i < batch.Count; i += batchSize)
                    {
                        var chunk = batch.Skip(i).Take(batchSize).ToList();
                        await songCallback(chunk);
                    }
                }
            }

            progress?.Report((1, 1, $"扫描完成，共 {allSongs.Count} 首"));
        }
        else if (!useMediaStore)
        {
            // 如果禁用 MediaStore 且没有添加本地文件夹，提示用户
            progress?.Report((0, 1, "请先在设置中添加音乐文件夹"));
        }

        return allSongs;
    }
}
