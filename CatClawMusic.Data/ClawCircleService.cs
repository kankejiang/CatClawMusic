using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using CatClawMusic.Core.Interfaces;
using CatClawMusic.Core.Models;

namespace CatClawMusic.Data;

/// <summary>
/// 猫爪圈服务实现：同局域网设备发现 + 本机曲库共享。
/// <para>
/// 设计原则：完全自包含，不依赖任何外部/互联网服务器。
/// 发现使用 UDP 广播（定时播报本机存在）+ 单播 Ping/Pong 即时刷新；
/// 共享使用内置迷你 HTTP 服务（基于 <see cref="TcpListener"/>，无需 Windows URL ACL 预留），
/// 对外提供 <c>/api/info</c>、<c>/api/songs</c>、<c>/api/stream/{"{id}"}</c> 三个端点。
/// 从对端拉取同样使用 <see cref="HttpClient"/> 访问其局域网地址。
/// </para>
/// </summary>
public class ClawCircleService : IClawCircleService, IDisposable
{
    private const int UdpBasePort = 37821;
    private const int TcpBasePort = 37822;
    private const string AnnounceTag = "clawcircle";
    private const string PingTag = "clawcircle-ping";

    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

    private readonly MusicDatabase _db;
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(12) };
    private readonly ConcurrentDictionary<string, ClawCirclePeer> _peers = new();
    private readonly object _stateLock = new();

    private UdpClient? _udp;
    private TcpListener? _tcp;
    private CancellationTokenSource? _cts;
    private Timer? _announceTimer;
    private Timer? _pruneTimer;
    private bool _running;
    private int _udpPort;
    private int _tcpPort;
    private string _localAddress = "";

    private string _deviceName = "猫爪用户";
    private bool _shareLibrary;
    private int _songCount;
    private string _nowPlaying = "";

    /// <summary>打开本地歌曲流（由平台在启动时注入，处理 Android content:// 等情况）。</summary>
    public Func<int, Stream?>? SongStreamOpener { get; set; }

    /// <summary>获取本机共享歌曲数量（由平台注入）。</summary>
    public Func<int>? SongCountProvider { get; set; }

    /// <summary>获取本机正在播放的歌曲标题（由平台注入，仅用于展示）。</summary>
    public Func<string>? NowPlayingProvider { get; set; }

    public ClawCircleService(MusicDatabase db)
    {
        _db = db;
    }

    public bool IsRunning => _running;
    public int Port => _tcpPort;
    public string LocalAddress => _localAddress;

    public event EventHandler? PeersChanged;

    public async Task StartAsync(string deviceName, bool shareLibrary, CancellationToken ct = default)
    {
        if (_running) return;

        _deviceName = string.IsNullOrWhiteSpace(deviceName) ? "猫爪用户" : deviceName;
        _shareLibrary = shareLibrary;
        try { _songCount = await Task.Run(() => SongCountProvider?.Invoke() ?? 0, ct); } catch { _songCount = 0; }
        try { _nowPlaying = await Task.Run(() => NowPlayingProvider?.Invoke() ?? "", ct); } catch { _nowPlaying = ""; }

        _cts = new CancellationTokenSource();
        var token = _cts.Token;

        // 解析本机首选地址：IPv6 优先（全局 > 链路本地 > IPv4），异步以避免阻塞主线程
        try
        {
            var hostEntry = await Dns.GetHostEntryAsync(Dns.GetHostName());
            _localAddress = "";
            // ① 全局 IPv6
            foreach (var ip in hostEntry.AddressList)
                if (ip.AddressFamily == AddressFamily.InterNetworkV6
                    && !IPAddress.IsLoopback(ip) && !ip.IsIPv6LinkLocal && !ip.IsIPv6SiteLocal)
                { _localAddress = ip.ToString(); break; }
            // ② 链路本地 IPv6
            if (string.IsNullOrEmpty(_localAddress))
                foreach (var ip in hostEntry.AddressList)
                    if (ip.AddressFamily == AddressFamily.InterNetworkV6
                        && !IPAddress.IsLoopback(ip) && ip.IsIPv6LinkLocal)
                    { _localAddress = ip.ToString(); break; }
            // ③ IPv4 兜底
            if (string.IsNullOrEmpty(_localAddress))
                foreach (var ip in hostEntry.AddressList)
                    if (ip.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(ip))
                    { _localAddress = ip.ToString(); break; }
        }
        catch { _localAddress = ""; }

        // ── 启动 UDP 发现 ──
        if (!BindUdp(out _udp, out _udpPort))
        {
            System.Diagnostics.Debug.WriteLine("[ClawCircle] UDP 端口绑定失败，发现能力不可用");
        }

        // ── 启动迷你 HTTP 服务 ──
        if (!BindTcp(out _tcp, out _tcpPort))
        {
            System.Diagnostics.Debug.WriteLine("[ClawCircle] TCP 端口绑定失败，共享能力不可用");
        }

        _running = true;

        // 后台接收 UDP（发现）
        if (_udp != null)
            _ = Task.Run(() => UdpReceiveLoop(token), token);

        // 后台接受 HTTP（共享）
        if (_tcp != null)
            _ = Task.Run(() => HttpAcceptLoop(token), token);

        // 定时广播本机存在
        _announceTimer = new Timer(_ => SafeAnnounce(), null, 0, 4000);
        // 定时清理离线对端
        _pruneTimer = new Timer(_ => PrunePeers(), null, 12000, 12000);

        // 立即广播一次以加速被发现
        SafeAnnounce();

        await Task.CompletedTask;
    }

    public async Task StopAsync()
    {
        if (!_running && _cts == null) return;
        _running = false;

        try { _announceTimer?.Dispose(); } catch { }
        try { _pruneTimer?.Dispose(); } catch { }
        try { _cts?.Cancel(); } catch { }

        var udp = _udp; _udp = null;
        var tcp = _tcp; _tcp = null;

        try { udp?.Close(); } catch { }
        try { tcp?.Stop(); } catch { }
        try { udp?.Dispose(); } catch { }
        try { tcp?.Dispose(); } catch { }
        try { _cts?.Dispose(); } catch { }
        _cts = null;

        _peers.Clear();
        PeersChanged?.Invoke(this, EventArgs.Empty);

        await Task.CompletedTask;
    }

    public IReadOnlyList<ClawCirclePeer> GetPeers()
    {
        lock (_stateLock)
        {
            return _peers.Values.OrderBy(p => p.DeviceName).ToList();
        }
    }

    public async Task RefreshPeersAsync()
    {
        if (!_running || _udp == null) return;
        try
        {
            var ping = JsonSerializer.Serialize(new { t = PingTag, port = _tcpPort });
            var bytes = Encoding.UTF8.GetBytes(ping);
            await _udp.SendAsync(bytes, bytes.Length, new IPEndPoint(IPAddress.Broadcast, _udpPort));
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ClawCircle] Ping 广播失败: {ex.Message}");
        }
    }

    public async Task<List<ClawCircleSongInfo>?> GetPeerSongsAsync(ClawCirclePeer peer, CancellationToken ct = default)
    {
        try
        {
            var url = $"http://{peer.Ip}:{peer.Port}/api/songs";
            var json = await _http.GetStringAsync(url, ct);
            return JsonSerializer.Deserialize<List<ClawCircleSongInfo>>(json, JsonOpts);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ClawCircle] 获取对端歌单失败 ({peer.Ip}): {ex.Message}");
            return null;
        }
    }

    public async Task<ClawCircleSongStreamResult?> GetPeerSongStreamAsync(ClawCirclePeer peer, int songId, CancellationToken ct = default)
    {
        try
        {
            var url = $"http://{peer.Ip}:{peer.Port}/api/stream/{songId}";
            var resp = await _http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
            if (!resp.IsSuccessStatusCode) return null;

            var fileName = "";
            if (resp.Content.Headers.TryGetValues("X-Original-Name", out var vals))
                fileName = vals.FirstOrDefault() ?? "";

            return new ClawCircleSongStreamResult
            {
                Stream = await resp.Content.ReadAsStreamAsync(ct),
                FileName = fileName,
                Size = resp.Content.Headers.ContentLength ?? 0
            };
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ClawCircle] 拉取对端歌曲流失败 ({peer.Ip}:{songId}): {ex.Message}");
            return null;
        }
    }

    // ──────────────────────────────────────────────────────────
    // UDP 发现
    // ──────────────────────────────────────────────────────────

    private async Task UdpReceiveLoop(CancellationToken ct)
    {
        var udp = _udp;
        if (udp == null) return;
        try
        {
            while (!ct.IsCancellationRequested)
            {
                UdpReceiveResult result;
                try
                {
                    result = await udp.ReceiveAsync(ct);
                }
                catch (OperationCanceledException) { break; }
                catch (ObjectDisposedException) { break; }
                catch (Exception ex)
                {
                    if (ct.IsCancellationRequested) break;
                    System.Diagnostics.Debug.WriteLine($"[ClawCircle] UDP 接收错误: {ex.Message}");
                    continue;
                }

                _ = Task.Run(() => HandleUdpPacket(result, ct), ct);
            }
        }
        catch (OperationCanceledException) { }
    }

    private void HandleUdpPacket(UdpReceiveResult result, CancellationToken ct)
    {
        try
        {
            var text = Encoding.UTF8.GetString(result.Buffer);
            using var doc = JsonDocument.Parse(text);
            var root = doc.RootElement;
            if (!root.TryGetProperty("t", out var tagEl)) return;
            var tag = tagEl.GetString();

            if (tag == PingTag)
            {
                // 收到 Ping：单播回一个本机播报，让对方立即发现我
                SafeAnnounce(result.RemoteEndPoint);
            }
            else if (tag == AnnounceTag)
            {
                UpsertPeer(root, result.RemoteEndPoint);
            }
        }
        catch { /* 忽略无法解析的数据包 */ }
    }

    private void UpsertPeer(JsonElement root, IPEndPoint remote)
    {
        var ip = remote.Address.ToString();
        // 忽略自己（本机多个网卡可能收到自己的广播/multicast；含 IPv4/IPv6 回环）
        if (IPAddress.TryParse(ip, out var ipAddr))
        {
            if (IPAddress.IsLoopback(ipAddr) || ip == "0.0.0.0" || ip == "::")
                return;
        }
        if (!string.IsNullOrEmpty(_localAddress) && ip == _localAddress)
            return;

        var port = root.TryGetProperty("port", out var p) && p.TryGetInt32(out var pv) ? pv : _tcpPort;
        if (port <= 0) return;
        var id = $"{ip}:{port}";

        var name = root.TryGetProperty("name", out var n) ? (n.GetString() ?? "") : "";
        var sc = root.TryGetProperty("sc", out var s) && s.TryGetInt32(out var sv) ? sv : 0;
        var np = root.TryGetProperty("np", out var npEl) ? (npEl.GetString() ?? "") : "";

        bool changed;
        var peer = _peers.GetOrAdd(id, _ => new ClawCirclePeer
        {
            Ip = ip,
            Port = port,
            DeviceName = name,
            SongCount = sc,
            NowPlaying = np,
            LastSeen = DateTime.UtcNow
        });
        lock (_stateLock)
        {
            changed = peer.DeviceName != name || peer.SongCount != sc || peer.NowPlaying != np;
            peer.DeviceName = name;
            peer.SongCount = sc;
            peer.NowPlaying = np;
            peer.Ip = ip;
            peer.Port = port;
            peer.LastSeen = DateTime.UtcNow;
        }

        if (changed)
            PeersChanged?.Invoke(this, EventArgs.Empty);
    }

    private void PrunePeers()
    {
        bool changed = false;
        foreach (var kv in _peers)
        {
            if (!kv.Value.IsOnline)
            {
                if (_peers.TryRemove(kv.Key, out _))
                    changed = true;
            }
        }
        if (changed)
            PeersChanged?.Invoke(this, EventArgs.Empty);
    }

    private void SafeAnnounce(IPEndPoint? unicast = null)
    {
        if (_udp == null) return;
        try
        {
            // 刷新动态信息（歌曲数 / 正在播放）
            try { _songCount = SongCountProvider?.Invoke() ?? _songCount; } catch { }
            try { _nowPlaying = NowPlayingProvider?.Invoke() ?? ""; } catch { }

            var payload = JsonSerializer.Serialize(new
            {
                t = AnnounceTag,
                v = 1,
                name = _deviceName,
                port = _tcpPort,
                sc = _songCount,
                np = _nowPlaying
            });
            var bytes = Encoding.UTF8.GetBytes(payload);

            if (unicast != null)
            {
                _udp.Send(bytes, bytes.Length, unicast);
            }
            else
            {
                _udp.Send(bytes, bytes.Length, new IPEndPoint(IPAddress.Broadcast, _udpPort));
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ClawCircle] 播报失败: {ex.Message}");
        }
    }

    // ──────────────────────────────────────────────────────────
    // 迷你 HTTP 服务（共享本机曲库）
    // ──────────────────────────────────────────────────────────

    private async Task HttpAcceptLoop(CancellationToken ct)
    {
        var tcp = _tcp;
        if (tcp == null) return;
        try
        {
            while (!ct.IsCancellationRequested)
            {
                TcpClient client;
                try
                {
                    client = await tcp.AcceptTcpClientAsync(ct);
                }
                catch (OperationCanceledException) { break; }
                catch (ObjectDisposedException) { break; }
                catch (Exception ex)
                {
                    if (ct.IsCancellationRequested) break;
                    System.Diagnostics.Debug.WriteLine($"[ClawCircle] HTTP Accept 错误: {ex.Message}");
                    continue;
                }

                _ = Task.Run(() => HandleHttpClientAsync(client, ct), ct);
            }
        }
        catch (OperationCanceledException) { }
    }

    private async Task HandleHttpClientAsync(TcpClient client, CancellationToken ct)
    {
        try
        {
            using (client)
            {
                client.ReceiveTimeout = 15000;
                client.SendTimeout = 60000;
                var stream = client.GetStream();

                // 读取 HTTP 头（直到 \r\n\r\n）
                var headerBuf = new MemoryStream();
                var one = new byte[1];
                while (headerBuf.Length < 64 * 1024)
                {
                    int r = await stream.ReadAsync(one, 0, 1, ct);
                    if (r == 0) return;
                    headerBuf.WriteByte(one[0]);
                    var len = (int)headerBuf.Length;
                    var arr = headerBuf.GetBuffer();
                    if (len >= 4 && arr[len - 4] == '\r' && arr[len - 3] == '\n' && arr[len - 2] == '\r' && arr[len - 1] == '\n')
                        break;
                }

                var headerText = Encoding.ASCII.GetString(headerBuf.ToArray());
                var lines = headerText.Split('\n');
                if (lines.Length == 0) return;

                var requestLine = lines[0].Trim();
                var parts = requestLine.Split(' ');
                if (parts.Length < 2) return;
                var method = parts[0];
                var path = parts[1];

                var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                for (int i = 1; i < lines.Length; i++)
                {
                    var hl = lines[i].TrimEnd('\r');
                    if (string.IsNullOrEmpty(hl)) continue;
                    var idx = hl.IndexOf(':');
                    if (idx > 0) headers[hl.Substring(0, idx).Trim()] = hl.Substring(idx + 1).Trim();
                }

                await RouteHttpRequestAsync(stream, method, path, headers, ct);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ClawCircle] HTTP 处理错误: {ex.Message}");
        }
    }

    private async Task RouteHttpRequestAsync(NetworkStream ns, string method, string path, IReadOnlyDictionary<string, string> headers, CancellationToken ct)
    {
        try
        {
            if (path.StartsWith("/api/info", StringComparison.OrdinalIgnoreCase))
            {
                try { _songCount = SongCountProvider?.Invoke() ?? _songCount; } catch { }
                try { _nowPlaying = NowPlayingProvider?.Invoke() ?? ""; } catch { }
                var info = new ClawCircleDeviceInfo
                {
                    Name = _deviceName,
                    Port = _tcpPort,
                    SongCount = _shareLibrary ? _songCount : 0,
                    NowPlaying = _nowPlaying
                };
                await WriteJsonAsync(ns, info, ct);
                return;
            }

            if (path.StartsWith("/api/songs", StringComparison.OrdinalIgnoreCase))
            {
                if (!_shareLibrary)
                {
                    await WriteJsonAsync(ns, new List<ClawCircleSongInfo>(), ct);
                    return;
                }
                var list = await BuildSongListAsync();
                await WriteJsonAsync(ns, list, ct);
                return;
            }

            if (path.StartsWith("/api/stream/", StringComparison.OrdinalIgnoreCase))
            {
                await HandleStreamRequestAsync(ns, method, path, headers, ct);
                return;
            }

            await WriteStatusAsync(ns, 404, "Not Found", ct);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ClawCircle] 路由错误: {ex.Message}");
            try { await WriteStatusAsync(ns, 500, "Internal Server Error", ct); } catch { }
        }
    }

    private async Task HandleStreamRequestAsync(NetworkStream ns, string method, string path, IReadOnlyDictionary<string, string> headers, CancellationToken ct)
    {
        var idStr = path.Substring("/api/stream/".Length).Split('?')[0];
        if (!int.TryParse(idStr, out var id) || id <= 0)
        {
            await WriteStatusAsync(ns, 400, "Bad Request", ct);
            return;
        }

        var song = await _db.GetSongByIdAsync(id);
        if (song == null)
        {
            await WriteStatusAsync(ns, 404, "Song Not Found", ct);
            return;
        }

        var src = SongStreamOpener?.Invoke(id);
        if (src == null)
        {
            await WriteStatusAsync(ns, 404, "Unavailable", ct);
            return;
        }

        await using (src)
        {
            long total = src.CanSeek ? src.Length : (song.FileSize > 0 ? song.FileSize : 0);
            if (total <= 0) total = 1;

            // 解析 Range（仅支持单一范围，用于拖动进度条）
            long start = 0;
            long end = total - 1;
            bool isRange = false;

            if (headers.TryGetValue("Range", out var rangeHeader) && !string.IsNullOrEmpty(rangeHeader)
                && rangeHeader.StartsWith("bytes=", StringComparison.OrdinalIgnoreCase))
            {
                var range = rangeHeader.Substring(6).Trim();
                var dashIdx = range.IndexOf('-');
                if (dashIdx > 0)
                {
                    var startStr = range.Substring(0, dashIdx);
                    var endStr = range.Substring(dashIdx + 1);
                    if (long.TryParse(startStr, out var s) && s >= 0 && s < total)
                    {
                        start = s;
                        isRange = true;
                        if (long.TryParse(endStr, out var e) && e >= start && e < total)
                            end = e;
                    }
                }
            }

            if (method == "HEAD")
            {
                var headStatus = isRange ? 206 : 200;
                var headName = Path.GetFileName(song.FilePath);
                await WriteStreamHeadersAsync(ns, headStatus, isRange ? "Partial Content" : "OK", total, start, end, isRange, headName, ct);
                return;
            }

            var status = isRange ? 206 : 200;
            var statusText = isRange ? "Partial Content" : "OK";
            var originalName = Path.GetFileName(song.FilePath);
            await WriteStreamHeadersAsync(ns, status, statusText, total, start, end, isRange, originalName, ct);

            if (src.CanSeek)
                src.Seek(start, SeekOrigin.Begin);
            else if (start > 0)
            {
                // 不可寻址流（如部分 content://）无法满足偏移，从头传输
                start = 0;
            }

            const int chunk = 64 * 1024;
            var buf = new byte[chunk];
            long remaining = end - start + 1;
            while (remaining > 0)
            {
                int toRead = (int)Math.Min(buf.Length, remaining);
                int n = await src.ReadAsync(buf, 0, toRead, ct);
                if (n == 0) break;
                await ns.WriteAsync(buf, 0, n, ct);
                remaining -= n;
            }
            await ns.FlushAsync(ct);
        }
    }

    private async Task<List<ClawCircleSongInfo>> BuildSongListAsync()
    {
        var songs = await _db.GetSongsWithDetailsAsync();
        var list = new List<ClawCircleSongInfo>(songs.Count);
        foreach (var s in songs)
        {
            list.Add(new ClawCircleSongInfo
            {
                Id = s.Id,
                Title = s.Title,
                Artist = s.Artist,
                Album = s.Album,
                DurationMs = s.Duration,
                Size = s.FileSize
            });
        }
        return list;
    }

    // ──────────────────────────────────────────────────────────
    // HTTP 响应辅助
    // ──────────────────────────────────────────────────────────

    private async Task WriteJsonAsync(NetworkStream ns, object obj, CancellationToken ct)
    {
        var json = JsonSerializer.Serialize(obj, JsonOpts);
        var body = Encoding.UTF8.GetBytes(json);
        var header = $"HTTP/1.1 200 OK\r\nContent-Type: application/json; charset=utf-8\r\nContent-Length: {body.Length}\r\nConnection: close\r\n\r\n";
        var hb = Encoding.ASCII.GetBytes(header);
        await ns.WriteAsync(hb, 0, hb.Length, ct);
        await ns.WriteAsync(body, 0, body.Length, ct);
        await ns.FlushAsync(ct);
    }

    private async Task WriteStatusAsync(NetworkStream ns, int status, string text, CancellationToken ct)
    {
        var header = $"HTTP/1.1 {status} {text}\r\nContent-Length: 0\r\nConnection: close\r\n\r\n";
        var hb = Encoding.ASCII.GetBytes(header);
        await ns.WriteAsync(hb, 0, hb.Length, ct);
        await ns.FlushAsync(ct);
    }

    private async Task WriteStreamHeadersAsync(NetworkStream ns, int status, string text, long total, long start, long end, bool isRange, string originalName, CancellationToken ct)
    {
        var contentLength = end - start + 1;
        var sb = new StringBuilder();
        sb.Append($"HTTP/1.1 {status} {text}\r\n");
        sb.Append("Content-Type: application/octet-stream\r\n");
        sb.Append($"Content-Length: {contentLength}\r\n");
        sb.Append("Accept-Ranges: bytes\r\n");
        if (isRange)
            sb.Append($"Content-Range: bytes {start}-{end}/{total}\r\n");
        if (!string.IsNullOrEmpty(originalName))
            sb.Append($"X-Original-Name: {originalName}\r\n");
        sb.Append("Connection: close\r\n\r\n");
        var hb = Encoding.ASCII.GetBytes(sb.ToString());
        await ns.WriteAsync(hb, 0, hb.Length, ct);
    }

    // ──────────────────────────────────────────────────────────
    // 绑定辅助
    // ──────────────────────────────────────────────────────────

    private static bool BindUdp(out UdpClient? client, out int port)
    {
        // IPv6 双栈优先（可同时收 IPv4 映射包），失败退纯 IPv4
        if (TryBindUdp(IPAddress.IPv6Any, true, out client, out port)) return true;
        return TryBindUdp(IPAddress.Any, false, out client, out port);
    }

    private static bool TryBindUdp(IPAddress addr, bool dualMode, out UdpClient? client, out int port)
    {
        client = null;
        port = UdpBasePort;
        for (int i = 0; i < 20; i++)
        {
            try
            {
                var udp = new UdpClient();
                udp.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
                if (dualMode) udp.Client.DualMode = true;
                udp.Client.Bind(new IPEndPoint(addr, port));
                udp.EnableBroadcast = true;
                client = udp;
                return true;
            }
            catch
            {
                port++;
                client?.Dispose();
                client = null;
            }
        }
        client = null;
        return false;
    }

    private static bool BindTcp(out TcpListener? listener, out int port)
    {
        // IPv6 双栈优先
        if (TryBindTcp(IPAddress.IPv6Any, true, out listener, out port)) return true;
        return TryBindTcp(IPAddress.Any, false, out listener, out port);
    }

    private static bool TryBindTcp(IPAddress addr, bool dualMode, out TcpListener? listener, out int port)
    {
        listener = null;
        port = TcpBasePort;
        for (int i = 0; i < 40; i++)
        {
            try
            {
                var tcp = new TcpListener(addr, port);
                tcp.Server.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
                if (dualMode) tcp.Server.DualMode = true;
                tcp.Start();
                listener = tcp;
                return true;
            }
            catch
            {
                port++;
                listener?.Stop();
                listener = null;
            }
        }
        listener = null;
        return false;
    }

    private static string? GetLocalIPv4()
    {
        try
        {
            var host = Dns.GetHostEntry(Dns.GetHostName());
            foreach (var ip in host.AddressList)
            {
                if (ip.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(ip))
                    return ip.ToString();
            }
        }
        catch { }
        return null;
    }

    public void Dispose()
    {
        try { _ = StopAsync(); } catch { }
        try { _http.Dispose(); } catch { }
    }
}
