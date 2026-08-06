using CatClawMusic.Core.Interfaces;
using CatClawMusic.Core.Models;

namespace CatClawMusic.Plugins.Netease;

/// <summary>
/// 网易云音乐音源插件：官方接口（老 web API 匿名优先 + 可选 Cookie），
/// 覆盖搜索 / 歌单广场 / 歌单内歌曲 / 播放直链（三级兜底）/ 歌词 / 私人漫游 / 每日推荐。
/// 用户可在宿主「插件管理 → 网易云 → 配置」粘贴网页版 Cookie 增强推荐个性化。
/// </summary>
public class NetEaseMusicPlugin : IOnlineMusicPlugin
{
    private readonly NeteaseOpenApiClient _client = new();

    public string PluginId => "netEaseMusic";
    public string Name => "网易云音乐";
    public string Version => "2.0.0";
    public string Author => "CatClawMusic";
    public string Description => "网易云官方接口（搜索/歌单/漫游/每日推荐/播放/歌词）";
    public List<string> Capabilities => new() { "search", "play", "lyrics", "playlist", "fm", "daily" };

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
}
