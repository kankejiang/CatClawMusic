using System.Net;
using System.Text;
using System.Text.Json;
using CatClawMusic.Core.Interfaces;
using CatClawMusic.Core.Services;

using CatClawMusic.Core.Models;

namespace CatClawMusic.Core.Services.AI;

/// <summary>
/// OpenAI 兼容的 LLM 客户端实现，支持多服务商对接、退回模型机制、连接测试与模型列表查询。
/// </summary>
public class OpenAiCompatibleLlmClient : ILlmClient
{
    /// <summary>HTTP 客户端，用于发送对话请求</summary>
    private readonly HttpClient _httpClient;
    /// <summary>当前生效 LLM 配置的提供函数</summary>
    private readonly Func<LlmConfig> _configProvider;
    /// <summary>退回配置列表的提供函数（可选）</summary>
    private readonly Func<List<LlmConfig>>? _fallbackConfigsProvider;

    /// <summary>JSON 序列化选项，使用蛇形命名与忽略 null 值</summary>
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        WriteIndented = false,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    /// <summary>
    /// 构造 OpenAiCompatibleLlmClient 实例
    /// </summary>
    /// <param name="configProvider">当前生效配置提供函数</param>
    /// <param name="fallbackConfigsProvider">退回配置列表提供函数（可选）</param>
    public OpenAiCompatibleLlmClient(Func<LlmConfig> configProvider, Func<List<LlmConfig>>? fallbackConfigsProvider = null)
    {
        _configProvider = configProvider;
        _fallbackConfigsProvider = fallbackConfigsProvider;
        _httpClient = new HttpClient(new HttpClientHandler
        {
            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate
        });
        _httpClient.Timeout = TimeSpan.FromSeconds(120);
    }

    /// <summary>获取所有可用的退回配置（启用了 FallbackEnabled 且 Enabled 的配置，按列表顺序）</summary>
    private List<LlmConfig> GetFallbackConfigs()
    {
        if (_fallbackConfigsProvider == null) return new();
        var currentConfig = _configProvider();
        return _fallbackConfigsProvider()
            .Where(c => c.FallbackEnabled && c.Enabled
                && !string.IsNullOrWhiteSpace(c.ApiUrl)
                && !string.IsNullOrWhiteSpace(c.ApiKey)
                && c.Name != currentConfig.Name)
            .ToList();
    }

    /// <summary>
    /// 发起对话请求，自动支持退回模型机制
    /// </summary>
    /// <param name="messages">对话消息列表</param>
    /// <param name="tools">可用工具定义列表（可选）</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>LLM 响应</returns>
    /// <exception cref="InvalidOperationException">API 未配置或所有退回均失败时抛出</exception>
    public async Task<LlmResponse> ChatAsync(List<ChatMessage> messages, List<ToolDefinition>? tools = null, CancellationToken ct = default)
    {
        var config = _configProvider();
        if (string.IsNullOrWhiteSpace(config.ApiUrl) || string.IsNullOrWhiteSpace(config.ApiKey))
            throw new InvalidOperationException("AI 服务未配置，请先在设置中配置 API 信息");

        // 尝试当前配置
        try
        {
            return await ChatWithConfigAsync(config, messages, tools, ct);
        }
        catch (Exception primaryEx)
        {
            // 尝试退回配置
            var fallbacks = GetFallbackConfigs();
            if (fallbacks.Count == 0)
                throw;

            System.Diagnostics.Debug.WriteLine($"[LlmClient] 主模型 {config.Name} 调用失败: {primaryEx.Message}，尝试退回模型...");

            foreach (var fallback in fallbacks)
            {
                try
                {
                    System.Diagnostics.Debug.WriteLine($"[LlmClient] 尝试退回模型: {fallback.Name} ({fallback.Model})");
                    var result = await ChatWithConfigAsync(fallback, messages, tools, ct);
                    System.Diagnostics.Debug.WriteLine($"[LlmClient] 退回模型 {fallback.Name} 调用成功");
                    return result;
                }
                catch (Exception fbEx)
                {
                    System.Diagnostics.Debug.WriteLine($"[LlmClient] 退回模型 {fallback.Name} 也失败: {fbEx.Message}");
                }
            }

            // 所有退回都失败，抛出原始异常
            throw;
        }
    }

    /// <summary>
    /// 使用指定配置发起对话请求
    /// </summary>
    /// <param name="config">LLM 配置</param>
    /// <param name="messages">对话消息列表</param>
    /// <param name="tools">可用工具定义列表</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>LLM 响应</returns>
    private async Task<LlmResponse> ChatWithConfigAsync(LlmConfig config, List<ChatMessage> messages, List<ToolDefinition>? tools, CancellationToken ct)
    {
        var url = BuildChatUrl(config.ApiUrl);
        var body = BuildRequestBody(messages, tools, config);

        var request = new HttpRequestMessage(HttpMethod.Post, url);
        request.Headers.Add("Authorization", $"Bearer {config.ApiKey}");
        request.Content = new StringContent(body, Encoding.UTF8, "application/json");

        using var response = await _httpClient.SendAsync(request, ct);
        var responseBody = await response.Content.ReadAsStringAsync(ct);

        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"API 请求失败 ({(int)response.StatusCode}): {Truncate(responseBody, 500)}");

        return ParseResponse(responseBody);
    }

    /// <summary>
    /// 测试当前配置的连接是否可用（发送一个极简请求）
    /// </summary>
    /// <returns>连接成功返回 true，否则返回 false</returns>
    public async Task<bool> TestConnectionAsync()
    {
        try
        {
            var config = _configProvider();
            if (string.IsNullOrWhiteSpace(config.ApiUrl) || string.IsNullOrWhiteSpace(config.ApiKey))
                return false;

            var url = BuildChatUrl(config.ApiUrl);
            var testBody = new
            {
                model = config.Model,
                messages = new[] { new { role = "user", content = "Hi" } },
                max_tokens = 5
            };

            var request = new HttpRequestMessage(HttpMethod.Post, url);
            request.Headers.Add("Authorization", $"Bearer {config.ApiKey}");
            request.Content = new StringContent(JsonSerializer.Serialize(testBody, JsonOpts), Encoding.UTF8, "application/json");

            using var response = await _httpClient.SendAsync(request);
            return response.IsSuccessStatusCode;
        }
        catch { return false; }
    }

    /// <summary>
    /// 获取当前配置对应的 API 所支持的模型列表
    /// </summary>
    /// <returns>模型 ID 字符串列表</returns>
    /// <exception cref="InvalidOperationException">API 未配置或请求失败时抛出</exception>
    public async Task<List<string>> GetModelsAsync()
    {
        var config = _configProvider();
        if (string.IsNullOrWhiteSpace(config.ApiUrl) || string.IsNullOrWhiteSpace(config.ApiKey))
            throw new InvalidOperationException("请先填写 API 地址和 Key");

        var url = BuildModelsUrl(config.ApiUrl);

        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Add("Authorization", $"Bearer {config.ApiKey}");

        using var response = await _httpClient.SendAsync(request);
        var responseBody = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"获取模型列表失败 ({(int)response.StatusCode})");

        return ParseModelsResponse(responseBody);
    }

    /// <summary>
    /// 根据 API 基础地址构建获取模型列表的完整 URL
    /// </summary>
    /// <param name="apiUrl">API 基础地址</param>
    /// <returns>模型列表接口 URL</returns>
    private static string BuildModelsUrl(string apiUrl)
    {
        var url = apiUrl.TrimEnd('/');
        if (url.EndsWith("/chat/completions", StringComparison.OrdinalIgnoreCase))
            return url.Replace("/chat/completions", "/models");
        if (url.EndsWith("/v1", StringComparison.OrdinalIgnoreCase))
            return url + "/models";
        if (url.EndsWith("/v1/", StringComparison.OrdinalIgnoreCase))
            return url + "models";
        return url + "/v1/models";
    }

    /// <summary>
    /// 解析 /models 接口返回的 JSON 响应
    /// </summary>
    /// <param name="responseBody">响应体字符串</param>
    /// <returns>模型 ID 列表（按字母顺序排序）</returns>
    private static List<string> ParseModelsResponse(string responseBody)
    {
        var models = new List<string>();
        try
        {
            using var doc = JsonDocument.Parse(responseBody);
            var root = doc.RootElement;

            if (root.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in data.EnumerateArray())
                {
                    var id = item.TryGetProperty("id", out var idProp) ? idProp.GetString() : null;
                    if (!string.IsNullOrEmpty(id))
                        models.Add(id);
                }
            }

            models.Sort(StringComparer.OrdinalIgnoreCase);
        }
        catch (JsonException) { }

        return models;
    }

    /// <summary>
    /// 根据 API 基础地址构建对话补全接口的完整 URL
    /// </summary>
    /// <param name="apiUrl">API 基础地址</param>
    /// <returns>对话补全接口 URL</returns>
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
    /// 构建对话请求的 JSON 请求体
    /// </summary>
    /// <param name="messages">对话消息列表</param>
    /// <param name="tools">可用工具定义列表</param>
    /// <param name="config">LLM 配置</param>
    /// <returns>JSON 字符串请求体</returns>
    private static string BuildRequestBody(List<ChatMessage> messages, List<ToolDefinition>? tools, LlmConfig config)
    {
        var msgList = new List<object>();
        foreach (var m in messages)
        {
            if (m.Role == "assistant" && m.ToolCalls != null && m.ToolCalls.Count > 0)
            {
                msgList.Add(new
                {
                    role = m.Role,
                    content = (string?)null,
                    tool_calls = m.ToolCalls.Select(tc => new
                    {
                        id = tc.Id,
                        type = tc.Type,
                        function = new { name = tc.Function.Name, arguments = tc.Function.Arguments }
                    }).ToArray()
                });
            }
            else if (m.Role == "tool")
            {
                msgList.Add(new
                {
                    role = m.Role,
                    content = m.Content,
                    tool_call_id = m.ToolCallId
                });
            }
            else
            {
                msgList.Add(new { role = m.Role, content = m.Content });
            }
        }

        var body = new Dictionary<string, object?>
        {
            ["model"] = config.Model,
            ["messages"] = msgList,
            ["temperature"] = config.Temperature,
            ["max_tokens"] = config.MaxTokens
        };

        if (tools != null && tools.Count > 0)
        {
            body["tools"] = tools.Select(t => new
            {
                type = t.Type,
                function = new
                {
                    name = t.Function.Name,
                    description = t.Function.Description,
                    parameters = new
                    {
                        type = t.Function.Parameters.Type,
                        properties = t.Function.Parameters.Properties.ToDictionary(
                            kvp => kvp.Key,
                            kvp => (object)BuildPropertyObj(kvp.Value)),
                        required = t.Function.Parameters.Required
                    }
                }
            }).ToArray();
        }

        return JsonSerializer.Serialize(body, JsonOpts);
    }

    /// <summary>
    /// 构建工具参数属性对象（包含 type、description 与可选 enum）
    /// </summary>
    /// <param name="prop">工具参数属性</param>
    /// <returns>用于 JSON 序列化的字典对象</returns>
    private static object BuildPropertyObj(ToolParameterProperty prop)
    {
        var dict = new Dictionary<string, object?>
        {
            ["type"] = prop.Type,
            ["description"] = prop.Description
        };
        if (prop.Enum != null && prop.Enum.Count > 0)
            dict["enum"] = prop.Enum;
        return dict;
    }

    /// <summary>
    /// 解析对话接口返回的 JSON 响应
    /// </summary>
    /// <param name="responseBody">响应体字符串</param>
    /// <returns>解析得到的 LLM 响应对象</returns>
    /// <exception cref="InvalidOperationException">响应体格式错误或包含 API 错误时抛出</exception>
    private static LlmResponse ParseResponse(string responseBody)
    {
        var result = new LlmResponse();
        try
        {
            using var doc = JsonDocument.Parse(responseBody);
            var root = doc.RootElement;

            if (root.TryGetProperty("choices", out var choices) && choices.ValueKind == JsonValueKind.Array && choices.GetArrayLength() > 0)
            {
                var choice = choices[0];
                var message = choice.GetProperty("message");

                result.Content = message.TryGetProperty("content", out var content) && content.ValueKind != JsonValueKind.Null ? content.GetString() ?? "" : "";
                result.FinishReason = choice.TryGetProperty("finish_reason", out var fr) && fr.ValueKind != JsonValueKind.Null ? fr.GetString() ?? "" : "";

                if (message.TryGetProperty("tool_calls", out var toolCalls) && toolCalls.ValueKind == JsonValueKind.Array)
                {
                    foreach (var tc in toolCalls.EnumerateArray())
                    {
                        var toolCall = new ToolCall
                        {
                            Id = tc.TryGetProperty("id", out var id) ? id.GetString() ?? "" : "",
                            Type = tc.TryGetProperty("type", out var type) ? type.GetString() ?? "function" : "function",
                            Function = new ToolCallFunction
                            {
                                Name = tc.GetProperty("function").GetProperty("name").GetString() ?? "",
                                Arguments = tc.GetProperty("function").GetProperty("arguments").GetString() ?? "{}"
                            }
                        };
                        result.ToolCalls.Add(toolCall);
                    }
                }
            }
            else if (root.TryGetProperty("error", out var error))
            {
                var msg = error.TryGetProperty("message", out var em) ? em.GetString() : error.GetRawText();
                throw new InvalidOperationException($"API 错误: {msg}");
            }
            else
            {
                throw new InvalidOperationException($"API 返回空响应（choices 为空），可能是限流或内容过滤。\n{Truncate(responseBody, 500)}");
            }
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException($"解析 API 响应失败: {ex.Message}\n{Truncate(responseBody, 300)}");
        }

        System.Diagnostics.Debug.WriteLine($"[CatClaw] AI ParseResponse (C# fallback): content='{Truncate(result.Content, 200)}', toolCalls={result.ToolCalls.Count}, finishReason={result.FinishReason}");
        return result;
    }

    /// <summary>截断字符串到指定长度，超出部分以 "..." 结尾</summary>
    /// <param name="s">原字符串</param>
    /// <param name="maxLen">最大长度</param>
    /// <returns>截断后的字符串</returns>
    private static string Truncate(string s, int maxLen) =>
        s.Length <= maxLen ? s : s[..maxLen] + "...";
}
