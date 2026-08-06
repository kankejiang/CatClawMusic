using System.Text;
using System.Text.Json;
using CatClawMusic.Core.Models;

namespace CatClawMusic.Plugins.Netease;

/// <summary>
/// 网易云开放接口客户端（老 web API：匿名优先 + 可选用户 Cookie 增强）。
/// 覆盖：搜索 / 歌单广场 / 歌单详情 / 播放直链（outer 外链 + enhance + 公共 API 三级兜底）/
/// 歌词 / 私人漫游（radio.get）/ 每日推荐（v3 discovery）。
/// 登录：由宿主 WebView 打开 music.163.com 登录页，提取 Cookie 后回传 SetCookie。
/// 全部接口实测可用（2026-08），播放直链/封面统一 https。
/// </summary>
public class NeteaseOpenApiClient
{
    private readonly HttpClient _http;
    private string? _cookie;

    /// <summary>用户 Cookie 持久化文件（宿主与插件约定的路径）</summary>
    private static readonly string CookieFilePath =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "CatClawMusic.Maui", "netease_cookie.txt");

    private static readonly string NicknameFilePath =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "CatClawMusic.Maui", "netease_nickname.txt");

    public NeteaseOpenApiClient()
    {
        // 禁用自动 Cookie 管理：二维码登录/播放直链需精确控制携带的 Cookie（用户 Cookie 优先）
        _http = new HttpClient(new HttpClientHandler { UseCookies = false }) { Timeout = TimeSpan.FromSeconds(20) };
        _http.DefaultRequestHeaders.UserAgent.ParseAdd(
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");
        _http.DefaultRequestHeaders.Referrer = new Uri("https://music.163.com/");
    }

    /// <summary>设置用户 Cookie（增强推荐个性化/播放完整度；可空 = 匿名）</summary>
    public void SetCookie(string? cookie) => _cookie = cookie;

    public bool HasCookie => !string.IsNullOrWhiteSpace(_cookie);

    /// <summary>
    /// 浏览器登录配置：宿主 WebView 打开 music.163.com 登录页，
    /// 用户登录后从 WebView 提取 MUSIC_U 等 Cookie 回传 <see cref="ApplyLoginCookie"/>。
    /// </summary>
    public Task<BrowserLoginInfo?> GetBrowserLoginInfoAsync()
    {
        return Task.FromResult<BrowserLoginInfo?>(new BrowserLoginInfo
        {
            LoginUrl = "https://music.163.com/#/login",
            CookieDomain = "music.163.com",
            SuccessCookieNames = new List<string> { "MUSIC_U" },
            // 登录成功后通常跳转到首页或个人页
            SuccessUrlPattern = "music.163.com/#/m/loginsuccess",
            Title = "网易云登录"
        });
    }

    /// <summary>
    /// 接收宿主从 WebView 提取的完整 Cookie 字符串，持久化并刷新内存状态。
    /// </summary>
    public Task ApplyLoginCookieAsync(string cookie)
    {
        if (!string.IsNullOrWhiteSpace(cookie))
        {
            _cookie = cookie;
            PersistCookie(cookie);
        }
        return Task.CompletedTask;
    }

    /// <summary>已登录账号昵称（/api/nuser/account/get 实名验证；失败回退本地缓存）</summary>
    public async Task<string?> GetAccountNameAsync()
    {
        if (string.IsNullOrWhiteSpace(_cookie)) return null;
        try
        {
            using var doc = await GetJsonAsync("https://music.163.com/api/nuser/account/get");
            if (doc != null && doc.RootElement.TryGetProperty("profile", out var p) &&
                p.TryGetProperty("nickname", out var n) && !string.IsNullOrWhiteSpace(n.GetString()))
            {
                var nickname = n.GetString();
                if (!string.IsNullOrWhiteSpace(nickname))
                {
                    try { File.WriteAllText(NicknameFilePath, nickname); } catch { }
                }
                return nickname;
            }
        }
        catch { }
        // 兜底：读取登录时缓存的昵称
        try
        {
            if (File.Exists(NicknameFilePath))
                return File.ReadAllText(NicknameFilePath).Trim();
        }
        catch { }
        return null;
    }

    /// <summary>退出登录：清空内存 Cookie 并删除持久化文件</summary>
    public async Task LogoutAsync()
    {
        _cookie = null;
        await Task.CompletedTask;
        try { if (File.Exists(CookieFilePath)) File.Delete(CookieFilePath); } catch { }
        try { if (File.Exists(NicknameFilePath)) File.Delete(NicknameFilePath); } catch { }
    }

    /// <summary>持久化登录 Cookie（供插件 InitializeAsync 重启后恢复）</summary>
    private void PersistCookie(string cookie)
    {
        try
        {
            var dir = Path.GetDirectoryName(CookieFilePath);
            if (!string.IsNullOrWhiteSpace(dir)) Directory.CreateDirectory(dir);
            File.WriteAllText(CookieFilePath, cookie);
        }
        catch { }
    }

    /// <summary>排行榜列表（/api/toplist，63 个榜单；榜单可当歌单打开）</summary>
    public async Task<List<OnlinePlaylist>> GetToplistsAsync()
    {
        try
        {
            using var doc = await GetJsonAsync("https://music.163.com/api/toplist");
            if (doc == null || !doc.RootElement.TryGetProperty("list", out var list) || list.ValueKind != JsonValueKind.Array)
                return new List<OnlinePlaylist>();
            var result = new List<OnlinePlaylist>();
            foreach (var t in list.EnumerateArray())
            {
                result.Add(new OnlinePlaylist
                {
                    Id = t.TryGetProperty("id", out var idEl) ? idEl.GetInt64().ToString() : "",
                    Platform = "netease",
                    Name = t.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "",
                    CoverUrl = ToHttps(t.TryGetProperty("coverImgUrl", out var c) ? c.GetString() : null),
                    Description = t.TryGetProperty("description", out var d) ? d.GetString() : null,
                    SongCount = t.TryGetProperty("total", out var tc) ? tc.GetInt32() : 0,
                });
            }
            return result;
        }
        catch { return new List<OnlinePlaylist>(); }
    }

    /// <summary>歌单搜索（cloudsearch type=1000）</summary>
    public async Task<List<OnlinePlaylist>> SearchPlaylistsAsync(string keyword, int limit = 20)
    {
        try
        {
            var body = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["s"] = keyword, ["type"] = "1000", ["offset"] = "0", ["limit"] = limit.ToString()
            });
            var req = Build(HttpMethod.Post, "https://music.163.com/api/cloudsearch/pc");
            req.Content = body;
            using var resp = await _http.SendAsync(req);
            var json = await resp.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("result", out var result) ||
                !result.TryGetProperty("playlists", out var pls) || pls.ValueKind != JsonValueKind.Array)
                return new List<OnlinePlaylist>();
            var list = new List<OnlinePlaylist>();
            foreach (var pl in pls.EnumerateArray())
            {
                list.Add(new OnlinePlaylist
                {
                    Id = pl.TryGetProperty("id", out var idEl) ? idEl.GetInt64().ToString() : "",
                    Platform = "netease",
                    Name = pl.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "",
                    CoverUrl = ToHttps(pl.TryGetProperty("coverImgUrl", out var c) ? c.GetString() : null),
                    Description = pl.TryGetProperty("description", out var d) ? d.GetString() : null,
                    SongCount = pl.TryGetProperty("trackCount", out var tc) ? tc.GetInt32() : 0,
                });
            }
            return list;
        }
        catch { return new List<OnlinePlaylist>(); }
    }

    /// <summary>歌手热门歌曲（搜歌手名 → 取第一个歌手 → 热门 50 首）</summary>
    public async Task<List<OnlineSong>?> GetArtistHotSongsAsync(string artistName)
    {
        try
        {
            var body = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["s"] = artistName, ["type"] = "100", ["offset"] = "0", ["limit"] = "1"
            });
            var req = Build(HttpMethod.Post, "https://music.163.com/api/cloudsearch/pc");
            req.Content = body;
            using var resp = await _http.SendAsync(req);
            var json = await resp.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("result", out var result) ||
                !result.TryGetProperty("artists", out var artists) || artists.ValueKind != JsonValueKind.Array ||
                artists.GetArrayLength() == 0)
                return null;
            var artistId = artists[0].TryGetProperty("id", out var idEl) ? idEl.GetInt64() : 0;
            if (artistId == 0) return null;

            using var doc2 = await GetJsonAsync($"https://music.163.com/api/artist/top/song?id={artistId}");
            if (doc2 == null || !doc2.RootElement.TryGetProperty("songs", out var songs) || songs.ValueKind != JsonValueKind.Array)
                return null;
            var list = new List<OnlineSong>();
            foreach (var s in songs.EnumerateArray())
            {
                var song = ParseSong(s);
                if (song != null) list.Add(song);
            }
            return list;
        }
        catch { return null; }
    }

    /// <summary>搜索歌曲（/api/cloudsearch/pc）</summary>
    public async Task<List<OnlineSong>?> SearchSongsAsync(string keyword, int page = 1, int pageSize = 20)
    {
        try
        {
            var offset = (page - 1) * pageSize;
            var body = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["s"] = keyword, ["type"] = "1", ["offset"] = offset.ToString(), ["limit"] = pageSize.ToString()
            });
            var req = Build(HttpMethod.Post, "https://music.163.com/api/cloudsearch/pc");
            req.Content = body;
            using var resp = await _http.SendAsync(req);
            var json = await resp.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("result", out var result) ||
                !result.TryGetProperty("songs", out var songs) || songs.ValueKind != JsonValueKind.Array)
                return null;
            var list = new List<OnlineSong>();
            foreach (var s in songs.EnumerateArray())
            {
                var song = ParseSong(s);
                if (song != null) list.Add(song);
            }
            return list;
        }
        catch { return null; }
    }

    /// <summary>热门歌单（歌单广场 /api/playlist/list，支持分类）</summary>
    public async Task<List<OnlinePlaylist>> GetPlaylistsAsync(string? category = null)
    {
        try
        {
            var cat = string.IsNullOrWhiteSpace(category) || category == "全部" ? "全部" : category.Trim();
            var url = $"https://music.163.com/api/playlist/list?cat={Uri.EscapeDataString(cat)}&order=hot&limit=60&offset=0";
            using var doc = await GetJsonAsync(url);
            if (doc == null || !doc.RootElement.TryGetProperty("playlists", out var pls) || pls.ValueKind != JsonValueKind.Array)
                return new List<OnlinePlaylist>();
            var list = new List<OnlinePlaylist>();
            foreach (var pl in pls.EnumerateArray())
            {
                list.Add(new OnlinePlaylist
                {
                    Id = pl.TryGetProperty("id", out var idEl) ? idEl.GetInt64().ToString() : "",
                    Platform = "netease",
                    Name = pl.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "",
                    CoverUrl = ToHttps(pl.TryGetProperty("coverImgUrl", out var c) ? c.GetString() : null),
                    Description = pl.TryGetProperty("description", out var d) ? d.GetString() : null,
                    SongCount = pl.TryGetProperty("trackCount", out var tc) ? tc.GetInt32() : 0,
                });
            }
            return list;
        }
        catch { return new List<OnlinePlaylist>(); }
    }

    /// <summary>歌单内歌曲（v6 歌单详情，n=1000 一次拉全）</summary>
    public async Task<List<OnlineSong>?> GetPlaylistSongsAsync(OnlinePlaylist playlist, int page = 1, int pageSize = 200)
    {
        if (string.IsNullOrWhiteSpace(playlist.Id)) return null;
        try
        {
            var url = $"https://music.163.com/api/v6/playlist/detail?id={playlist.Id}&n=1000&s=8";
            using var doc = await GetJsonAsync(url);
            if (doc == null || !doc.RootElement.TryGetProperty("playlist", out var pl) ||
                !pl.TryGetProperty("tracks", out var tracks) || tracks.ValueKind != JsonValueKind.Array)
                return null;
            var all = new List<OnlineSong>();
            foreach (var s in tracks.EnumerateArray())
            {
                var song = ParseSong(s);
                if (song != null) all.Add(song);
            }
            var start = (page - 1) * pageSize;
            if (start >= all.Count) return new List<OnlineSong>();
            return all.Skip(start).Take(pageSize).ToList();
        }
        catch { return null; }
    }

    /// <summary>
    /// 私人漫游（随机推荐 /api/v1/radio/get）。
    /// 该接口一次通常只返回 1 首（网易云私人 FM 模型），故循环拉取并去重，
    /// 直到凑齐 <paramref name="num"/> 首或达到安全上限，模拟官方"无限电台"的首批缓冲。
    /// </summary>
    public async Task<List<OnlineSong>?> GetPrivateFmAsync(int num = 10)
    {
        try
        {
            var collected = new List<OnlineSong>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            int attempts = 0;
            int maxAttempts = num * 3; // 安全上限，避免接口异常时死循环
            while (collected.Count < num && attempts < maxAttempts)
            {
                attempts++;
                using var doc = await GetJsonAsync("https://music.163.com/api/v1/radio/get");
                if (doc == null || !doc.RootElement.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array)
                    break;
                foreach (var s in data.EnumerateArray())
                {
                    var song = ParseSong(s);
                    if (song != null && !string.IsNullOrWhiteSpace(song.Id) && seen.Add(song.Id))
                        collected.Add(song);
                }
            }
            return collected.Count > 0 ? collected : null;
        }
        catch { return null; }
    }

    /// <summary>每日推荐歌曲（/api/v3/discovery/recommend/songs；匿名可用，登录后个性化）</summary>
    public async Task<List<OnlineSong>?> GetDailyRecommendAsync(int num = 20)
    {
        try
        {
            using var doc = await GetJsonAsync("https://music.163.com/api/v3/discovery/recommend/songs");
            if (doc == null || !doc.RootElement.TryGetProperty("data", out var data) ||
                !data.TryGetProperty("dailySongs", out var songs) || songs.ValueKind != JsonValueKind.Array)
                return null;
            var list = new List<OnlineSong>();
            foreach (var s in songs.EnumerateArray())
            {
                var song = ParseSong(s);
                if (song != null) list.Add(song);
            }
            return list.Take(num).ToList();
        }
        catch { return null; }
    }

    /// <summary>播放直链（outer 外链 → enhance+cookie → 公共 API 实例 三级兜底）</summary>
    public async Task<string?> GetPlayUrlAsync(string songId)
    {
        if (string.IsNullOrWhiteSpace(songId)) return null;

        // 方案1：免登录外链（302 到 CDN）
        try
        {
            var outer = $"https://music.163.com/song/media/outer/url?id={songId}.mp3";
            using var resp = await _http.GetAsync(outer, HttpCompletionOption.ResponseHeadersRead);
            if (resp.StatusCode is System.Net.HttpStatusCode.OK or System.Net.HttpStatusCode.PartialContent)
                return ToHttps(resp.RequestMessage?.RequestUri?.ToString() ?? outer);
        }
        catch { }

        // 方案2：enhance/player/url + 静态 cookie（免登录接口不带 cookie 会风控空 url）
        try
        {
            var req = Build(HttpMethod.Get, $"https://music.163.com/api/song/enhance/player/url?id={songId}&ids=[{songId}]&br=320000");
            req.Headers.TryAddWithoutValidation("Cookie", "os=pc; appver=8.9.70");
            using var resp = await _http.SendAsync(req);
            var json = await resp.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in data.EnumerateArray())
                {
                    if (item.TryGetProperty("url", out var u))
                    {
                        var playUrl = u.GetString();
                        if (!string.IsNullOrWhiteSpace(playUrl)) return ToHttps(playUrl);
                    }
                }
            }
        }
        catch { }

        // 方案3：公共 NeteaseCloudMusicApi 实例兜底
        foreach (var api in PublicApiBases)
        {
            try
            {
                using var doc = await GetJsonAsync($"{api}/song/url?id={songId}");
                if (doc == null || !doc.RootElement.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array)
                    continue;
                foreach (var item in data.EnumerateArray())
                {
                    if (item.TryGetProperty("url", out var u))
                    {
                        var playUrl = u.GetString();
                        if (!string.IsNullOrWhiteSpace(playUrl)) return ToHttps(playUrl);
                    }
                }
            }
            catch { }
        }
        return null;
    }

    /// <summary>歌词（LRC + 翻译）</summary>
    public async Task<(string? Lrc, string? TLrc)?> GetLyricsAsync(string songId)
    {
        if (string.IsNullOrWhiteSpace(songId)) return null;
        try
        {
            using var doc = await GetJsonAsync($"https://music.163.com/api/song/lyric?id={songId}&lv=1&kv=1&tv=-1");
            if (doc == null) return null;
            string? lrc = null, tlyric = null;
            if (doc.RootElement.TryGetProperty("lrc", out var ln) && ln.TryGetProperty("lyric", out var lt))
                lrc = lt.GetString();
            if (doc.RootElement.TryGetProperty("tlyric", out var tn) && tn.TryGetProperty("lyric", out var tt))
                tlyric = tt.GetString();
            if (string.IsNullOrWhiteSpace(lrc)) return null;
            return (lrc, string.IsNullOrWhiteSpace(tlyric) ? null : tlyric);
        }
        catch { return null; }
    }

    // ── 内部 ──

    /// <summary>公共 NeteaseCloudMusicApi 实例（播放直链兜底；实测 zm.wwoyun.cn / iwenwiki.com:3000 可用）</summary>
    private static readonly string[] PublicApiBases =
    {
        "https://zm.wwoyun.cn",
        "http://iwenwiki.com:3000",
    };

    private HttpRequestMessage Build(HttpMethod method, string url)
    {
        var req = new HttpRequestMessage(method, url);
        if (!string.IsNullOrWhiteSpace(_cookie))
            req.Headers.TryAddWithoutValidation("Cookie", _cookie);
        return req;
    }

    private async Task<JsonDocument?> GetJsonAsync(string url)
    {
        try
        {
            using var resp = await _http.SendAsync(Build(HttpMethod.Get, url));
            if (!resp.IsSuccessStatusCode) return null;
            var json = await resp.Content.ReadAsStringAsync();
            return JsonDocument.Parse(json);
        }
        catch { return null; }
    }

    /// <summary>解析标准歌曲 JSON（兼容 ar/artists、al/album、dt/duration 字段变体）</summary>
    private OnlineSong? ParseSong(JsonElement s)
    {
        try
        {
            if (!s.TryGetProperty("id", out var idEl) || idEl.ValueKind == JsonValueKind.Null)
                return null;
            var artists = new List<string>();
            if (s.TryGetProperty("ar", out var ar))
                CollectArtistNames(ar, artists);
            if (artists.Count == 0 && s.TryGetProperty("artists", out var ats))
                CollectArtistNames(ats, artists);

            string? album = null, cover = null;
            if (s.TryGetProperty("al", out var al))
            {
                if (al.TryGetProperty("name", out var aln)) album = aln.GetString();
                if (al.TryGetProperty("picUrl", out var pic)) cover = pic.GetString();
            }
            if (album == null && s.TryGetProperty("album", out var alb))
            {
                if (alb.TryGetProperty("name", out var albn)) album = albn.GetString();
                if (cover == null && alb.TryGetProperty("picUrl", out var albp)) cover = albp.GetString();
            }

            long dur = 0;
            if (s.TryGetProperty("dt", out var dt)) dur = dt.GetInt64();
            if (dur == 0 && s.TryGetProperty("duration", out var du)) dur = du.GetInt64();
            if (dur == 0 && s.TryGetProperty("durationMs", out var dm)) dur = dm.GetInt64();

            return new OnlineSong
            {
                Id = idEl.GetInt64().ToString(),
                Platform = "netease",
                PlatformName = "网易云音乐",
                Title = s.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "",
                Artist = string.Join(" / ", artists),
                Album = album ?? string.Empty,
                DurationMs = dur,
                CoverUrl = ToHttps(cover),
            };
        }
        catch { return null; }
    }

    private static void CollectArtistNames(JsonElement arr, List<string> artists)
    {
        if (arr.ValueKind != JsonValueKind.Array) return;
        foreach (var a in arr.EnumerateArray())
        {
            var name = a.TryGetProperty("name", out var an) ? an.GetString() : null;
            if (!string.IsNullOrWhiteSpace(name)) artists.Add(name);
        }
    }

    /// <summary>http 明文统一转 https（WinUI/Android 拒绝明文；网易云 CDN 支持 https）</summary>
    private static string? ToHttps(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return url;
        return url.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            ? "https://" + url.Substring(7)
            : url;
    }
}
