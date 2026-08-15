using System.Collections.ObjectModel;
using System.Text.Json;
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
public class DownloadManager : IDisposable
{
    /// <summary>下载路径默认值（Android 外部存储根目录）</summary>
    public const string DefaultFolderPath = "/storage/emulated/0/CatClawMusic";

    /// <summary>下载路径偏好键</summary>
    public const string PrefKey = "download_folder_path";

    private const int MaxConcurrent = 2;
    private static readonly string TasksFilePath =
        Path.Combine(FileSystem.AppDataDirectory, "download_tasks.json");

    private readonly HttpClient _http;
    private readonly SemaphoreSlim _gate = new(MaxConcurrent);
    private readonly Dictionary<string, CancellationTokenSource> _ctsMap = new();
    private readonly object _lock = new();
    private bool _disposed;

    /// <summary>全部下载任务（按创建时间排序）</summary>
    public ObservableCollection<DownloadTaskItem> Tasks { get; } = new();

    /// <summary>任务集合变化（增删）时触发</summary>
    public event Action? TasksChanged;

    /// <summary>单个任务进度/状态变化时触发</summary>
    public event Action<DownloadTaskItem>? TaskUpdated;

    public DownloadManager()
    {
        _http = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
        _http.DefaultRequestHeaders.Add("User-Agent", "CatClawMusic/1.0");
        LoadTasks();
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
        if (task != null && task.Status == DownloadStatus.Downloading)
        {
            UpdateTask(task, t => { t.Status = DownloadStatus.Paused; t.IsPaused = true; t.SpeedText = ""; });
        }
    }

    /// <summary>继续任务（重新发起下载，清除旧进度）</summary>
    public void Resume(string id)
    {
        var task = Find(id);
        if (task == null || task.Status != DownloadStatus.Paused) return;
        UpdateTask(task, t =>
        {
            t.Status = DownloadStatus.Queued;
            t.IsPaused = false;
            t.Error = "";
            t.DownloadedBytes = 0;
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
        string? fileError = null;
        if (task != null)
        {
            DeletePartFile(task);
            if (deleteFile && !string.IsNullOrEmpty(task.LocalPath) && File.Exists(task.LocalPath))
            {
                try { File.Delete(task.LocalPath); }
                catch (Exception ex)
                {
                    fileError = ex is IOException
                        ? "文件正被占用（可能正在播放），请先停止播放后再试"
                        : $"文件删除失败：{ex.Message}";
                    Log.Debug("DownloadManager", $"[Download] 删除文件失败: {task.LocalPath} - {ex.Message}");
                }
            }
            MainThread.BeginInvokeOnMainThread(() => Tasks.Remove(task));
            SaveTasks();
            TasksChanged?.Invoke();
        }
        return fileError;
    }

    /// <summary>失败任务重新下载</summary>
    public void Retry(string id)
    {
        var task = Find(id);
        if (task == null || task.Status != DownloadStatus.Failed) return;
        UpdateTask(task, t =>
        {
            t.Status = DownloadStatus.Queued;
            t.Error = "";
            t.DownloadedBytes = 0;
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

    // ═══════════════════════════════════════════════════════
    // 内部执行
    // ═══════════════════════════════════════════════════════

    private readonly Dictionary<string, Func<CancellationToken, Task<Stream?>>> _networkProviders = new();

    private async Task RunAsync(DownloadTaskItem task)
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_disposed) return;
            if (IsTerminal(task)) return;

            var cts = new CancellationTokenSource();
            lock (_lock) _ctsMap[task.Id] = cts;

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
            finally
            {
                lock (_lock) { _ctsMap.Remove(task.Id); cts.Dispose(); }
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task RunStreamAsync(DownloadTaskItem task, Func<CancellationToken, Task<Stream?>> provider)
    {
        if (provider == null) { MarkFailed(task, "缺少下载源"); return; }
        lock (_lock) _networkProviders[task.Id] = provider;

        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_disposed) return;
            if (IsTerminal(task)) return;

            var cts = new CancellationTokenSource();
            lock (_lock) _ctsMap[task.Id] = cts;

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
            finally
            {
                lock (_lock) { _ctsMap.Remove(task.Id); cts.Dispose(); }
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>HTTP 下载：先探测总大小，再流式写入临时文件</summary>
    private async Task DownloadFromUrlAsync(DownloadTaskItem task, CancellationToken ct)
    {
        var partPath = task.LocalPath + ".part";
        DeletePartFile(task);

        long total = -1;
        using (var probe = new HttpRequestMessage(HttpMethod.Get, task.Url))
        {
            using var probeResp = await _http.SendAsync(probe, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
            probeResp.EnsureSuccessStatusCode();
            total = probeResp.Content.Headers.ContentLength ?? -1;
            UpdateTask(task, t => t.TotalBytes = total);

            await using var src = await probeResp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            await using var dst = new FileStream(partPath, FileMode.Create, FileAccess.Write);
            await CopyStreamAsync(task, src, dst, total, ct).ConfigureAwait(false);
        }

        if (ct.IsCancellationRequested)
        {
            DeletePartFile(task);
            return;
        }
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

    private async Task CopyStreamAsync(DownloadTaskItem task, Stream src, FileStream dst, long total, CancellationToken ct)
    {
        var buffer = new byte[81920];
        long read = 0;
        long lastTick = Environment.TickCount64;
        long lastBytes = 0;
        int n;
        while ((n = await src.ReadAsync(buffer, 0, buffer.Length, ct).ConfigureAwait(false)) > 0)
        {
            await dst.WriteAsync(buffer, 0, n, ct).ConfigureAwait(false);
            read += n;
            if (total > 0) UpdateTask(task, t => t.TotalBytes = total);

            var now = Environment.TickCount64;
            if (now - lastTick >= 500)
            {
                var elapsedSec = (now - lastTick) / 1000.0;
                var speed = (read - lastBytes) / Math.Max(elapsedSec, 0.001);
                var speedText = $"{DownloadTaskItem.FormatBytes((long)speed)}/s";
                UpdateTask(task, t =>
                {
                    t.DownloadedBytes = read;
                    t.SpeedText = speedText;
                });
                lastTick = now;
                lastBytes = read;
            }
            else
            {
                UpdateTask(task, t => t.DownloadedBytes = read);
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
                Tasks.Add(new DownloadTaskItem
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
                });
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
        _gate.Dispose();
    }
}
