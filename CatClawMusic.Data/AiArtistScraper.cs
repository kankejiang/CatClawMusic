using System.Text.Json;
using CatClawMusic.Core.Models;
using CatClawMusic.Core.Services.AI;

namespace CatClawMusic.Data;

/// <summary>
/// AI 艺术家元数据刮削器 — 使用 LLM 搜索艺术家的性别、国籍、简介等信息
/// </summary>
public class AiArtistScraper : IArtistMetadataScraper
{
    /// <summary>HTTP 客户端，用于调用 LLM API</summary>
    private readonly HttpClient _httpClient;
    /// <summary>艺术家封面缓存目录绝对路径</summary>
    private readonly string _artistCoverCacheDir;
    /// <summary>LLM 配置提供函数（运行时动态获取，确保使用最新配置）</summary>
    private readonly Func<LlmConfig> _configProvider;

    /// <summary>数据源名称：AI 搜索</summary>
    public string SourceName => "AI搜索";

    /// <summary>
    /// 初始化 AI 艺术家元数据刮削器。
    /// </summary>
    /// <param name="artistCoverCacheDir">艺术家封面缓存目录（不存在会自动创建）。</param>
    /// <param name="configProvider">LLM 配置提供函数，运行时动态获取以确保使用最新配置。</param>
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

    /// <summary>
    /// 使用 LLM 搜索艺术家信息（性别、国籍、生日、简介、别名、本名等）。
    /// 通过构造结构化 prompt 要求 LLM 返回 JSON 格式结果，并解析为统一的 ArtistSearchResult。
    /// </summary>
    /// <param name="name">艺术家名称关键词。</param>
    /// <param name="limit">最大返回数量（AI 通常只返回 1 条最匹配结果，此参数被忽略）。</param>
    /// <returns>包含艺术家信息的搜索结果列表；未配置或请求失败时返回空列表。</returns>
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

    /// <summary>
    /// 下载艺术家封面图片到缓存目录。
    /// </summary>
    /// <param name="coverUrl">封面图片 URL。</param>
    /// <param name="artistName">艺术家名称（用于生成缓存文件名）。</param>
    /// <returns>缓存文件绝对路径；URL 为空或下载失败时返回 null。</returns>
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

    /// <summary>
    /// 根据用户配置的 API URL 构建完整的 Chat Completions 请求 URL。
    /// 自动补全 /v1/chat/completions 路径，兼容用户只填域名或填到 /v1 的情况。
    /// </summary>
    /// <param name="apiUrl">用户配置的 API 基础 URL。</param>
    /// <returns>完整的 Chat Completions URL。</returns>
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

    /// <summary>
    /// 从 LLM API 响应体中提取 message.content 字段内容。
    /// 兼容标准 OpenAI Chat Completions 响应格式。
    /// </summary>
    /// <param name="responseBody">LLM API 的原始 JSON 响应体。</param>
    /// <returns>提取出的文本内容；解析失败时返回空字符串。</returns>
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

    /// <summary>
    /// 从 LLM 返回的文本中提取 JSON 对象字符串。
    /// 兼容三种情况：1) 直接返回 JSON；2) 包裹在 ```json ``` 代码块中；3) 文本中嵌入 JSON 对象。
    /// </summary>
    /// <param name="text">LLM 返回的原始文本。</param>
    /// <returns>提取出的 JSON 字符串；未找到时返回空字符串。</returns>
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

    /// <summary>
    /// 清理文件名中的非法字符（替换为下划线），空名返回 "unknown"。
    /// </summary>
    /// <param name="name">原始名称。</param>
    /// <returns>合法的文件名（不含路径）。</returns>
    private static string SanitizeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var sanitized = new string(name.Select(c => invalid.Contains(c) ? '_' : c).ToArray());
        return string.IsNullOrWhiteSpace(sanitized) ? "unknown" : sanitized;
    }
}