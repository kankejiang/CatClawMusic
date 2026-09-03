using System.Collections.ObjectModel;
using System.Text.Json;
using CatClawMusic.Core.Interfaces;
using CommunityToolkit.Mvvm.ComponentModel;

namespace CatClawMusic.Maui.Services;

/// <summary>下载任务状态</summary>
public enum DownloadStatus
{
    /// <summary>排队等待</summary>
    Queued,
    /// <summary>正在下载</summary>
    Downloading,
    /// <summary>已暂停（可恢复）</summary>
    Paused,
    /// <summary>已完成</summary>
    Completed,
    /// <summary>下载失败</summary>
    Failed,
    /// <summary>已取消</summary>
    Canceled
}

/// <summary>
/// 下载任务项：一个待下载/正在下载/已完成的文件任务。
/// 持久化字段由 DownloadManager 序列化；运行时的进度/状态以 INPC 驱动 UI 刷新。
/// </summary>
public partial class DownloadTaskItem : ObservableObject
{
    /// <summary>任务唯一 ID</summary>
    public string Id { get; init; } = Guid.NewGuid().ToString("N");

    /// <summary>展示名称（文件显示名）</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>源地址（HTTP URL；网络歌曲为协议标识/远程路径）</summary>
    public string Url { get; set; } = string.Empty;

    /// <summary>本地保存路径（完成后为最终文件路径，下载中为 .part 临时文件路径）</summary>
    public string LocalPath { get; set; } = string.Empty;

    /// <summary>任务类型：url=普通 URL 下载；network=网络歌曲下载</summary>
    public string Kind { get; set; } = "url";

    /// <summary>创建时间（Unix 秒）</summary>
    public long CreatedAt { get; set; }

    [ObservableProperty]
    private long _totalBytes;

    [ObservableProperty]
    private long _downloadedBytes;

    [ObservableProperty]
    private DownloadStatus _status = DownloadStatus.Queued;

    [ObservableProperty]
    private string _error = string.Empty;

    /// <summary>瞬时下载速度展示文本（如 1.2 MB/s），运行时字段</summary>
    [ObservableProperty]
    private string _speedText = string.Empty;

    /// <summary>是否暂停中（运行时字段）</summary>
    [ObservableProperty]
    private bool _isPaused;

    /// <summary>展示名称回退：无文件名时显示 URL</summary>
    public string DisplayName => string.IsNullOrWhiteSpace(Name) ? Url : Name;

    /// <summary>进度（0-1，MAUI ProgressBar 范围）</summary>
    public double Progress => TotalBytes > 0 ? Math.Clamp((double)DownloadedBytes / TotalBytes, 0, 1) : 0;

    /// <summary>状态展示文本</summary>
    public string StatusText => Status switch
    {
        DownloadStatus.Queued => "排队中",
        DownloadStatus.Downloading => "下载中",
        DownloadStatus.Paused => "已暂停",
        DownloadStatus.Completed => "已完成",
        DownloadStatus.Failed => $"失败：{Error}",
        DownloadStatus.Canceled => "已取消",
        _ => ""
    };

    /// <summary>状态颜色</summary>
    public string StatusColor => Status switch
    {
        DownloadStatus.Completed => "#4CAF50",
        DownloadStatus.Failed => "#F44336",
        DownloadStatus.Downloading or DownloadStatus.Queued => "#8C7BFF",
        DownloadStatus.Paused or DownloadStatus.Canceled => "#9E9E9E",
        _ => "#9E9E9E"
    };

    /// <summary>已下载/总大小展示文本</summary>
    public string SizeText => TotalBytes > 0
        ? $"{FormatBytes(DownloadedBytes)} / {FormatBytes(TotalBytes)}"
        : FormatBytes(DownloadedBytes);

    /// <summary>已下载大小 + 速度展示文本</summary>
    public string DownloadingText => string.IsNullOrWhiteSpace(SpeedText)
        ? SizeText
        : $"{SizeText} · {SpeedText}";

    /// <summary>保存路径展示文本</summary>
    public string PathText => string.IsNullOrWhiteSpace(LocalPath) ? "" : LocalPath;

    /// <summary>完成时间文本（运行时字段，持久化恢复后重新计算）</summary>
    public string CompletedText => Status == DownloadStatus.Completed ? $"保存至 {LocalPath}" : "";

    /// <summary>批量刷新计算属性（供 DownloadManager 在进度/状态变化后调用）</summary>
    public void RaiseDerivedChanged()
    {
        OnPropertyChanged(nameof(Progress));
        OnPropertyChanged(nameof(SizeText));
        OnPropertyChanged(nameof(DownloadingText));
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(StatusColor));
        OnPropertyChanged(nameof(CompletedText));
    }

    public static string FormatBytes(long bytes)
    {
        if (bytes >= 1073741824) return $"{bytes / 1073741824.0:F1} GB";
        if (bytes >= 1048576) return $"{bytes / 1048576.0:F1} MB";
        if (bytes >= 1024) return $"{bytes / 1024.0:F1} KB";
        return $"{bytes} B";
    }
}

/// <summary>
/// 下载管理器：维护下载任务队列（支持 URL 直下与网络歌曲流式下载），
/// 提供并发控制、进度回调、暂停/继续/取消/删除、任务持久化与下载路径设置。
/// </summary>
public class DownloadManager : IDisposable, IDownloadManager
{
    /// <summary>下载路径默认值（Android 外部存储根目录）</summary>
    public const string DefaultFolderPath = "/storage/emulated/0/CatClawMusic";

    /// <summary>下载路径偏好键</summary>
    public const string PrefKey = "download_folder_path";

    /// <summary>最大并发下载任务数偏好键</summary>
    public const string ConcurrentPrefKey = "download_max_concurrent";
    /// <summary>每任务下载限速偏好键（KB/s，0=不限）</summary>
    public const string SpeedPrefKey = "download_max_speed_kbps";

    /// <summary>默认最大并发下载任务数</summary>
    public const int DefaultConcurrent = 2;
    /// <summary>并发上限（设置面板可选最小值~最大值；信号量容量取上限值）</summary>
    public const int ConcurrentMin = 1;
    public const int ConcurrentMax = 5;

    private static readonly string TasksFilePath =
        Path.Combine(FileSystem.AppDataDirectory, "download_tasks.json");

    private readonly HttpClient _http;
    /// <summary>排队任务等待"并发槽位空出"的通知信号（0 初始许可；ReleaseSlot 每次释放一个许可，无固定轮询）</summary>
    private readonly SemaphoreSlim _slotWake = new(0);
    private readonly Dictionary<string, CancellationTokenSource> _ctsMap = new();
    private readonly Dictionary<string, Func<CancellationToken, Task<Stream?>>> _networkProviders = new();
    private readonly object _lock = new();
    /// <summary>当前实际并发下载数（配对 <see cref="AcquireSlotAsync"/> / <see cref="ReleaseSlot"/>）</summary>
    private int _active;
    private bool _disposed;

    /// <summary>当前最大并发下载任务数（可运行时调整，立即生效）</summary>
    public int ConcurrentLimit { get; private set; } = DefaultConcurrent;

    /// <summary>每任务下载限速（字节/秒，0=不限）。设置后立即生效，仅作用于新下载的数据块。</summary>
    public long MaxDownloadBytesPerSecond { get; private set; }

    /// <summary>磁力（BT）下载引擎懒工厂（DI 注入；首次真正使用磁力功能时才构造 ClientEngine——端口监听+DHT 启动，冷启动零开销。测试环境可为 null）</summary>
    private readonly Func<BitTorrentDownloadService?>? _btFactory;

    /// <summary>解析 BT 引擎（懒构造；同一单例重复解析开销可忽略）</summary>
    private BitTorrentDownloadService? Bt => _btFactory?.Invoke();

    /// <summary>下载任务集合（按创建时间排序）</summary>
    public ObservableCollection<DownloadTaskItem> Tasks { get; } = new();

    /// <summary>任务集合变化（增删）时触发</summary>
    public event Action? TasksChanged;

    /// <summary>单个任务进度/状态变化时触发</summary>
    public event Action<DownloadTaskItem>? TaskUpdated;

    public DownloadManager(Func<BitTorrentDownloadService?>? btFactory = null)
    {
        _btFactory = btFactory;
        _http = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
        _http.DefaultRequestHeaders.Add("User-Agent", "CatClawMusic/1.0");
        ConcurrentLimit = LoadConcurrentLimit();
        MaxDownloadBytesPerSecond = LoadSpeedLimit();
        LoadTasks();
    }

    /// <summary>读取并发数偏好并夹取到 [1,5]</summary>
    private static int LoadConcurrentLimit()
    {
        var v = Preferences.Default.Get(ConcurrentPrefKey, DefaultConcurrent);
        return Math.Clamp(v, ConcurrentMin, ConcurrentMax);
    }

    /// <summary>读取每任务限速偏好（KB/s→字节/秒），非法值按 0（不限）</summary>
    private static long LoadSpeedLimit()
    {
        var kbps = Preferences.Default.Get(SpeedPrefKey, 0);
        return kbps > 0 ? kbps * 1024L : 0;
    }

    /// <summary>设置并发数上限（立即生效，并持久化）。提高并发时唤醒排队任务使其立即自动开始。</summary>
    public void SetConcurrentLimit(int count)
    {
        var v = Math.Clamp(count, ConcurrentMin, ConcurrentMax);
        int delta;
        lock (_lock)
        {
            delta = v - ConcurrentLimit;
            ConcurrentLimit = v;
        }
        if (delta > 0)
        {
            // 提升并发上限：补发对应数量的"槽位空出"通知，唤醒阻塞中的排队任务
            try { _slotWake.Release(delta); } catch (SemaphoreFullException) { }
        }
        Preferences.Default.Set(ConcurrentPrefKey, v);
    }

    /// <summary>设置每任务限速（KB/s；0=不限）。立即生效并持久化。</summary>
    public void SetSpeedLimitKbps(int kbps)
    {
        var safe = Math.Max(0, kbps);
        MaxDownloadBytesPerSecond = safe > 0 ? safe * 1024L : 0;
        Preferences.Default.Set(SpeedPrefKey, safe);
    }

    /// <summary>获取下载目录：优先用户设置，否则平台默认值</summary>
    public static string GetDownloadFolderPath()
    {
        var saved = Preferences.Default.Get(PrefKey, string.Empty);
        if (!string.IsNullOrWhiteSpace(saved)) return saved;
#if ANDROID
        return DefaultFolderPath;
#else
        var music = Environment.GetFolderPath(Environment.SpecialFolder.MyMusic);
        return string.IsNullOrWhiteSpace(music)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "CatClawMusic")
            : Path.Combine(music, "CatClawMusic");
#endif
    }

    /// <summary>当前下载目录</summary>
    public string DownloadFolderPath => GetDownloadFolderPath();

    /// <summary>保存下载目录设置</summary>
    public void SetDownloadFolderPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return;
        Preferences.Default.Set(PrefKey, path.TrimEnd('/', '\\'));
    }

    // ═══════════════════════════════════════════════════════
    // 任务创建
    // ═══════════════════════════════════════════════════════

    /// <summary>新建普通 URL 下载任务</summary>
    /// <param name="url">下载地址</param>
    /// <param name="fileName">保存文件名（缺省取 URL 末段）</param>
    public DownloadTaskItem EnqueueUrl(string url, string? fileName = null)
    {
        var name = string.IsNullOrWhiteSpace(fileName)
            ? DeriveFileName(url, "download")
            : SanitizeFileName(fileName);
        var item = CreateTask(url, name, "url");
        _ = RunAsync(item);
        return item;
    }

    /// <summary>IDownloadManager 显式实现：入队并返回任务 ID（插件经 Core 接口访问）。</summary>
    string IDownloadManager.EnqueueUrl(string url, string? fileName) => EnqueueUrl(url, fileName).Id;

    /// <summary>当前任务数（IDownloadManager）。</summary>
    int IDownloadManager.TaskCount => Tasks.Count;

    /// <summary>新建磁力（BT）下载任务：magnet: 链接由内置 BT 引擎下载（DHT/tracker 发现做种者）</summary>
    /// <param name="magnet">magnet: 链接</param>
    /// <param name="displayName">可选展示名（缺省取 magnet dn= 参数或 infohash）</param>
    public DownloadTaskItem EnqueueMagnet(string magnet, string? displayName = null)
    {
        var name = !string.IsNullOrWhiteSpace(displayName)
            ? SanitizeFileName(displayName)
            : DeriveMagnetName(magnet);
        // BT 任务保存到 下载目录/BT/名称/（多文件种子保持目录结构）
        var dir = Path.Combine(DownloadFolderPath, "BT");
        try { Directory.CreateDirectory(dir); } catch { }
        var saveDir = GetUniqueDirPath(Path.Combine(dir, name));

        var item = new DownloadTaskItem
        {
            Name = name,
            Url = magnet,
            Kind = "magnet",
            CreatedAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            LocalPath = saveDir
        };
        AddTask(item);
        _ = RunMagnetAsync(item, magnet, saveDir);
        return item;
    }

    /// <summary>新建网络歌曲下载任务（通过 streamProvider 获取远程音频流后落盘）</summary>
    /// <param name="displayName">展示名（歌曲标题）</param>
    /// <param name="sourceId">源标识（如 subsonic songId / 远程路径），用于展示与重试</param>
    /// <param name="fileName">保存文件名（含扩展名）</param>
    /// <param name="streamProvider">打开远程音频流的委托（每次执行下载时调用）</param>
    public DownloadTaskItem EnqueueStream(string displayName, string sourceId, string fileName,
        Func<CancellationToken, Task<Stream?>> streamProvider)
    {
        var item = CreateTask(sourceId, SanitizeFileName(fileName), "network");
        item.Name = string.IsNullOrWhiteSpace(displayName) ? Path.GetFileNameWithoutExtension(fileName) : displayName;
        _ = RunStreamAsync(item, streamProvider);
        return item;
    }

    private DownloadTaskItem CreateTask(string url, string fileName, string kind)
    {
        var dir = DownloadFolderPath;
        try { Directory.CreateDirectory(dir); } catch { }

        var item = new DownloadTaskItem
        {
            Name = fileName,
            Url = url,
            Kind = kind,
            CreatedAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            LocalPath = Path.Combine(dir, fileName)
        };
        // 文件名冲突时自动追加序号
        item.LocalPath = GetUniquePath(item.LocalPath);
        item.Name = Path.GetFileName(item.LocalPath);

        AddTask(item);
        return item;
    }

    // ═══════════════════════════════════════════════════════
    // 任务控制
    // ═══════════════════════════════════════════════════════

    /// <summary>暂停任务（中断本次下载，保留任务与已下字节数，恢复后从头续传）</summary>
    public void Pause(string id)
    {
        lock (_lock)
        {
            if (_ctsMap.TryGetValue(id, out var cts))
                cts.Cancel();
        }
        var task = Find(id);
        if (task == null) return;
        if (task.Kind == "magnet")
        {
            _ = Bt?.PauseAsync(id);
            if (task.Status == DownloadStatus.Downloading)
            {
                UpdateTask(task, t => { t.Status = DownloadStatus.Paused; t.IsPaused = true; t.SpeedText = ""; });
            }
            return;
        }
        if (task.Status == DownloadStatus.Downloading)
        {
            UpdateTask(task, t => { t.Status = DownloadStatus.Paused; t.IsPaused = true; t.SpeedText = ""; });
        }
    }

    /// <summary>继续任务（重新发起下载，清除旧进度）</summary>
    public void Resume(string id)
    {
        var task = Find(id);
        if (task == null || task.Status != DownloadStatus.Paused) return;
        if (task.Kind == "magnet")
        {
            UpdateTask(task, t => { t.Status = DownloadStatus.Queued; t.IsPaused = false; t.Error = ""; });
            _ = Bt?.ResumeAsync(id);
            return;
        }
        UpdateTask(task, t =>
        {
            t.Status = DownloadStatus.Queued;
            t.IsPaused = false;
            t.Error = "";
        });
        if (task.Kind == "network")
        {
            var provider = _networkProviders.ContainsKey(id) ? _networkProviders[id] : null;
            if (provider != null) _ = RunStreamAsync(task, provider);
            else MarkFailed(task, "缺少下载源");
        }
        else
        {
            _ = RunAsync(task);
        }
    }

    /// <summary>取消任务（删除临时文件，任务标记已取消）</summary>
    public void Cancel(string id)
    {
        lock (_lock)
        {
            if (_ctsMap.TryGetValue(id, out var cts))
                cts.Cancel();
        }
        var task = Find(id);
        if (task == null) return;
        DeletePartFile(task);
        UpdateTask(task, t =>
        {
            t.Status = DownloadStatus.Canceled;
            t.IsPaused = false;
            t.SpeedText = "";
        });
    }

    /// <summary>删除任务记录；deleteFile=true 时同时删除已下载文件。
    /// 返回 null=删除成功；否则返回失败原因（如文件被占用），任务记录仍会被移除。</summary>
    public string? Delete(string id, bool deleteFile = false)
    {
        var task = Find(id);
        lock (_lock)
        {
            if (_ctsMap.TryGetValue(id, out var cts)) { cts.Cancel(); cts.Dispose(); _ctsMap.Remove(id); }
            _networkProviders.Remove(id);
        }
        if (task?.Kind == "magnet")
            _ = Bt?.RemoveAsync(id);

        string? fileError = null;
        if (task != null)
        {
            DeletePartFile(task);
            var target = task.LocalPath;
            if (deleteFile && !string.IsNullOrEmpty(target))
            {
                // BT 任务是目录（多文件种子），HTTP 任务是文件
                var isDir = task.Kind == "magnet" && Directory.Exists(target);
                if (isDir)
                {
                    try { Directory.Delete(target, recursive: true); }
                    catch (Exception ex)
                    {
                        fileError = ex is IOException
                            ? "文件夹正被占用（可能正在播放），请先停止播放后再试"
                            : $"删除失败：{ex.Message}";
                        Log.Debug("DownloadManager", $"[Download] 删除 BT 目录失败: {target} - {ex.Message}");
                    }
                }
                else if (File.Exists(target))
                {
                    try { File.Delete(target); }
                    catch (Exception ex)
                    {
                        fileError = ex is IOException
                            ? "文件正被占用（可能正在播放），请先停止播放后再试"
                            : $"文件删除失败：{ex.Message}";
                        Log.Debug("DownloadManager", $"[Download] 删除文件失败: {target} - {ex.Message}");
                    }
                }
            }
            MainThread.BeginInvokeOnMainThread(() => Tasks.Remove(task));
            SaveTasks();
            TasksChanged?.Invoke();
        }
        return fileError;
    }

    /// <summary>失败任务重试 / 已完成任务重新下载</summary>
    public void Retry(string id)
    {
        var task = Find(id);
        if (task == null || task.Status is not (DownloadStatus.Failed or DownloadStatus.Completed)) return;
        // 已完成任务触发的是"重新下载"：清除旧结果与临时文件后从头下载（磁力已完成目录不重下，避免覆盖）
        if (task.Status == DownloadStatus.Completed)
        {
            if (task.Kind == "magnet") return;
            DeletePartFile(task);
            try { if (File.Exists(task.LocalPath)) File.Delete(task.LocalPath); } catch { }
        }
        UpdateTask(task, t =>
        {
            t.Status = DownloadStatus.Queued;
            t.Error = "";
            t.DownloadedBytes = 0;
        });
        if (task.Kind == "magnet")
        {
            _ = RunMagnetAsync(task, task.Url, task.LocalPath);
        }
        else if (task.Kind == "network")
        {
            var provider = _networkProviders.ContainsKey(id) ? _networkProviders[id] : null;
            if (provider != null) _ = RunStreamAsync(task, provider);
            else MarkFailed(task, "缺少下载源");
        }
        else
        {
            _ = RunAsync(task);
        }
    }

    // ═══════════════════════════════════════════════════════
    // 内部执行
    // ═══════════════════════════════════════════════════════

    /// <summary>磁力任务执行：委托 BT 引擎下载，回调更新任务进度/状态</summary>
    private async Task RunMagnetAsync(DownloadTaskItem task, string magnet, string saveDir)
    {
        var bt = Bt;
        if (bt == null)
        {
            MarkFailed(task, "磁力下载引擎未初始化");
            return;
        }
        try
        {
            UpdateTask(task, t => { t.Status = DownloadStatus.Downloading; t.SpeedText = ""; });
            var error = await bt.StartAsync(task.Id, magnet, saveDir,
                onState: text => UpdateTask(task, t => { if (t.Status != DownloadStatus.Completed) t.Error = text == "下载中" ? "" : text; }),
                onProgress: p => UpdateTask(task, t =>
                {
                    if (t.TotalBytes > 0) t.DownloadedBytes = (long)(t.TotalBytes * p / 100.0);
                }),
                onBytes: (downloaded, total) => UpdateTask(task, t =>
                {
                    t.DownloadedBytes = downloaded;
                    t.TotalBytes = total;
                }),
                onComplete: () =>
                {
                    if (IsTerminal(task)) return;
                    UpdateTask(task, t =>
                    {
                        t.Status = DownloadStatus.Completed;
                        t.DownloadedBytes = t.TotalBytes;
                        t.SpeedText = "";
                    });
                    SaveTasks();
                });
            if (error != null)
            {
                MarkFailed(task, error);
            }
        }
        catch (Exception ex)
        {
            MarkFailed(task, ex.Message);
        }
    }

    /// <summary>从 magnet 链接提取展示名（dn= 参数优先，否则取 infohash）</summary>
    private static string DeriveMagnetName(string magnet)
    {
        try
        {
            var q = magnet.Contains('?') ? magnet[(magnet.IndexOf('?') + 1)..] : "";
            foreach (var part in q.Split('&'))
            {
                if (part.StartsWith("dn=", StringComparison.OrdinalIgnoreCase))
                {
                    var dn = Uri.UnescapeDataString(part[3..]).Trim();
                    if (!string.IsNullOrWhiteSpace(dn)) return SanitizeFileName(dn);
                }
            }
            var btih = System.Text.RegularExpressions.Regex.Match(magnet, @"btih:([0-9a-fA-F]{40})").Groups[1].Value;
            if (!string.IsNullOrEmpty(btih)) return btih[..12];
        }
        catch { }
        return "bt-download";
    }

    /// <summary>目标目录已存在时追加序号（BT 任务同名目录不覆盖）</summary>
    private static string GetUniqueDirPath(string path)
    {
        if (!Directory.Exists(path)) return path;
        for (int i = 1; ; i++)
        {
            var candidate = $"{path} ({i})";
            if (!Directory.Exists(candidate)) return candidate;
        }
    }

    private async Task RunAsync(DownloadTaskItem task)
    {
        var cts = new CancellationTokenSource();
        lock (_lock) _ctsMap[task.Id] = cts;
        var reserved = false;
        try
        {
            // 排队阶段即注册 cts：排队/等待 slot 的任务也能被取消，暂停同样生效
            await AcquireSlotAsync(cts.Token).ConfigureAwait(false);
            reserved = true;
            if (_disposed || IsTerminal(task)) return;

            UpdateTask(task, t => { t.Status = DownloadStatus.Downloading; t.IsPaused = false; });
            try
            {
                await DownloadFromUrlAsync(task, cts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // 取消/暂停由控制方法处理状态
            }
            catch (Exception ex)
            {
                if (task.Status == DownloadStatus.Downloading)
                    MarkFailed(task, ex.Message);
            }
        }
        finally
        {
            if (reserved) ReleaseSlot();
            lock (_lock) { _ctsMap.Remove(task.Id); cts.Dispose(); }
        }
    }

    private async Task RunStreamAsync(DownloadTaskItem task, Func<CancellationToken, Task<Stream?>> provider)
    {
        if (provider == null) { MarkFailed(task, "缺少下载源"); return; }
        lock (_lock) _networkProviders[task.Id] = provider;

        var cts = new CancellationTokenSource();
        lock (_lock) _ctsMap[task.Id] = cts;
        var reserved = false;
        try
        {
            await AcquireSlotAsync(cts.Token).ConfigureAwait(false);
            reserved = true;
            if (_disposed || IsTerminal(task)) return;

            UpdateTask(task, t => { t.Status = DownloadStatus.Downloading; t.IsPaused = false; });
            try
            {
                Stream? stream = null;
                try
                {
                    stream = await provider(cts.Token).ConfigureAwait(false);
                    if (stream == null) throw new Exception("无法打开远程音频流");
                    await WriteStreamToFileAsync(task, stream, cts.Token).ConfigureAwait(false);
                }
                finally { stream?.Dispose(); }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                if (task.Status == DownloadStatus.Downloading)
                    MarkFailed(task, ex.Message);
            }
        }
        finally
        {
            if (reserved) ReleaseSlot();
            lock (_lock) { _ctsMap.Remove(task.Id); cts.Dispose(); }
        }
    }

    /// <summary>获取一个下载并发槽位。并发未满时立即返回；已满则等待"槽位空出"通知，
    /// 由 <see cref="ReleaseSlot"/> 或并发上限提升时唤醒，实现排队任务即时自动开始（无固定轮询）。</summary>
    private async Task AcquireSlotAsync(CancellationToken ct)
    {
        while (true)
        {
            lock (_lock)
            {
                if (_active < ConcurrentLimit)
                {
                    _active++;
                    return;
                }
            }
            await _slotWake.WaitAsync(ct).ConfigureAwait(false);
        }
    }

    private void ReleaseSlot()
    {
        lock (_lock) _active--;
        // 释放一个空位许可，唤醒一个等待中的排队任务立即开始
        try { _slotWake.Release(1); } catch (SemaphoreFullException) { }
    }

    /// <summary>HTTP 下载：先探测总大小，再流式写入临时文件。存在有效 .part 时优先用 Range 断点续传。</summary>
    private async Task DownloadFromUrlAsync(DownloadTaskItem task, CancellationToken ct)
    {
        var partPath = task.LocalPath + ".part";
        // 存在有效 .part（暂停遗留）时从断点续传，服务器需支持 HTTP Range
        long resumeFrom = 0;
        if (File.Exists(partPath))
        {
            try { resumeFrom = new FileInfo(partPath).Length; } catch { resumeFrom = 0; }
            if (resumeFrom <= 0) DeletePartFile(task);
        }

        using (var req = new HttpRequestMessage(HttpMethod.Get, task.Url))
        {
            if (resumeFrom > 0)
                req.Headers.TryAddWithoutValidation("Range", $"bytes={resumeFrom}-");

            using var resp = await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);

            if (resp.StatusCode == System.Net.HttpStatusCode.PartialContent)
            {
                // 服务器支持续传：追加写入 .part
                resp.EnsureSuccessStatusCode();
                var total = resp.Content.Headers.ContentRange?.Length ??
                            resp.Content.Headers.ContentLength ?? resumeFrom;
                UpdateTask(task, t => { t.TotalBytes = total; t.DownloadedBytes = resumeFrom; });
                await using var src = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
                await using var dst = new FileStream(partPath, FileMode.Append, FileAccess.Write);
                await CopyStreamAsync(task, src, dst, total, ct, start: resumeFrom).ConfigureAwait(false);
            }
            else
            {
                // 服务器不支持 Range：从头下载
                resp.EnsureSuccessStatusCode();
                DeletePartFile(task);
                var total = resp.Content.Headers.ContentLength ?? -1;
                UpdateTask(task, t => t.TotalBytes = total);
                await using var src = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
                await using var dst = new FileStream(partPath, FileMode.Create, FileAccess.Write);
                await CopyStreamAsync(task, src, dst, total, ct).ConfigureAwait(false);
            }
        }

        // 取消/暂停时保留 .part 供后续续传；正常完成才落盘为最终文件
        if (ct.IsCancellationRequested) return;
        FinalizeDownload(task, partPath);
    }

    /// <summary>把远程流写入临时文件，完成时落盘为最终文件</summary>
    private async Task WriteStreamToFileAsync(DownloadTaskItem task, Stream src, CancellationToken ct)
    {
        var partPath = task.LocalPath + ".part";
        DeletePartFile(task);

        await using var dst = new FileStream(partPath, FileMode.Create, FileAccess.Write);
        await CopyStreamAsync(task, src, dst, -1, ct).ConfigureAwait(false);

        if (ct.IsCancellationRequested)
        {
            DeletePartFile(task);
            return;
        }
        FinalizeDownload(task, partPath);
    }

    /// <summary>流式拷贝：进度/速度按 ~500ms 节流上报主线程，并按 <see cref="MaxDownloadBytesPerSecond"/> 限速。</summary>
    private async Task CopyStreamAsync(DownloadTaskItem task, Stream src, FileStream dst, long total, CancellationToken ct, long start = 0)
    {
        var buffer = new byte[81920];
        long read = start;
        long lastTick = Environment.TickCount64;
        long lastBytes = start;
        // 首次上报已下字节（断点续传时 >0），让进度条立即反映
        UpdateTask(task, t => { if (total > 0) t.TotalBytes = total; t.DownloadedBytes = read; });

        // 限速记账（滑动窗口累加已写字节，超出配额时 sleep 压制），限速可运行时即时生效
        long limWindowStart = Environment.TickCount64;
        long limBytes = 0;

        int n;
        while ((n = await src.ReadAsync(buffer, 0, buffer.Length, ct).ConfigureAwait(false)) > 0)
        {
            await dst.WriteAsync(buffer, 0, n, ct).ConfigureAwait(false);
            read += n;

            var speedLimit = MaxDownloadBytesPerSecond;
            if (speedLimit > 0)
            {
                limBytes += n;
                var now = Environment.TickCount64;
                var elapsed = now - limWindowStart;
                if (elapsed >= 1000L)
                {
                    limWindowStart = now;
                    limBytes = n;
                }
                else if (limBytes > speedLimit * elapsed / 1000.0)
                {
                    var over = limBytes - speedLimit * elapsed / 1000.0;
                    var sleepMs = (int)(over * 1000.0 / speedLimit) + 1;
                    await Task.Delay(Math.Min(sleepMs, 500), ct).ConfigureAwait(false);
                }
            }

            if (Environment.TickCount64 - lastTick >= 500)
            {
                var elapsedSec = (Environment.TickCount64 - lastTick) / 1000.0;
                var speed = (read - lastBytes) / Math.Max(elapsedSec, 0.001);
                var speedText = $"{DownloadTaskItem.FormatBytes((long)speed)}/s";
                UpdateTask(task, t =>
                {
                    if (total > 0) t.TotalBytes = total;
                    t.DownloadedBytes = read;
                    t.SpeedText = speedText;
                });
                lastTick = Environment.TickCount64;
                lastBytes = read;
            }
        }
        UpdateTask(task, t => { t.DownloadedBytes = read; t.SpeedText = ""; });
    }

    private void FinalizeDownload(DownloadTaskItem task, string partPath)
    {
        try
        {
            if (File.Exists(task.LocalPath)) File.Delete(task.LocalPath);
            File.Move(partPath, task.LocalPath);
        }
        catch (Exception ex)
        {
            DeletePartFile(task);
            MarkFailed(task, $"写入文件失败：{ex.Message}");
            return;
        }
        UpdateTask(task, t =>
        {
            t.Status = DownloadStatus.Completed;
            t.SpeedText = "";
            try { t.TotalBytes = new FileInfo(task.LocalPath).Length; } catch { }
            t.DownloadedBytes = t.TotalBytes;
        });
        SaveTasks();
        TasksChanged?.Invoke();
    }

    private void MarkFailed(DownloadTaskItem task, string message)
    {
        DeletePartFile(task);
        UpdateTask(task, t => { t.Status = DownloadStatus.Failed; t.Error = message; t.SpeedText = ""; });
        SaveTasks();
        TasksChanged?.Invoke();
    }

    // ═══════════════════════════════════════════════════════
    // 工具
    // ═══════════════════════════════════════════════════════

    private void AddTask(DownloadTaskItem item)
    {
        MainThread.BeginInvokeOnMainThread(() => Tasks.Insert(0, item));
        SaveTasks();
        TasksChanged?.Invoke();
    }

    private void UpdateTask(DownloadTaskItem task, Action<DownloadTaskItem> apply)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            apply(task);
            task.RaiseDerivedChanged();
            TaskUpdated?.Invoke(task);
        });
    }

    private DownloadTaskItem? Find(string id) => Tasks.FirstOrDefault(t => t.Id == id);

    private static bool IsTerminal(DownloadTaskItem task) =>
        task.Status is DownloadStatus.Completed or DownloadStatus.Failed or DownloadStatus.Canceled;

    private static void DeletePartFile(DownloadTaskItem task)
    {
        try { if (File.Exists(task.LocalPath + ".part")) File.Delete(task.LocalPath + ".part"); } catch { }
    }

    /// <summary>从 URL 推导文件名；不合法时用 fallback</summary>
    private static string DeriveFileName(string url, string fallback)
    {
        try
        {
            var name = Path.GetFileName(new Uri(url).AbsolutePath);
            if (!string.IsNullOrWhiteSpace(name)) return SanitizeFileName(name);
        }
        catch { }
        return fallback;
    }

    /// <summary>清理文件名中的非法字符</summary>
    private static string SanitizeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var cleaned = new string(name.Where(c => !invalid.Contains(c) && c != '/' && c != '\\').ToArray()).Trim();
        return string.IsNullOrWhiteSpace(cleaned) ? "download" : cleaned;
    }

    /// <summary>目标文件已存在时追加序号，避免覆盖</summary>
    private static string GetUniquePath(string path)
    {
        if (!File.Exists(path)) return path;
        var dir = Path.GetDirectoryName(path) ?? "";
        var ext = Path.GetExtension(path);
        var baseName = Path.GetFileNameWithoutExtension(path);
        for (int i = 1; ; i++)
        {
            var candidate = Path.Combine(dir, $"{baseName} ({i}){ext}");
            if (!File.Exists(candidate)) return candidate;
        }
    }

    // ═══════════════════════════════════════════════════════
    // 持久化
    // ═══════════════════════════════════════════════════════

    private class TaskDto
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public string Url { get; set; } = "";
        public string LocalPath { get; set; } = "";
        public string Kind { get; set; } = "url";
        public long CreatedAt { get; set; }
        public long TotalBytes { get; set; }
        public long DownloadedBytes { get; set; }
        public DownloadStatus Status { get; set; }
        public string Error { get; set; } = "";
    }

    private void SaveTasks()
    {
        try
        {
            List<TaskDto> list;
            lock (_lock)
            {
                list = Tasks.Select(t => new TaskDto
                {
                    Id = t.Id,
                    Name = t.Name,
                    Url = t.Url,
                    LocalPath = t.LocalPath,
                    Kind = t.Kind,
                    CreatedAt = t.CreatedAt,
                    TotalBytes = t.TotalBytes,
                    DownloadedBytes = t.DownloadedBytes,
                    Status = t.Status,
                    Error = t.Error
                }).ToList();
            }
            File.WriteAllText(TasksFilePath, JsonSerializer.Serialize(list));
        }
        catch { }
    }

    private void LoadTasks()
    {
        try
        {
            if (!File.Exists(TasksFilePath)) return;
            var list = JsonSerializer.Deserialize<List<TaskDto>>(File.ReadAllText(TasksFilePath));
            if (list == null) return;
            foreach (var dto in list.OrderByDescending(d => d.CreatedAt))
            {
                // 恢复时：下载中的任务视为暂停，避免应用重启后自动重下
                var status = dto.Status == DownloadStatus.Downloading ? DownloadStatus.Paused : dto.Status;
                var item = new DownloadTaskItem
                {
                    Id = dto.Id,
                    Name = dto.Name,
                    Url = dto.Url,
                    LocalPath = dto.LocalPath,
                    Kind = dto.Kind,
                    CreatedAt = dto.CreatedAt,
                    TotalBytes = dto.TotalBytes,
                    DownloadedBytes = dto.DownloadedBytes,
                    Status = status,
                    Error = dto.Error,
                    IsPaused = status == DownloadStatus.Paused
                };
                Tasks.Add(item);

                // 磁力任务：重启后自动续传（BT 引擎校验已下数据后继续，无需重新开始）
                var resumedBt = dto.Kind == "magnet" ? Bt : null;
                if (dto.Kind == "magnet" && resumedBt != null
                    && status is DownloadStatus.Queued or DownloadStatus.Paused or DownloadStatus.Downloading)
                {
                    item.Status = DownloadStatus.Queued;
                    item.IsPaused = false;
                    _ = RunMagnetAsync(item, dto.Url, dto.LocalPath);
                }
            }
        }
        catch { }
    }

    public void Dispose()
    {
        _disposed = true;
        lock (_lock)
        {
            foreach (var cts in _ctsMap.Values) cts.Cancel();
            _ctsMap.Clear();
        }
        _http.Dispose();
        _slotWake.Dispose();
    }
}
