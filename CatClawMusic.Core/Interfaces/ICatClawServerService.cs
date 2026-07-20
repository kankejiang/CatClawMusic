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
/// 服务端状态
/// </summary>
public class ServerStatus
{
    /// <summary>服务端歌曲总数</summary>
    public int Songs { get; set; }

    /// <summary>服务端艺术家总数</summary>
    public int Artists { get; set; }

    /// <summary>服务端专辑总数</summary>
    public int Albums { get; set; }

    /// <summary>DHT 网络中的节点数</summary>
    public int DhtPeers { get; set; }

    /// <summary>当前请求的速率限制（请求数/时间窗口）</summary>
    public int RateLimit { get; set; }

    /// <summary>服务端标识的设备名称</summary>
    public string Device { get; set; } = "";
}
