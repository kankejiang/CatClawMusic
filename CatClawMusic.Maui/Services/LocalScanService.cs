using CatClawMusic.Core.Interfaces;
using CatClawMusic.Core.Models;
using CatClawMusic.Core.Services;
using CatClawMusic.Data;
using System.Collections.Concurrent;

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
    private readonly MusicLibrarySnapshotService _snapshotService;

    /// <summary>
    /// 静态标记：上次扫描后库内容已变更，发现页等页面需要重新加载。
    /// 页面在 OnAppearing 时检查并消费此标记。
    /// </summary>
    public static bool NeedsReload { get; set; }

    /// <summary>
    /// 扫描完成事件，当扫描导入新歌曲后触发。
    /// 参数为本次新导入的歌曲数量。
    /// </summary>
    public static event EventHandler<int>? ScanCompleted;

    /// <summary>
    /// 静态标记：网络音乐库（WebDAV/SMB/Navidrome）同步后库内容已变更，
    /// 网络音乐库卡片、网络 tab 及合并列表需要重新加载。页面在 OnAppearing 时检查并消费此标记。
    /// </summary>
    public static bool NetworkNeedsReload { get; set; }

    /// <summary>
    /// 网络音乐库同步完成事件，当远程连接扫描导入/元数据回填完成后触发。
    /// 参数为本次同步的歌曲数量。供 LibraryPage 据此刷新网络音乐库视图，
    /// 解决"网络音乐有缓存但网络音乐库未同步"的问题。
    /// </summary>
    public static event EventHandler<int>? NetworkSyncCompleted;

    /// <summary>
    /// 时长回填完成事件。扫描/播放时基于性能跳过读 duration 导致部分歌曲 Duration=0（音乐库总时长显示 0.0 小时），
    /// 后台单飞回填完成后触发，供 LibraryViewModel 刷新总时长统计。
    /// </summary>
    public static event EventHandler<int>? DurationBackfillCompleted;

    /// <summary>单飞令牌：0=空闲，1=回填进行中，防止并发重复回填</summary>
    private static int _backfillRunning;

    /// <summary>
    /// 后台单飞回填缺失的歌曲时长。
    /// 扫描时 readDuration:false 仅读标签（避免全文件 IO 卡死），此处用 readDuration:true 读取真实时长并批量回写。
    /// 全文件 IO 较重：限并发≤2 且按批分片慢跑（每批 120 首、批间让出 IO），既不阻塞 UI 也不长时间占满磁盘带宽。
    /// </summary>
    public async Task<bool> BackfillMissingDurationsAsync()
    {
        if (Interlocked.CompareExchange(ref _backfillRunning, 1, 0) != 0)
            return false;

        var updated = 0;
        try
        {
            var songs = await _db.GetLocalSongsMissingDurationAsync();
            if (songs.Count == 0) return true;

            // content:// URI（SAF）无法用文件路径读取，跳过；仅处理真实文件系统路径
            var targets = songs
                .Where(s => !string.IsNullOrEmpty(s.FilePath)
                            && !s.FilePath.StartsWith("content://", StringComparison.OrdinalIgnoreCase))
                .ToList();
            if (targets.Count == 0) return true;

            // 全文件 IO 较重，限并发≤2 后台执行：既避免阻塞 UI，也避免与扫描后的
            // 页面刷新/封面解析抢磁盘 IO 造成"整个 app 卡顿"（此前并发 4 会长时间占满 IO）。
            var degree = Math.Min(2, Math.Max(1, Environment.ProcessorCount));
            var options = new ParallelOptions { MaxDegreeOfParallelism = degree };

            // 分片慢跑：每次只回填一批（batchSize 首）后休眠片刻再继续，把数千首
            // 全文件读取的 IO 压力摊薄平缓，回填期间 app 保持流畅（重启后 30s 错峰
            // 一次性大流量读取正是"等一会儿才不卡"的直接原因）。
            const int batchSize = 120;
            const int batchIdleMs = 6000;
            var processedBatches = 0;
            var totalBatches = (targets.Count + batchSize - 1) / batchSize;
            foreach (var batch in Chunk(targets, batchSize))
            {
                var batchDurations = new ConcurrentDictionary<int, int>();
                Parallel.ForEach(batch, options, s =>
                {
                    try
                    {
                        var song = TagReader.ReadSongInfo(s.FilePath!, readDuration: true);
                        if (song != null && song.Duration > 0)
                            batchDurations[s.Id] = song.Duration;
                    }
                    catch (Exception ex)
                    {
                        Log.Debug("LocalScanService", $"[DurationBackfill] {s.FilePath}: {ex.Message}");
                    }
                });

                if (batchDurations.Count > 0)
                    await _db.UpdateSongDurationsBatchAsync(batchDurations);
                updated += batchDurations.Count;

                // 批间错峰休眠，让出磁盘 IO；最后一批不再等待
                processedBatches++;
                if (processedBatches < totalBatches)
                    await Task.Delay(batchIdleMs);
            }

            if (updated > 0)
                Log.Debug("LocalScanService", $"[DurationBackfill] 回填 {updated} 首歌曲时长");
        }
        catch (Exception ex)
        {
            Log.Debug("LocalScanService", $"[DurationBackfill] Error: {ex.Message}");
        }
        finally
        {
            Interlocked.Exchange(ref _backfillRunning, 0);
        }

        if (updated > 0)
            DurationBackfillCompleted?.Invoke(null, updated);
        return true;
    }

    /// <summary>按块切分集合（LINQ Chunk 的本地替代，避免引入额外依赖）。</summary>
    private static IEnumerable<List<Song>> Chunk(List<Song> source, int chunkSize)
    {
        for (int i = 0; i < source.Count; i += chunkSize)
            yield return source.GetRange(i, Math.Min(chunkSize, source.Count - i));
    }

    /// <summary>触发后台时长回填（不等待完成，单飞防重入），供扫描完成后 / App 启动时调用。
    /// delaySeconds&gt;0 时延迟启动，与扫描后的页面刷新/封面解析等错峰，避免全文件读取抢光磁盘 IO。</summary>
    public void TriggerDurationBackfill(int delaySeconds = 0)
    {
        if (delaySeconds <= 0)
        {
            _ = BackfillMissingDurationsAsync();
            return;
        }
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(delaySeconds));
                await BackfillMissingDurationsAsync();
            }
            catch (Exception ex) { Log.Debug("LocalScanService", $"[DurationBackfill] 延迟启动失败: {ex.Message}"); }
        });
    }

    /// <summary>
    /// 标记网络音乐库已变更并触发 <see cref="NetworkSyncCompleted"/> 事件。
    /// 因 C# 事件仅能在声明类型内部引发，故提供此静态方法供外部（如同步页）调用。
    /// </summary>
    /// <param name="count">本次同步的歌曲数量</param>
    public static void NotifyNetworkSyncCompleted(int count)
    {
        NetworkNeedsReload = true;
        NetworkSyncCompleted?.Invoke(null, count);
    }

    /// <summary>支持的音频文件扩展名集合</summary>
    private static readonly HashSet<string> AudioExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mp3", ".flac", ".wav", ".m4a", ".aac", ".ogg", ".wma", ".ape", ".opus",
        ".m4b", ".mp4", ".alac", ".aiff", ".aif", ".wv", ".oga", ".tta", ".mka"
    };

    /// <summary>构造函数</summary>
    public LocalScanService(IMusicLibraryService musicLibrary, MusicDatabase db, MusicLibrarySnapshotService snapshotService)
    {
        _musicLibrary = musicLibrary;
        _db = db;
        _snapshotService = snapshotService;
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

            // 各来源并行扫描：MediaStore（系统查询）、SAF（content URI）、自定义文件夹
            // （TagLib 读文件）互不依赖，串行执行时总耗时 = 各段之和；并行后 ≈ 最慢段。
            // 每段各自收集到局部集合（无共享写），完成后统一合并去重。
            // 进度用加权平均：每段占 1/totalSteps 权重，段内进度 0~1。
            var stepProgress = new double[totalSteps];
            var stepLock = new object();
            int globalPctOf(double[] slots) => (int)(90.0 * slots.Sum() / totalSteps);

            void ReportStepProgress(int step, double localRatio, string status)
            {
                lock (stepLock)
                {
                    if (localRatio > stepProgress[step]) stepProgress[step] = localRatio;
                    progress?.Report((globalPctOf(stepProgress), 100, status));
                }
            }

            var scanTasks = new List<Task>();
            var stepIndex = 0;

            // 1. MediaStore 扫描
            if (useMediaStore)
            {
                var myStep = stepIndex++;
                scanTasks.Add(Task.Run(async () =>
                {
                    ReportStepProgress(myStep, 0, "正在通过系统媒体库扫描...");
#if ANDROID
                    try
                    {
                        var mediaStoreSongs = await Task.Run(() =>
                            Platforms.Android.AndroidMediaScanner.ScanFromMediaStore(), cancellationToken);
                        foreach (var s in mediaStoreSongs)
                            allSongs.Add(s);
                        ReportStepProgress(myStep, 1, $"媒体库扫描完成，发现 {mediaStoreSongs.Count} 首歌曲");
                    }
                    catch (Exception ex)
                    {
                        Log.Debug("LocalScanService", $"[LocalScan] MediaStore error: {ex.Message}");
                        ReportStepProgress(myStep, 1, "媒体库扫描失败");
                    }
#endif
                    ReportStepProgress(myStep, 1, "系统媒体库扫描完成");
                }, cancellationToken));
            }

            // 2. SAF 文件夹扫描
            if (useSafScan && hasSafFolders)
            {
                var myStep = stepIndex++;
                scanTasks.Add(Task.Run(async () =>
                {
                    ReportStepProgress(myStep, 0, "正在通过 SAF 扫描已选文件夹...");
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
                            new Progress<(int done, int total, string s)>(p =>
                            {
                                var localRatio = p.total > 0 ? (double)p.done / p.total : 0;
                                ReportStepProgress(myStep, localRatio, $"{p.s} (已发现 {safSongs.Count} 首)");
                            }),
                            existingModTimes,
                            null
                        );
                        foreach (var s in safSongs)
                            allSongs.Add(s);
                        ReportStepProgress(myStep, 1, $"SAF 扫描完成，发现 {safSongs.Count} 首歌曲");
                    }
                    catch (Exception ex)
                    {
                        Log.Debug("LocalScanService", $"[LocalScan] SAF error: {ex.Message}");
                        ReportStepProgress(myStep, 1, "SAF 扫描失败");
                    }
#endif
                }, cancellationToken));
            }

            // 3. 自定义文件夹扫描（逐文件读取元数据，内部报告渐进进度）
            if (hasCustomFolders)
            {
                var myStep = stepIndex++;
                scanTasks.Add(Task.Run(async () =>
                {
                    try
                    {
                        ReportStepProgress(myStep, 0, "正在扫描自定义文件夹...");
                        var seenPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                        var allFilePaths = new List<string>();

                        ReportStepProgress(myStep, 0.03, "正在枚举文件...");
                        foreach (var folder in customFolders)
                        {
                            Log.Debug("LocalScanService", $"[LocalScan] 自定义文件夹: '{folder}', Directory.Exists={Directory.Exists(folder)}");
                            if (!Directory.Exists(folder)) continue;
                            try
                            {
                                var filePaths = MusicUtility.ScanFolderRecursive(folder);
                                Log.Debug("LocalScanService", $"[LocalScan] 文件夹 '{folder}' 递归发现音频文件数: {filePaths.Count}");
                                foreach (var path in filePaths)
                                {
                                    if (seenPaths.Add(path))
                                        allFilePaths.Add(path);
                                }
                            }
                            catch (Exception ex)
                            {
                                Log.Debug("LocalScanService", $"[LocalScan] Scan folder error: {folder}, {ex.Message}");
                            }
                        }

                        var totalFiles = allFilePaths.Count;
                        // 并发收集，避免并行循环内向 List 加锁
                        var customSongs = new ConcurrentBag<Song>();
                        var processed = 0;

                        await Task.Run(() =>
                        {
                            // 并行度上限 8，充分利用八核 CPU
                            var degree = Math.Min(8, Math.Max(2, Environment.ProcessorCount));
                            var options = new ParallelOptions
                            {
                                MaxDegreeOfParallelism = degree,
                                CancellationToken = cancellationToken
                            };
                            Parallel.ForEach(allFilePaths, options, path =>
                            {
                                try
                                {
                                    // readDuration: false —— TagLib 读 duration 需全文件 IO（VBR/大 flac），
                                    // 1000 首会卡死；时长由播放器播放时回填
                                    var song = TagReader.ReadSongInfo(path, readDuration: false);
                                    if (song != null)
                                    {
                                        song.Source = SongSource.Local;
                                        customSongs.Add(song);
                                    }
                                }
                                catch (Exception ex)
                                {
                                    Log.Debug("LocalScanService", $"[LocalScan] ReadSongInfo error: {path}, {ex.Message}");
                                }
                                // 原子自增进度
                                var p = Interlocked.Increment(ref processed);
                                // 每 5 个文件或最后一个文件报告一次进度，避免过于频繁
                                if (p % 5 == 0 || p == totalFiles)
                                {
                                    var localRatio = totalFiles > 0 ? (double)p / totalFiles : 0;
                                    ReportStepProgress(myStep, localRatio, $"读取元数据 {p}/{totalFiles} (已发现 {customSongs.Count} 首)");
                                }
                            });
                        }, cancellationToken);

                        foreach (var s in customSongs)
                            allSongs.Add(s);
                        ReportStepProgress(myStep, 1, $"自定义文件夹扫描完成，发现 {customSongs.Count} 首歌曲");
                    }
                    catch (Exception ex)
                    {
                        Log.Debug("LocalScanService", $"[LocalScan] Custom folders error: {ex.Message}");
                        ReportStepProgress(myStep, 1, "自定义文件夹扫描失败");
                    }
                }, cancellationToken));
            }

            // 并行等待全部扫描来源完成（MediaStore / SAF / 自定义文件夹）
            await Task.WhenAll(scanTasks);

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
                        Log.Debug("LocalScanService", $"[LocalScan] 清理了 {removedCount} 首过时本地歌曲");
                    }
                }
                catch (Exception cex)
                {
                    Log.Debug("LocalScanService", $"[LocalScan] Cleanup stale songs error: {cex.Message}");
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
            Log.Debug("LocalScanService", $"[LocalScan] Error: {ex}");
            progress?.Report((0, 100, $"扫描失败: {ex.Message}"));
        }

        // 标记库已变更，通知发现页等需要重新加载
        if (totalImported > 0)
        {
            NeedsReload = true;
            _ = _snapshotService.GenerateSnapshotAsync(_db);
            ScanCompleted?.Invoke(null, totalImported);
        }

        // 扫描导入的新歌时长缺失（readDuration:false），后台单飞回填以修正音乐库总时长统计。
        // ⚠ 延迟 25s 错峰：扫描刚完成时页面刷新/封面解析正密集读盘，立即启动几千首全文件
        // 读取会与这些 IO 抢带宽，导致"扫描完整个 app 特别卡顿"。
        TriggerDurationBackfill(delaySeconds: 25);

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
