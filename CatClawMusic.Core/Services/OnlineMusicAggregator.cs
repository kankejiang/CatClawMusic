using CatClawMusic.Core.Interfaces;
using CatClawMusic.Core.Models;

namespace CatClawMusic.Core.Services;

/// <summary>
/// 在线音乐聚合器 —— 把「已启用的所有 <see cref="IOnlineMusicPlugin"/>」包装成统一的搜索/取流/歌词入口。
/// <para>
/// 宿主 UI 只依赖本聚合器，不感知具体音源：搜索时并行请求全部插件并合并结果，
/// 取播放直链/歌词时按歌曲的 <see cref="OnlineSong.Platform"/> 路由到对应插件。
/// </para>
/// </summary>
public class OnlineMusicAggregator
{
    private readonly IPluginManager _pluginManager;

    public OnlineMusicAggregator(IPluginManager pluginManager)
    {
        _pluginManager = pluginManager;
    }

    /// <summary>所有已启用的在线音乐插件（按注册顺序）</summary>
    public IReadOnlyList<IOnlineMusicPlugin> GetProviders()
    {
        return _pluginManager.GetEnabledPlugins<IOnlineMusicPlugin>();
    }

    /// <summary>并行搜索所有已启用插件，合并结果（单个插件失败不影响其他）</summary>
    public async Task<List<OnlineSong>> SearchAllAsync(string keyword, int page = 1, int pageSize = 8)
    {
        var providers = GetProviders();
        if (providers.Count == 0) return new List<OnlineSong>();

        var tasks = providers.Select(p => Task.Run(async () =>
        {
            try
            {
                return await p.SearchAsync(keyword, page, pageSize).ConfigureAwait(false);
            }
            catch
            {
                return null;
            }
        })).ToArray();

        var results = await Task.WhenAll(tasks).ConfigureAwait(false);
        var songs = new List<OnlineSong>();
        foreach (var r in results)
        {
            if (r == null) continue;
            foreach (var s in r)
            {
                if (s.PlatformName.Length == 0 && providers.FirstOrDefault(x => x.PlatformName == s.Platform) is { } src)
                    s.PlatformName = src.PlatformName;
                songs.Add(s);
            }
        }
        return songs;
    }

    /// <summary>取播放直链：优先使用搜索时带回的直链，否则路由到对应平台插件获取</summary>
    public async Task<string?> GetPlayUrlAsync(OnlineSong song, int quality = 0)
    {
        if (!string.IsNullOrWhiteSpace(song.DirectPlayUrl))
            return song.DirectPlayUrl;

        foreach (var p in GetProviders())
        {
            if (!string.Equals(p.PlatformName, song.Platform, StringComparison.OrdinalIgnoreCase))
                continue;
            try
            {
                var url = await p.GetPlayUrlAsync(song, quality).ConfigureAwait(false);
                if (!string.IsNullOrWhiteSpace(url)) return url;
            }
            catch { }
        }
        return null;
    }

    /// <summary>获取歌词（LRC 原文 + 翻译），路由到对应平台插件</summary>
    public async Task<(string? Lrc, string? TLrc)?> GetLyricsAsync(OnlineSong song)
    {
        foreach (var p in GetProviders())
        {
            if (!string.Equals(p.PlatformName, song.Platform, StringComparison.OrdinalIgnoreCase))
                continue;
            try
            {
                var r = await p.GetLyricsAsync(song).ConfigureAwait(false);
                if (r != null && !string.IsNullOrWhiteSpace(r.Value.Lrc)) return r;
            }
            catch { }
        }
        return null;
    }
}
