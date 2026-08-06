namespace CatClawMusic.Core.Models;

/// <summary>
/// 在线歌曲（音源插件搜索结果），跨平台统一模型。
/// 由 <see cref="Interfaces.IOnlineMusicPlugin"/> 返回，聚合器汇总后展示；
/// 播放时先经 <see cref="Interfaces.IOnlineMusicPlugin.GetPlayUrlAsync"/> 取直链，
/// 再转换为 <see cref="Song"/> 接入现有播放链路。
/// </summary>
public class OnlineSong
{
    /// <summary>歌曲在来源平台中的唯一 ID</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>来源平台标识（如 netease / qq / kugou / soda / apple）</summary>
    public string Platform { get; set; } = string.Empty;

    /// <summary>来源平台显示名（如 网易云 / QQ音乐）</summary>
    public string PlatformName { get; set; } = string.Empty;

    /// <summary>歌曲标题</summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>艺术家名（多艺术家以 " / " 分隔）</summary>
    public string Artist { get; set; } = string.Empty;

    /// <summary>专辑名</summary>
    public string Album { get; set; } = string.Empty;

    /// <summary>歌曲时长（毫秒）</summary>
    public long DurationMs { get; set; }

    /// <summary>封面图 URL</summary>
    public string? CoverUrl { get; set; }

    /// <summary>源特有字段（如酷狗的 FileHash），用于取歌词/播放地址等后续操作</summary>
    public Dictionary<string, object>? Internal { get; set; }

    /// <summary>可直接播放的 URL（若搜索时已得到，如 iTunes 预览直链）；否则由 GetPlayUrlAsync 获取</summary>
    public string? DirectPlayUrl { get; set; }
}
