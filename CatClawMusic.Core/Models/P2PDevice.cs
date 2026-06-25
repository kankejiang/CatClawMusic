namespace CatClawMusic.Core.Models;

/// <summary>
/// P2P 设备信息（对应 DHT 网络中发现的设备）
/// </summary>
public class P2PDevice
{
    /// <summary>设备显示名称</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>设备 IP 地址</summary>
    public string Ip { get; set; } = string.Empty;

    /// <summary>HTTP API 端口</summary>
    public int HttpPort { get; set; } = 5000;

    /// <summary>DHT UDP 端口</summary>
    public int DhtPort { get; set; } = 6881;

    /// <summary>设备唯一 ID（DHT NodeId）</summary>
    public string NodeId { get; set; } = string.Empty;

    /// <summary>最后在线时间</summary>
    public DateTime LastSeen { get; set; } = DateTime.UtcNow;

    /// <summary>歌曲数量</summary>
    public int SongCount { get; set; }

    /// <summary>是否在线</summary>
    public bool IsOnline => (DateTime.UtcNow - LastSeen).TotalSeconds < 120;

    /// <summary>构建 HTTP 基础 URL</summary>
    public string GetBaseUrl() => $"http://{Ip}:{HttpPort}";
}

/// <summary>
/// P2P 配置
/// </summary>
public class P2PConfig
{
    /// <summary>是否启用 P2P 功能</summary>
    public bool Enabled { get; set; } = false;

    /// <summary>下载限速 (KB/s)，默认 128</summary>
    public int RateLimitKBs { get; set; } = 128;

    /// <summary>Bootstrap 节点地址</summary>
    public string BootstrapNode { get; set; } = "music.08102516.xyz:6881";

    /// <summary>本地 DHT 端口（Android 端作为轻客户端，仅查询用）</summary>
    public int DhtPort { get; set; } = 6882;

    /// <summary>本地设备名</summary>
    public string DeviceName { get; set; } = "CatClawApp";
}

/// <summary>
/// 服务端元数据响应
/// </summary>
public class ServerMetadata
{
    /// <summary>歌曲列表</summary>
    public List<ServerSong> Songs { get; set; } = new();

    /// <summary>艺术家列表</summary>
    public List<ServerArtist> Artists { get; set; } = new();

    /// <summary>专辑列表</summary>
    public List<ServerAlbum> Albums { get; set; } = new();

    /// <summary>数据库版本（用于增量同步）</summary>
    public long Version { get; set; }
}

public class ServerSong
{
    public long Id { get; set; }
    public string Title { get; set; } = "";
    public long ArtistId { get; set; }
    public long AlbumId { get; set; }
    public string Artist { get; set; } = "";
    public string Album { get; set; } = "";
    public int Duration { get; set; }
    public string FilePath { get; set; } = "";
    public long FileSize { get; set; }
    public int Bitrate { get; set; }
    public int TrackNumber { get; set; }
    public int Year { get; set; }
    public string Genre { get; set; } = "";
    public long DateAdded { get; set; }
    public long DateModified { get; set; }
    public string CoverArtPath { get; set; } = "";
    public string LyricsPath { get; set; } = "";
    public string AllArtists { get; set; } = "";
}

public class ServerArtist
{
    public long Id { get; set; }
    public string Name { get; set; } = "";
    public string Cover { get; set; } = "";
}

public class ServerAlbum
{
    public long Id { get; set; }
    public string Title { get; set; } = "";
    public long ArtistId { get; set; }
    public string Artist { get; set; } = "";
}
