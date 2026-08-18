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

    /// <summary>
    /// 获取含罗马音的歌词（LRC 原文 + 翻译 + 罗马音，参考 lx-music 音源的 lrc/tlyric/romalrc 三流）。
    /// 接口默认实现返回 null——旧插件无需实现本方法即可保持加载兼容（接口签名稳定，
    /// 插件程序集引用的 CatClawMusic.Core.dll 与宿主版本不一致时不会 TypeLoadException）。
    /// </summary>
    /// <param name="song">搜索结果歌曲</param>
    /// <returns>LRC/翻译/罗马音文本；未实现或失败返回 null</returns>
    Task<(string? Lrc, string? TLrc, string? RLrc)?> GetLyricsWithRomaAsync(OnlineSong song)
        => Task.FromResult<(string? Lrc, string? TLrc, string? RLrc)?>(null);

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

    /// <summary>排行榜列表（榜单可当作歌单打开）；未实现返回空列表</summary>
    Task<List<OnlinePlaylist>> GetToplistsAsync() => Task.FromResult(new List<OnlinePlaylist>());

    // ── 账号登录（浏览器登录方式；由插件提供配置，宿主打开 WebView） ──

    /// <summary>
    /// 获取浏览器登录配置（登录页 URL、Cookie 域名、成功标识 Cookie 等）。
    /// 宿主据此打开 WebView 让用户在真实网页中登录，登录成功后提取 Cookie 回传。
    /// 未实现返回 null，宿主提示"该音源暂不支持登录"。
    /// </summary>
    Task<BrowserLoginInfo?> GetBrowserLoginInfoAsync() => Task.FromResult<BrowserLoginInfo?>(null);

    /// <summary>
    /// 接收宿主从 WebView 提取的完整 Cookie 字符串并完成登录。
    /// 插件负责持久化 Cookie、刷新账号状态。
    /// </summary>
    /// <param name="cookie">WebView 中指定域名的完整 Cookie（形如 "key1=val1; key2=val2"）</param>
    Task SetLoginCookieAsync(string cookie) => Task.CompletedTask;

    /// <summary>当前是否已登录</summary>
    Task<bool> IsLoggedInAsync() => Task.FromResult(false);

    /// <summary>已登录账号昵称；未登录返回 null</summary>
    Task<string?> GetAccountNameAsync() => Task.FromResult<string?>(null);

    /// <summary>退出登录</summary>
    Task LogoutAsync() => Task.CompletedTask;

    /// <summary>红心/取消红心歌曲（写「我喜欢的音乐」；未实现或未登录返回 false，宿主本地收藏不受影响）</summary>
    /// <param name="songId">平台歌曲 id（RemoteId 冒号后段）</param>
    /// <param name="like">true=红心，false=取消</param>
    Task<bool> LikeSongAsync(string songId, bool like) => Task.FromResult(false);

    /// <summary>私人漫游（FM）歌曲红心/取消红心（影响推荐；未实现返回 false）</summary>
    Task<bool> FmLikeAsync(string songId, bool like) => Task.FromResult(false);

    /// <summary>
    /// 获取私人漫游可用的推荐模式与场景模式列表（供宿主渲染模式选择抽屉）。
    /// 返回空列表表示不支持模式切换。
    /// </summary>
    Task<List<FmModeCategory>> GetFmModesAsync() => Task.FromResult(new List<FmModeCategory>());

    /// <summary>
    /// 切换到指定推荐模式/场景模式并重新加载电台。
    /// </summary>
    /// <param name="modeCode">模式代码（DEFAULT / FAMILIAR / EXPLORE / ROCK / JAZZ 等，由 GetFmModesAsync 返回）</param>
    /// <returns>切换后的模式显示名；失败返回 null</returns>
    Task<string?> TrySetFmModeAsync(string modeCode) => Task.FromResult<string?>(null);

    /// <summary>
    /// 获取当前私人漫游推荐模式显示名（如"默认模式"）；不在 FM 模式或不支持返回 null。
    /// 宿主播放页首次进入 FM 模式时调用以同步按钮文字。
    /// </summary>
    Task<string?> GetFmModeLabelAsync() => Task.FromResult<string?>(null);
}
