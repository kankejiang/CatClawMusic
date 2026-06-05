using CatClawMusic.Core.Interfaces;
using CatClawMusic.Core.Services.AI;
using System.Text.Json;

namespace CatClawMusic.Data;

/// <summary>
/// AI 联网搜索刮削服务，利用已配置的 AI 模型搜索艺术家信息
/// 封面 fallback 到网易云
/// </summary>
public class AiArtistScraper : IArtistMetadataScraper
{
    private readonly ILlmClient _llmClient;
    private readonly string _artistCoverCacheDir;
    private readonly Func<bool> _isConfiguredCheck;
    private readonly Func<NetEaseMusicScraper?> _neteaseScraperFactory;

    public string SourceName => "AI 搜索";

    public AiArtistScraper(ILlmClient llmClient, string artistCoverCacheDir,
        Func<bool>? isConfiguredCheck = null, Func<NetEaseMusicScraper?>? neteaseScraperFactory = null)
    {
        _llmClient = llmClient;
        _artistCoverCacheDir = artistCoverCacheDir;
        _isConfiguredCheck = isConfiguredCheck ?? (() => true);
        _neteaseScraperFactory = neteaseScraperFactory ?? (() => null);
        Directory.CreateDirectory(_artistCoverCacheDir);
    }

    /// <summary>检查 AI 服务是否已配置</summary>
    public bool IsConfigured => _isConfiguredCheck();

    /// <summary>通过 AI 搜索艺术家信息</summary>
    public async Task<List<ArtistSearchResult>> SearchArtistsAsync(string name, int limit = 10)
    {
        var results = new List<ArtistSearchResult>();
        try
        {
            if (!IsConfigured)
            {
                System.Diagnostics.Debug.WriteLine("[AiArtistScraper] AI 服务未配置");
                return results;
            }

            var messages = new List<ChatMessage>
            {
                new()
                {
                    Role = "system",
                    Content = @"你是一个音乐百科助手。用户会给你一个艺术家名称，你需要提供该艺术家的详细信息。

请严格按照以下 JSON 数组格式返回结果，不要包含任何其他文字说明：
[
  {
    ""name"": ""艺术家名称"",
    ""alias"": ""别名/艺名（如有，多个用/分隔）"",
    ""gender"": ""性别（男/女/组合）"",
    ""birthday"": ""出生日期（如：1990-01-15，组合填成立日期）"",
    ""region"": ""国籍或地区（如：中国、日本、韩国、美国、英国等）"",
    ""description"": ""详细介绍（风格、代表作、主要成就等，100字以内）""
  }
]

要求：
1. 信息必须准确，不确定的字段填 null
2. 最多返回3个可能的匹配结果
3. 优先返回最知名、最匹配的结果
4. 只返回 JSON，不要有其他文字"
                },
                new()
                {
                    Role = "user",
                    Content = $"搜索艺术家: {name}"
                }
            };

            var response = await _llmClient.ChatAsync(messages);
            var content = response.Content?.Trim() ?? "";

            // 提取 JSON 部分
            var jsonStart = content.IndexOf('[');
            var jsonEnd = content.LastIndexOf(']');
            if (jsonStart < 0 || jsonEnd < 0 || jsonEnd <= jsonStart) return results;

            var json = content[jsonStart..(jsonEnd + 1)];
            using var doc = JsonDocument.Parse(json);

            foreach (var item in doc.RootElement.EnumerateArray())
            {
                var result = new ArtistSearchResult { Source = SourceName };

                if (item.TryGetProperty("name", out var nameProp))
                    result.Name = nameProp.GetString() ?? "";
                if (item.TryGetProperty("alias", out var aliasProp))
                    result.Alias = aliasProp.GetString();
                if (item.TryGetProperty("gender", out var genderProp))
                    result.Gender = genderProp.GetString();
                if (item.TryGetProperty("birthday", out var birthdayProp))
                    result.Birthday = birthdayProp.GetString();
                if (item.TryGetProperty("region", out var regionProp))
                    result.Region = regionProp.GetString();
                if (item.TryGetProperty("description", out var descProp))
                    result.Description = descProp.GetString();

                result.Id = $"ai_{result.Name.GetHashCode():x}";

                // AI 不返回封面 URL，尝试从网易云获取封面
                if (!string.IsNullOrEmpty(result.Name))
                {
                    try
                    {
                        var netease = _neteaseScraperFactory();
                        if (netease != null)
                        {
                            var coverPath = await netease.GetArtistCoverAsync(result.Name);
                            if (coverPath != null)
                                result.CoverUrl = coverPath; // 本地路径，直接可用
                        }
                    }
                    catch { }
                }

                results.Add(result);
                if (results.Count >= limit) break;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[AiArtistScraper] 搜索失败: {ex.Message}");
        }
        return results;
    }

    /// <summary>下载并保存艺术家封面到缓存</summary>
    public async Task<string?> DownloadAndCacheArtistCoverAsync(string coverUrl, string artistName)
    {
        // 如果已经是本地路径，直接返回
        if (!string.IsNullOrEmpty(coverUrl) && System.IO.File.Exists(coverUrl))
            return coverUrl;

        try
        {
            var cachePath = GetArtistCoverPath(artistName);

            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
            client.DefaultRequestHeaders.Add("User-Agent",
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36");

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
            System.Diagnostics.Debug.WriteLine($"[AiArtistScraper] 下载封面失败: {ex.Message}");
        }
        return null;
    }

    private string GetArtistCoverPath(string artistName)
    {
        var safeName = string.Join("_", artistName.Split(Path.GetInvalidFileNameChars()));
        return Path.Combine(_artistCoverCacheDir, $"{safeName}_ai.jpg");
    }
}
