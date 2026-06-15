using System.Text.Json;
using CatClawMusic.Core.Services.AI;

namespace CatClawMusic.Data;

/// <summary>
/// AI 艺术家元数据刮削器 — 使用 LLM 搜索艺术家的性别、国籍、简介等信息
/// </summary>
public class AiArtistScraper : IArtistMetadataScraper
{
    private readonly HttpClient _httpClient;
    private readonly string _artistCoverCacheDir;
    private readonly Func<LlmConfig> _configProvider;

    public string SourceName => "AI搜索";

    public AiArtistScraper(string artistCoverCacheDir, Func<LlmConfig> configProvider)
    {
        _artistCoverCacheDir = artistCoverCacheDir;
        Directory.CreateDirectory(_artistCoverCacheDir);
        _configProvider = configProvider;

        _httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(15)
        };
        _httpClient.DefaultRequestHeaders.Add("User-Agent",
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36");
    }

    public async Task<List<ArtistSearchResult>> SearchArtistsAsync(string name, int limit = 10)
    {
        var results = new List<ArtistSearchResult>();
        try
        {
            var config = _configProvider();
            if (string.IsNullOrWhiteSpace(config.ApiUrl) || string.IsNullOrWhiteSpace(config.ApiKey) || !config.Enabled)
                return results;
            if (string.IsNullOrWhiteSpace(name))
                return results;

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));

            var prompt = $@"请搜索音乐艺术家「{name}」的详细信息，以 JSON 格式返回（不要包含 markdown 代码块标记）：

{{
  ""name"": ""艺术家名称"",
  ""gender"": ""男/女/组合"",
  ""region"": ""国家或地区"",
  ""birthday"": ""出生日期（如 1990-01-01）"",
  ""description"": ""简短简介（50字左右）"",
  ""alias"": ""别名/外文名"",
  ""realName"": ""本名""
}}

如果找不到信息，对应字段返回空字符串。只返回 JSON，不要任何其他文字。";

            var messages = new List<ChatMessage>
            {
                new() { Role = "system", Content = "你是一个音乐数据库助手，负责提供艺术家的准确信息。只输出JSON，不输出任何其他内容。" },
                new() { Role = "user", Content = prompt }
            };

            var bodyObj = new
            {
                model = config.Model,
                messages = messages.Select(m => new { role = m.Role, content = m.Content }).ToArray(),
                temperature = 0.3,
                max_tokens = 600
            };

            var jsonBody = JsonSerializer.Serialize(bodyObj);
            var url = BuildChatUrl(config.ApiUrl);

            var request = new HttpRequestMessage(HttpMethod.Post, url);
            request.Headers.Add("Authorization", $"Bearer {config.ApiKey}");
            request.Content = new StringContent(jsonBody, System.Text.Encoding.UTF8, "application/json");

            using var response = await _httpClient.SendAsync(request, cts.Token);
            var responseBody = await response.Content.ReadAsStringAsync(cts.Token);

            if (!response.IsSuccessStatusCode)
            {
                System.Diagnostics.Debug.WriteLine($"[AiArtistScraper] API 请求失败 ({(int)response.StatusCode})");
                return results;
            }

            var content = ExtractContent(responseBody);
            if (string.IsNullOrWhiteSpace(content)) return results;

            // 尝试提取 JSON
            var json = ExtractJson(content);
            if (string.IsNullOrWhiteSpace(json)) return results;

            var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            var result = new ArtistSearchResult
            {
                Source = SourceName,
                Id = name,
                Name = root.TryGetProperty("name", out var n) ? n.GetString() ?? name : name,
                Gender = root.TryGetProperty("gender", out var g) ? g.GetString() : null,
                Region = root.TryGetProperty("region", out var r) ? r.GetString() : null,
                Birthday = root.TryGetProperty("birthday", out var bd) ? bd.GetString() : null,
                Description = root.TryGetProperty("description", out var desc) ? desc.GetString() : null,
                Alias = root.TryGetProperty("alias", out var alias) ? alias.GetString() : null,
                RealName = root.TryGetProperty("realName", out var rn) ? rn.GetString() : null
            };

            results.Add(result);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[AiArtistScraper] 搜索失败: {ex.Message}");
        }

        return results;
    }

    public async Task<string?> DownloadAndCacheArtistCoverAsync(string coverUrl, string artistName)
    {
        if (string.IsNullOrWhiteSpace(coverUrl)) return null;

        try
        {
            var safeName = SanitizeFileName(artistName);
            var cachePath = Path.Combine(_artistCoverCacheDir, $"{safeName}.jpg");

            var bytes = await _httpClient.GetByteArrayAsync(coverUrl);
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

    private static string BuildChatUrl(string apiUrl)
    {
        var url = apiUrl.TrimEnd('/');
        if (url.EndsWith("/chat/completions", StringComparison.OrdinalIgnoreCase))
            return url;
        if (url.EndsWith("/v1", StringComparison.OrdinalIgnoreCase))
            return url + "/chat/completions";
        if (url.EndsWith("/v1/", StringComparison.OrdinalIgnoreCase))
            return url + "chat/completions";
        return url + "/v1/chat/completions";
    }

    private static string ExtractContent(string responseBody)
    {
        try
        {
            using var doc = JsonDocument.Parse(responseBody);
            var root = doc.RootElement;

            if (root.TryGetProperty("choices", out var choices) &&
                choices.ValueKind == JsonValueKind.Array &&
                choices.GetArrayLength() > 0)
            {
                var message = choices[0].GetProperty("message");
                if (message.TryGetProperty("content", out var content) &&
                    content.ValueKind != JsonValueKind.Null)
                {
                    return content.GetString() ?? "";
                }
            }
        }
        catch { }

        return "";
    }

    private static string ExtractJson(string text)
    {
        text = text.Trim();

        // 尝试找 ```json ... ``` 代码块
        if (text.StartsWith("```"))
        {
            var endIdx = text.LastIndexOf("```", StringComparison.Ordinal);
            if (endIdx > 3)
            {
                var startIdx = text.IndexOf('\n');
                if (startIdx < 0) startIdx = 3;
                else startIdx += 1;
                text = text[startIdx..endIdx].Trim();
            }
        }

        // 找到第一个 { 和最后一个 }
        var braceStart = text.IndexOf('{');
        var braceEnd = text.LastIndexOf('}');
        if (braceStart >= 0 && braceEnd > braceStart)
        {
            return text[braceStart..(braceEnd + 1)];
        }

        return "";
    }

    private static string SanitizeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var sanitized = new string(name.Select(c => invalid.Contains(c) ? '_' : c).ToArray());
        return string.IsNullOrWhiteSpace(sanitized) ? "unknown" : sanitized;
    }
}