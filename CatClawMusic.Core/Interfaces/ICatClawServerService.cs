using CatClawMusic.Core.Models;

namespace CatClawMusic.Core.Interfaces;

/// <summary>
/// CatClaw 服务端连接服务接口 —— 用于从 NAS 服务端快速获取元数据
/// </summary>
public interface ICatClawServerService
{
    /// <summary>服务端地址</summary>
    string ServerUrl { get; set; }

    /// <summary>测试连接是否可用</summary>
    Task<bool> TestConnectionAsync();

    /// <summary>获取服务端状态（歌曲数、艺术家数等）</summary>
    Task<ServerStatus?> GetStatusAsync();

    /// <summary>从服务端拉取全量元数据并入库</summary>
    Task<int> SyncMetadataAsync(IProgress<(string, int)>? progress = null);

    /// <summary>搜索服务端歌曲</summary>
    Task<List<Song>> SearchServerAsync(string keyword);

    /// <summary>获取服务端音频流 URL</summary>
    string GetStreamUrl(int songId);
}

/// <summary>
/// P2P 服务接口 —— 负责设备发现和 P2P 下载
/// </summary>
public interface IP2PService
{
    /// <summary>P2P 配置</summary>
    P2PConfig Config { get; }

    /// <summary>启动 P2P 客户端</summary>
    Task StartAsync();

    /// <summary>停止 P2P 客户端</summary>
    Task StopAsync();

    /// <summary>是否正在运行</summary>
    bool IsRunning { get; }

    /// <summary>发现在线设备列表</summary>
    Task<List<P2PDevice>> DiscoverDevicesAsync();

    /// <summary>从指定设备下载歌曲</summary>
    Task<Stream?> DownloadFromDeviceAsync(P2PDevice device, int songId, IProgress<(long, long)>? progress = null);

    /// <summary>搜索所有可发现的设备</summary>
    Task<List<Song>> SearchAllDevicesAsync(string keyword);
}

/// <summary>
/// 服务端状态
/// </summary>
public class ServerStatus
{
    public int Songs { get; set; }
    public int Artists { get; set; }
    public int Albums { get; set; }
    public int DhtPeers { get; set; }
    public int RateLimit { get; set; }
    public string Device { get; set; } = "";
}
