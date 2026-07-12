using CatClawMusic.Core.ClawCircle;

namespace CatClawMusic.Data;

/// <summary>
/// 猫爪圈跨网 P2P 服务门面：封装 Stage 2 信令客户端 + Stage 3 传输引擎 + 本地数据提供者，
/// 供 MAUI 应用层一键启动/停止/下载。启动后自动 STUN 注册、上报曲库摘要并具备做种能力。
/// </summary>
public class ClawCircleP2PService : IDisposable
{
    private readonly MusicDatabase _db;
    private readonly string _downloadDir;
    private readonly Func<string, Stream?>? _streamOpener;

    private ClawCircleTrackerClient? _sig;
    private P2PTransferEngine? _engine;
    private ClawCircleP2PDataProvider? _provider;

    public bool IsRunning => _engine != null;
    public event Action<TransferProgress>? ProgressChanged;

    /// <summary>按 FilePath 打开只读流（平台注入：Android content:// 走 ContentResolver）。未设置则用 File.OpenRead。</summary>
    public Func<string, Stream?>? SongStreamOpener { get; set; }

    /// <param name="downloadDir">下载落盘目录（通常 {AppData}/clawcircle/downloads）。</param>
    /// <param name="streamOpener">按 FilePath 打开只读流（平台注入：Android content:// 走 ContentResolver）。</param>
    public ClawCircleP2PService(MusicDatabase db, string downloadDir, Func<string, Stream?>? streamOpener = null)
    {
        _db = db;
        _downloadDir = downloadDir;
        _streamOpener = streamOpener;
    }

    /// <summary>启动：连接 tracker、STUN 注册、上报曲库、开启做种/下载引擎。</summary>
    public async Task StartAsync(string deviceId, string name, string trackerUrl, string token, CancellationToken ct = default)
    {
        if (_engine != null) return;
        if (string.IsNullOrWhiteSpace(trackerUrl)) throw new InvalidOperationException("未配置 tracker 地址");

        var (host, httpPort) = ParseTrackerUrl(trackerUrl);
        _provider = new ClawCircleP2PDataProvider(_db, _downloadDir) { SongStreamOpener = SongStreamOpener ?? _streamOpener };

        var library = await BuildLibraryAsync(_provider, ct);

        _sig = new ClawCircleTrackerClient(trackerUrl, token);
        await _sig.ConnectAsync(deviceId, name, library, ct);

        _engine = new P2PTransferEngine(_sig, _provider, deviceId, host, httpPort + 1);
        _engine.ProgressChanged += p => ProgressChanged?.Invoke(p);
        _engine.Start();
    }

    /// <summary>下载一首歌到指定路径（含打洞 + 分块 + 校验）。</summary>
    public Task<bool> RequestSongAsync(string songKey, string destinationPath, CancellationToken ct = default)
        => _engine?.RequestSongAsync(songKey, destinationPath, ct) ?? Task.FromResult(false);

    public async Task StopAsync()
    {
        try { _engine?.Stop(); } catch { }
        _engine = null;
        if (_sig != null) { try { await _sig.DisconnectAsync(); } catch { } }
        _sig = null;
        _provider = null;
    }

    private static async Task<LibrarySummary> BuildLibraryAsync(ClawCircleP2PDataProvider provider, CancellationToken ct)
    {
        try
        {
            var keys = await provider.GetLocalSongKeysAsync(ct);
            return new LibrarySummary
            {
                SongCount = keys.Count,
                SongKeys = keys.Take(5000).ToList()
            };
        }
        catch
        {
            return new LibrarySummary();
        }
    }

    private static (string host, int port) ParseTrackerUrl(string url)
    {
        // 形如 ws://host:port/ws/clawcircle 或 ws://host:port?token=...
        var noScheme = url.Contains("://") ? url.Substring(url.IndexOf("://") + 3) : url;
        var end = noScheme.IndexOfAny(new[] { '/', '?' });
        var authority = end >= 0 ? noScheme.Substring(0, end) : noScheme;
        var colon = authority.LastIndexOf(':');
        string host; int port;
        if (colon >= 0)
        {
            host = authority.Substring(0, colon);
            int.TryParse(authority.Substring(colon + 1), out port);
        }
        else
        {
            host = authority;
            port = 37823;
        }
        if (port <= 0) port = 37823;
        return (host, port);
    }

    public void Dispose()
    {
        try { _ = StopAsync(); } catch { }
    }
}
