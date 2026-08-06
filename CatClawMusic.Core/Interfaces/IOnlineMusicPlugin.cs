using CatClawMusic.Core.Models;

namespace CatClawMusic.Core.Interfaces;

/// <summary>
/// 在线音乐音源插件接口 —— 让播放器以插件形式接入任意在线音乐平台。
/// <para>
/// 参考 MusicFree / 洛雪音乐的音源机制：音源插件只负责
/// 「搜索 → 取播放直链 → 取歌词 → 取封面 → 歌单」数据能力，
/// 播放、缓存、UI 全部由宿主播放器统一处理。
/// </para>
/// <para>
/// 任何实现了本接口的 <see cref="IPlugin"/>（内置或动态安装的 .dll）都会被
/// <see cref="Services.OnlineMusicAggregator"/> 自动聚合，搜索结果按平台合并展示。
/// </para>
/// </summary>
public interface IOnlineMusicPlugin : IPlugin
{
    /// <summary>来源平台标识（如 netease / qq / kugou / soda / apple），用于匹配歌曲所属平台</summary>
    string PlatformName { get; }

    /// <summary>搜索歌曲</summary>
    /// <param name="keyword">搜索关键词</param>
    /// <param name="page">页码（从 1 开始）</param>
    /// <param name="pageSize">每页数量</param>
    /// <returns>歌曲列表；失败返回 null</returns>
    Task<List<OnlineSong>?> SearchAsync(string keyword, int page = 1, int pageSize = 8);

    /// <summary>获取播放直链（用于 ExoPlayer 直接播放）</summary>
    /// <param name="song">搜索结果歌曲</param>
    /// <param name="quality">音质档位（0=默认，越大越高，平台相关）</param>
    /// <returns>可播放的 URL；无法获取返回 null</returns>
    Task<string?> GetPlayUrlAsync(OnlineSong song, int quality = 0);

    /// <summary>获取歌词（LRC 原文 + 翻译）</summary>
    /// <param name="song">搜索结果歌曲</param>
    /// <returns>LRC 文本与翻译文本；无歌词或失败返回 null</returns>
    Task<(string? Lrc, string? TLrc)?> GetLyricsAsync(OnlineSong song);

    /// <summary>获取歌单列表（无歌单能力返回空列表）</summary>
    /// <param name="category">歌单分类（平台相关，可为 null 取默认/全部）</param>
    Task<List<OnlinePlaylist>> GetPlaylistsAsync(string? category = null);

    /// <summary>获取歌单内歌曲列表（无歌单能力返回空列表）</summary>
    /// <param name="playlist">歌单（由 GetPlaylistsAsync 返回）</param>
    /// <param name="page">页码（从 1 开始）</param>
    /// <param name="pageSize">每页数量</param>
    Task<List<OnlineSong>?> GetPlaylistSongsAsync(OnlinePlaylist playlist, int page = 1, int pageSize = 50);

    /// <summary>私人漫游（随机推荐歌曲，类似私人 FM）；未实现返回 null</summary>
    Task<List<OnlineSong>?> GetPrivateFmAsync(int num = 10) => Task.FromResult<List<OnlineSong>?>(null);

    /// <summary>每日推荐歌曲；未实现返回 null</summary>
    Task<List<OnlineSong>?> GetDailyRecommendAsync(int num = 20) => Task.FromResult<List<OnlineSong>?>(null);
}
