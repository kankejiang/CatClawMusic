using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CatClawMusic.Core.Interfaces;

namespace CatClawMusic.Core.ClawCircle;

/// <summary>
/// 猫爪圈 Stage 3 传输引擎：基于 Stage 2 信令做 UDP NAT 打洞，打洞成功后走直连 UDP 通道，
/// 以 BitComet 式分块 + 逐片 SHA256 校验 + 滑动窗口 + 超时重传 实现可靠传输，并支持做种
/// （收到别人的分片请求时回传本地拥有的分片）。
/// </summary>
public class P2PTransferEngine : IDisposable
{
    private const int DefaultPieceSize = 16 * 1024; // 16KB（须 < UDP 数据报上限 ~65507；过大会导致发送失败）
    private const int Window = 16;                   // 最大在途分片数
    private const int PunchIntervalMs = 250;
    private const int PunchDurationMs = 9000;
    private const int RetransmitMs = 1000;

    // relay 消息反序列化：大小写不敏感（信令客户端以 camelCase 发送）
    private static readonly JsonSerializerOptions RelayOpts = new() { PropertyNameCaseInsensitive = true };
    private const int ChunkWaitMs = 600;

    private readonly IClawCircleSignaling _signaling;
    private readonly IClawCircleDataProvider _data;
    private readonly UdpDirectChannel _udp = new();
    private readonly int _pieceSize;
    private readonly string _deviceId;
    private readonly SemaphoreSlim _chunkEvent = new(0);
    private readonly object _lock = new();

    private CancellationTokenSource? _cts;
    private Task? _recvLoop;
    private PeerEndpoint? _selfEndpoint;
    private readonly Random _rng = new();

    // 下载（leech）状态，按 songKey 区分（同 songKey 同时只进行一次下载）
    private readonly Dictionary<string, TransferState> _transfers = new();
    // 已知对端端点，按 "wan:port" 索引，用于打洞回包与匹配
    private readonly Dictionary<string, PeerEndpoint> _knownPeers = new();
    // 打洞是否已通（按 deviceId）
    private readonly HashSet<string> _holeOpened = new();
    // 做种方已向某 (senderKey:songKey) 发送过清单，避免重复
    private readonly HashSet<string> _manifestSent = new();
    // 做种方已构建的清单缓存（避免每次重算哈希）
    private readonly ConcurrentDictionary<string, PieceManifest> _seedManifests = new();

    private readonly string _serverHost;
    private readonly int _stunPort;

    public event Action<TransferProgress>? ProgressChanged;

    public UdpDirectChannel Udp => _udp;

    public P2PTransferEngine(IClawCircleSignaling signaling, IClawCircleDataProvider data, string deviceId,
        string serverHost, int stunPort, int pieceSize = DefaultPieceSize)
    {
        _signaling = signaling;
        _data = data;
        _deviceId = deviceId;
        _pieceSize = pieceSize;
        _serverHost = serverHost;
        _stunPort = stunPort;
    }

    // ── 生命周期 ──

    public void Start()
    {
        _signaling.RelayReceived += OnRelay;
        _cts = new CancellationTokenSource();
        _recvLoop = Task.Run(() => UdpRecvLoopAsync(_cts.Token));
        // 向服务端 STUN 端口打个包，让服务端观察到本机 UDP 反射端点（NAT 打洞前提）
        _ = StunRegisterAsync(_cts.Token);
    }

    /// <summary>向服务端 STUN 端口发送本机 deviceId，触发服务端记录 UDP 反射端点。</summary>
    public async Task StunRegisterAsync(CancellationToken ct)
    {
        if (string.IsNullOrEmpty(_serverHost) || _stunPort <= 0) return;
        var payload = System.Text.Encoding.UTF8.GetBytes($"{{\"deviceId\":\"{_deviceId}\"}}");
        // 连打几次以防丢包
        for (int i = 0; i < 3; i++)
        {
            try { await _udp.SendAsync(_serverHost, _stunPort, payload, ct); } catch { }
            await Task.Delay(150, ct);
        }
    }

    public void Stop()
    {
        _signaling.RelayReceived -= OnRelay;
        try { _cts?.Cancel(); } catch { }
        try { _udp.Dispose(); } catch { }
    }

    /// <summary>获取并缓存本机在 tracker 观察到的 UDP 反射端点（供打洞时告知对端）。
    /// STUN 包可能在查询时尚未处理，故重试若干次直到拿到 wan/port。</summary>
    public async Task<PeerEndpoint?> EnsureSelfEndpointAsync(CancellationToken ct)
    {
        if (_selfEndpoint != null && !string.IsNullOrEmpty(_selfEndpoint.Wan) && _selfEndpoint.Port is > 0)
            return _selfEndpoint;
        for (int i = 0; i < 10; i++)
        {
            var self = await _signaling.QueryPeerAsync(_deviceId, ct);
            if (self != null && !string.IsNullOrEmpty(self.Wan) && self.Port is > 0)
            {
                _selfEndpoint = self;
                return self;
            }
            await Task.Delay(300, ct);
        }
        return _selfEndpoint;
    }

    /// <summary>向 tracker 上报本机曲库摘要。</summary>
    public Task PublishLibraryAsync(LibrarySummary library, CancellationToken ct)
        => _signaling.UpdateLibraryAsync(library, ct);

    // ── 下载一首歌（leech）──

    public async Task<bool> RequestSongAsync(string songKey, string destinationPath, CancellationToken ct = default)
    {
        var holders = await _signaling.FindSongAsync(songKey, ct);
        var holder = holders.FirstOrDefault(h => !string.IsNullOrEmpty(h.Wan) && h.Port is > 0);
        if (holder == null)
        {
            Report(songKey, TransferStateKind.Failed, 0, 0, null, "无可用在线节点");
            return false;
        }

        var self = await EnsureSelfEndpointAsync(ct);
        if (self == null || string.IsNullOrEmpty(self.Wan) || self.Port is not > 0)
        {
            Report(songKey, TransferStateKind.Failed, 0, 0, holder.DeviceId, "无法获取本机反射端点");
            return false;
        }

        var state = new TransferState
        {
            SongKey = songKey,
            Peer = holder,
            Ct = ct,
            HoleOpenTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously)
        };
        lock (_lock) _transfers[songKey] = state;
        lock (_lock) _knownPeers[$"{holder.Wan}:{holder.Port}"] = holder;

        // 1) 打洞：发送 punch-request 信令 + 开始向对端打洞包
        var nonce = Guid.NewGuid().ToString("N");
        await _signaling.SendSignalAsync(holder.DeviceId, new P2PRelayMessage
        {
            Kind = "punch-request",
            DeviceId = _deviceId,
            FromWan = self.Wan,
            FromPort = self.Port,
            Nonce = nonce
        }, ct);

        Report(songKey, TransferStateKind.Punching, 0, 0, holder.DeviceId, "正在 NAT 打洞…");
        _ = BeginPunchAsync(holder, nonce, ct);

        // 2) 等待直连通道打通（对端打洞包到达）
        using var punchCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        punchCts.CancelAfter(PunchDurationMs);
        bool holeOpen;
        try { holeOpen = await state.HoleOpenTcs.Task.WaitAsync(punchCts.Token); }
        catch (OperationCanceledException) { holeOpen = false; }

        if (!holeOpen)
        {
            Report(songKey, TransferStateKind.Failed, 0, 0, holder.DeviceId, "NAT 打洞失败（对称型 NAT 可能无法直连）");
            lock (_lock) _transfers.Remove(songKey);
            return false;
        }

        // 3) 请求首片（触发对端先发 MANIFEST），并纳入在途跟踪以便超时重传
        Report(songKey, TransferStateKind.Transferring, 0, 0, holder.DeviceId, "直连已建立，开始分块传输");
        state.InFlight.Add(0);
        state.LastRequest[0] = DateTime.UtcNow;
        state.NextToRequest = 1;
        await SendFrameAsync(holder, P2PFrameType.ChunkRequest, P2PFrame.MakeChunkRequest(songKey, 0), ct);

        // 4) 等待清单
        using var manCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        manCts.CancelAfter(TimeSpan.FromSeconds(15));
        PieceManifest manifest;
        try { manifest = await state.ManifestTcs.Task.WaitAsync(manCts.Token); }
        catch (OperationCanceledException)
        {
            Report(songKey, TransferStateKind.Failed, 0, 0, holder.DeviceId, "未收到分块清单");
            lock (_lock) _transfers.Remove(songKey);
            return false;
        }

        state.Session = await _data.BeginReceiveAsync(songKey, manifest, ct);
        // Received[] 已在 HandleManifest 中按清单片数建好；此处把会话就绪前已缓冲的分片落盘
        FlushPending(state);

        // 5) 窗口化请求 + 超时重传，直到收齐
        bool allDone = state.ReceivedCount >= manifest.PieceCount;
        while (!allDone)
        {
            FillWindow(state);
            try { await _chunkEvent.WaitAsync(ChunkWaitMs, ct); }
            catch (OperationCanceledException) { break; }
            ResendTimedOut(state);
            allDone = state.ReceivedCount >= manifest.PieceCount;
        }

        // 6) 整体校验 + 落盘
        Report(songKey, TransferStateKind.Verifying, manifest.PieceCount, manifest.PieceCount,
            holder.DeviceId, "分片收齐，整体校验中");
        var ok = await VerifyAndFinalizeAsync(state, destinationPath, ct);
        if (ok)
        {
            // 通知对端完成
            try { await SendFrameAsync(holder, P2PFrameType.Done, P2PFrame.MakeDone(songKey, HexToBytes(manifest.OverallHash)), ct); } catch { }
            Report(songKey, TransferStateKind.Completed, manifest.PieceCount, manifest.PieceCount,
                holder.DeviceId, "下载完成");
        }
        else
        {
            Report(songKey, TransferStateKind.Failed, state.ReceivedCount, manifest.PieceCount,
                holder.DeviceId, "整体校验失败");
        }

        lock (_lock) _transfers.Remove(songKey);
        return ok;
    }

    private void FillWindow(TransferState s)
    {
        if (s.Manifest == null) return;
        int queued = 0;
        while (s.InFlight.Count < Window && s.NextToRequest < s.Manifest.PieceCount && queued < Window)
        {
            var idx = s.NextToRequest++;
            s.InFlight.Add(idx);
            s.LastRequest[idx] = DateTime.UtcNow;
            _ = SendFrameAsync(s.Peer, P2PFrameType.ChunkRequest, P2PFrame.MakeChunkRequest(s.SongKey, idx), s.Ct);
            queued++;
        }
    }

    private void ResendTimedOut(TransferState s)
    {
        var now = DateTime.UtcNow;
        foreach (var idx in s.InFlight.ToArray())
        {
            if (s.Received[idx]) { s.InFlight.Remove(idx); continue; }
            if ((now - s.LastRequest[idx]).TotalMilliseconds > RetransmitMs)
            {
                s.LastRequest[idx] = now;
                _ = SendFrameAsync(s.Peer, P2PFrameType.ChunkRequest, P2PFrame.MakeChunkRequest(s.SongKey, idx), s.Ct);
            }
        }
    }

    private async Task<bool> VerifyAndFinalizeAsync(TransferState s, string destinationPath, CancellationToken ct)
    {
        if (s.Manifest == null || s.Session == null) return false;
        try
        {
            await _data.FinalizeReceiveAsync(s.Session, destinationPath, ct);
            return true;
        }
        catch { return false; }
    }

    // ── 信令回调：打洞协调 ──

    private async void OnRelay(string from, JsonElement data)
    {
        try
        {
            var msg = data.Deserialize<P2PRelayMessage>(RelayOpts);
            if (msg == null) return;
            if (msg.Kind == "punch-request")
            {
                if (string.IsNullOrEmpty(msg.FromWan) || msg.FromPort is not > 0) return;
                var peer = new PeerEndpoint { DeviceId = from, Wan = msg.FromWan, Port = msg.FromPort };
                lock (_lock) _knownPeers[$"{peer.Wan}:{peer.Port}"] = peer;
                // 回打洞包 + 告知已就绪
                _ = BeginPunchAsync(peer, msg.Nonce ?? "", CancellationToken.None);
                var self = _selfEndpoint
                           ?? await SafeSelfEndpointAsync();
                if (self != null)
                    await _signaling.SendSignalAsync(from, new P2PRelayMessage
                    {
                        Kind = "punch-ready",
                        DeviceId = _deviceId,
                        FromWan = self.Wan,
                        FromPort = self.Port,
                        Nonce = msg.Nonce
                    }, CancellationToken.None);
            }
            else if (msg.Kind == "punch-ready")
            {
                if (!string.IsNullOrEmpty(msg.FromWan) && msg.FromPort is > 0)
                {
                    var peer = new PeerEndpoint { DeviceId = from, Wan = msg.FromWan, Port = msg.FromPort };
                    lock (_lock) _knownPeers[$"{peer.Wan}:{peer.Port}"] = peer;
                    _ = BeginPunchAsync(peer, msg.Nonce ?? "", CancellationToken.None);
                }
            }
        }
        catch { }
    }

    private async Task<PeerEndpoint?> SafeSelfEndpointAsync()
    {
        try { return await EnsureSelfEndpointAsync(CancellationToken.None); }
        catch { return null; }
    }

    // ── 打洞：持续向对端发送 PUNCH 帧直至通道打通 ──

    private async Task BeginPunchAsync(PeerEndpoint peer, string nonce, CancellationToken ct)
    {
        var frame = P2PFrame.Encode(P2PFrameType.Punch, P2PFrame.MakePunch(_deviceId, nonce));
        var deadline = DateTime.UtcNow + TimeSpan.FromMilliseconds(PunchDurationMs);
        try
        {
            while (DateTime.UtcNow < deadline && !ct.IsCancellationRequested)
            {
                bool opened;
                lock (_lock) opened = _holeOpened.Contains(peer.DeviceId);
                if (opened) break;
                try { await _udp.SendAsync(peer.Wan!, peer.Port!.Value, frame, ct); }
                catch (Exception ex) { Log.Debug("P2PTransferEngine", $"[ClawCircle] 打洞发送失败: {ex.Message}"); }
                await Task.Delay(PunchIntervalMs, ct);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { Log.Debug("P2PTransferEngine", $"[ClawCircle] 打洞异常: {ex.Message}"); }
    }

    // ── 直连 UDP 接收循环 ──

    private async Task UdpRecvLoopAsync(CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                string host; int port; byte[] frame;
                try { (host, port, frame) = await _udp.ReceiveAsync(ct); }
                catch (OperationCanceledException) { break; }
                catch { if (ct.IsCancellationRequested) break; continue; }

                if (!P2PFrame.TryDecode(frame, out var type, out var payload))
                    continue; // 非 P2P 帧（如 STUN 回包），忽略

                switch (type)
                {
                    case P2PFrameType.Punch:
                        HandlePunch(host, port, payload);
                        break;
                    case P2PFrameType.ChunkRequest:
                        _ = HandleChunkRequestAsync(host, port, payload);
                        break;
                    case P2PFrameType.Chunk:
                        HandleChunk(host, port, payload);
                        break;
                    case P2PFrameType.Manifest:
                        HandleManifest(payload);
                        break;
                    // Ack / Done：leecher 已发送确认；做种方无需处理，忽略
                    default:
                        break;
                }
            }
        }
        catch (OperationCanceledException) { }
    }

    private void HandlePunch(string host, int port, byte[] payload)
    {
        P2PFrame.ParsePunch(payload, out var fromId, out _);
        // 回一个打洞包，帮对端打通
        var peerEp = ResolvePeer(host, port, fromId);
        if (peerEp != null)
        {
            _ = _udp.SendAsync(peerEp.Wan!, peerEp.Port!.Value,
                P2PFrame.Encode(P2PFrameType.Punch, P2PFrame.MakePunch(_deviceId, "pong")), CancellationToken.None);
            lock (_lock)
            {
                _holeOpened.Add(peerEp.DeviceId);
                if (_transfers.TryGetValue(FirstSongKeyForPeer(peerEp.DeviceId), out var st))
                    st.HoleOpenTcs.TrySetResult(true);
            }
        }
    }

    private PeerEndpoint? ResolvePeer(string host, int port, string fromId)
    {
        lock (_lock)
        {
            if (_knownPeers.TryGetValue($"{host}:{port}", out var p)) return p;
            if (!string.IsNullOrEmpty(fromId) && _knownPeers.Values.FirstOrDefault(p => p.DeviceId == fromId) is { } byId)
                return byId;
        }
        return null;
    }

    private string FirstSongKeyForPeer(string deviceId)
    {
        lock (_lock)
            return _transfers.Values.FirstOrDefault(t => t.Peer.DeviceId == deviceId)?.SongKey ?? "";
    }

    private void HandleManifest(byte[] payload)
    {
        P2PFrame.ParseManifest(payload, out var songKey, out var manifest);
        if (string.IsNullOrEmpty(songKey)) return;
        lock (_lock)
        {
            if (_transfers.TryGetValue(songKey, out var st) && st.Manifest == null)
            {
                st.Manifest = manifest;
                st.Received = new bool[manifest.PieceCount];
                st.ManifestTcs.TrySetResult(manifest);
            }
        }
    }

    private void HandleChunk(string host, int port, byte[] payload)
    {
        P2PFrame.ParseChunk(payload, out var songKey, out var idx, out var total, out var data);
        TransferState? st = null;
        lock (_lock) _transfers.TryGetValue(songKey, out st);
        if (st == null || st.Manifest == null) return;
        if (idx < 0 || idx >= st.Manifest.PieceCount) return;
        if (st.Received[idx]) return;

        // 逐片校验
        var hash = SHA256.HashData(data);
        var hex = BytesToHex(hash);
        if (!string.Equals(hex, st.Manifest.PieceHashes[idx], StringComparison.OrdinalIgnoreCase))
            return; // 校验失败，不标记，等重传

        // 写盘 + 确认
        var peerEp = ResolvePeer(host, port, "");
        if (st.Session == null)
        {
            // 会话尚未就绪（BeginReceive 未完成）：先缓存已校验分片，等会话就绪后落盘
            st.PendingChunks[idx] = data;
            return;
        }

        _ = _data.WriteReceivedPieceAsync(st.Session, idx, data, st.Ct);
        if (peerEp != null)
            _ = _udp.SendAsync(peerEp.Wan!, peerEp.Port!.Value,
                P2PFrame.Encode(P2PFrameType.Ack, P2PFrame.MakeAck(songKey, idx)), CancellationToken.None);

        st.Received[idx] = true;
        st.InFlight.Remove(idx);
        st.ReceivedBytes += data.Length;
        st.ReceivedCount++;
        _chunkEvent.Release();
        Report(songKey, TransferStateKind.Transferring, st.ReceivedCount, st.Manifest.PieceCount,
            st.Peer.DeviceId, "", st.ReceivedBytes, st.Manifest.TotalSize);
    }

    /// <summary>会话就绪后，把此前已校验并缓存的分片落盘。</summary>
    private void FlushPending(TransferState st)
    {
        if (st.Session == null || st.Manifest == null) return;
        if (st.PendingChunks.Count == 0) return;
        foreach (var kv in st.PendingChunks)
        {
            var idx = kv.Key;
            var data = kv.Value;
            if (idx >= 0 && idx < st.Received.Length && !st.Received[idx])
            {
                _ = _data.WriteReceivedPieceAsync(st.Session, idx, data, st.Ct);
                st.Received[idx] = true;
                st.InFlight.Remove(idx);
                st.ReceivedBytes += data.Length;
                st.ReceivedCount++;
                _chunkEvent.Release();
            }
        }
        st.PendingChunks.Clear();
    }

    private async Task HandleChunkRequestAsync(string host, int port, byte[] payload)
    {
        P2PFrame.ParseChunkRequest(payload, out var songKey, out var idx);
        try
        {
            var totalSizeOpt = await _data.GetLocalSongSizeAsync(songKey, CancellationToken.None);
            if (!totalSizeOpt.HasValue) return; // 本机无此歌，忽略
            var totalSize = totalSizeOpt.Value;
            var pieceCount = (int)((totalSize + _pieceSize - 1) / _pieceSize);
            var piece = await _data.ReadLocalPieceAsync(songKey, idx, _pieceSize, CancellationToken.None);
            if (piece == null) return;

            // 首次为该 (sender, songKey) 发送清单
            var senderKey = $"{host}:{port}";
            var mkey = $"{senderKey}:{songKey}";
            bool needManifest;
            lock (_lock) needManifest = _manifestSent.Add(mkey);
            if (needManifest)
            {
                var manifest = await GetOrBuildSeedManifestAsync(songKey, totalSize, pieceCount);
                await _udp.SendAsync(host, port,
                    P2PFrame.Encode(P2PFrameType.Manifest, P2PFrame.MakeManifest(songKey, manifest)),
                    CancellationToken.None);
            }

            await _udp.SendAsync(host, port,
                P2PFrame.Encode(P2PFrameType.Chunk, P2PFrame.MakeChunk(songKey, idx, pieceCount, piece)),
                CancellationToken.None);
        }
        catch { }
    }

    private async Task<PieceManifest> GetOrBuildSeedManifestAsync(string songKey, long totalSize, int pieceCount)
    {
        if (_seedManifests.TryGetValue(songKey, out var cached)) return cached;
        var hashes = new List<string>(pieceCount);
        using var overall = SHA256.Create();
        // 分片哈希 + 整体哈希（按片顺序拼接）
        for (int i = 0; i < pieceCount; i++)
        {
            var piece = await _data.ReadLocalPieceAsync(songKey, i, _pieceSize, CancellationToken.None)
                        ?? Array.Empty<byte>();
            hashes.Add(BytesToHex(SHA256.HashData(piece)));
            overall.TransformBlock(piece, 0, piece.Length, null, 0);
        }
        overall.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
        var manifest = new PieceManifest
        {
            SongKey = songKey,
            TotalSize = totalSize,
            PieceSize = _pieceSize,
            PieceHashes = hashes,
            OverallHash = BytesToHex(overall.Hash!)
        };
        _seedManifests[songKey] = manifest;
        return manifest;
    }

    // ── 辅助 ──

    private async Task SendFrameAsync(PeerEndpoint peer, P2PFrameType type, byte[] payload, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(peer.Wan) || peer.Port is not > 0) return;
        try { await _udp.SendAsync(peer.Wan, peer.Port.Value, P2PFrame.Encode(type, payload), ct); }
        catch { }
    }

    private void Report(string songKey, TransferStateKind state, int recv, int total, string? peer, string msg = "", long recvBytes = 0, long totalBytes = 0)
    {
        ProgressChanged?.Invoke(new TransferProgress
        {
            SongKey = songKey,
            State = state,
            ReceivedPieces = recv,
            TotalPieces = total,
            ReceivedBytes = recvBytes,
            TotalBytes = totalBytes,
            PeerDeviceId = peer,
            Message = msg
        });
    }

    private static string BytesToHex(byte[] b)
    {
        var sb = new StringBuilder(b.Length * 2);
        foreach (var x in b) sb.Append(x.ToString("x2"));
        return sb.ToString();
    }

    private static byte[] HexToBytes(string hex)
    {
        var n = hex.Length / 2;
        var arr = new byte[n];
        for (int i = 0; i < n; i++) arr[i] = Convert.ToByte(hex.Substring(i * 2, 2), 16);
        return arr;
    }

    public void Dispose() => Stop();

    // 单次下载状态
    private class TransferState
    {
        public string SongKey = "";
        public PeerEndpoint Peer = null!;
        public PieceManifest? Manifest;
        public object? Session;
        public bool[] Received = Array.Empty<bool>();
        public HashSet<int> InFlight { get; } = new();
        public Dictionary<int, DateTime> LastRequest { get; } = new();
        public int NextToRequest;
        public int ReceivedCount;
        public long ReceivedBytes;
        public CancellationToken Ct;
        public TaskCompletionSource<bool> HoleOpenTcs = null!;
        public TaskCompletionSource<PieceManifest> ManifestTcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
        /// <summary>会话就绪前已校验并缓存的分片（按片号）。</summary>
        public Dictionary<int, byte[]> PendingChunks { get; } = new();
    }
}
