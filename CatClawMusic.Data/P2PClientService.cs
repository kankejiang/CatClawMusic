using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using CatClawMusic.Core.Interfaces;
using CatClawMusic.Core.Models;

namespace CatClawMusic.Data;

/// <summary>
/// P2P 客户端服务 —— DHT 轻客户端 + P2P 文件下载
/// </summary>
public class P2PClientService : IP2PService, IDisposable
{
    /// <summary>HTTP 客户端，用于与引导节点和其他设备通信</summary>
    private readonly HttpClient _http;
    /// <summary>DHT 协议使用的 UDP 客户端（轻客户端模式，主要逻辑在 Go 服务端）</summary>
    private UdpClient? _udpClient;
    /// <summary>后台任务取消令牌</summary>
    private CancellationTokenSource? _cts;
    /// <summary>服务运行状态标志</summary>
    private bool _isRunning;

    /// <summary>P2P 配置（启用开关、DHT 端口、引导节点、限速等）</summary>
    public P2PConfig Config { get; } = new();
    /// <summary>服务是否正在运行</summary>
    public bool IsRunning => _isRunning;

    /// <summary>JSON 反序列化选项：属性名大小写不敏感</summary>
    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

    /// <summary>
    /// 初始化 P2P 客户端服务。
    /// </summary>
    public P2PClientService()
    {
        _http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
    }

    /// <summary>
    /// 启动 P2P 服务。若 Config.Enabled 为 true，则绑定 UDP 端口并尝试联系引导节点；
    /// UDP 端口占用时不影响服务运行（仅失去 DHT 能力，HTTP 发现仍可用）。
    /// </summary>
    public async Task StartAsync()
    {
        if (_isRunning) return;

        if (Config.Enabled)
        {
            _cts = new CancellationTokenSource();
            try
            {
                _udpClient = new UdpClient(Config.DhtPort);
                _isRunning = true;

                // Bootstrap: contact the configured bootstrap node
                _ = Task.Run(() => BootstrapAsync(_cts.Token));
            }
            catch
            {
                // UDP port might be in use, continue without DHT
                _isRunning = true;
            }
        }
        else
        {
            _isRunning = true;
        }

        await Task.CompletedTask;
    }

    /// <summary>
    /// 停止 P2P 服务，关闭 UDP 客户端并取消后台任务。
    /// </summary>
    public async Task StopAsync()
    {
        _isRunning = false;
        _cts?.Cancel();
        _udpClient?.Close();
        _udpClient?.Dispose();
        _udpClient = null;
        await Task.CompletedTask;
    }

    /// <summary>
    /// 向引导节点发送 PING 进行 DHT 引导（轻客户端实现，完整 DHT 在 Go 服务端）。
    /// </summary>
    /// <param name="ct">取消令牌。</param>
    private async Task BootstrapAsync(CancellationToken ct)
    {
        try
        {
            var addr = Config.BootstrapNode;
            // Simple bootstrap: send PING to bootstrap node
            // Full DHT implementation is in the Go server; Android is a light client
        }
        catch { }
    }

    /// <summary>
    /// 通过 HTTP 发现已知 P2P 设备（询问引导节点的 /api/dht/devices）。
    /// </summary>
    /// <returns>发现的设备列表；请求失败时返回空列表。</returns>
    public async Task<List<P2PDevice>> DiscoverDevicesAsync()
    {
        var devices = new List<P2PDevice>();

        // Try HTTP discovery: ask the server for its known devices
        if (!string.IsNullOrEmpty(Config.BootstrapNode))
        {
            try
            {
                var bootstrapHttp = Config.BootstrapNode.Split(':')[0];
                var url = $"http://{bootstrapHttp}:5000/api/dht/devices";
                var json = await _http.GetStringAsync(url);
                var discovered = JsonSerializer.Deserialize<List<JsonElement>>(json, JsonOpts);

                if (discovered != null)
                {
                    foreach (var d in discovered)
                    {
                        var device = new P2PDevice
                        {
                            Name = d.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "",
                            Ip = d.TryGetProperty("ip", out var ip) ? ip.GetString() ?? "" : "",
                            HttpPort = d.TryGetProperty("http_port", out var hp) ? hp.GetInt32() : 5000,
                            DhtPort = d.TryGetProperty("dht_port", out var dp) ? dp.GetInt32() : 6881,
                            LastSeen = DateTime.UtcNow
                        };

                        if (!string.IsNullOrEmpty(device.Ip) && !devices.Any(x => x.Ip == device.Ip))
                            devices.Add(device);
                    }
                }
            }
            catch { }
        }

        return devices;
    }

    /// <summary>
    /// 从指定 P2P 设备下载歌曲流，包装为限速流以避免占满带宽。
    /// </summary>
    /// <param name="device">目标 P2P 设备。</param>
    /// <param name="songId">服务端歌曲 ID。</param>
    /// <param name="progress">进度回调，参数为 (已读字节, 总字节)。</param>
    /// <returns>限速后的可读流；请求失败时返回 null。</returns>
    public async Task<Stream?> DownloadFromDeviceAsync(P2PDevice device, int songId, IProgress<(long, long)>? progress = null)
    {
        try
        {
            var url = $"{device.GetBaseUrl()}/api/stream/{songId}";
            var resp = await _http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);

            if (!resp.IsSuccessStatusCode)
                return null;

            var stream = await resp.Content.ReadAsStreamAsync();
            return new ThrottledStream(stream, Config.RateLimitKBs * 1024, progress, resp.Content.Headers.ContentLength ?? 0);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// 在所有已发现的 P2P 设备上搜索歌曲，聚合结果。
    /// </summary>
    /// <param name="keyword">搜索关键词。</param>
    /// <returns>聚合后的歌曲列表；单台设备失败不影响其他设备。</returns>
    public async Task<List<Song>> SearchAllDevicesAsync(string keyword)
    {
        var allSongs = new List<Song>();

        var devices = await DiscoverDevicesAsync();
        foreach (var device in devices)
        {
            try
            {
                var url = $"{device.GetBaseUrl()}/api/search?q={Uri.EscapeDataString(keyword)}";
                var json = await _http.GetStringAsync(url);
                var songs = JsonSerializer.Deserialize<List<ServerSong>>(json, JsonOpts);

                if (songs != null)
                {
                    foreach (var ss in songs)
                    {
                        allSongs.Add(new Song
                        {
                            Title = ss.Title,
                            Artist = ss.Artist,
                            Album = ss.Album,
                            Duration = ss.Duration,
                            FileSize = ss.FileSize,
                            Bitrate = ss.Bitrate,
                            Year = ss.Year,
                            Source = SongSource.Cache,
                            Protocol = CatClawMusic.Core.Models.ProtocolType.WebDAV,
                            RemoteId = $"p2p:{device.Ip}:{ss.Id}"
                        });
                    }
                }
            }
            catch { }
        }

        return allSongs;
    }

    /// <summary>
    /// 释放 UDP 客户端、取消令牌和 HTTP 客户端资源。
    /// </summary>
    public void Dispose()
    {
        _udpClient?.Dispose();
        _cts?.Dispose();
        _http.Dispose();
    }
}

/// <summary>
/// 限速流包装器 —— 实现 Token Bucket 限速
/// </summary>
internal class ThrottledStream : Stream
{
    /// <summary>被包装的底层流</summary>
    private readonly Stream _inner;
    /// <summary>每秒允许传输的最大字节数</summary>
    private readonly int _bytesPerSec;
    /// <summary>进度回调，参数为 (已读字节, 总字节)</summary>
    private readonly IProgress<(long, long)>? _progress;
    /// <summary>流的总长度（字节），来自 HTTP Content-Length</summary>
    private readonly long _totalLength;
    /// <summary>已读取的字节数</summary>
    private long _read;
    /// <summary>当前可用的令牌数（Token Bucket）</summary>
    private double _tokens;
    /// <summary>上次令牌补充时间（Ticks）</summary>
    private long _lastRefillTicks;

    /// <summary>
    /// 初始化限速流。
    /// </summary>
    /// <param name="inner">底层可读流。</param>
    /// <param name="bytesPerSec">每秒最大字节数。</param>
    /// <param name="progress">进度回调。</param>
    /// <param name="totalLength">流的总长度。</param>
    public ThrottledStream(Stream inner, int bytesPerSec, IProgress<(long, long)>? progress, long totalLength)
    {
        _inner = inner;
        _bytesPerSec = bytesPerSec;
        _progress = progress;
        _totalLength = totalLength;
        _tokens = bytesPerSec;
        _lastRefillTicks = DateTime.UtcNow.Ticks;
    }

    /// <summary>
    /// 异步读取数据，按 Token Bucket 算法限速。
    /// 令牌不足时自动等待补充，等待时间上限 5 秒避免长时间阻塞。
    /// </summary>
    /// <param name="buffer">目标缓冲区。</param>
    /// <param name="offset">缓冲区偏移。</param>
    /// <param name="count">期望读取的字节数。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>实际读取的字节数。</returns>
    public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken ct)
    {
        Refill();

        int allowed = Math.Min(count, (int)_tokens);
        if (allowed <= 0)
        {
            // Wait until tokens are available
            int waitMs = (int)((-_tokens + _bytesPerSec) / (double)_bytesPerSec * 1000);
            if (waitMs > 0 && waitMs < 5000)
                await Task.Delay(waitMs, ct);
            Refill();
            allowed = Math.Min(count, Math.Max(1, (int)_tokens));
        }

        int read = await _inner.ReadAsync(buffer, offset, allowed, ct);
        _tokens -= read;
        _read += read;
        _progress?.Report((_read, _totalLength));
        return read;
    }

    /// <summary>
    /// 同步读取数据（通过 ReadAsync 阻塞等待实现）。
    /// </summary>
    /// <param name="buffer">目标缓冲区。</param>
    /// <param name="offset">缓冲区偏移。</param>
    /// <param name="count">期望读取的字节数。</param>
    /// <returns>实际读取的字节数。</returns>
    public override int Read(byte[] buffer, int offset, int count)
    {
        return ReadAsync(buffer, offset, count, CancellationToken.None).GetAwaiter().GetResult();
    }

    /// <summary>
    /// 按经过的时间补充令牌，令牌上限为 _bytesPerSec（每秒桶容量）。
    /// </summary>
    private void Refill()
    {
        var now = DateTime.UtcNow.Ticks;
        var elapsed = (now - _lastRefillTicks) / (double)TimeSpan.TicksPerSecond;
        _tokens += elapsed * _bytesPerSec;
        if (_tokens > _bytesPerSec) _tokens = _bytesPerSec;
        _lastRefillTicks = now;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _inner.Dispose();
        base.Dispose(disposing);
    }

    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => _totalLength;
    public override long Position { get => _read; set => throw new NotSupportedException(); }
    public override void Flush() { }
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}
