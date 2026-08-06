using CatClawMusic.Core.Interfaces;
using CatClawMusic.Core.Models;
using Microsoft.Extensions.DependencyInjection;

namespace CatClawMusic.Plugins.Netease;

/// <summary>
/// 网易云音乐音源插件：官方接口（老 web API 匿名优先 + 可选 Cookie），
/// 覆盖搜索 / 歌单广场 / 歌单内歌曲 / 播放直链（三级兜底）/ 歌词 / 私人漫游 / 每日推荐。
/// 用户可在宿主「插件管理 → 网易云 → 配置」粘贴网页版 Cookie 增强推荐个性化。
/// <para>
/// 同时实现 <see cref="IViewContributorPlugin"/>：向宿主贡献一个完整的"网易云音乐"入口页面，
/// 取代原来客户端内置的在线音乐页面，由插件自治提供 UI 和业务逻辑。
/// </para>
/// </summary>
public class NetEaseMusicPlugin : IOnlineMusicPlugin, IViewContributorPlugin
{
    private readonly NeteaseOpenApiClient _client = new();

    public string PluginId => "netEaseMusic";
    public string Name => "网易云音乐";
    public string Version => "2.0.0";
    public string Author => "CatClawMusic";
    public string Description => "网易云官方接口（搜索/歌单/漫游/每日推荐/播放/歌词）";
    public List<string> Capabilities => new() { "search", "play", "lyrics", "playlist", "fm", "daily" };

    // ── IViewContributorPlugin：插件向宿主贡献完整入口页面 ──

    /// <summary>发现页入口显示标题</summary>
    public string EntryTitle => "网易云音乐";

    /// <summary>发现页入口图标（Emoji）</summary>
    public string EntryIcon => "🎵";

    /// <summary>
    /// 创建入口页面实例。宿主在用户点击入口时调用此方法，
    /// 返回的 <see cref="NeteaseOnlineMusicPage"/> 会被 Push 到导航栈。
    /// </summary>
    /// <param name="services">宿主服务提供者，用于获取 PlayQueue、IAudioPlayerService 等</param>
    /// <returns>NeteaseOnlineMusicPage 实例</returns>
    public object CreateEntryPage(IServiceProvider services)
    {
        var queue = services.GetRequiredService<CatClawMusic.Core.Services.PlayQueue>();
        var audioPlayer = services.GetRequiredService<CatClawMusic.Core.Interfaces.IAudioPlayerService>();
        var vm = new NeteaseOnlineMusicViewModel(this, queue, audioPlayer);
        return new NeteaseOnlineMusicPage(vm, services);
    }

    /// <summary>来源平台标识</summary>
    public string PlatformName => "netease";

    /// <summary>初始化：读取宿主写入的 Cookie 配置文件（可选，纯 .NET 路径避免 MAUI 依赖）</summary>
    public Task InitializeAsync()
    {
        try
        {
            var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CatClawMusic.Maui");
            var cfg = Path.Combine(dir, "netease_cookie.txt");
            if (File.Exists(cfg))
            {
                var cookie = File.ReadAllText(cfg).Trim();
                if (!string.IsNullOrWhiteSpace(cookie)) _client.SetCookie(cookie);
            }
        }
        catch { }
        return Task.CompletedTask;
    }

    public Task ShutdownAsync() => Task.CompletedTask;

    public Task<List<OnlineSong>?> SearchAsync(string keyword, int page = 1, int pageSize = 20)
        => _client.SearchSongsAsync(keyword, page, pageSize);

    public Task<List<OnlinePlaylist>> GetPlaylistsAsync(string? category = null)
        => _client.GetPlaylistsAsync(category);

    public Task<List<OnlineSong>?> GetPlaylistSongsAsync(OnlinePlaylist playlist, int page = 1, int pageSize = 200)
        => _client.GetPlaylistSongsAsync(playlist, page, pageSize);

    public Task<string?> GetPlayUrlAsync(OnlineSong song, int quality = 0)
        => _client.GetPlayUrlAsync(song.Id);

    public Task<(string? Lrc, string? TLrc)?> GetLyricsAsync(OnlineSong song)
        => _client.GetLyricsAsync(song.Id);

    /// <summary>私人漫游（随机推荐）</summary>
    public Task<List<OnlineSong>?> GetPrivateFmAsync(int num = 10)
        => _client.GetPrivateFmAsync(num);

    /// <summary>每日推荐歌曲</summary>
    public Task<List<OnlineSong>?> GetDailyRecommendAsync(int num = 20)
        => _client.GetDailyRecommendAsync(num);

    /// <summary>排行榜列表</summary>
    public Task<List<OnlinePlaylist>> GetToplistsAsync()
        => _client.GetToplistsAsync();

    // ── 浏览器登录 ──

    /// <summary>获取浏览器登录配置（宿主 WebView 打开登录页）</summary>
    public Task<BrowserLoginInfo?> GetBrowserLoginInfoAsync()
        => _client.GetBrowserLoginInfoAsync();

    /// <summary>接收宿主从 WebView 提取的 Cookie，完成登录</summary>
    public Task SetLoginCookieAsync(string cookie)
        => _client.ApplyLoginCookieAsync(cookie);

    /// <summary>当前是否已登录</summary>
    public Task<bool> IsLoggedInAsync()
        => Task.FromResult(_client.HasCookie);

    /// <summary>已登录账号昵称</summary>
    public Task<string?> GetAccountNameAsync()
        => _client.GetAccountNameAsync();

    /// <summary>退出登录</summary>
    public Task LogoutAsync()
        => _client.LogoutAsync();
}
