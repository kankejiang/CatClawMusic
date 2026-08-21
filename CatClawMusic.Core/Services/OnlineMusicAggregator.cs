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

        // SearchAsync 本身返回 Task，直接并行即可（Task.Run 包裹已 async 的 lambda 只会多余跳线程）
        var tasks = providers.Select(async p =>
        {
            try
            {
                return await p.SearchAsync(keyword, page, pageSize).ConfigureAwait(false);
            }
            catch
            {
                return null;
            }
        }).ToArray();

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

    /// <summary>获取歌词（原文 + 翻译 + 罗马音）：优先走支持罗马音的插件方法，旧插件回退二元组</summary>
    public async Task<(string? Lrc, string? TLrc, string? RLrc)?> GetLyricsWithRomaAsync(OnlineSong song)
    {
        foreach (var p in GetProviders())
        {
            if (!string.Equals(p.PlatformName, song.Platform, StringComparison.OrdinalIgnoreCase))
                continue;
            try
            {
                var r = await p.GetLyricsWithRomaAsync(song).ConfigureAwait(false);
                if (r != null && !string.IsNullOrWhiteSpace(r.Value.Lrc)) return r;
                // 旧插件未实现新方法：回退二元组，罗马音置空
                var basic = await p.GetLyricsAsync(song).ConfigureAwait(false);
                if (basic != null && !string.IsNullOrWhiteSpace(basic.Value.Lrc))
                    return (basic.Value.Lrc, basic.Value.TLrc, null);
            }
            catch { }
        }
        return null;
    }

    /// <summary>合并所有插件的歌单列表（分平台返回，不跨平台去重）。
    /// <para>category=null：取各插件的默认/推荐歌单（最热/最新）；category 有值时传给每个插件。</para>
    /// </summary>
    public async Task<List<OnlinePlaylist>> GetPlaylistsAsync(string? category = null)
    {
        var providers = GetProviders();
        if (providers.Count == 0) return new List<OnlinePlaylist>();
        var tasks = providers.Select(async p =>
        {
            try { return await p.GetPlaylistsAsync(category).ConfigureAwait(false); }
            catch { return new List<OnlinePlaylist>(); }
        }).ToArray();
        var results = await Task.WhenAll(tasks).ConfigureAwait(false);
        var list = new List<OnlinePlaylist>();
        foreach (var r in results)
        {
            if (r == null || r.Count == 0) continue;
            foreach (var pl in r)
            {
                if (string.IsNullOrEmpty(pl.Platform))
                    pl.Platform = providers.FirstOrDefault(x => x.PlatformName == pl.Platform)?.PlatformName ?? "";
            }
            list.AddRange(r);
        }
        return list;
    }

    /// <summary>合并所有插件的排行榜列表（分平台返回，每个插件通常返回 20~30 个榜单）。</summary>
    public async Task<List<OnlinePlaylist>> GetToplistsAsync()
    {
        var providers = GetProviders();
        if (providers.Count == 0) return new List<OnlinePlaylist>();
        var tasks = providers.Select(async p =>
        {
            try { return await p.GetToplistsAsync().ConfigureAwait(false); }
            catch { return new List<OnlinePlaylist>(); }
        }).ToArray();
        var results = await Task.WhenAll(tasks).ConfigureAwait(false);
        var list = new List<OnlinePlaylist>();
        foreach (var r in results)
        {
            if (r == null || r.Count == 0) continue;
            list.AddRange(r);
        }
        return list;
    }

    /// <summary>打开歌单/榜单内歌曲（按 playlist.Platform 路由到对应插件）。失败返回 null。</summary>
    public async Task<List<OnlineSong>?> GetPlaylistSongsAsync(OnlinePlaylist playlist, int page = 1, int pageSize = 50)
    {
        if (playlist == null) return null;
        foreach (var p in GetProviders())
        {
            if (!string.IsNullOrEmpty(playlist.Platform) &&
                !string.Equals(p.PlatformName, playlist.Platform, StringComparison.OrdinalIgnoreCase))
                continue;
            // Platform 为空时尝试所有插件：任何返回非空且非 null 的都采用
            try
            {
                var r = await p.GetPlaylistSongsAsync(playlist, page, pageSize).ConfigureAwait(false);
                if (r == null) continue;
                // 路由命中：回填 PlatformName
                foreach (var s in r)
                {
                    if (string.IsNullOrEmpty(s.PlatformName) && !string.IsNullOrEmpty(p.PlatformName))
                        s.PlatformName = p.PlatformName;
                }
                return r;
            }
            catch { }
        }
        return null;
    }

    /// <summary>歌单搜索（并行查询全部插件，合并结果）。未实现返回空列表。</summary>
    public async Task<List<OnlinePlaylist>> SearchPlaylistsAsync(string keyword, int page = 1, int pageSize = 20)
    {
        if (string.IsNullOrWhiteSpace(keyword)) return new List<OnlinePlaylist>();
        var providers = GetProviders();
        if (providers.Count == 0) return new List<OnlinePlaylist>();
        var tasks = providers.Select(async p =>
        {
            try { return await p.SearchPlaylistsAsync(keyword, page, pageSize).ConfigureAwait(false); }
            catch { return null; }
        }).ToArray();
        var results = await Task.WhenAll(tasks).ConfigureAwait(false);
        var list = new List<OnlinePlaylist>();
        foreach (var r in results)
        {
            if (r == null || r.Count == 0) continue;
            list.AddRange(r);
        }
        return list;
    }
}
