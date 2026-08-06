using CatClawMusic.Core.Interfaces;
using CatClawMusic.Core.Models;

namespace CatClawMusic.Plugins.OnlineMusic;

/// <summary>
/// 在线音乐音源插件基类 —— 统一实现 <see cref="IPlugin"/> 元数据与共享 HttpClient，
/// 各平台插件只需实现 <see cref="PlatformName"/> / <see cref="SearchAsync"/> /
/// <see cref="GetPlayUrlAsync"/> / <see cref="GetLyricsAsync"/>。
/// 本插件程序集独立于宿主（CatClawMusic.Maui）编译，经插件管理页导入后由
/// PluginManager 加载；宿主仅保留接口、聚合器与 UI（空壳）。
/// </summary>
public abstract class OnlineMusicPluginBase : IOnlineMusicPlugin
{
    /// <summary>共享 HttpClient（浏览器 UA，超时 12 秒）</summary>
    protected readonly HttpClient Http;

    protected OnlineMusicPluginBase()
    {
        Http = new HttpClient { Timeout = TimeSpan.FromSeconds(12) };
        Http.DefaultRequestHeaders.Add("User-Agent",
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");
    }

    public abstract string PluginId { get; }
    public abstract string Name { get; }
    public string Version => "1.0.0";
    public string Author => "CatClawMusic";
    public abstract string Description { get; }
    public List<string> Capabilities => new() { "search", "play", "lyrics" };

    /// <summary>来源平台标识（如 netease / qq / kugou / soda / apple）</summary>
    public abstract string PlatformName { get; }

    public virtual Task InitializeAsync() => Task.CompletedTask;
    public virtual Task ShutdownAsync() => Task.CompletedTask;

    public abstract Task<List<OnlineSong>?> SearchAsync(string keyword, int page = 1, int pageSize = 8);
    public abstract Task<string?> GetPlayUrlAsync(OnlineSong song, int quality = 0);
    public abstract Task<(string? Lrc, string? TLrc)?> GetLyricsAsync(OnlineSong song);

    /// <summary>默认不支持歌单浏览</summary>
    public virtual Task<List<OnlinePlaylist>> GetPlaylistsAsync(string? category = null)
        => Task.FromResult(new List<OnlinePlaylist>());

    /// <summary>构造搜索结果条目（填充平台标识与显示名）</summary>
    protected OnlineSong MakeSong(string id, string title, string artist, string album, long durationMs, string? coverUrl = null)
    {
        return new OnlineSong
        {
            Id = id,
            Platform = PlatformName,
            PlatformName = Name,
            Title = title,
            Artist = artist,
            Album = album ?? string.Empty,
            DurationMs = durationMs,
            CoverUrl = coverUrl
        };
    }
}
