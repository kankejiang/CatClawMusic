using System.Text.Json;

namespace CatClawMusic.Data;

/// <summary>
/// QQ音乐艺术家元数据刮削器（华语覆盖最好）
/// 实现 IArtistMetadataScraper 接口，提供歌手搜索和元数据获取
/// </summary>
public class MultiSourcePhotoScraper : IArtistMetadataScraper
{
    private readonly string _artistCoverCacheDir;

    public string SourceName => "QQ音乐";

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

    /// <summary>搜索艺术家（只使用 QQ音乐）</summary>
    public async Task<List<ArtistSearchResult>> SearchArtistsAsync(string name, int limit = 10)
    {
        return await SearchQqMusicAsync(name, limit);
    }

    /// <summary>下载并保存艺术家封面到缓存</summary>
    public async Task<string?> DownloadAndCacheArtistCoverAsync(string coverUrl, string artistName)
    {
        if (!string.IsNullOrEmpty(coverUrl) && File.Exists(coverUrl))
            return coverUrl;

        try
        {
            var cachePath = GetArtistCoverPath(artistName);
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
            System.Diagnostics.Debug.WriteLine($"[QQMusic] 下载封面失败: {ex.Message}");
        }

        return null;
    }

    // ==================== QQ音乐详情接口 ====================

    /// <summary>QQ音乐歌手详情接口，获取性别、地区、简介等元数据</summary>
    private async Task<(string? Gender, string? Region, string? Birthday, string? Description)> GetQqMusicArtistInfoAsync(string mid)
    {
        try
        {
            using var client = CreateClient();
            var url = "https://u.y.qq.com/cgi-bin/musicu.fcg";
            var postJson = "{\"req_1\":{\"method\":\"GetSingerDetail\",\"module\":\"music.web_singer_info_svr\",\"param\":{\"singermid\":\"" + mid + "\"}}}";
            var content = new StringContent(postJson, System.Text.Encoding.UTF8, "application/json");
            var response = await client.PostAsync(url, content);
            if (!response.IsSuccessStatusCode) return (null, null, null, null);

            var body = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(body);

            string? gender = null;
            string? region = null;
            string? birthday = null;
            string? desc = null;

            if (doc.RootElement.TryGetProperty("req_1", out var req) &&
                req.TryGetProperty("data", out var infoData))
            {
                // 性别：1=男，2=女
                if (infoData.TryGetProperty("sex", out var sexProp) && sexProp.ValueKind == JsonValueKind.Number)
                {
                    var sex = sexProp.GetInt32();
                    gender = sex switch { 1 => "男", 2 => "女", _ => "" };
                }
                // 地区/国家
                if (infoData.TryGetProperty("country", out var countryProp))
                    region = countryProp.GetString();
                // 生日
                if (infoData.TryGetProperty("birth", out var birthProp))
                {
                    var b = birthProp.GetString();
                    if (!string.IsNullOrEmpty(b) && b != "0000-00-00") birthday = b;
                }
                // 简介
                if (infoData.TryGetProperty("desc", out var descProp))
                    desc = descProp.GetString();
            }
            return (gender, region, birthday, desc);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[QQMusic] 详情获取失败: {ex.Message}");
            return (null, null, null, null);
        }
    }

    private async Task<List<ArtistSearchResult>> SearchQqMusicAsync(string name, int limit)
    {
        var results = new List<ArtistSearchResult>();
        try
        {
            using var client = CreateClient();
            var url = $"https://c.y.qq.com/splcloud/fcg-bin/smartbox_new.fcg?key={Uri.EscapeDataString(name)}&format=json";
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

                var result = new ArtistSearchResult
                {
                    Source = SourceName,
                    Id = $"qq_{mid ?? singerName.GetHashCode().ToString("x")}",
                    Name = singerName,
                    CoverUrl = mid != null
                        ? $"https://y.gtimg.cn/music/photo_new/T001R300x300M000{mid}.jpg"
                        : null
                };

                // 如果有 mid，尝试获取详情
                if (mid != null)
                {
                    var (g, r, b, d) = await GetQqMusicArtistInfoAsync(mid);
                    result.Gender = g;
                    result.Region = r;
                    result.Birthday = b;
                    result.Description = d;
                }

                results.Add(result);
                if (results.Count >= limit) break;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[QQMusic] 搜索失败: {ex.Message}");
        }
        return results;
    }

    // ==================== 缓存路径 ====================

    private string GetArtistCoverPath(string artistName)
    {
        var safeName = string.Join("_", artistName.Split(Path.GetInvalidFileNameChars()));
        return Path.Combine(_artistCoverCacheDir, $"{safeName}_qq.jpg");
    }
}
