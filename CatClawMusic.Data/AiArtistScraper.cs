using CatClawMusic.Core.Interfaces;
using CatClawMusic.Core.Services.AI;
using System.Text.Json;

namespace CatClawMusic.Data;

/// <summary>
/// AI 联网搜索刮削服务，利用已配置的 AI 模型搜索艺术家封面
/// </summary>
public class AiArtistScraper : IArtistMetadataScraper
{
    private readonly ILlmClient _llmClient;
    private readonly string _artistCoverCacheDir;
    private readonly Func<bool> _isConfiguredCheck;

    public string SourceName => "AI 搜索";

    public AiArtistScraper(ILlmClient llmClient, string artistCoverCacheDir, Func<bool>? isConfiguredCheck = null)
    {
        _llmClient = llmClient;
        _artistCoverCacheDir = artistCoverCacheDir;
        _isConfiguredCheck = isConfiguredCheck ?? (() => true);
        Directory.CreateDirectory(_artistCoverCacheDir);
    }

    /// <summary>检查 AI 服务是否已配置</summary>
    public bool IsConfigured => _isConfiguredCheck();

    /// <summary>通过 AI 搜索艺术家封面</summary>
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
                    Content = @"你是一个音乐元数据搜索助手。用户会给你一个艺术家名称，你需要搜索并提供该艺术家的信息。

请严格按照以下 JSON 数组格式返回结果，不要包含任何其他文字说明：
[
  {
    ""name"": ""艺术家名称"",
    ""alias"": ""别名/艺名（如有）"",
    ""gender"": ""性别（男/女/组合）"",
    ""region"": ""国籍或地区（如：中国、日本、韩国、美国、英国等）"",
    ""description"": ""简短描述（风格、代表作、成就等，50字以内）"",
    ""cover_url"": ""艺术家官方照片或代表性图片的直链URL"",
    ""source_url"": ""图片来源网页URL""
  }
]

要求：
1. cover_url 必须是可直接访问的图片URL（jpg/png/webp），不是网页链接
2. 如果找不到图片直链，cover_url 设为 null
3. 最多返回3个可能的匹配结果
4. 优先返回最知名、最匹配的结果
5. 只返回 JSON，不要有其他文字"
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
                if (item.TryGetProperty("region", out var regionProp))
                    result.Region = regionProp.GetString();
                if (item.TryGetProperty("description", out var descProp))
                    result.Description = descProp.GetString();
                if (item.TryGetProperty("cover_url", out var coverProp) && coverProp.ValueKind == JsonValueKind.String)
                {
                    var url = coverProp.GetString();
                    if (!string.IsNullOrEmpty(url) && url.StartsWith("http"))
                        result.CoverUrl = url;
                }

                result.Id = $"ai_{result.Name.GetHashCode():x}";
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
