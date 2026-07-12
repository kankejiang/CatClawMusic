namespace CatClawMusic.Core.Models;

/// <summary>
/// 猫爪圈中发现的对端设备（同局域网内运行猫爪音乐并开启猫爪圈的其他用户）。
/// </summary>
public class ClawCirclePeer
{
    /// <summary>对端显示的设备名</summary>
    public string DeviceName { get; set; } = string.Empty;

    /// <summary>对端 IP 地址（局域网）</summary>
    public string Ip { get; set; } = string.Empty;

    /// <summary>对端 HTTP 服务端口</summary>
    public int Port { get; set; }

    /// <summary>对端共享的歌曲数量</summary>
    public int SongCount { get; set; }

    /// <summary>对端正在播放的歌曲标题（可能为空）</summary>
    public string NowPlaying { get; set; } = string.Empty;

    /// <summary>最后收到心跳/广播的时间（UTC）</summary>
    public DateTime LastSeen { get; set; } = DateTime.UtcNow;

    /// <summary>是否在线（30 秒内收到过广播）</summary>
    public bool IsOnline => (DateTime.UtcNow - LastSeen).TotalSeconds < 45;

    /// <summary>设备唯一标识（IP:Port）</summary>
    public string Id => $"{Ip}:{Port}";

    /// <summary>展示用摘要：共享歌曲数与正在播放（可选）。</summary>
    public string Summary =>
        "共享 " + SongCount + " 首" +
        (string.IsNullOrEmpty(NowPlaying) ? "" : " · 在听：" + NowPlaying);
}

/// <summary>
/// 本机对外广播的设备信息载荷。
/// </summary>
public class ClawCircleDeviceInfo
{
    /// <summary>设备名</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>HTTP 服务端口</summary>
    public int Port { get; set; }

    /// <summary>共享歌曲数量</summary>
    public int SongCount { get; set; }

    /// <summary>正在播放的歌曲标题</summary>
    public string NowPlaying { get; set; } = string.Empty;
}

/// <summary>
/// 对端共享的歌曲概要，用于圈内浏览与搜索（不含文件本身，仅元数据）。
/// </summary>
public class ClawCircleSongInfo
{
    /// <summary>歌曲在对方数据库的 ID</summary>
    public int Id { get; set; }

    /// <summary>歌曲标题</summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>艺术家</summary>
    public string Artist { get; set; } = string.Empty;

    /// <summary>专辑</summary>
    public string Album { get; set; } = string.Empty;

    /// <summary>时长（毫秒）</summary>
    public int DurationMs { get; set; }

    /// <summary>文件大小（字节）</summary>
    public long Size { get; set; }

    /// <summary>展示用摘要：艺术家 · 专辑（专辑为空时仅艺术家）。</summary>
    public string Summary => string.IsNullOrEmpty(Album) ? Artist : $"{Artist} · {Album}";
}

/// <summary>
/// 从对端拉取歌曲流的结果：包含可读流、原始文件名（用于推断扩展名）与大小。
/// </summary>
public class ClawCircleSongStreamResult
{
    /// <summary>歌曲音频流（调用方负责释放）；失败为 null。</summary>
    public Stream? Stream { get; set; }

    /// <summary>对端原始文件名（含扩展名），用于在本机保存时推断类型。</summary>
    public string FileName { get; set; } = string.Empty;

    /// <summary>音频流总长度（字节），未知时为 0。</summary>
    public long Size { get; set; }
}
