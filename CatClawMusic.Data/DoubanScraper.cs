using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.RegularExpressions;
using CatClawMusic.Core.Interfaces;

namespace CatClawMusic.Data;

/// <summary>
/// 豆瓣艺术家元数据刮削器
/// 数据源：豆瓣音乐人/歌手页面，中文简介质量高
/// 注意：豆瓣 API 需要登录，这里使用网页抓取方式
/// </summary>
public class DoubanScraper : IArtistMetadataScraper
{
    /// <summary>HTTP 客户端，用于访问豆瓣网页/API</summary>
    private readonly HttpClient _http;
    /// <summary>艺术家封面缓存目录绝对路径</summary>
    private readonly string _cacheDir;

    /// <summary>数据源名称：豆瓣</summary>
    public string SourceName => "豆瓣";

    /// <summary>
    /// 初始化豆瓣刮削器，配置模拟浏览器请求头以规避反爬。
    /// </summary>
    /// <param name="cacheDir">艺术家封面缓存目录（不存在会自动创建）。</param>
    public DoubanScraper(string cacheDir)
    {
        _cacheDir = cacheDir;
        Directory.CreateDirectory(_cacheDir);
        _http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        // 模拟浏览器请求头，避免被反爬
        _http.DefaultRequestHeaders.UserAgent.ParseAdd(
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");
        _http.DefaultRequestHeaders.Add("Accept", "application/json");
        _http.DefaultRequestHeaders.Add("Accept", "text/html");
        _http.DefaultRequestHeaders.Add("Accept-Language", "zh-CN,zh;q=0.9");
        _http.DefaultRequestHeaders.Add("Referer", "https://www.douban.com/");
    }

    /// <summary>
    /// 搜索豆瓣艺术家。
    /// 优先使用 JSON 接口（cat=1002 音乐分类），失败或返回空时回退到网页搜索。
    /// 对每个结果尝试调用 EnrichArtistInfoAsync 获取详细信息。
    /// </summary>
    /// <param name="name">艺术家名称关键词。</param>
    /// <param name="limit">最大返回数量。</param>
    /// <returns>匹配的艺术家列表；请求失败时返回空列表。</returns>
    public async Task<List<ArtistSearchResult>> SearchArtistsAsync(string name, int limit = 10)
    {
        var results = new List<ArtistSearchResult>();
        try
        {
            // 豆瓣音乐搜索接口（JSON格式）
            // cat=1002 表示音乐分类
            var searchUrl = $"https://www.douban.com/search?q={Uri.EscapeDataString(name)}&cat=1002&format=json";
            var response = await _http.GetAsync(searchUrl);

            // 如果 JSON 接口不可用，尝试网页搜索
            if (!response.IsSuccessStatusCode)
            {
                return await SearchByWebAsync(name, limit);
            }

            var body = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(body);

            // 解析搜索结果
            if (doc.RootElement.TryGetProperty("items", out var items))
            {
                int count = 0;
                foreach (var item in items.EnumerateArray())
                {
                    if (count++ >= limit) break;

                    var title = item.TryGetProperty("title", out var t) ? t.GetString() ?? "" : "";
                    var url = item.TryGetProperty("url", out var u) ? u.GetString() ?? "" : "";
                    var cover = item.TryGetProperty("cover", out var c) ? c.GetString() : null;
                    var desc = item.TryGetProperty("desc", out var d) ? d.GetString() : null;

                    if (string.IsNullOrEmpty(title)) continue;

                    // 提取豆瓣 ID
                    var id = ExtractDoubanId(url);

                    var result = new ArtistSearchResult
                    {
                        Source = SourceName,
                        Id = string.IsNullOrEmpty(id) ? $"douban_{title.GetHashCode():x}" : $"douban_{id}",
                        Name = title,
                        CoverUrl = cover,
                        Description = desc?.Length > 300 ? desc[..300] + "..." : desc
                    };

                    // 尝试获取更详细的艺术家信息
                    if (!string.IsNullOrEmpty(url))
                    {
                        await EnrichArtistInfoAsync(result, url);
                    }

                    results.Add(result);
                }
            }

            // 如果 JSON 接口返回空，尝试网页搜索
            if (results.Count == 0)
            {
                return await SearchByWebAsync(name, limit);
            }
        }
        catch (Exception ex)
        {
            Log.Debug("DoubanScraper", $"DoubanScraper.SearchArtistsAsync 错误: {ex.Message}");
            // 发生错误时尝试网页搜索
            try { results = await SearchByWebAsync(name, limit); }
            catch { }
        }

        return results;
    }

    /// <summary>通过网页搜索获取艺术家信息（fallback）</summary>
    private async Task<List<ArtistSearchResult>> SearchByWebAsync(string name, int limit)
    {
        var results = new List<ArtistSearchResult>();
        try
        {
            // 使用豆瓣音乐人搜索页面
            var url = $"https://www.douban.com/search?q={Uri.EscapeDataString(name)}&cat=1002";
            var response = await _http.GetAsync(url);
            if (!response.IsSuccessStatusCode) return results;

            var html = await response.Content.ReadAsStringAsync();

            // 使用正则解析搜索结果（简单解析，不依赖 HTML 解析库）
            // 匹配音乐人/乐队条目
            var pattern = @"<div class=""result"">.*?<a href=""(https?://www\.douban\.com/celeb/(\d+)/)"".*?>(.*?)</a>.*?<img src=""(.*?)"".*?</div>";
            var matches = Regex.Matches(html, pattern, RegexOptions.Singleline);

            int count = 0;
            foreach (Match match in matches)
            {
                if (count++ >= limit) break;

                var celebUrl = match.Groups[1].Value;
                var celebId = match.Groups[2].Value;
                var title = System.Net.WebUtility.HtmlDecode(match.Groups[3].Value).Trim();
                var cover = match.Groups[4].Value;

                // 移除 HTML 标签
                title = Regex.Replace(title, "<.*?>", "");

                if (string.IsNullOrEmpty(title)) continue;

                var result = new ArtistSearchResult
                {
                    Source = SourceName,
                    Id = $"douban_{celebId}",
                    Name = title,
                    CoverUrl = cover
                };

                results.Add(result);
            }

            // 如果正则没匹配到，尝试另一种格式
            if (results.Count == 0)
            {
                var pattern2 = @"<a href=""/celeb/(\d+)/""[^>]*>([^<]+)</a>";
                var matches2 = Regex.Matches(html, pattern2);
                foreach (Match match in matches2)
                {
                    if (count++ >= limit) break;
                    var celebId = match.Groups[1].Value;
                    var title = System.Net.WebUtility.HtmlDecode(match.Groups[2].Value).Trim();
                    if (string.IsNullOrEmpty(title)) continue;
                    results.Add(new ArtistSearchResult
                    {
                        Source = SourceName,
                        Id = $"douban_{celebId}",
                        Name = title
                    });
                }
            }
        }
        catch (Exception ex)
        {
            Log.Debug("DoubanScraper", $"DoubanScraper.SearchByWebAsync 错误: {ex.Message}");
        }

        return results;
    }

    /// <summary>获取艺术家详细信息（简介、性别、地区等）</summary>
    private async Task EnrichArtistInfoAsync(ArtistSearchResult result, string url)
    {
        try
        {
            // 如果是音乐人页面，获取详情
            if (url.Contains("/celeb/") || url.Contains("/musician/"))
            {
                var response = await _http.GetAsync(url);
                if (!response.IsSuccessStatusCode) return;

                var html = await response.Content.ReadAsStringAsync();

                // 解析简介
                var bioPattern = @"<div class=""intro"">.*?<p>(.*?)</p>";
                var bioMatch = Regex.Match(html, bioPattern, RegexOptions.Singleline);
                if (bioMatch.Success)
                {
                    var bio = System.Net.WebUtility.HtmlDecode(bioMatch.Groups[1].Value).Trim();
                    bio = Regex.Replace(bio, "<.*?>", ""); // 移除 HTML 标签
                    if (!string.IsNullOrEmpty(bio))
                        result.Description = bio.Length > 500 ? bio[..500] + "..." : bio;
                }

                // 解析性别
                if (html.Contains("男") && !html.Contains("女"))
                    result.Gender = "男";
                else if (html.Contains("女") && !html.Contains("男"))
                    result.Gender = "女";

                // 解析地区
                var regionPattern = @"(中国|日本|韩国|美国|英国|德国|法国|加拿大|澳大利亚).*?(艺人|音乐人|歌手)";
                var regionMatch = Regex.Match(html, regionPattern);
                if (regionMatch.Success)
                    result.Region = regionMatch.Groups[1].Value;
            }
        }
        catch (Exception ex)
        {
            Log.Debug("DoubanScraper", $"DoubanScraper.EnrichArtistInfoAsync 错误: {ex.Message}");
        }
    }

    /// <summary>从豆瓣 URL 中提取 ID</summary>
    private static string? ExtractDoubanId(string? url)
    {
        if (string.IsNullOrEmpty(url)) return null;
        var match = Regex.Match(url, @"(?:celeb|musician|artist)/(\d+)");
        return match.Success ? match.Groups[1].Value : null;
    }

    /// <summary>
    /// 下载豆瓣艺术家封面图片到缓存目录。
    /// 文件名添加 "_douban" 后缀以避免与其他来源的封面冲突。
    /// </summary>
    /// <param name="coverUrl">封面图片 URL。</param>
    /// <param name="artistName">艺术家名称（用于生成缓存文件名）。</param>
    /// <returns>缓存文件绝对路径；URL 为空、已存在缓存或下载失败时返回 null。</returns>
    public async Task<string?> DownloadAndCacheArtistCoverAsync(string coverUrl, string artistName)
    {
        if (string.IsNullOrEmpty(coverUrl)) return null;
        try
        {
            var safeName = string.Join("_", artistName.Split(Path.GetInvalidFileNameChars(), StringSplitOptions.RemoveEmptyEntries));
            var cachePath = Path.Combine(_cacheDir, $"{safeName}_douban.jpg");

            if (File.Exists(cachePath)) return cachePath;

            var response = await _http.GetAsync(coverUrl);
            if (!response.IsSuccessStatusCode) return null;

            using var fs = File.Create(cachePath);
            await response.Content.CopyToAsync(fs);
            return cachePath;
        }
        catch (Exception ex)
        {
            Log.Debug("DoubanScraper", $"DoubanScraper.DownloadAndCacheArtistCoverAsync 错误: {ex.Message}");
            return null;
        }
    }
}
