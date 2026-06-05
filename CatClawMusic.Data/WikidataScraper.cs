using System.Text.Json;

namespace CatClawMusic.Data;

/// <summary>
/// Wikidata 元数据刮削服务，通过 Wikidata API 获取艺术家头像
/// 通常与 MusicBrainz 关联使用：MusicBrainz artist -> Wikidata ID -> Wikidata 图片
/// API 文档: https://www.wikidata.org/w/api.php
/// </summary>
public class WikidataScraper : IArtistMetadataScraper
{
    private readonly HttpClient _httpClient;
    private readonly string _artistCoverCacheDir;
    private readonly MusicBrainzScraper _musicBrainzScraper;

    public string SourceName => "Wikidata";

    public WikidataScraper(string artistCoverCacheDir, MusicBrainzScraper musicBrainzScraper)
    {
        _artistCoverCacheDir = artistCoverCacheDir;
        Directory.CreateDirectory(_artistCoverCacheDir);
        _musicBrainzScraper = musicBrainzScraper;

        _httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(15)
        };
        _httpClient.DefaultRequestHeaders.Add("User-Agent",
            "CatClawMusic/1.0 (https://github.com/catclawmusic; contact@catclawmusic.local)");
    }

    /// <summary>搜索艺术家，通过 MusicBrainz 搜索后关联 Wikidata 获取封面</summary>
    public async Task<List<ArtistSearchResult>> SearchArtistsAsync(string name, int limit = 10)
    {
        var results = new List<ArtistSearchResult>();
        try
        {
            // 先从 MusicBrainz 搜索获取 MBID
            var mbResults = await _musicBrainzScraper.SearchArtistsAsync(name, limit);
            if (mbResults.Count == 0) return results;

            // 对每个 MusicBrainz 结果，尝试获取 Wikidata 封面
            foreach (var mbResult in mbResults)
            {
                if (string.IsNullOrEmpty(mbResult.Id)) continue;

                var wikidataId = await _musicBrainzScraper.GetWikidataIdAsync(mbResult.Id);
                if (string.IsNullOrEmpty(wikidataId)) continue;

                var wikiResult = await GetWikidataArtistAsync(wikidataId);
                if (wikiResult == null) continue;

                // 合并 MusicBrainz 和 Wikidata 的信息
                wikiResult.Id = mbResult.Id; // 保留 MBID
                wikiResult.Alias = mbResult.Alias ?? wikiResult.Alias;
                wikiResult.ExtraInfo = mbResult.ExtraInfo;
                if (string.IsNullOrEmpty(wikiResult.Description) && !string.IsNullOrEmpty(mbResult.Description))
                    wikiResult.Description = mbResult.Description;

                results.Add(wikiResult);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Wikidata] 搜索艺术家失败: {ex.Message}");
        }
        return results;
    }

    /// <summary>通过 Wikidata ID 获取艺术家信息</summary>
    private async Task<ArtistSearchResult?> GetWikidataArtistAsync(string wikidataId)
    {
        try
        {
            var url = $"https://www.wikidata.org/w/api.php?action=wbgetentities&ids={wikidataId}&format=json&props=labels|descriptions|claims";
            var response = await _httpClient.GetAsync(url);
            if (!response.IsSuccessStatusCode) return null;

            var body = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(body);

            if (!doc.RootElement.TryGetProperty("entities", out var entities)) return null;
            if (!entities.TryGetProperty(wikidataId, out var entity)) return null;

            var result = new ArtistSearchResult { Source = SourceName, Id = wikidataId };

            // 标签（名称）
            if (entity.TryGetProperty("labels", out var labels))
            {
                // 优先中文，然后英文
                var name = GetLabelValue(labels, "zh") ?? GetLabelValue(labels, "zh-hans")
                    ?? GetLabelValue(labels, "en") ?? GetLabelValue(labels, "zh-cn");
                result.Name = name ?? wikidataId;

                // 别名：其他语言名称
                var aliasNames = new List<string>();
                foreach (var lang in new[] { "en", "ja", "ko", "fr", "de" })
                {
                    var val = GetLabelValue(labels, lang);
                    if (!string.IsNullOrEmpty(val) && val != result.Name)
                        aliasNames.Add(val);
                }
                if (aliasNames.Count > 0)
                    result.Alias = string.Join(" / ", aliasNames.Take(3));
            }

            // 描述
            if (entity.TryGetProperty("descriptions", out var descriptions))
            {
                var desc = GetLabelValue(descriptions, "zh") ?? GetLabelValue(descriptions, "zh-hans")
                    ?? GetLabelValue(descriptions, "en");
                result.Description = desc;
            }

            // P18 = 图片
            if (entity.TryGetProperty("claims", out var claims) &&
                claims.TryGetProperty("P18", out var p18) &&
                p18.ValueKind == JsonValueKind.Array)
            {
                var firstImage = p18.EnumerateArray().FirstOrDefault();
                if (firstImage.TryGetProperty("mainsnak", out var mainsnak) &&
                    mainsnak.TryGetProperty("datavalue", out var datavalue) &&
                    datavalue.TryGetProperty("value", out var value))
                {
                    var imageName = value.GetString() ?? "";
                    if (!string.IsNullOrEmpty(imageName))
                    {
                        // Wikidata 图片 URL 格式: https://commons.wikimedia.org/w/index.php?title=Special:Redirect/file/{encoded}&width=300
                        result.CoverUrl = $"https://commons.wikimedia.org/w/index.php?title=Special:Redirect/file/{Uri.EscapeDataString(imageName)}&width=300";
                    }
                }
            }

            return result;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Wikidata] 获取实体失败: {ex.Message}");
            return null;
        }
    }

    /// <summary>下载并保存艺术家封面到缓存</summary>
    public async Task<string?> DownloadAndCacheArtistCoverAsync(string coverUrl, string artistName)
    {
        try
        {
            var cachePath = GetArtistCoverPath(artistName);

            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
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
            System.Diagnostics.Debug.WriteLine($"[Wikidata] 下载艺术家封面失败: {ex.Message}");
        }
        return null;
    }

    private static string? GetLabelValue(JsonElement labels, string language)
    {
        if (labels.TryGetProperty(language, out var label) &&
            label.TryGetProperty("value", out var value))
            return value.GetString();
        return null;
    }

    private string GetArtistCoverPath(string artistName)
    {
        var safeName = string.Join("_", artistName.Split(Path.GetInvalidFileNameChars()));
        return Path.Combine(_artistCoverCacheDir, $"{safeName}_wikidata.jpg");
    }
}
