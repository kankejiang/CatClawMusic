using System.Text.Json.Serialization;

namespace CatClawMusic.Core.ClawCircle;

/// <summary>
/// 分块传输的片清单（BitComet 式）：把一首歌按固定片大小切片，每片算 SHA256，
/// 接收方逐片校验、收齐后整体校验。songKey 格式与 Stage 2 库摘要一致（artist\u0001title 小写）。
/// </summary>
public class PieceManifest
{
    public string SongKey { get; set; } = "";
    public long TotalSize { get; set; }
    public int PieceSize { get; set; }
    public List<string> PieceHashes { get; set; } = new(); // 每片 SHA256 hex（小写）
    public string OverallHash { get; set; } = ""; // 整文件 SHA256 hex（小写）

    [JsonIgnore]
    public int PieceCount => PieceHashes.Count;
}

/// <summary>
/// 经 Stage 2 relay 通道传递的 P2P 控制消息（打洞协调用）。
/// </summary>
public class P2PRelayMessage
{
    /// <summary>punch-request = 发起打洞；punch-ready = 已就绪并开始回打。</summary>
    public string Kind { get; set; } = "";
    public string? DeviceId { get; set; }
    /// <summary>发送方观察到的自身公网（反射）端点，供对端回打。</summary>
    public string? FromWan { get; set; }
    public int? FromPort { get; set; }
    public string? Nonce { get; set; }
    public string? SongKey { get; set; }
}

/// <summary>持有某首歌的节点端点（来自 Stage 2 find_song 的 PeerInfo）。</summary>
public class PeerEndpoint
{
    public string DeviceId { get; set; } = "";
    public string? Wan { get; set; }
    public int? Port { get; set; }
}

/// <summary>传输进度状态（供 UI 展示）。</summary>
public enum TransferStateKind
{
    Idle,
    Punching,
    Transferring,
    Verifying,
    Completed,
    Failed
}

public class TransferProgress
{
    public string SongKey { get; set; } = "";
    public TransferStateKind State { get; set; }
    public int ReceivedPieces { get; set; }
    public int TotalPieces { get; set; }
    public long ReceivedBytes { get; set; }
    public long TotalBytes { get; set; }
    public string? PeerDeviceId { get; set; }
    public string Message { get; set; } = "";
}

/// <summary>
/// 曲库摘要（与 Stage 2 服务端 LibrarySummary 字段一致，camelCase 兼容）。
/// 上报给 tracker 用于 find_song 匹配。SongKeys 为 artist\u0001title 小写。
/// </summary>
public class LibrarySummary
{
    public int SongCount { get; set; }
    public int AlbumCount { get; set; }
    public int ArtistCount { get; set; }
    public List<string> SongKeys { get; set; } = new();
}

/// <summary>歌曲键工具：统一为 artist\u0001title 小写，作为曲库摘要与传输的匹配键。</summary>
public static class SongKey
{
    public static string Of(string? artist, string? title)
        => $"{(artist ?? "").ToLowerInvariant()}\u0001{(title ?? "").ToLowerInvariant()}";

    public static void Deconstruct(string key, out string artist, out string title)
    {
        var idx = key.IndexOf('\u0001');
        if (idx < 0) { artist = ""; title = key; return; }
        artist = key.Substring(0, idx);
        title = key.Substring(idx + 1);
    }
}

/// <summary>Stage 2 响应里的节点信息（仅需 DeviceId/Wan/Port 用于打洞直连）。</summary>
public class PeerInfoDto
{
    public string DeviceId { get; set; } = "";
    public string? Wan { get; set; }
    public int? Port { get; set; }
}
