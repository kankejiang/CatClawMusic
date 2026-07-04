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

/// <summary>服务端返回的单首歌曲元数据</summary>
public class ServerSong
{
    /// <summary>歌曲唯一 ID</summary>
    public long Id { get; set; }

    /// <summary>歌曲标题</summary>
    public string Title { get; set; } = "";

    /// <summary>主艺术家 ID</summary>
    public long ArtistId { get; set; }

    /// <summary>所属专辑 ID</summary>
    public long AlbumId { get; set; }

    /// <summary>主艺术家名称</summary>
    public string Artist { get; set; } = "";

    /// <summary>专辑名称</summary>
    public string Album { get; set; } = "";

    /// <summary>时长（秒）</summary>
    public int Duration { get; set; }

    /// <summary>服务端文件相对路径</summary>
    public string FilePath { get; set; } = "";

    /// <summary>文件大小（字节）</summary>
    public long FileSize { get; set; }

    /// <summary>比特率（kbps）</summary>
    public int Bitrate { get; set; }

    /// <summary>音轨号</summary>
    public int TrackNumber { get; set; }

    /// <summary>发行年份</summary>
    public int Year { get; set; }

    /// <summary>流派</summary>
    public string Genre { get; set; } = "";

    /// <summary>入库时间（Unix 时间戳）</summary>
    public long DateAdded { get; set; }

    /// <summary>文件最后修改时间（Unix 时间戳）</summary>
    public long DateModified { get; set; }

    /// <summary>封面图相对路径</summary>
    public string CoverArtPath { get; set; } = "";

    /// <summary>歌词文件相对路径</summary>
    public string LyricsPath { get; set; } = "";

    /// <summary>全部艺术家名称（分隔符拼接）</summary>
    public string AllArtists { get; set; } = "";
}

/// <summary>服务端返回的艺术家元数据</summary>
public class ServerArtist
{
    /// <summary>艺术家唯一 ID</summary>
    public long Id { get; set; }

    /// <summary>艺术家名称</summary>
    public string Name { get; set; } = "";

    /// <summary>封面图路径或 URL</summary>
    public string Cover { get; set; } = "";
}

/// <summary>服务端返回的专辑元数据</summary>
public class ServerAlbum
{
    /// <summary>专辑唯一 ID</summary>
    public long Id { get; set; }

    /// <summary>专辑标题</summary>
    public string Title { get; set; } = "";

    /// <summary>主艺术家 ID</summary>
    public long ArtistId { get; set; }

    /// <summary>主艺术家名称</summary>
    public string Artist { get; set; } = "";
}
