using System.Text.Json;
using CatClawMusic.Core.Interfaces;
using CatClawMusic.Core.Models;

namespace CatClawMusic.Core.Services.AI;

/// <summary>
/// 酷我音乐无损下载工具：r.s 搜索 → mobi.s convert_url_with_sign 免签名直链。
/// 实测（2026-08）：FORMATS 含 ALFLAC 的歌请求 2000kflac 直接返回真 FLAC 直链
/// （25MB fLaC 头验证）；全链路无需登录/签名。是当前免费无损的最佳来源。
/// </summary>
public class KuwoMusicDownloadTool : IAgentTool
{
    private static readonly HttpClient _http = new(new HttpClientHandler
    {
        AllowAutoRedirect = true,
        MaxAutomaticRedirections = 6,
    })
    {
        Timeout = TimeSpan.FromSeconds(15)
    };

    static KuwoMusicDownloadTool()
    {
        System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Linux; Android 10; K) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/91.0.4472.114 Mobile Safari/537.36");
    }

    public string Name => "kuwo_download";
    public string Description => "从酷我音乐搜索并下载歌曲（免费直连，支持无损 FLAC）。当用户要求'下载某首歌/某音乐/要无损音质'时优先使用本工具——酷我有真无损直链且无需会员。输入歌名（可附歌手名）。";
    public bool IsReadOnly => true;

    public ToolDefinition GetDefinition() => new()
    {
        Function = new ToolFunctionDef
        {
            Name = Name,
            Description = Description,
            Parameters = new ToolParameterDef
            {
                Properties = new Dictionary<string, ToolParameterProperty>
                {
                    ["keyword"] = new() { Type = "string", Description = "歌曲名称（必填），如：苏公堤" },
                    ["artist"] = new() { Type = "string", Description = "歌手名（可选），如：杨一歌。用于优先匹配目标歌手版本" }
                },
                Required = new List<string> { "keyword" }
            }
        }
    };

    public async Task<string> ExecuteAsync(string arguments)
    {
        var keyword = ArgHelper.ExtractStringArgFallback(arguments, "keyword")?.Trim();
        var artist = ArgHelper.ExtractStringArgFallback(arguments, "artist")?.Trim();

        if (string.IsNullOrWhiteSpace(keyword))
            return JsonSerializer.Serialize(new { error = "请提供歌曲名称" });

        try
        {
            var songs = await SearchAsync(keyword);
            if (songs.Count == 0)
                return JsonSerializer.Serialize(new { error = $"酷我音乐上未找到《{keyword}》，可尝试 netease_download 或更换歌名" });

            // 歌手匹配优先，否则按音质档位排序（FLAC > 320K > 128K）
            List<(KuwoSong Song, string Quality)> candidates;
            if (!string.IsNullOrWhiteSpace(artist))
            {
                candidates = songs
                    .OrderByDescending(s => s.Artist.Contains(artist, StringComparison.OrdinalIgnoreCase))
                    .Select(s => (s, BestQuality(s)))
                    .ToList();
            }
            else
            {
                candidates = songs.Select(s => (s, BestQuality(s))).ToList();
            }

            // 并行取直链（按候选音质链请求，mobi.s 自动降级到可用档）
            var probes = await Task.WhenAll(candidates.Take(6).Select(async c =>
            {
                var url = await GetDirectUrlAsync(c.Song.Rid, c.Quality);
                return (c.Song, c.Quality, url);
            }));

            var usable = probes.Where(p => p.url != null).ToList();
            if (usable.Count == 0)
                return JsonSerializer.Serialize(new
                {
                    success = false,
                    message = $"《{keyword}》在酷我音乐上未能获取到播放地址",
                    hint = "可尝试 netease_download 或如实告知用户"
                });

            var best = usable[0];
            var filename = $"{best.Item1.Name}-{best.Item1.Artist}.{Extension(best.Item2)}";
            var enqueued = DownloadAgentBridge.EnqueueDownload != null
                ? DownloadAgentBridge.EnqueueDownload(best.url!, filename)
                : null;

            return JsonSerializer.Serialize(new
            {
                success = true,
                song = best.Item1.Name,
                artist = best.Item1.Artist,
                quality = QualityLabel(best.Item2),
                version_note = !string.IsNullOrWhiteSpace(artist)
                    ? (best.Item1.Artist.Contains(artist, StringComparison.OrdinalIgnoreCase)
                        ? "匹配到目标歌手版本"
                        : $"未找到 {artist} 的版本，已选可下载的其他版本")
                    : "已选可下载的版本",
                download_started = enqueued != null,
                message = enqueued ?? "已找到直链（当前平台未接下载器）",
                direct_url = best.url,
                available_count = usable.Count
            });
        }
        catch (Exception ex)
        {
            return JsonSerializer.Serialize(new { error = $"酷我下载失败: {ex.Message}" });
        }
    }

    private sealed class KuwoSong
    {
        public string Rid { get; set; } = "";
        public string Name { get; set; } = "";
        public string Artist { get; set; } = "";
        public string Formats { get; set; } = "";
    }

    /// <summary>r.s 搜索接口：返回单引号 JSON（jsonp 风格），按歌曲块分割逐字段提取。
    /// FORMATS 字段（如 AAC48|ALFLAC|MP3128|MP3H）直接暴露该曲可用音质。</summary>
    private async Task<List<KuwoSong>> SearchAsync(string keyword)
    {
        var songs = new List<KuwoSong>();
        var url = "http://search.kuwo.cn/r.s?all=" + Uri.EscapeDataString(keyword) +
                  "&ft=music&itemset=web_2013&rformat=json&encoding=utf8&pn=0&rn=10";
        var response = await _http.GetAsync(url);
        if (!response.IsSuccessStatusCode) return songs;
        var text = await ReadTextAsync(response);

        foreach (var block in System.Text.RegularExpressions.Regex.Split(text, @"\{'AARTIST'"))
        {
            if (!block.Contains("'SONGNAME'")) continue;
            var song = System.Text.RegularExpressions.Regex.Match(block, @"'SONGNAME':\s*'([^']*)'");
            var artist = System.Text.RegularExpressions.Regex.Match(block, @"'ARTIST':\s*'([^']*)'");
            var rid = System.Text.RegularExpressions.Regex.Match(block, @"'MUSICRID':\s*'([^']*)'");
            var formats = System.Text.RegularExpressions.Regex.Match(block, @"'FORMATS':\s*'([^']*)'");
            if (!song.Success || !rid.Success) continue;
            songs.Add(new KuwoSong
            {
                Rid = rid.Groups[1].Value.Replace("MUSIC_", ""),
                Name = System.Net.WebUtility.HtmlDecode(song.Groups[1].Value),
                Artist = System.Net.WebUtility.HtmlDecode(artist.Success ? artist.Groups[1].Value : ""),
                Formats = formats.Success ? formats.Groups[1].Value : ""
            });
        }
        return songs;
    }

    /// <summary>按 FORMATS 推断最高可用档位：ALFLAC→FLAC，MP3H/MP3320→320K，其余→128K</summary>
    private static string BestQuality(KuwoSong song)
    {
        if (song.Formats.Contains("ALFLAC", StringComparison.OrdinalIgnoreCase) || song.Formats.Contains("FLAC", StringComparison.OrdinalIgnoreCase))
            return "flac";
        if (song.Formats.Contains("MP3H", StringComparison.OrdinalIgnoreCase) || song.Formats.Contains("MP3320", StringComparison.OrdinalIgnoreCase))
            return "320k";
        return "128k";
    }

    private static string QualityLabel(string quality) => quality switch
    {
        "flac" => "FLAC无损",
        "320k" => "320K",
        _ => "128K"
    };

    private static string Extension(string quality) => quality == "flac" ? "flac" : "mp3";

    /// <summary>mobi.s 免签名直链：type=convert_url_with_sign&surl=1 返回 JSON 含 surl 直链。
    /// 请求高音质档位时服务端自动降级到该曲可用最高档。</summary>
    private static async Task<string?> GetDirectUrlAsync(string rid, string quality)
    {
        var br = quality switch
        {
            "flac" => "2000kflac",
            "320k" => "320kmp3",
            _ => "128kmp3"
        };
        var url = $"https://mobi.kuwo.cn/mobi.s?f=web&rid={rid}&br={br}&source=jiakong&type=convert_url_with_sign&surl=1";
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Referrer = new Uri("https://www.kuwo.cn/");
            var response = await _http.SendAsync(request);
            if (!response.IsSuccessStatusCode) return null;
            var body = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty("data", out var data) && data.TryGetProperty("surl", out var surl))
            {
                var s = surl.GetString();
                if (!string.IsNullOrWhiteSpace(s) && s.StartsWith("http")) return s;
            }
            // 旧格式 url=xxx
            var m = System.Text.RegularExpressions.Regex.Match(body, @"url=([^&\s]+)");
            if (m.Success) return System.Uri.UnescapeDataString(m.Groups[1].Value);
            return null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>r.s 响应 GBK/UTF-8 自动探测解码</summary>
    private static async Task<string> ReadTextAsync(HttpResponseMessage response)
    {
        var bytes = await response.Content.ReadAsByteArrayAsync();
        foreach (var enc in new[] { System.Text.Encoding.UTF8, System.Text.Encoding.GetEncoding("GBK") })
        {
            try
            {
                return enc.GetString(bytes);
            }
            catch { }
        }
        return System.Text.Encoding.UTF8.GetString(bytes);
    }
}
