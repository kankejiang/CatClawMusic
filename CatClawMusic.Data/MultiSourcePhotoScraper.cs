using System.Text.Json;

namespace CatClawMusic.Data;

/// <summary>
/// 多源艺术家照片刮削器（免 API Key，开箱即用）
/// 内部串联：QQ音乐 → iTunes → Wikipedia，与网易云 API 混合使用
/// </summary>
public class MultiSourcePhotoScraper : IArtistMetadataScraper
{
    private readonly string _artistCoverCacheDir;

    public string SourceName => "多源聚合";

    public MultiSourcePhotoScraper(string artistCoverCacheDir)
    {
        _artistCoverCacheDir = artistCoverCacheDir;
        Directory.CreateDirectory(_artistCoverCacheDir);
    }

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(8) };
        client.DefaultRequestHeaders.Add("User-Agent",
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36");
        return client;
    }

    /// <summary>搜索艺术家，依次尝试 QQ音乐 → iTunes → Wikipedia，聚合结果</summary>
    public async Task<List<ArtistSearchResult>> SearchArtistsAsync(string name, int limit = 10)
    {
        var results = new List<ArtistSearchResult>();

        // 需要确保 limit 足够小，让三个源各分一些
        var perSource = Math.Max(2, limit / 3);

        // 1. QQ音乐（免 API Key，华语覆盖好）
        var qqResults = await SearchQqMusicAsync(name, perSource);
        if (qqResults.Count > 0) results.AddRange(qqResults);

        // 2. iTunes（免 API Key，国际覆盖好）
        if (results.Count < limit)
        {
            var itunesResults = await SearchItunesAsync(name, perSource);
            if (itunesResults.Count > 0) results.AddRange(itunesResults);
        }

        // 3. Wikipedia（免 API Key，简介+照片）
        if (results.Count < limit)
        {
            var wikiResults = await SearchWikipediaAsync(name, perSource);
            if (wikiResults.Count > 0) results.AddRange(wikiResults);
        }

        return results.Take(limit).ToList();
    }

    /// <summary>下载并保存艺术家封面到缓存</summary>
    public async Task<string?> DownloadAndCacheArtistCoverAsync(string coverUrl, string artistName)
    {
        // 已经是本地路径
        if (!string.IsNullOrEmpty(coverUrl) && File.Exists(coverUrl))
            return coverUrl;

        try
        {
            var cachePath = GetArtistCoverPath(artistName);

            // 检查缓存
            if (File.Exists(cachePath)) return cachePath;

            using var client = CreateClient();
            var bytes = await client.GetByteArrayAsync(coverUrl);
            if (bytes is { Length: > 0 })
            {
                await File.WriteAllBytesAsync(cachePath, bytes);
                return cachePath;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[MultiSource] 下载封面失败: {ex.Message}");
        }

        return null;
    }

    // ==================== QQ音乐 ====================

    private async Task<List<ArtistSearchResult>> SearchQqMusicAsync(string name, int limit)
    {
        var results = new List<ArtistSearchResult>();
        try
        {
            using var client = CreateClient();
            // QQ音乐智能搜索接口，返回歌手列表
            var url = $"https://c.y.qq.com/splcloud/fcgi-bin/smartbox_new.fcg?key={Uri.EscapeDataString(name)}&format=json";
            var response = await client.GetAsync(url);
            if (!response.IsSuccessStatusCode) return results;

            var body = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(body);

            if (!doc.RootElement.TryGetProperty("data", out var data)) return results;
            if (!data.TryGetProperty("singer", out var singer)) return results;
            if (!singer.TryGetProperty("itemlist", out var itemList)) return results;

            foreach (var item in itemList.EnumerateArray())
            {
                var singerName = item.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
                var mid = item.TryGetProperty("mid", out var m) ? m.GetString() : null;

                if (string.IsNullOrEmpty(singerName)) continue;

                results.Add(new ArtistSearchResult
                {
                    Source = SourceName + "·QQ",
                    Id = $"qq_{mid ?? singerName.GetHashCode().ToString("x")}",
                    Name = singerName,
                    CoverUrl = mid != null
                        ? $"https://y.gtimg.cn/music/photo_new/T001R300x300M000{mid}.jpg"
                        : null
                });

                if (results.Count >= limit) break;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[MultiSource] QQ音乐搜索失败: {ex.Message}");
        }
        return results;
    }

    // ==================== iTunes ====================

    private async Task<List<ArtistSearchResult>> SearchItunesAsync(string name, int limit)
    {
        var results = new List<ArtistSearchResult>();
        try
        {
            using var client = CreateClient();
            // iTunes Search API，免 Key，限流约 20 次/分钟
            var url = $"https://itunes.apple.com/search?term={Uri.EscapeDataString(name)}&entity=musicArtist&limit={limit}&country=cn";
            var response = await client.GetAsync(url);
            if (!response.IsSuccessStatusCode) return results;

            var body = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(body);

            if (!doc.RootElement.TryGetProperty("results", out var items)) return results;

            foreach (var item in items.EnumerateArray())
            {
                var artistName = item.TryGetProperty("artistName", out var an) ? an.GetString() ?? "" : "";
                var artistId = item.TryGetProperty("artistId", out var ai) ? ai.GetInt64() : 0;
                var genre = item.TryGetProperty("primaryGenreName", out var gn) ? gn.GetString() : null;
                var artworkUrl = item.TryGetProperty("artworkUrl100", out var aw)
                    ? aw.GetString()?.Replace("100x100", "600x600")
                    : null;

                if (string.IsNullOrEmpty(artistName)) continue;

                results.Add(new ArtistSearchResult
                {
                    Source = SourceName + "·iTunes",
                    Id = $"itunes_{artistId}",
                    Name = artistName,
                    CoverUrl = artworkUrl,
                    ExtraInfo = genre
                });

                if (results.Count >= limit) break;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[MultiSource] iTunes搜索失败: {ex.Message}");
        }
        return results;
    }

    // ==================== Wikipedia ====================

    private async Task<List<ArtistSearchResult>> SearchWikipediaAsync(string name, int limit)
    {
        var results = new List<ArtistSearchResult>();
        try
        {
            using var client = CreateClient();

            // 先用 Wikipedia REST API 获取摘要（优先中文维基）
            var summaryUrl = $"https://zh.wikipedia.org/api/rest_v1/page/summary/{Uri.EscapeDataString(name)}";
            var summaryResponse = await client.GetAsync(summaryUrl);

            if (!summaryResponse.IsSuccessStatusCode)
            {
                // 回退到英文维基
                summaryUrl = $"https://en.wikipedia.org/api/rest_v1/page/summary/{Uri.EscapeDataString(name)}";
                summaryResponse = await client.GetAsync(summaryUrl);
            }

            if (summaryResponse.IsSuccessStatusCode)
            {
                var body = await summaryResponse.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(body);

                var title = doc.RootElement.TryGetProperty("title", out var t) ? t.GetString() ?? "" : "";
                var extract = doc.RootElement.TryGetProperty("extract", out var e) ? e.GetString() : null;
                var type = doc.RootElement.TryGetProperty("type", out var ty) ? ty.GetString() : null;

                // 排除消歧义页面
                if (type != "disambiguation" && !string.IsNullOrEmpty(title))
                {
                    var thumbnail = doc.RootElement.TryGetProperty("thumbnail", out var th)
                        ? th.TryGetProperty("source", out var src) ? src.GetString() : null
                        : null;

                    results.Add(new ArtistSearchResult
                    {
                        Source = SourceName + "·Wikipedia",
                        Id = $"wiki_{title.GetHashCode():x}",
                        Name = title,
                        CoverUrl = thumbnail,
                        Description = extract?.Length > 200 ? extract[..200] + "..." : extract
                    });
                }
            }

            // 如果 REST API 没有结果，用搜索 API
            if (results.Count == 0)
            {
                await SearchWikipediaFallbackAsync(client, name, results, limit);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[MultiSource] Wikipedia搜索失败: {ex.Message}");
        }
        return results;
    }

    /// <summary>Wikipedia 搜索 API fallback（REST API 无结果时）</summary>
    private async Task SearchWikipediaFallbackAsync(HttpClient client, string name, List<ArtistSearchResult> results, int limit)
    {
        try
        {
            // 使用中文维基搜索
            var searchUrl = $"https://zh.wikipedia.org/w/api.php?action=query&list=search&srsearch={Uri.EscapeDataString(name)}&format=json&srlimit=3";
            var response = await client.GetAsync(searchUrl);
            if (!response.IsSuccessStatusCode) return;

            var body = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(body);

            if (!doc.RootElement.TryGetProperty("query", out var query)) return;
            if (!query.TryGetProperty("search", out var searchResults)) return;

            var titles = searchResults.EnumerateArray()
                .Select(r => r.TryGetProperty("title", out var t) ? t.GetString() : null)
                .Where(t => !string.IsNullOrEmpty(t))
                .Take(limit)
                .ToList();

            foreach (var title in titles)
            {
                if (title == null) continue;

                // 获取页面图片
                var pageUrl = $"https://zh.wikipedia.org/w/api.php?action=query&titles={Uri.EscapeDataString(title)}&prop=pageimages&format=json&pithumbsize=300";
                var pageResponse = await client.GetAsync(pageUrl);
                if (!pageResponse.IsSuccessStatusCode) continue;

                var pageBody = await pageResponse.Content.ReadAsStringAsync();
                using var pageDoc = JsonDocument.Parse(pageBody);

                if (!pageDoc.RootElement.TryGetProperty("query", out var pageQuery)) continue;
                if (!pageQuery.TryGetProperty("pages", out var pages)) continue;

                foreach (var page in pages.EnumerateObject())
                {
                    var pageTitle = page.Value.TryGetProperty("title", out var pt) ? pt.GetString() ?? "" : "";
                    if (pageTitle.Contains("消歧义") || pageTitle.Contains("(disambiguation)")) continue;

                    var thumbnail = page.Value.TryGetProperty("thumbnail", out var th)
                        ? th.TryGetProperty("source", out var src) ? src.GetString() : null
                        : null;

                    if (!string.IsNullOrEmpty(pageTitle))
                    {
                        results.Add(new ArtistSearchResult
                        {
                            Source = SourceName + "·Wikipedia",
                            Id = $"wiki_{pageTitle.GetHashCode():x}",
                            Name = pageTitle,
                            CoverUrl = thumbnail
                        });

                        if (results.Count >= limit) return;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[MultiSource] Wikipedia搜索回退失败: {ex.Message}");
        }
    }

    // ==================== 缓存路径 ====================

    private string GetArtistCoverPath(string artistName)
    {
        var safeName = string.Join("_", artistName.Split(Path.GetInvalidFileNameChars()));
        return Path.Combine(_artistCoverCacheDir, $"{safeName}_multi.jpg");
    }
}
