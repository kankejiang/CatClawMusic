using System.Text.Json;

namespace CatClawMusic.Data;

/// <summary>
/// MusicBrainz 元数据刮削服务，用于获取艺术家头像
/// API 文档: https://musicbrainz.org/doc/MusicBrainz_API
/// </summary>
public class MusicBrainzScraper : IArtistMetadataScraper
{
    private readonly HttpClient _httpClient;
    private readonly string _artistCoverCacheDir;

    public string SourceName => "MusicBrainz";

    public MusicBrainzScraper(string artistCoverCacheDir)
    {
        _artistCoverCacheDir = artistCoverCacheDir;
        Directory.CreateDirectory(_artistCoverCacheDir);

        _httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(15)
        };
        // MusicBrainz 要求设置有意义的 User-Agent
        _httpClient.DefaultRequestHeaders.Add("User-Agent",
            "CatClawMusic/1.0 (https://github.com/catclawmusic; contact@catclawmusic.local)");
    }

    /// <summary>搜索艺术家，返回多个匹配结果</summary>
    public async Task<List<ArtistSearchResult>> SearchArtistsAsync(string name, int limit = 10)
    {
        var results = new List<ArtistSearchResult>();
        try
        {
            var url = $"https://musicbrainz.org/ws/2/artist?query=artist:{Uri.EscapeDataString(name)}&fmt=json&limit={limit}";
            var response = await _httpClient.GetAsync(url);
            if (!response.IsSuccessStatusCode) return results;

            var body = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(body);

            if (!doc.RootElement.TryGetProperty("artists", out var artists)) return results;

            foreach (var artist in artists.EnumerateArray())
            {
                var item = new ArtistSearchResult { Source = SourceName };

                if (artist.TryGetProperty("id", out var idProp))
                    item.Id = idProp.GetString() ?? "";
                if (artist.TryGetProperty("name", out var nameProp))
                    item.Name = nameProp.GetString() ?? "";

                // 别名
                if (artist.TryGetProperty("aliases", out var aliases) && aliases.ValueKind == JsonValueKind.Array)
                {
                    var aliasList = aliases.EnumerateArray()
                        .Where(a => a.TryGetProperty("name", out var n) && n.GetString() != item.Name)
                        .Select(a => a.GetProperty("name").GetString())
                        .Where(a => !string.IsNullOrEmpty(a))
                        .Distinct()
                        .Take(3)
                        .ToList();
                    if (aliasList.Count > 0)
                        item.Alias = string.Join(" / ", aliasList);
                }

                // 国家/地区
                if (artist.TryGetProperty("country", out var country) && country.ValueKind == JsonValueKind.String)
                {
                    var countryStr = country.GetString();
                    if (!string.IsNullOrEmpty(countryStr))
                        item.Region = countryStr;
                }

                // 性别
                if (artist.TryGetProperty("gender", out var gender) && gender.ValueKind == JsonValueKind.String)
                {
                    var genderStr = gender.GetString();
                    if (!string.IsNullOrEmpty(genderStr))
                        item.Gender = genderStr switch
                        {
                            "male" => "男",
                            "female" => "女",
                            "other" => "其他",
                            _ => genderStr
                        };
                }

                // 国家 + 类型
                var extraParts = new List<string>();
                if (!string.IsNullOrEmpty(item.Region))
                    extraParts.Add(item.Region);
                if (artist.TryGetProperty("type", out var type) && type.ValueKind == JsonValueKind.String)
                    extraParts.Add(type.GetString() ?? "");
                if (artist.TryGetProperty("disambiguation", out var disambig) && disambig.ValueKind == JsonValueKind.String)
                {
                    var d = disambig.GetString();
                    if (!string.IsNullOrEmpty(d)) extraParts.Add(d);
                }
                if (extraParts.Count > 0)
                    item.ExtraInfo = string.Join(" · ", extraParts);

                // 标签/简介
                if (artist.TryGetProperty("tags", out var tags) && tags.ValueKind == JsonValueKind.Array)
                {
                    var tagList = tags.EnumerateArray()
                        .Where(t => t.TryGetProperty("name", out var n))
                        .OrderByDescending(t => t.TryGetProperty("count", out var c) ? c.GetInt32() : 0)
                        .Take(5)
                        .Select(t => t.GetProperty("name").GetString())
                        .Where(t => !string.IsNullOrEmpty(t))
                        .ToList();
                    if (tagList.Count > 0)
                        item.Description = string.Join(", ", tagList);
                }

                // 封面: 通过 Cover Art Archive 获取
                if (!string.IsNullOrEmpty(item.Id))
                {
                    item.CoverUrl = $"https://coverartarchive.org/artist/{item.Id}/front";
                }

                results.Add(item);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[MusicBrainz] 搜索艺术家失败: {ex.Message}");
        }
        return results;
    }

    /// <summary>下载并保存艺术家封面到缓存</summary>
    public async Task<string?> DownloadAndCacheArtistCoverAsync(string coverUrl, string artistName)
    {
        try
        {
            var cachePath = GetArtistCoverPath(artistName, "musicbrainz");

            // Cover Art Archive 的 front 端点会 302 重定向到实际图片
            var handler = new HttpClientHandler { AllowAutoRedirect = true };
            using var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(15) };
            client.DefaultRequestHeaders.Add("User-Agent",
                "CatClawMusic/1.0 (https://github.com/catclawmusic; contact@catclawmusic.local)");

            var response = await client.GetAsync(coverUrl);
            if (!response.IsSuccessStatusCode) return null;

            var bytes = await response.Content.ReadAsByteArrayAsync();
            if (bytes.Length > 0)
            {
                await File.WriteAllBytesAsync(cachePath, bytes);
                return cachePath;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[MusicBrainz] 下载艺术家封面失败: {ex.Message}");
        }
        return null;
    }

    /// <summary>通过 MusicBrainz ID 获取 Wikidata ID（用于 Wikidata 封面）</summary>
    public async Task<string?> GetWikidataIdAsync(string musicBrainzArtistId)
    {
        try
        {
            var url = $"https://musicbrainz.org/ws/2/artist/{musicBrainzArtistId}?fmt=json&inc=url-rels";
            var response = await _httpClient.GetAsync(url);
            if (!response.IsSuccessStatusCode) return null;

            var body = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(body);

            if (doc.RootElement.TryGetProperty("relations", out var relations))
            {
                foreach (var rel in relations.EnumerateArray())
                {
                    if (rel.TryGetProperty("type", out var type) && type.GetString() == "wikidata")
                    {
                        if (rel.TryGetProperty("url", out var urlObj) &&
                            urlObj.TryGetProperty("resource", out var resource))
                        {
                            var wikiUrl = resource.GetString() ?? "";
                            // https://www.wikidata.org/wiki/Q12345 -> Q12345
                            var idx = wikiUrl.LastIndexOf('/');
                            if (idx >= 0 && idx < wikiUrl.Length - 1)
                                return wikiUrl[(idx + 1)..];
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[MusicBrainz] 获取 Wikidata ID 失败: {ex.Message}");
        }
        return null;
    }

    private string GetArtistCoverPath(string artistName, string source)
    {
        var safeName = string.Join("_", artistName.Split(Path.GetInvalidFileNameChars()));
        return Path.Combine(_artistCoverCacheDir, $"{safeName}_{source}.jpg");
    }
}
