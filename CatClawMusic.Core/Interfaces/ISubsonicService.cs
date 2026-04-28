using CatClawMusic.Core.Models;

namespace CatClawMusic.Core.Interfaces;

/// <summary>
/// Subsonic / Navidrome API 服务接口
/// 协议文档: http://www.subsonic.org/pages/api.jsp
/// </summary>
public interface ISubsonicService
{
    /// <summary>测试连接（ping）</summary>
    Task<(bool Success, string Message)> PingAsync(ConnectionProfile profile);

    /// <summary>搜索歌曲/艺术家/专辑</summary>
    Task<List<Song>> SearchAsync(string query, ConnectionProfile profile);

    /// <summary>浏览音乐库（按字母索引获取艺术家）</summary>
    Task<List<Song>> GetSongsAsync(ConnectionProfile profile);

    /// <summary>获取专辑列表</summary>
    Task<List<Album>> GetAlbumsAsync(ConnectionProfile profile);

    /// <summary>获取歌曲流 URL（用于播放）</summary>
    string GetStreamUrl(string songId, ConnectionProfile profile);

    /// <summary>获取封面图 URL</summary>
    string GetCoverArtUrl(string coverArtId, ConnectionProfile profile);

    /// <summary>下载封面图字节</summary>
    Task<byte[]?> GetCoverArtAsync(string coverArtId, ConnectionProfile profile);

    /// <summary>获取歌词</summary>
    Task<string?> GetLyricsAsync(string songId, ConnectionProfile profile);
}
