using MonoTorrent;
using MonoTorrent.Client;

namespace CatClawMusic.Maui.Services;

/// <summary>
/// 磁力下载服务：基于 MonoTorrent 3.x 引擎的 BT 下载（支持 magnet: 链接、DHT、做种者发现）。
/// 任务与 HTTP 下载共用 DownloadManager 的任务模型（DownloadTaskItem, Kind="magnet"），
/// 由 DownloadManager 委托本服务执行与回调进度。
/// </summary>
public class BitTorrentDownloadService : IDisposable
{
    /// <summary>BT 监听 TCP 端口（连接做种者）</summary>
    private const int ListenPort = 51413;

    /// <summary>DHT 监听 UDP 端口（磁力无 tracker 时靠 DHT 找做种者）</summary>
    private const int DhtPort = 51414;

    private readonly ClientEngine _engine;
    private readonly Dictionary<string, TorrentManager> _managers = new();
    private readonly object _lock = new();
    private bool _disposed;

    public BitTorrentDownloadService()
    {
        var cacheDir = Path.Combine(FileSystem.AppDataDirectory, "bt");
        try { Directory.CreateDirectory(cacheDir); } catch { }

        var builder = new EngineSettingsBuilder
        {
            CacheDirectory = cacheDir,
            MaximumConnections = 120,
            MaximumDownloadRate = 0,
            MaximumUploadRate = 0,
            ListenEndPoints = new Dictionary<string, System.Net.IPEndPoint>
            {
                ["ipv4"] = new(System.Net.IPAddress.Any, ListenPort),
            },
            DhtEndPoint = new System.Net.IPEndPoint(System.Net.IPAddress.Any, DhtPort),
        };
        _engine = new ClientEngine(builder.ToSettings());
    }

    /// <summary>任务 ID → TorrentManager</summary>
    private TorrentManager? GetManager(string taskId)
    {
        lock (_lock) return _managers.TryGetValue(taskId, out var m) ? m : null;
    }

    /// <summary>开始磁力下载：解析 magnet → 获取元数据 → 下载到 saveDir。
    /// 回调：onState（状态文本）、onProgress（0~100）、onBytes（已下/总量）、onComplete。
    /// 返回 null=成功启动；否则返回错误信息。</summary>
    public async Task<string?> StartAsync(string taskId, string magnet, string saveDir,
        Action<string> onState, Action<double> onProgress, Action<long, long> onBytes, Action onComplete)
    {
        try
        {
            if (!magnet.StartsWith("magnet:", StringComparison.OrdinalIgnoreCase))
                return "不是有效的磁力链接（需以 magnet: 开头）";

            // 必须用 MagnetLink.Parse + AddAsync(MagnetLink)：AddAsync(string) 会把
            // magnet 字符串当 torrent 文件路径尝试读取（Path.Combine 当前目录），
            // 抛"文件名、目录名或卷标语法不正确"IOException。
            if (!MagnetLink.TryParse(magnet.Trim(), out var link))
                return "磁力链接解析失败（btih 信息不完整）";

            var manager = await _engine.AddAsync(link, saveDir);
            lock (_lock) _managers[taskId] = manager;

            // AddAsync 后状态为 Stopped，需显式 StartAsync 才开始 DHT/tracker 查找做种者
            await manager.StartAsync();

            manager.TorrentStateChanged += (_, e) =>
            {
                onState(StateText(e.NewState));
                if (e.NewState == TorrentState.Seeding || e.NewState == TorrentState.Stopped)
                    onComplete();
            };

            // 周期进度上报（每 500ms）：进度优先用 manager.Progress（0~100），字节数取 Monitor
            _ = Task.Run(async () =>
            {
                while (!_disposed)
                {
                    var m = GetManager(taskId);
                    if (m == null) return;
                    if (m.State is TorrentState.Seeding or TorrentState.Stopped or TorrentState.Error)
                    {
                        if (m.State == TorrentState.Seeding) onComplete();
                        return;
                    }
                    onProgress(m.Progress);
                    var downloaded = m.Monitor.DataBytesDownloaded;
                    var total = m.HasMetadata ? m.Torrent!.Size : 0L;
                    onBytes(downloaded, total);
                    await Task.Delay(500);
                }
            });
            return null;
        }
        catch (Exception ex)
        {
            Log.Debug("BitTorrent", $"[BT] 启动磁力下载失败: {ex.Message}");
            return ex.Message;
        }
    }

    /// <summary>暂停任务</summary>
    public async Task PauseAsync(string taskId)
    {
        var m = GetManager(taskId);
        if (m != null) await m.PauseAsync();
    }

    /// <summary>继续任务</summary>
    public async Task ResumeAsync(string taskId)
    {
        var m = GetManager(taskId);
        if (m != null) await m.StartAsync();
    }

    /// <summary>移除任务（停止下载并清理会话，已下载文件保留）</summary>
    public async Task RemoveAsync(string taskId)
    {
        var m = GetManager(taskId);
        if (m == null) return;
        lock (_lock) _managers.Remove(taskId);
        try { await _engine.RemoveAsync(m); } catch { }
    }

    /// <summary>状态文本映射</summary>
    private static string StateText(TorrentState state) => state switch
    {
        TorrentState.Metadata => "获取种子信息...",
        TorrentState.Hashing => "校验数据...",
        TorrentState.Downloading => "下载中",
        TorrentState.Seeding => "已完成",
        TorrentState.Paused => "已暂停",
        TorrentState.Stopped => "已停止",
        TorrentState.Error => "出错",
        _ => state.ToString()
    };

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try { _engine.Dispose(); } catch { }
    }
}
