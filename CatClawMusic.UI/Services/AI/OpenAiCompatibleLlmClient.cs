using System.Net;
using System.Text;
using System.Text.Json;
using CatClawMusic.Core.Interfaces;

namespace CatClawMusic.UI.Services.AI;

public class OpenAiCompatibleLlmClient : ILlmClient
{
    private readonly HttpClient _httpClient;
    private readonly Func<LlmConfig> _configProvider;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        WriteIndented = false,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    public OpenAiCompatibleLlmClient(Func<LlmConfig> configProvider)
    {
        _configProvider = configProvider;
        _httpClient = new HttpClient(new HttpClientHandler
        {
            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate
        });
        _httpClient.Timeout = TimeSpan.FromSeconds(120);
    }

    public async Task<LlmResponse> ChatAsync(List<ChatMessage> messages, List<ToolDefinition>? tools = null, CancellationToken ct = default)
    {
        var config = _configProvider();
        if (string.IsNullOrWhiteSpace(config.ApiUrl) || string.IsNullOrWhiteSpace(config.ApiKey))
            throw new InvalidOperationException("AI 服务未配置，请先在设置中配置 API 信息");

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

    private static string BuildChatUrl(string apiUrl)
    {
        var nativeUrl = Services.NativeInterop.AiBuildUrl(apiUrl);
        if (nativeUrl != null) return nativeUrl;

        var url = apiUrl.TrimEnd('/');
        if (url.EndsWith("/chat/completions", StringComparison.OrdinalIgnoreCase))
            return url;
        if (url.EndsWith("/v1", StringComparison.OrdinalIgnoreCase))
            return url + "/chat/completions";
        if (url.EndsWith("/v1/", StringComparison.OrdinalIgnoreCase))
            return url + "chat/completions";
        return url + "/v1/chat/completions";
    }

    private static string BuildRequestBody(List<ChatMessage> messages, List<ToolDefinition>? tools, LlmConfig config)
    {
        var nativeBody = Services.NativeInterop.AiBuildChatRequest(
            config.Model, messages, tools, config.Temperature, config.MaxTokens);
        if (nativeBody != null) return nativeBody;

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

    private static LlmResponse ParseResponse(string responseBody)
    {
        var nativeResult = Services.NativeInterop.AiParseChatResponse(responseBody);
        if (nativeResult != null) return nativeResult;

        var result = new LlmResponse();
        try
        {
            using var doc = JsonDocument.Parse(responseBody);
            var root = doc.RootElement;

            if (root.TryGetProperty("choices", out var choices) && choices.GetArrayLength() > 0)
            {
                var choice = choices[0];
                var message = choice.GetProperty("message");

                result.Content = message.TryGetProperty("content", out var content) ? content.GetString() ?? "" : "";
                result.FinishReason = choice.TryGetProperty("finish_reason", out var fr) ? fr.GetString() ?? "" : "";

                if (message.TryGetProperty("tool_calls", out var toolCalls))
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
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException($"解析 API 响应失败: {ex.Message}\n{Truncate(responseBody, 300)}");
        }
        return result;
    }

    private static string Truncate(string s, int maxLen) =>
        s.Length <= maxLen ? s : s[..maxLen] + "...";
}
