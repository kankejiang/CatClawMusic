namespace CatClawMusic.Core.Models;

/// <summary>
/// 在线歌单（音源插件歌单），供歌单浏览/推荐功能使用。
/// 一期定义模型，歌单 UI 后续接入。
/// </summary>
public class OnlinePlaylist
{
    /// <summary>歌单在来源平台中的唯一 ID</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>来源平台标识</summary>
    public string Platform { get; set; } = string.Empty;

    /// <summary>歌单名称</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>歌单封面 URL</summary>
    public string? CoverUrl { get; set; }

    /// <summary>歌单描述</summary>
    public string? Description { get; set; }

    /// <summary>歌单歌曲数量</summary>
    public int SongCount { get; set; }
}
