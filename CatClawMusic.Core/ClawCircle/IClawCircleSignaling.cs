using System.Text.Json;

namespace CatClawMusic.Core.ClawCircle;

/// <summary>
/// 猫爪圈信令通道抽象（Stage 2 tracker / WebSocket hub）。
/// 实现：<see cref="ClawCircleTrackerClient"/>（基于 ClientWebSocket）。
/// 便于无头测试时替换为内存实现。
/// </summary>
public interface IClawCircleSignaling
{
    /// <summary>收到服务端转发的 relay 消息（来自某对端，data 为转发载荷）。</summary>
    event Action<string, JsonElement>? RelayReceived;

    /// <summary>连接并注册到 tracker。</summary>
    Task ConnectAsync(string deviceId, string name, LibrarySummary? library, CancellationToken ct);

    /// <summary>断开连接。</summary>
    Task DisconnectAsync();

    /// <summary>经 tracker 向指定对端转发一条信令（data 可为任意 JSON 可序列化对象）。</summary>
    Task SendSignalAsync(string toDeviceId, object data, CancellationToken ct);

    /// <summary>查询哪些在线节点拥有某首歌（含 wan/port 供打洞直连）。</summary>
    Task<List<PeerEndpoint>> FindSongAsync(string songKey, CancellationToken ct);

    /// <summary>查询某节点信息（可用于获取自身反射端点）。</summary>
    Task<PeerEndpoint?> QueryPeerAsync(string deviceId, CancellationToken ct);

    /// <summary>更新本机曲库摘要（变更后广播给好友）。</summary>
    Task UpdateLibraryAsync(LibrarySummary library, CancellationToken ct);
}
