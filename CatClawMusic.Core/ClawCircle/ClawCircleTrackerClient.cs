using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

namespace CatClawMusic.Core.ClawCircle;

/// <summary>
/// 基于 ClientWebSocket 的猫爪圈信令客户端，对接 Stage 2 的 <c>/ws/clawcircle</c> tracker。
/// 负责 register / find_song / query_peer / signal(relay) / library_update。
/// </summary>
public class ClawCircleTrackerClient : IClawCircleSignaling, IDisposable
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    private readonly string _url;
    private ClientWebSocket? _ws;
    private CancellationTokenSource? _cts;
    private readonly ConcurrentDictionary<string, TaskCompletionSource<JsonElement>> _pending = new();
    private readonly SemaphoreSlim _sendLock = new(1, 1);

    public event Action<string, JsonElement>? RelayReceived;

    /// <summary>
    /// <param name="trackerUrl">形如 ws://host:port/ws/clawcircle</param>
    /// <param name="token">内网访问令牌（与 Web API 一致）</param>
    /// </summary>
    public ClawCircleTrackerClient(string trackerUrl, string token)
    {
        var sep = trackerUrl.Contains('?') ? '&' : '?';
        _url = $"{trackerUrl}{sep}token={Uri.EscapeDataString(token)}";
    }

    public async Task ConnectAsync(string deviceId, string name, LibrarySummary? library, CancellationToken ct)
    {
        _ws = new ClientWebSocket();
        await _ws.ConnectAsync(new Uri(_url), ct);
        _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);

        // 发送 register（deviceId/name 放消息体；token 已在 URL）
        await SendRawAsync(new
        {
            type = "register",
            deviceId,
            name,
            library
        }, ct);

        _ = Task.Run(() => ReceiveLoopAsync(_cts.Token), _cts.Token);
    }

    public async Task DisconnectAsync()
    {
        try { _cts?.Cancel(); } catch { }
        try { if (_ws?.State == WebSocketState.Open) await _ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "bye", CancellationToken.None); } catch { }
        try { _ws?.Dispose(); } catch { }
        _ws = null;
    }

    public async Task SendSignalAsync(string toDeviceId, object data, CancellationToken ct)
    {
        await SendRawAsync(new { type = "signal", to = toDeviceId, data }, ct);
    }

    public async Task UpdateLibraryAsync(LibrarySummary library, CancellationToken ct)
    {
        await SendRawAsync(new { type = "library_update", library }, ct);
    }

    public async Task<List<PeerEndpoint>> FindSongAsync(string songKey, CancellationToken ct)
    {
        var tcs = RegisterPending("song_holders");
        await SendRawAsync(new { type = "find_song", songKey }, ct);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct);
        linked.CancelAfter(TimeSpan.FromSeconds(10));
        var el = await tcs.Task.WaitAsync(linked.Token);
        var holders = new List<PeerEndpoint>();
        if (el.TryGetProperty("holders", out var arr) && arr.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in arr.EnumerateArray())
            {
                holders.Add(new PeerEndpoint
                {
                    DeviceId = item.TryGetProperty("deviceId", out var d) ? (d.GetString() ?? "") : "",
                    Wan = item.TryGetProperty("wan", out var w) ? w.GetString() : null,
                    Port = item.TryGetProperty("port", out var p) && p.TryGetInt32(out var pv) ? pv : null
                });
            }
        }
        return holders;
    }

    public async Task<PeerEndpoint?> QueryPeerAsync(string deviceId, CancellationToken ct)
    {
        var tcs = RegisterPending("peer_info");
        await SendRawAsync(new { type = "query_peer", deviceId }, ct);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct);
        linked.CancelAfter(TimeSpan.FromSeconds(10));
        var el = await tcs.Task.WaitAsync(linked.Token);
        if (el.TryGetProperty("peer", out var peer) && peer.ValueKind == JsonValueKind.Object)
        {
            return new PeerEndpoint
            {
                DeviceId = peer.TryGetProperty("deviceId", out var d) ? (d.GetString() ?? "") : "",
                Wan = peer.TryGetProperty("wan", out var w) ? w.GetString() : null,
                Port = peer.TryGetProperty("port", out var p) && p.TryGetInt32(out var pv) ? pv : null
            };
        }
        return null;
    }

    // ── 内部 ──

    private TaskCompletionSource<JsonElement> RegisterPending(string responseType)
    {
        var tcs = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending[responseType] = tcs;
        return tcs;
    }

    private async Task SendRawAsync(object obj, CancellationToken ct)
    {
        if (_ws == null) throw new InvalidOperationException("未连接");
        var bytes = JsonSerializer.SerializeToUtf8Bytes(obj, JsonOpts);
        await _sendLock.WaitAsync(ct);
        try
        {
            await _ws.SendAsync(bytes, WebSocketMessageType.Text, true, ct);
        }
        finally
        {
            _sendLock.Release();
        }
    }

    private async Task ReceiveLoopAsync(CancellationToken ct)
    {
        var buf = new byte[8192];
        try
        {
            while (!ct.IsCancellationRequested && _ws?.State == WebSocketState.Open)
            {
                using var ms = new MemoryStream();
                WebSocketReceiveResult result;
                do
                {
                    result = await _ws.ReceiveAsync(new ArraySegment<byte>(buf), ct);
                    if (result.MessageType == WebSocketMessageType.Close) return;
                    ms.Write(buf, 0, result.Count);
                } while (!result.EndOfMessage);

                ms.Position = 0;
                using var doc = await JsonDocument.ParseAsync(ms, cancellationToken: ct);
                var root = doc.RootElement;
                var type = root.TryGetProperty("type", out var tEl) ? (tEl.GetString() ?? "") : "";

                if (type == "relay")
                {
                    var from = root.TryGetProperty("from", out var f) ? (f.GetString() ?? "") : "";
                    if (root.TryGetProperty("data", out var dataEl))
                        RelayReceived?.Invoke(from, dataEl.Clone());
                }
                else if (_pending.TryRemove(type, out var tcs))
                {
                    tcs.TrySetResult(root.Clone());
                }
                // 其余（welcome/peer_online/peer_update/peer_offline/error）忽略
            }
        }
        catch (OperationCanceledException) { }
        catch (WebSocketException) { }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ClawCircle] 信令接收异常: {ex.Message}");
        }
    }

    public void Dispose()
    {
        try { _ = DisconnectAsync(); } catch { }
    }
}
