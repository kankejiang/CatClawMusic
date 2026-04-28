using CatClawMusic.Core.Models;

namespace CatClawMusic.Core.Interfaces;

/// <summary>
/// 网络音乐服务工厂——按协议类型创建对应的服务
/// </summary>
public interface INetworkMusicService
{
    /// <summary>获取已配置的连接列表</summary>
    Task<List<ConnectionProfile>> GetProfilesAsync();

    /// <summary>扫描网络音乐库</summary>
    Task<List<Song>> ScanAsync(ConnectionProfile profile);

    /// <summary>搜索网络歌曲</summary>
    Task<List<Song>> SearchAsync(string keyword, ConnectionProfile profile);

    /// <summary>获取专辑封面流</summary>
    Task<Stream?> GetCoverAsync(string songId, ConnectionProfile profile);

    /// <summary>获取流媒体 URL（用于播放）</summary>
    Task<string> GetStreamUrlAsync(Song song, ConnectionProfile profile);
}
