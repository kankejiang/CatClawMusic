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
    /// <param name="profile">连接配置</param>
    /// <param name="progress">进度回调 (已处理, 总数, 状态文本)</param>
    /// <param name="songBatchCallback">每批次歌曲回调，用于增量入库和刷新列表</param>
    /// <param name="quickScan">快速扫描模式：仅扫描文件名，跳过元数据下载，后续可用 BackfillMetadataAsync 回填</param>
    Task<List<Song>> ScanAsync(ConnectionProfile profile,
        IProgress<(int done, int total, string status)>? progress = null,
        Action<List<Song>>? songBatchCallback = null,
        bool quickScan = false);

    /// <summary>回填网络歌曲元数据（扫描阶段跳过的元数据在此补全）</summary>
    /// <param name="profile">连接配置</param>
    /// <param name="progress">进度回调</param>
    /// <param name="ct">取消令牌</param>
    Task BackfillMetadataAsync(ConnectionProfile profile,
        IProgress<(int done, int total, string status)>? progress = null,
        CancellationToken ct = default);

    /// <summary>搜索网络歌曲</summary>
    Task<List<Song>> SearchAsync(string keyword, ConnectionProfile profile);

    /// <summary>获取专辑封面流</summary>
    /// <param name="coverPath">同目录侧车封面图远程路径（folder.jpg / cover.jpg 等）；非空时优先下载该图片</param>
    Task<Stream?> GetCoverAsync(string songId, ConnectionProfile profile, string? coverPath = null);

    /// <summary>获取流媒体 URL（用于播放）</summary>
    Task<string> GetStreamUrlAsync(Song song, ConnectionProfile profile);

    /// <summary>打开音频流（用于「分享音频文件」：把远程音频下载到本地后分享）。按协议返回可读流，调用方负责释放。</summary>
    Task<Stream?> OpenAudioStreamAsync(Song song, ConnectionProfile profile);

    /// <summary>解析 WebDAV 播放 URL：自动检测服务器类型并修复/dav前缀或获取OpenList签名URL</summary>
    Task<string?> ResolveWebDavPlaybackUrlAsync(string url);

    /// <summary>获取远程歌词文本（LRC 或纯文本）</summary>
    Task<string?> GetLyricsAsync(string remotePath, ConnectionProfile profile);

    /// <summary>按需获取网络歌曲元数据（从远程音频文件读取 Tag）</summary>
    Task<Song?> FetchSongMetadataAsync(Song song, ConnectionProfile profile);
}
