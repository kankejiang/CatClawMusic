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
    private readonly HttpClient _http;
    private UdpClient? _udpClient;
    private CancellationTokenSource? _cts;
    private bool _isRunning;

    public P2PConfig Config { get; } = new();
    public bool IsRunning => _isRunning;

    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

    public P2PClientService()
    {
        _http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
    }

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

    public async Task StopAsync()
    {
        _isRunning = false;
        _cts?.Cancel();
        _udpClient?.Close();
        _udpClient?.Dispose();
        _udpClient = null;
        await Task.CompletedTask;
    }

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
    private readonly Stream _inner;
    private readonly int _bytesPerSec;
    private readonly IProgress<(long, long)>? _progress;
    private readonly long _totalLength;
    private long _read;
    private double _tokens;
    private long _lastRefillTicks;

    public ThrottledStream(Stream inner, int bytesPerSec, IProgress<(long, long)>? progress, long totalLength)
    {
        _inner = inner;
        _bytesPerSec = bytesPerSec;
        _progress = progress;
        _totalLength = totalLength;
        _tokens = bytesPerSec;
        _lastRefillTicks = DateTime.UtcNow.Ticks;
    }

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

    public override int Read(byte[] buffer, int offset, int count)
    {
        return ReadAsync(buffer, offset, count, CancellationToken.None).GetAwaiter().GetResult();
    }

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
