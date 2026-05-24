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
///   <item>MediaStore — 读取 Android 系统媒体库</item>
///   <item>ManageStorage + TagLib — 拥有"所有文件访问"权限时，先查 MediaStore，再递归扫描本地目录并用 TagLib 读取标签</item>
/// </list>
/// <para>如果以上条件均不满足，则回退到仅 MediaStore 扫描。</para>
/// </summary>
public class AndroidLocalScanner
{
    /// <summary>
    /// 异步扫描 Android 设备上的本地音乐文件。
    /// <para>
    /// 该方法会根据设备权限状态和传入参数，自动选择合适的扫描策略（SAF / MediaStore / ManageStorage+TagLib），
    /// 并在扫描过程中通过 <paramref name="progress"/> 报告进度，通过 <paramref name="songCallback"/> 实时回调已发现的歌曲。</para>
    /// </summary>
    /// <param name="customFolders">
    /// 用户自定义的扫描路径列表。可包含：
    /// <list type="bullet">
    ///   <item>SAF 内容 URI（以 "content://" 开头）— 触发 SAF 扫描策略</item>
    ///   <item>本地文件系统路径 — 在 ManageStorage 策略下追加到默认扫描目录</item>
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
        // UseMediaStore：是否优先使用 MediaStore 扫描（而非 SAF）
        // FilterShortAudio：是否过滤掉时长过短的音频文件
        // MinDurationSec：短音频的最小时长阈值（秒），低于此值的音频将被过滤
        bool useMediaStore = ScanSettings.UseMediaStore;
        bool filterShort = ScanSettings.FilterShortAudio;
        int minDuration = ScanSettings.MinDurationSec;

        // ── 检测设备权限状态 ──────────────────────────────────────────
        // hasManageStorage：Android 11+ 是否拥有"所有文件访问"权限（MANAGE_EXTERNAL_STORAGE）
        bool hasManageStorage = global::Android.OS.Build.VERSION.SdkInt >= global::Android.OS.BuildVersionCodes.R
            && global::Android.OS.Environment.IsExternalStorageManager;

        // ── 判断是否走 SAF 扫描策略 ──────────────────────────────────
        // 当 customFolders 中包含 "content://" 开头的 URI 时，说明用户通过
        // Storage Access Framework 选择了文件夹，优先使用 SAF 策略
        bool hasSafFolders = customFolders != null && customFolders.Count > 0
            && customFolders.Any(f => f.StartsWith("content://", StringComparison.OrdinalIgnoreCase));

        // ══════════════════════════════════════════════════════════════
        // 策略一：SAF（Storage Access Framework）扫描
        // ══════════════════════════════════════════════════════════════
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
        // ══════════════════════════════════════════════════════════════
        // 策略二：MediaStore 扫描（用户配置优先）
        // ══════════════════════════════════════════════════════════════
        // 当 ScanSettings.UseMediaStore 为 true 且没有 SAF 文件夹时，
        // 仅通过 Android 系统 MediaStore 数据库查询音乐信息。
        // 速度最快，但只能获取系统已索引的文件。
        else if (useMediaStore)
        {
            var hasLocalCustomFolders = customFolders != null && customFolders.Any(f =>
                !string.IsNullOrWhiteSpace(f) && !f.StartsWith("content://"));

            progress?.Report((0, 1, "扫描 Android 媒体库..."));
            try
            {
                var mediaSongs = AndroidMediaScanner.ScanFromMediaStore();
                foreach (var s in mediaSongs)
                {
                    if (filterShort && s.Duration < minDuration) continue;
                    if (hasLocalCustomFolders)
                    {
                        var fp = s.FilePath ?? "";
                        if (!customFolders!.Any(f => fp.StartsWith(f, StringComparison.OrdinalIgnoreCase)))
                            continue;
                    }
                    if (existingPaths.Add(s.FilePath))
                        allSongs.Add(s);
                }
            }
            catch { }

            if (allSongs.Count > 0 && songCallback != null)
                await songCallback(allSongs.ToList());

            progress?.Report((1, 1, $"扫描完成，共 {allSongs.Count} 首"));
        }
        // ══════════════════════════════════════════════════════════════
        // 策略三：ManageStorage + TagLib 双重扫描
        // ══════════════════════════════════════════════════════════════
        // 当拥有"所有文件访问"权限（Android 11+ MANAGE_EXTERNAL_STORAGE）时，
        // 采用两阶段扫描：
        //   阶段1：先通过 MediaStore 快速获取系统已索引的歌曲
        //   阶段2：再递归扫描本地目录（Music、Download 及自定义路径），
        //          使用 TagLib（TagReader）读取文件标签，补充 MediaStore 未收录的文件
        // 此策略覆盖面最广，但耗时较长。
        else if (hasManageStorage)
        {
            var hasLocalCustomFolders = customFolders != null && customFolders.Any(f =>
                !string.IsNullOrWhiteSpace(f) && !f.StartsWith("content://"));

            progress?.Report((0, 2, "扫描系统媒体库..."));
            try
            {
                var mediaSongs = AndroidMediaScanner.ScanFromMediaStore();
                foreach (var s in mediaSongs)
                {
                    if (filterShort && s.Duration < minDuration) continue;
                    if (hasLocalCustomFolders)
                    {
                        var fp = s.FilePath ?? "";
                        if (!customFolders!.Any(f => fp.StartsWith(f, StringComparison.OrdinalIgnoreCase)))
                            continue;
                    }
                    if (existingPaths.Add(s.FilePath))
                        allSongs.Add(s);
                }
            }
            catch { }

            if (allSongs.Count > 0 && songCallback != null)
                await songCallback(allSongs.ToList());

            // ── 阶段2：本地文件递归扫描 + TagLib 标签读取 ──────────────
            // 默认扫描 /Music 和 /Download 目录，并追加用户自定义的本地路径
            progress?.Report((1, 2, "扫描本地文件..."));
            var scanDirs = new List<string> { "/storage/emulated/0/Music", "/storage/emulated/0/Download" };
            if (customFolders != null)
            {
                foreach (var f in customFolders)
                    if (!string.IsNullOrWhiteSpace(f) && !f.StartsWith("content://") && Directory.Exists(f) && !scanDirs.Contains(f))
                        scanDirs.Add(f);
            }

            var allScanPaths = new List<string>();
            foreach (var dir in scanDirs)
            {
                if (Directory.Exists(dir))
                {
                    try { allScanPaths.AddRange(MusicUtility.ScanFolderRecursive(dir)); }
                    catch { }
                }
            }

            // 过滤掉 MediaStore 阶段已收录的路径，只处理新增文件
            var newPaths = allScanPaths.Where(p => !existingPaths.Contains(p)).ToList();

            // 使用并行处理 + TagLib 读取新增文件的标签信息
            // 限制最大并行度为 CPU 核心数与 4 中的较小值，避免 I/O 争抢
            if (newPaths.Count > 0)
            {
                var songBag = new ConcurrentBag<Song>();
                var processedCount = 0;
                var totalToProcess = newPaths.Count;

                Parallel.ForEach(newPaths, new ParallelOptions
                {
                    MaxDegreeOfParallelism = Math.Min(Environment.ProcessorCount, 4)
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
        // ══════════════════════════════════════════════════════════════
        // 兜底策略：仅 MediaStore 扫描
        // ══════════════════════════════════════════════════════════════
        // 既没有 SAF 文件夹，也没有开启 UseMediaStore，也没有"所有文件访问"权限，
        // 只能回退到 MediaStore 扫描作为兜底方案。
        else
        {
            var hasLocalCustomFolders = customFolders != null && customFolders.Any(f =>
                !string.IsNullOrWhiteSpace(f) && !f.StartsWith("content://"));

            progress?.Report((0, 1, "扫描系统媒体库..."));
            try
            {
                var mediaSongs = AndroidMediaScanner.ScanFromMediaStore();
                foreach (var s in mediaSongs)
                {
                    if (filterShort && s.Duration < minDuration) continue;
                    if (hasLocalCustomFolders)
                    {
                        var fp = s.FilePath ?? "";
                        if (!customFolders!.Any(f => fp.StartsWith(f, StringComparison.OrdinalIgnoreCase)))
                            continue;
                    }
                    if (existingPaths.Add(s.FilePath))
                        allSongs.Add(s);
                }
            }
            catch { }

            if (allSongs.Count > 0 && songCallback != null)
                await songCallback(allSongs.ToList());

            progress?.Report((1, 1, $"扫描完成，共 {allSongs.Count} 首"));
        }

        return allSongs;
    }
}
