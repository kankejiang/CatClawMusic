using System.Text;
using System.Text.Json;
using CatClawMusic.Core.Interfaces;

namespace CatClawMusic.Data;

/// <summary>
/// 搜索结果条目
/// </summary>
public class SearchResultItem
{
    /// <summary>来源标识：netease / qq / kugou / soda / apple</summary>
    public string Source { get; set; } = "";
    /// <summary>歌曲在对应来源中的唯一 ID</summary>
    public string Id { get; set; } = "";
    /// <summary>歌曲标题</summary>
    public string Title { get; set; } = "";
    /// <summary>艺术家名（多艺术家以 " / " 分隔）</summary>
    public string Artist { get; set; } = "";
    /// <summary>专辑名</summary>
    public string? Album { get; set; }
    /// <summary>歌曲时长（毫秒）</summary>
    public long DurationMs { get; set; }
    /// <summary>封面图 URL</summary>
    public string? CoverUrl { get; set; }
    /// <summary>歌词文本（LRC 格式）</summary>
    public string? LrcContent { get; set; }
    /// <summary>翻译歌词文本（LRC 格式）</summary>
    public string? TlyricContent { get; set; }
    /// <summary>源特有字段（如酷狗的 FileHash），用于歌词获取等后续操作</summary>
    public Dictionary<string, object>? Internal { get; set; }
}

/// <summary>
/// 多源在线搜索服务 — 聚合网易云、QQ音乐、酷狗、汽水音乐、Apple Music
/// 参考 Lyrico-Plugins 各源实现
/// </summary>
public class MultiSourceSearchService
{
    /// <summary>共享的 HttpClient，超时 12 秒</summary>
    private readonly HttpClient _http;

    /// <summary>
    /// 初始化多源搜索服务，配置默认 User-Agent 模拟浏览器请求。
    /// </summary>
    public MultiSourceSearchService()
    {
        _http = new HttpClient { Timeout = TimeSpan.FromSeconds(12) };
        _http.DefaultRequestHeaders.Add("User-Agent",
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");
    }

    // ═══════════════════════════════════════════
    // 公开接口
    // ═══════════════════════════════════════════

    /// <summary>从所有源并行搜索歌曲</summary>
    public async Task<List<SearchResultItem>> SearchAllAsync(string keyword)
    {
        var results = new List<SearchResultItem>();
        var tasks = new[]
        {
            SearchNetEaseAsync(keyword),
            SearchQQAsync(keyword),
            SearchKuGouAsync(keyword),
            SearchSodaAsync(keyword),
            SearchAppleAsync(keyword)
        };

        var all = await Task.WhenAll(tasks);
        foreach (var r in all)
            if (r != null) results.AddRange(r);
        return results;
    }

    /// <summary>从指定源获取歌词</summary>
    public async Task<SearchResultItem?> FetchLyricAsync(SearchResultItem song)
    {
        return song.Source switch
        {
            "netease" => await FetchNetEaseLyricAsync(song),
            "qq" => await FetchQQLyricAsync(song),
            "kugou" => await FetchKuGouLyricAsync(song),
            "soda" => await FetchSodaLyricAsync(song),
            "apple" => await FetchAppleLyricAsync(song),
            _ => null
        };
    }

    // ═══════════════════════════════════════════
    // 网易云音乐
    // ═══════════════════════════════════════════

    /// <summary>
    /// 通过网易云 cloudsearch API 搜索歌曲。
    /// </summary>
    /// <param name="keyword">搜索关键词。</param>
    /// <returns>搜索结果列表；请求失败时返回 null。</returns>
    private async Task<List<SearchResultItem>?> SearchNetEaseAsync(string keyword)
    {
        try
        {
            // Use cloudsearch API (more reliable than old /api/search/get)
            var url = "https://music.163.com/api/cloudsearch/pc";
            var body = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["s"] = keyword, ["type"] = "1", ["offset"] = "0", ["limit"] = "8"
            });
            var resp = await _http.PostAsync(url, body);
            var json = await resp.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);

            var songs = doc.RootElement.TryGetProperty("result", out var result) &&
                        result.TryGetProperty("songs", out var sArr) ? sArr : default;

            if (songs.ValueKind != JsonValueKind.Array) return null;

            var results = new List<SearchResultItem>();
            foreach (var s in songs.EnumerateArray())
            {
                var artists = new List<string>();
                if (s.TryGetProperty("ar", out var ar))
                    foreach (var a in ar.EnumerateArray())
                    {
                        var an = a.GetProperty("name").GetString();
                        if (!string.IsNullOrWhiteSpace(an)) artists.Add(an);
                    }

                results.Add(new SearchResultItem
                {
                    Source = "netease",
                    Id = s.GetProperty("id").GetInt64().ToString(),
                    Title = s.GetProperty("name").GetString() ?? "",
                    Artist = string.Join(" / ", artists),
                    Album = s.TryGetProperty("al", out var al) ? al.GetProperty("name").GetString() : null,
                    DurationMs = s.TryGetProperty("dt", out var dt) ? dt.GetInt64() : 0,
                    CoverUrl = s.TryGetProperty("al", out var alc) && alc.TryGetProperty("picUrl", out var pic)
                        ? pic.GetString() : null
                });
            }
            return results;
        }
        catch { return null; }
    }

    /// <summary>
    /// 获取网易云歌曲歌词（含翻译歌词）。
    /// </summary>
    /// <param name="song">待获取歌词的歌曲（需含 Id）。</param>
    /// <returns>更新后的歌曲对象；失败时返回 null。</returns>
    private async Task<SearchResultItem?> FetchNetEaseLyricAsync(SearchResultItem song)
    {
        try
        {
            var url = $"https://music.163.com/api/song/lyric?id={song.Id}&lv=1&kv=1&tv=-1";
            var json = await _http.GetStringAsync(url);
            using var doc = JsonDocument.Parse(json);

            string? lrc = null, tlyric = null;
            if (doc.RootElement.TryGetProperty("lrc", out var ln) && ln.TryGetProperty("lyric", out var lt))
                lrc = lt.GetString();
            if (doc.RootElement.TryGetProperty("tlyric", out var tn) && tn.TryGetProperty("lyric", out var tt))
                tlyric = tt.GetString();
            if (string.IsNullOrWhiteSpace(lrc)) return null;

            song.LrcContent = lrc;
            song.TlyricContent = tlyric;
            return song;
        }
        catch { return null; }
    }

    // ═══════════════════════════════════════════
    // QQ音乐
    // ═══════════════════════════════════════════

    /// <summary>
    /// 通过 QQ 音乐 client_search_cp 接口搜索歌曲。
    /// </summary>
    /// <param name="keyword">搜索关键词。</param>
    /// <returns>搜索结果列表；请求失败时返回 null。</returns>
    private async Task<List<SearchResultItem>?> SearchQQAsync(string keyword)
    {
        try
        {
            var url = $"https://c.y.qq.com/soso/fcgi-bin/client_search_cp?p=1&n=8&w={Uri.EscapeDataString(keyword)}&format=json&platform=yqq";
            var json = await _http.GetStringAsync(url);
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("data", out var data)) return null;
            if (!data.TryGetProperty("song", out var songData)) return null;
            if (!songData.TryGetProperty("list", out var list)) return null;

            var results = new List<SearchResultItem>();
            foreach (var s in list.EnumerateArray())
            {
                var singers = new List<string>();
                if (s.TryGetProperty("singer", out var singerArr))
                    foreach (var si in singerArr.EnumerateArray())
                    {
                        var sn = si.GetProperty("name").GetString();
                        if (!string.IsNullOrWhiteSpace(sn)) singers.Add(sn);
                    }

                var songmid = s.GetProperty("songmid").GetString() ?? "";
                var albummid = s.TryGetProperty("albummid", out var am) ? am.GetString() : "";

                results.Add(new SearchResultItem
                {
                    Source = "qq",
                    Id = songmid,
                    Title = s.GetProperty("songname").GetString() ?? "",
                    Artist = string.Join(" / ", singers),
                    Album = s.TryGetProperty("albumname", out var an) ? an.GetString() : null,
                    DurationMs = s.TryGetProperty("interval", out var dur) ? dur.GetInt64() * 1000 : 0,
                    CoverUrl = !string.IsNullOrEmpty(albummid)
                        ? $"https://y.gtimg.cn/music/photo_new/T002R1200x1200M000{albummid}.jpg"
                        : null
                });
            }
            return results;
        }
        catch { return null; }
    }

    /// <summary>
    /// 获取 QQ 音乐歌词（含翻译歌词）。需设置 Referer 头规避防盗链。
    /// QQ 接口可能返回 base64 编码的歌词，自动检测并解码。
    /// </summary>
    /// <param name="song">待获取歌词的歌曲（需含 Id 即 songmid）。</param>
    /// <returns>更新后的歌曲对象；失败时返回 null。</returns>
    private async Task<SearchResultItem?> FetchQQLyricAsync(SearchResultItem song)
    {
        try
        {
            var url = $"https://c.y.qq.com/lyric/fcgi-bin/fcg_query_lyric_new.fcg?songmid={song.Id}&format=json&nobase64=1";
            var req = new HttpRequestMessage(HttpMethod.Get, url);
            req.Headers.Referrer = new Uri("https://y.qq.com/");
            var resp = await _http.SendAsync(req);
            var json = await resp.Content.ReadAsStringAsync();

            using var doc = JsonDocument.Parse(json);
            string? lrc = null, tlyric = null;
            if (doc.RootElement.TryGetProperty("lyric", out var ln))
            {
                var v = ln.ValueKind == JsonValueKind.String ? ln.GetString() : "";
                if (!string.IsNullOrWhiteSpace(v))
                {
                    // QQ sometimes returns base64 even with nobase64=1
                    if (!v.Contains('[') && v.All(c => char.IsLetterOrDigit(c) || c == '/' || c == '+' || c == '=' || c == '\n'))
                    {
                        try { v = Encoding.UTF8.GetString(Convert.FromBase64String(v)); } catch { }
                    }
                    lrc = v;
                }
            }
            if (doc.RootElement.TryGetProperty("trans", out var tn))
            {
                var v = tn.ValueKind == JsonValueKind.String ? tn.GetString() : "";
                if (!string.IsNullOrWhiteSpace(v) && lrc != null)
                {
                    if (!v.Contains('[') && v.All(c => char.IsLetterOrDigit(c) || c == '/' || c == '+' || c == '=' || c == '\n'))
                    {
                        try { v = Encoding.UTF8.GetString(Convert.FromBase64String(v)); } catch { }
                    }
                    tlyric = v;
                }
            }

            if (string.IsNullOrWhiteSpace(lrc)) return null;
            song.LrcContent = lrc;
            song.TlyricContent = tlyric;
            return song;
        }
        catch { return null; }
    }

    // ═══════════════════════════════════════════
    // 酷狗音乐
    // ═══════════════════════════════════════════

    /// <summary>
    /// 通过酷狗 complexsearch 接口搜索歌曲。
    /// </summary>
    /// <param name="keyword">搜索关键词。</param>
    /// <returns>搜索结果列表；请求失败时返回 null。</returns>
    private async Task<List<SearchResultItem>?> SearchKuGouAsync(string keyword)
    {
        try
        {
            var url = $"https://complexsearch.kugou.com/v2/search/song?keyword={Uri.EscapeDataString(keyword)}&page=1&pagesize=8&clientver=20000&platform=WebFilter";
            var req = new HttpRequestMessage(HttpMethod.Get, url);
            req.Headers.Add("x-router", "complexsearch.kugou.com");
            var resp = await _http.SendAsync(req);
            var json = await resp.Content.ReadAsStringAsync();

            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("data", out var data)) return null;
            if (!data.TryGetProperty("lists", out var lists)) return null;

            var results = new List<SearchResultItem>();
            foreach (var s in lists.EnumerateArray())
            {
                var singers = new List<string>();
                if (s.TryGetProperty("Singers", out var singerArr))
                    foreach (var si in singerArr.EnumerateArray())
                    {
                        var sn = si.TryGetProperty("name", out var nn) ? nn.GetString() :
                                 si.TryGetProperty("Name", out var nm) ? nm.GetString() : null;
                        if (!string.IsNullOrWhiteSpace(sn)) singers.Add(sn);
                    }

                results.Add(new SearchResultItem
                {
                    Source = "kugou",
                    Id = s.TryGetProperty("ID", out var idEl) ? idEl.GetString() ?? "" : "",
                    Title = s.TryGetProperty("SongName", out var snEl) ? snEl.GetString() ?? "" : "",
                    Artist = string.Join(" / ", singers),
                    Album = s.TryGetProperty("AlbumName", out var anEl) ? anEl.GetString() : null,
                    DurationMs = s.TryGetProperty("Duration", out var dEl) ? dEl.GetInt64() * 1000 : 0,
                    CoverUrl = s.TryGetProperty("Image", out var imgEl) ? imgEl.GetString() : null,
                    Internal = new Dictionary<string, object> {
                        ["hash"] = s.TryGetProperty("FileHash", out var fh) ? fh.GetString() ?? "" : ""
                    }
                });
            }
            return results;
        }
        catch { return null; }
    }

    /// <summary>
    /// 获取酷狗歌词。两步流程：先通过 hash 搜索候选歌词，再下载 LRC 格式歌词。
    /// </summary>
    /// <param name="song">待获取歌词的歌曲（Internal 中需含 FileHash）。</param>
    /// <returns>更新后的歌曲对象；失败时返回 null。</returns>
    private async Task<SearchResultItem?> FetchKuGouLyricAsync(SearchResultItem song)
    {
        try
        {
            var hash = song.Internal?.GetValueOrDefault("hash", "")?.ToString() ?? "";
            if (string.IsNullOrEmpty(hash)) return null;

            // Step 1: search lyrics by hash
            var searchUrl = $"https://lyrics.kugou.com/v1/search?" +
                $"keyword={Uri.EscapeDataString(song.Artist + " - " + song.Title)}" +
                $"&hash={hash}&duration={song.DurationMs}&lrctxt=1&man=no&clientver=20000&platform=WebFilter";
            var searchJson = await _http.GetStringAsync(searchUrl);
            using var searchDoc = JsonDocument.Parse(searchJson);
            if (!searchDoc.RootElement.TryGetProperty("candidates", out var cands) ||
                cands.GetArrayLength() == 0) return null;

            var candidate = cands[0];
            var accesskey = candidate.GetProperty("accesskey").GetString() ?? "";
            var lyricId = candidate.GetProperty("id").GetString() ?? "";

            // Step 2: download lyrics (request LRC format, not KRC)
            var dlUrl = $"https://lyrics.kugou.com/download?" +
                $"id={lyricId}&accesskey={accesskey}&charset=utf8&fmt=lrc&ver=1&clientver=20000&platform=WebFilter";
            var dlJson = await _http.GetStringAsync(dlUrl);
            using var dlDoc = JsonDocument.Parse(dlJson);

            string? lyricText = null;
            if (dlDoc.RootElement.TryGetProperty("content", out var content))
            {
                lyricText = content.GetString();
                // If it's still KRC (base64), try to get what we can
                if (!string.IsNullOrWhiteSpace(lyricText) &&
                    !lyricText.Contains('[') && lyricText.All(c => char.IsLetterOrDigit(c) || c == '/' || c == '+' || c == '=' || c == '\n'))
                {
                    try { lyricText = Encoding.UTF8.GetString(Convert.FromBase64String(lyricText)); } catch { }
                }
            }

            if (string.IsNullOrWhiteSpace(lyricText)) return null;
            song.LrcContent = lyricText;
            return song;
        }
        catch { return null; }
    }

    // ═══════════════════════════════════════════
    // 汽水音乐 (Soda / 抖音音乐)
    // ═══════════════════════════════════════════

    /// <summary>
    /// 通过抖音 web search item 接口搜索汽水音乐（抖音音乐）。
    /// </summary>
    /// <param name="keyword">搜索关键词。</param>
    /// <returns>搜索结果列表；请求失败时返回 null。</returns>
    private async Task<List<SearchResultItem>?> SearchSodaAsync(string keyword)
    {
        try
        {
            // Use douyin music search API
            var url = $"https://www.douyin.com/aweme/v1/web/search/item/?keyword={Uri.EscapeDataString(keyword)}&type=music&cursor=0&count=8&aid=6383";
            var json = await _http.GetStringAsync(url);
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("data", out var dataArr) || dataArr.ValueKind != JsonValueKind.Array)
                return null;

            var results = new List<SearchResultItem>();
            foreach (var item in dataArr.EnumerateArray())
            {
                if (!item.TryGetProperty("music_info", out var music)) continue;

                var singers = new List<string>();
                if (music.TryGetProperty("author_list", out var authorList) && authorList.ValueKind == JsonValueKind.Array)
                    foreach (var a in authorList.EnumerateArray())
                    {
                        var an = a.GetProperty("name").GetString();
                        if (!string.IsNullOrWhiteSpace(an)) singers.Add(an);
                    }
                if (singers.Count == 0 && music.TryGetProperty("author", out var auth))
                {
                    var an = auth.GetString();
                    if (!string.IsNullOrWhiteSpace(an)) singers.Add(an);
                }

                var coverUrl = "";
                if (music.TryGetProperty("cover_large", out var cov))
                {
                    if (cov.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var c in cov.EnumerateArray())
                        {
                            var u = c.GetString();
                            if (!string.IsNullOrWhiteSpace(u)) { coverUrl = u; break; }
                        }
                    }
                    else coverUrl = cov.GetString() ?? "";
                }

                results.Add(new SearchResultItem
                {
                    Source = "soda",
                    Id = music.TryGetProperty("id_str", out var idStr) ? idStr.GetString() ?? "" :
                         music.TryGetProperty("id", out var idNum) ? idNum.GetInt64().ToString() : "",
                    Title = music.TryGetProperty("title", out var t) ? t.GetString() ?? "" : "",
                    Artist = string.Join(" / ", singers),
                    DurationMs = music.TryGetProperty("duration", out var dur) ? dur.GetInt64() * 1000 : 0,
                    CoverUrl = coverUrl
                });
            }
            return results;
        }
        catch { return null; }
    }

    /// <summary>
    /// 获取汽水音乐歌词。当前为占位实现（汽水音乐 Luna API 需复杂签名，暂不支持）。
    /// </summary>
    /// <param name="song">待获取歌词的歌曲。</param>
    /// <returns>始终返回 null。</returns>
    private async Task<SearchResultItem?> FetchSodaLyricAsync(SearchResultItem song)
    {
        // 汽水音乐使用 Luna API，需要复杂的签名逻辑
        // 作为简化实现，暂不直接获取，用户可安装 Lyrico 插件获得支持
        return null;
    }

    // ═══════════════════════════════════════════
    // Apple Music
    // ═══════════════════════════════════════════

    /// <summary>
    /// 通过 iTunes Search API 搜索 Apple Music 歌曲。
    /// </summary>
    /// <param name="keyword">搜索关键词。</param>
    /// <returns>搜索结果列表；请求失败时返回 null。</returns>
    private async Task<List<SearchResultItem>?> SearchAppleAsync(string keyword)
    {
        try
        {
            var url = $"https://itunes.apple.com/search?term={Uri.EscapeDataString(keyword)}&media=music&entity=song&limit=8&country=CN&lang=zh_cn";
            var json = await _http.GetStringAsync(url);
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("results", out var resultsArr)) return null;

            var results = new List<SearchResultItem>();
            foreach (var r in resultsArr.EnumerateArray())
            {
                results.Add(new SearchResultItem
                {
                    Source = "apple",
                    Id = r.GetProperty("trackId").GetInt64().ToString(),
                    Title = r.TryGetProperty("trackName", out var tn) ? tn.GetString() ?? "" : "",
                    Artist = r.TryGetProperty("artistName", out var an) ? an.GetString() ?? "" : "",
                    Album = r.TryGetProperty("collectionName", out var cn) ? cn.GetString() : null,
                    DurationMs = r.TryGetProperty("trackTimeMillis", out var dur) ? dur.GetInt64() : 0,
                    CoverUrl = r.TryGetProperty("artworkUrl100", out var art)
                        ? art.GetString()?.Replace("100x100", "600x600") : null
                });
            }
            return results;
        }
        catch { return null; }
    }

    /// <summary>
    /// 通过 paxsenix 第三方 API 获取 Apple Music 歌词（无需 Developer Token）。
    /// 优先级：lrc &gt; elrc &gt; ttmlContent。
    /// </summary>
    /// <param name="song">待获取歌词的歌曲（需含 Id）。</param>
    /// <returns>更新后的歌曲对象；失败时返回 null。</returns>
    private async Task<SearchResultItem?> FetchAppleLyricAsync(SearchResultItem song)
    {
        if (string.IsNullOrWhiteSpace(song.Id)) return null;

        try
        {
            // 使用 paxsenix 第三方 Apple Music 歌词 API（无需 Developer Token）
            // 参考 Lyrico-Plugins apple/source.js getThirdPartyLyrics
            var url = $"https://lyrics.paxsenix.org/apple-music/lyrics?id={Uri.EscapeDataString(song.Id)}&ttml=false";
            var req = new HttpRequestMessage(HttpMethod.Get, url);
            req.Headers.Add("accept", "application/json");
            var resp = await _http.SendAsync(req);
            var json = await resp.Content.ReadAsStringAsync();

            using var doc = JsonDocument.Parse(json);

            // This API returns JSON with lrc (plain LRC), elrc (enhanced LRC), ttmlContent fields
            string? rawLrc = null;
            if (doc.RootElement.TryGetProperty("lrc", out var lrcEl) && lrcEl.ValueKind == JsonValueKind.String)
                rawLrc = lrcEl.GetString();
            if (string.IsNullOrWhiteSpace(rawLrc) &&
                doc.RootElement.TryGetProperty("elrc", out var elrcEl) && elrcEl.ValueKind == JsonValueKind.String)
                rawLrc = elrcEl.GetString();
            if (string.IsNullOrWhiteSpace(rawLrc) &&
                doc.RootElement.TryGetProperty("ttmlContent", out var ttmlEl) && ttmlEl.ValueKind == JsonValueKind.String)
                rawLrc = ttmlEl.GetString();

            if (string.IsNullOrWhiteSpace(rawLrc)) return null;
            song.LrcContent = rawLrc;
            return song;
        }
        catch (Exception ex)
        {
            Log.Debug("MultiSourceSearchService", $"[MultiSrc] Apple lyric: {ex.Message}");
            return null;
        }
    }
}
