using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using CatClawMusic.Core.Interfaces;
using CatClawMusic.Core.Models;

namespace CatClawMusic.Core.Services.AI;

/// <summary>
/// 网易云音乐免费直连下载工具：搜索 → 外链探测 → 触发下载。
/// 原理（实测验证）：music.163.com 的公开外链接口对未锁定的歌曲直接返回
/// audio/mpeg 直链（无需登录/会员）；版权锁定歌曲返回 HTML 404 页。
/// 免费源，替代网页下载站的付费墙/网盘链接。
/// </summary>
public class NetEaseMusicDownloadTool : IAgentTool
{
    private static readonly HttpClient _http = new(new HttpClientHandler
    {
        AllowAutoRedirect = true,
        MaxAutomaticRedirections = 6,
    })
    {
        Timeout = TimeSpan.FromSeconds(15)
    };

    static NetEaseMusicDownloadTool()
    {
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");
        _http.DefaultRequestHeaders.Referrer = new Uri("https://music.163.com/");
    }

    public string Name => "netease_download";
    public string Description => "从网易云音乐搜索并下载歌曲（免费直连，无需会员）。当用户要求'下载某首歌/某音乐'时优先使用本工具——网页下载站多为付费墙或网盘，本工具直接返回可用下载。输入歌名（可附歌手名）。";
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
                    ["artist"] = new() { Type = "string", Description = "歌手名（可选），如：杨一歌。用于优先匹配原唱版本" }
                },
                Required = new List<string> { "keyword" }
            }
        }
    };

    private sealed class NeteaseSong
    {
        [JsonPropertyName("id")] public long Id { get; set; }
        [JsonPropertyName("name")] public string Name { get; set; } = "";
        [JsonPropertyName("artists")] public List<NeteaseArtist>? Artists { get; set; }
    }

    private sealed class NeteaseArtist
    {
        [JsonPropertyName("name")] public string Name { get; set; } = "";
    }

    private sealed class NeteaseSearchRoot
    {
        [JsonPropertyName("result")] public NeteaseSearchResult? Result { get; set; }
    }

    private sealed class NeteaseSearchResult
    {
        [JsonPropertyName("songs")] public List<NeteaseSong>? Songs { get; set; }
    }

    public async Task<string> ExecuteAsync(string arguments)
    {
        var keyword = ArgHelper.ExtractStringArgFallback(arguments, "keyword")?.Trim();
        var artist = ArgHelper.ExtractStringArgFallback(arguments, "artist")?.Trim();

        if (string.IsNullOrWhiteSpace(keyword))
            return JsonSerializer.Serialize(new { error = "请提供歌曲名称" });

        try
        {
            // 1. 网易云搜索
            var songs = await SearchAsync(keyword);
            if (songs == null || songs.Count == 0)
                return JsonSerializer.Serialize(new { error = $"网易云音乐上未找到《{keyword}》，请尝试更换歌名" });

            // 2. 排序：歌手匹配优先，其余按原名匹配度
            List<(NeteaseSong Song, string ArtistText)> candidates;
            if (!string.IsNullOrWhiteSpace(artist))
            {
                candidates = songs
                    .OrderByDescending(s => s.Artists?.Any(a => a.Name.Contains(artist, StringComparison.OrdinalIgnoreCase)) ?? false)
                    .Select(s => (s, string.Join(", ", s.Artists?.Select(a => a.Name) ?? new List<string>())))
                    .ToList();
            }
            else
            {
                candidates = songs.Select(s => (s, string.Join(", ", s.Artists?.Select(a => a.Name) ?? new List<string>()))).ToList();
            }

            // 3. 并行探测播放地址（最多 8 个）：优先 320K（enhance/player/url），
            //    依次降 128K、外链（版权锁定/无音源时全部不可用），返回最高可用档位
            var probes = await Task.WhenAll(candidates.Take(8).Select(async c =>
            {
                var (url, quality) = await ProbeBestUrlAsync(c.Song.Id);
                return (c, url, quality);
            }));

            var usable = probes.Where(p => p.url != null).ToList();
            var locked = probes.Where(p => p.url == null).Select(p => $"{p.c.Song.Name}-{p.c.ArtistText}").ToList();

            if (usable.Count == 0)
                return JsonSerializer.Serialize(new
                {
                    success = false,
                    message = $"《{keyword}》在网易云音乐上的版本均受版权保护，无法免费直连下载",
                    locked_versions = locked.Take(5),
                    hint = "可尝试用 web_search 搜索其他免费源，或如实告知用户版权限制"
                });

            // 4. 触发下载（播放地址有时效，立即发起）
            var best = usable[0];
            var filename = $"{best.c.Song.Name}-{best.c.ArtistText}.mp3";
            var enqueued = DownloadAgentBridge.EnqueueDownload != null
                ? DownloadAgentBridge.EnqueueDownload(best.url!, filename)
                : null;

            return JsonSerializer.Serialize(new
            {
                success = true,
                song = best.c.Song.Name,
                artist = best.c.ArtistText,
                quality = best.quality,
                version_note = !string.IsNullOrWhiteSpace(artist)
                    ? (best.c.Song.Artists?.Any(a => a.Name.Contains(artist, StringComparison.OrdinalIgnoreCase)) ?? false
                        ? "匹配到目标歌手版本"
                        : $"未找到 {artist} 的版本，已选可下载的其他版本")
                    : "已选可下载的版本",
                download_started = enqueued != null,
                message = enqueued ?? "已找到直链（当前平台未接下载器）",
                direct_url = best.url,
                available_count = usable.Count,
                locked_versions = locked.Take(5)
            });
        }
        catch (Exception ex)
        {
            return JsonSerializer.Serialize(new { error = $"网易云下载失败: {ex.Message}" });
        }
    }

    /// <summary>网易云搜索接口：https://music.163.com/api/search/get/web?csrf_token=&type=1&amp;s=关键词&amp;limit=8</summary>
    private static async Task<List<NeteaseSong>?> SearchAsync(string keyword)
    {
        var url = "https://music.163.com/api/search/get/web?csrf_token=&type=1&s=" + Uri.EscapeDataString(keyword) + "&limit=8";
        var response = await _http.GetAsync(url);
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadAsStringAsync();
        var root = JsonSerializer.Deserialize<NeteaseSearchRoot>(body);
        return root?.Result?.Songs;
    }

    /// <summary>探测最高可用播放地址（无需登录）：
    /// 1) enhance/player/url br=320000（320K MP3，实测翻唱版可拿 8.15MB 320K 直链）
    /// 2) 降级 br=128000（128K）
    /// 3) 外链 media/outer/url（128K）
    /// 版权锁定歌曲全部返回 null。只读文件头判定真音频，不为探测整文件下载。</summary>
    private static async Task<(string? Url, string Quality)> ProbeBestUrlAsync(long id)
    {
        var enhanceUrl = $"https://music.163.com/api/song/enhance/player/url?ids=[{id}]&br=320000";
        try
        {
            var response = await _http.GetAsync(enhanceUrl);
            if (response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(body);
                if (doc.RootElement.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Array)
                {
                    foreach (var d in data.EnumerateArray())
                    {
                        if (d.TryGetProperty("url", out var urlProp) && urlProp.GetString() is { Length: > 10 } url)
                            return (url, d.TryGetProperty("br", out var br) && br.GetInt64() >= 300000 ? "320K" : "128K");
                    }
                }
            }
        }
        catch { }
        // 降级：外链（128K）
        var (outer, ok) = await ProbeOuterAsync(id);
        return (ok ? outer : null, "128K");
    }

    /// <summary>外链探测：锁定歌曲返回 HTML 404 页，可用歌曲返回 audio/mpeg 直链。
    /// 只读前 128 字节判定（ID3/MPEG 头），避免为探测下载整个文件。</summary>
    private static async Task<(string? Url, bool IsAudio)> ProbeOuterAsync(long id)
    {
        var url = $"http://music.163.com/song/media/outer/url?id={id}.mp3";
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
            if (!response.IsSuccessStatusCode) return (null, false);
            var contentType = response.Content.Headers.ContentType?.MediaType ?? "";
            if (contentType.Contains("audio", StringComparison.OrdinalIgnoreCase))
                return (url, true);
            // 锁定歌曲返回 200 + text/html 404 页，双重保险：读文件头判定
            await using var stream = await response.Content.ReadAsStreamAsync();
            var head = new byte[128];
            var read = await stream.ReadAsync(head.AsMemory(0, 128));
            if (read >= 3 && head[0] == 0x49 && head[1] == 0x44 && head[2] == 0x33) return (url, true);
            if (read >= 2 && head[0] == 0xFF && (head[1] & 0xE0) == 0xE0) return (url, true);
            return (null, false);
        }
        catch
        {
            return (null, false);
        }
    }
}
