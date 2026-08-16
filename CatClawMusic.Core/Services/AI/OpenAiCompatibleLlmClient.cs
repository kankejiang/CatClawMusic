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

    /// <summary>
    /// 临时配置覆盖（仅用于编辑页测试连接/获取模型列表时使用，不污染持久化存储）。
    /// 设置后，所有 _configProvider() 调用都会优先返回此配置；置空则回退到持久化配置。
    /// 实例属性而非静态：静态共享会让多实例/并发测试互相污染。
    /// </summary>
    public LlmConfig? TempConfigOverride { get; set; }

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

    /// <summary>获取当前生效的配置：优先返回临时覆盖，无则从持久化存储读取</summary>
    private LlmConfig GetEffectiveConfig()
    {
        return TempConfigOverride ?? _configProvider();
    }

    /// <summary>克隆配置并覆盖推理力度（避免修改共享配置实例，保证并发请求互不干扰）</summary>
    private static LlmConfig CloneWithEffort(LlmConfig c, string effort) => new()
    {
        Name = c.Name,
        Provider = c.Provider,
        ApiUrl = c.ApiUrl,
        ApiKey = c.ApiKey,
        Model = c.Model,
        Temperature = c.Temperature,
        MaxTokens = c.MaxTokens,
        Enabled = c.Enabled,
        FallbackEnabled = c.FallbackEnabled,
        ReasoningEffort = effort,
        TopP = c.TopP,
        FrequencyPenalty = c.FrequencyPenalty,
        PresencePenalty = c.PresencePenalty,
        ResponseFormat = c.ResponseFormat,
        MaxCompletionTokens = c.MaxCompletionTokens,
        ContextCaching = c.ContextCaching
    };

    /// <summary>获取所有可用的退回配置（启用了 FallbackEnabled 的配置，按列表顺序，不再要求 Enabled）</summary>
    private List<LlmConfig> GetFallbackConfigs()
    {
        if (_fallbackConfigsProvider == null) return new();
        var currentConfig = GetEffectiveConfig();
        // 只要求「作为备用」开关开启，不要再用 Enabled 二次过滤：
        // 用户勾了"作为备用"即明确意图；若同时要求 Enabled=true，编辑页里只勾备用、
        // 未勾「启用」的模型会被静默排除，导致主/备用额度耗尽时完全不会退到它
        //（实测：标为备用的最后一个模型从未被调用）。
        return _fallbackConfigsProvider()
            .Where(c => c.FallbackEnabled
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
    /// <param name="reasoningEffortOverride">请求级推理力度覆盖（如后台简单任务固定 low），null 表示跟随配置/全局</param>
    /// <returns>LLM 响应</returns>
    /// <exception cref="InvalidOperationException">API 未配置或所有退回均失败时抛出</exception>
    public Task<LlmResponse> ChatAsync(List<ChatMessage> messages, List<ToolDefinition>? tools = null, CancellationToken ct = default, string? reasoningEffortOverride = null)
        => ChatWithFallbackAsync(messages, tools, null, ct, reasoningEffortOverride);

    /// <summary>
    /// 流式对话请求：SSE 实时回调正文/思考过程增量，返回最终完整响应。
    /// 支持工具调用（流结束后随返回的 ToolCalls 给出）与主/备用模型回退。
    /// </summary>
    /// <param name="messages">对话消息列表</param>
    /// <param name="tools">可用工具定义列表（可选）</param>
    /// <param name="onDelta">流式增量回调（每次收到 delta 调用一次，来自 HTTP 读取线程）</param>
    /// <param name="ct">取消令牌</param>
    /// <param name="reasoningEffortOverride">请求级推理力度覆盖，null 表示跟随配置/全局</param>
    /// <returns>LLM 响应（Content/ReasoningContent 为流式累积的完整文本）</returns>
    public Task<LlmResponse> ChatStreamAsync(List<ChatMessage> messages, List<ToolDefinition>? tools,
        Action<LlmStreamDelta>? onDelta, CancellationToken ct = default, string? reasoningEffortOverride = null)
        => ChatWithFallbackAsync(messages, tools, onDelta, ct, reasoningEffortOverride);

    /// <summary>对话请求统一入口：主配置优先，失败时按序尝试备用配置（onDelta 非空走流式）。</summary>
    private async Task<LlmResponse> ChatWithFallbackAsync(List<ChatMessage> messages, List<ToolDefinition>? tools,
        Action<LlmStreamDelta>? onDelta, CancellationToken ct, string? effortOverride = null)
    {
        var config = GetEffectiveConfig();
        if (string.IsNullOrWhiteSpace(config.ApiUrl) || string.IsNullOrWhiteSpace(config.ApiKey))
            throw new InvalidOperationException("AI 服务未配置，请先在设置中配置 API 信息");

        // 请求级推理力度覆盖：克隆配置再改（不污染共享配置实例/全局设置，并发请求互不干扰）
        if (!string.IsNullOrEmpty(effortOverride))
            config = CloneWithEffort(config, effortOverride);

        // 尝试当前配置
        try
        {
            return await ChatWithConfigAsync(config, messages, tools, onDelta, ct);
        }
        catch (Exception primaryEx)
        {
            // 尝试退回配置
            var fallbacks = GetFallbackConfigs();
            if (fallbacks.Count == 0)
                throw;

            Log.Debug("OpenAiCompatibleLlmClient", $"[LlmClient] 主模型 {config.Name} 调用失败: {primaryEx.Message}，尝试退回模型...");

            foreach (var fallback in fallbacks)
            {
                try
                {
                    Log.Debug("OpenAiCompatibleLlmClient", $"[LlmClient] 尝试退回模型: {fallback.Name} ({fallback.Model})");
                    var result = await ChatWithConfigAsync(fallback, messages, tools, onDelta, ct);
                    Log.Debug("OpenAiCompatibleLlmClient", $"[LlmClient] 退回模型 {fallback.Name} 调用成功");
                    return result;
                }
                catch (Exception fbEx)
                {
                    Log.Debug("OpenAiCompatibleLlmClient", $"[LlmClient] 退回模型 {fallback.Name} 也失败: {fbEx.Message}");
                }
            }

            // 所有退回都失败：抛出聚合异常，明确说明主模型与备用模型均被尝试，
            // 便于上层提示与日志排查（否则只抛主模型异常，无法确认备用是否被尝试过）
            throw new InvalidOperationException(
                $"主模型 {config.Name} 及 {fallbacks.Count} 个备用模型均调用失败，最后错误: {primaryEx.Message}",
                primaryEx);
        }
    }

    /// <summary>
    /// 使用指定配置发起对话请求（onDelta 非空时走 SSE 流式）
    /// </summary>
    /// <param name="config">LLM 配置</param>
    /// <param name="messages">对话消息列表</param>
    /// <param name="tools">可用工具定义列表</param>
    /// <param name="onDelta">流式增量回调（可选）</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>LLM 响应</returns>
    private async Task<LlmResponse> ChatWithConfigAsync(LlmConfig config, List<ChatMessage> messages, List<ToolDefinition>? tools, Action<LlmStreamDelta>? onDelta, CancellationToken ct)
    {
        // 网络请求统一在后台线程发起：.NET Android 的 HttpClient（AndroidMessageHandler）
        // 底层 HttpURLConnection 在调用线程同步建立连接，若调用方是 UI 线程（如聊天发送
        // 命令）会抛 NetworkOnMainThreadException——桌面无此限制所以只在手机上显现。
        // Task.Run 确保 connect/请求体写出/响应读取都不占主线程；onDelta 回调仍从
        // HTTP 读取线程触发（调用方自行 marshal 到 UI）。
        return await Task.Run(async () =>
        {
            var url = BuildChatUrl(config.ApiUrl);
            var body = BuildRequestBody(messages, tools, config, stream: onDelta != null);

            var request = new HttpRequestMessage(HttpMethod.Post, url);
            request.Headers.Add("Authorization", $"Bearer {config.ApiKey}");
            request.Content = new StringContent(body, Encoding.UTF8, "application/json");

            using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct)
                .ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                var errorBody = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                throw new InvalidOperationException($"API 请求失败 ({(int)response.StatusCode}): {Truncate(errorBody, 500)}");
            }

            if (onDelta != null)
                return await ReadStreamAsync(response, onDelta, ct).ConfigureAwait(false);

            var responseBody = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            return ParseResponse(responseBody);
        }, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// 解析 SSE 流式响应：逐行读取 data: 块，累积正文/思考过程/工具调用，
    /// 每收到一个 delta 通过 onDelta 实时回调（正文与思考过程各自增量）。
    /// </summary>
    private static async Task<LlmResponse> ReadStreamAsync(HttpResponseMessage response,
        Action<LlmStreamDelta> onDelta, CancellationToken ct)
    {
        var contentBuilder = new StringBuilder();
        var reasoningBuilder = new StringBuilder();
        var finishReason = "";
        // OpenAI 流式 tool_calls 分片：按 index 聚合，最后合并完整 arguments
        var toolCallSlots = new Dictionary<int, (ToolCall call, StringBuilder args)>();

        using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var reader = new StreamReader(stream, Encoding.UTF8);

        while (!reader.EndOfStream)
        {
            ct.ThrowIfCancellationRequested();
            var line = await reader.ReadLineAsync(ct);
            if (string.IsNullOrWhiteSpace(line) || !line.StartsWith("data:"))
                continue;

            var data = line["data:".Length..].Trim();
            if (data == "[DONE]")
                break;

            try
            {
                using var doc = JsonDocument.Parse(data);
                var root = doc.RootElement;
                if (!root.TryGetProperty("choices", out var choices)
                    || choices.ValueKind != JsonValueKind.Array
                    || choices.GetArrayLength() == 0)
                    continue;
                var choice = choices[0];

                if (choice.TryGetProperty("finish_reason", out var fr) && fr.ValueKind == JsonValueKind.String)
                    finishReason = fr.GetString() ?? "";

                if (!choice.TryGetProperty("delta", out var delta))
                    continue;

                string content = "";
                if (delta.TryGetProperty("content", out var cProp) && cProp.ValueKind == JsonValueKind.String)
                    content = cProp.GetString() ?? "";

                string reasoning = "";
                // DeepSeek/智谱/Kimi 等推理模型：reasoning_content 或 reasoning 字段
                if (delta.TryGetProperty("reasoning_content", out var rcProp) && rcProp.ValueKind == JsonValueKind.String)
                    reasoning = rcProp.GetString() ?? "";
                else if (delta.TryGetProperty("reasoning", out var rProp) && rProp.ValueKind == JsonValueKind.String)
                    reasoning = rProp.GetString() ?? "";

                if (!string.IsNullOrEmpty(content))
                    contentBuilder.Append(content);
                if (!string.IsNullOrEmpty(reasoning))
                    reasoningBuilder.Append(reasoning);

                // 工具调用分片聚合（OpenAI 流式格式：同 index 多片，arguments 增量拼接）
                if (delta.TryGetProperty("tool_calls", out var tcs))
                {
                    foreach (var tc in tcs.EnumerateArray())
                    {
                        int idx = tc.TryGetProperty("index", out var idxProp) ? idxProp.GetInt32() : 0;
                        if (!toolCallSlots.TryGetValue(idx, out var slot))
                        {
                            slot = (new ToolCall { Type = "function", Function = new ToolCallFunction() }, new StringBuilder());
                            toolCallSlots[idx] = slot;
                        }
                        if (tc.TryGetProperty("id", out var idProp) && idProp.ValueKind == JsonValueKind.String)
                            slot.call.Id = idProp.GetString() ?? "";
                        if (tc.TryGetProperty("function", out var fn))
                        {
                            if (fn.TryGetProperty("name", out var nProp) && nProp.ValueKind == JsonValueKind.String
                                && string.IsNullOrEmpty(slot.call.Function.Name))
                                slot.call.Function.Name = nProp.GetString() ?? "";
                            if (fn.TryGetProperty("arguments", out var aProp) && aProp.ValueKind == JsonValueKind.String)
                                slot.args.Append(aProp.GetString() ?? "");
                        }
                    }
                }

                onDelta(new LlmStreamDelta
                {
                    Content = content,
                    ReasoningContent = reasoning,
                    FinishReason = finishReason
                });
            }
            catch (JsonException)
            {
                // 忽略无法解析的 SSE 块（部分服务端会夹杂注释/空块）
            }
        }

        var toolCalls = toolCallSlots.OrderBy(kv => kv.Key)
            .Select(kv =>
            {
                kv.Value.call.Function.Arguments = kv.Value.args.ToString();
                return kv.Value.call;
            })
            .ToList();

        return new LlmResponse
        {
            Content = contentBuilder.ToString(),
            ReasoningContent = reasoningBuilder.ToString(),
            ToolCalls = toolCalls,
            FinishReason = finishReason
        };
    }

    /// <summary>
    /// 测试当前配置的连接是否可用（发送一个极简请求）
    /// </summary>
    /// <returns>连接成功返回 true，否则返回 false</returns>
    public async Task<bool> TestConnectionAsync()
    {
        try
        {
            var config = GetEffectiveConfig();
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

            // 与 ChatWithConfigAsync 同理：Android 上 HttpURLConnection 在调用线程同步
            // 建连，UI 线程直发会抛 NetworkOnMainThreadException，切线程池发起
            return await Task.Run(async () =>
            {
                using var response = await _httpClient.SendAsync(request).ConfigureAwait(false);
                return response.IsSuccessStatusCode;
            }, CancellationToken.None).ConfigureAwait(false);
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
        var config = GetEffectiveConfig();
        if (string.IsNullOrWhiteSpace(config.ApiUrl) || string.IsNullOrWhiteSpace(config.ApiKey))
            throw new InvalidOperationException("请先填写 API 地址和 Key");

        var url = BuildModelsUrl(config.ApiUrl);

        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Add("Authorization", $"Bearer {config.ApiKey}");

        // 与 ChatWithConfigAsync 同理：切线程池发起，避免 Android 主线程同步建连崩溃
        var responseBody = await Task.Run(async () =>
        {
            using var response = await _httpClient.SendAsync(request).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
                throw new InvalidOperationException($"获取模型列表失败 ({(int)response.StatusCode})");
            return await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        }, CancellationToken.None).ConfigureAwait(false);

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
    /// <param name="stream">是否启用 SSE 流式响应</param>
    /// <returns>JSON 字符串请求体</returns>
    private static string BuildRequestBody(List<ChatMessage> messages, List<ToolDefinition>? tools, LlmConfig config, bool stream = false)
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
            ["messages"] = msgList
        };

        // SSE 流式（思考过程/正文实时增量）
        if (stream)
            body["stream"] = true;

        // 温度：仅对非推理模型发送（推理模型 o1/o3/deepseek-reasoner 等不支持 temperature）
        var isReasoningModel = !string.IsNullOrEmpty(config.ReasoningEffort)
                               && config.ReasoningEffort != "disabled";
        if (!isReasoningModel)
        {
            body["temperature"] = config.Temperature;
        }

        // 输出长度上限：优先使用 max_completion_tokens（含推理 token，OpenAI 推荐的新字段）
        // 旧字段 max_tokens 作为兼容回退
        if (config.MaxCompletionTokens > 0)
            body["max_completion_tokens"] = config.MaxCompletionTokens;
        else if (config.MaxTokens > 0)
            body["max_tokens"] = config.MaxTokens;
        else
        {
            // 未配置输出上限时按模型自动设置：大部分模型支持大输出（200k+ 上下文），
            // 服务端会按各自实际上限截断，给大值安全；推理模型（DeepSeek 等）的
            // reasoning_content 会占用大量 token，长输出场景需要更大的上限
            body["max_tokens"] = GetDefaultMaxTokens(config.Model);
        }

        // 核采样（仅在非默认值时发送，避免与服务端默认冲突）
        if (config.TopP > 0 && config.TopP < 1.0)
            body["top_p"] = config.TopP;

        // 频率惩罚（仅在非默认值时发送）
        if (config.FrequencyPenalty != 0)
            body["frequency_penalty"] = config.FrequencyPenalty;

        // 存在惩罚（仅在非默认值时发送）
        if (config.PresencePenalty != 0)
            body["presence_penalty"] = config.PresencePenalty;

        // 响应格式（仅非 text 时发送，部分模型不支持 json_object）
        if (!string.IsNullOrEmpty(config.ResponseFormat)
            && config.ResponseFormat != "text")
        {
            body["response_format"] = new Dictionary<string, object> { ["type"] = config.ResponseFormat };
        }

        // 上下文缓存：各平台多为隐式/自动缓存（DeepSeek 自动前缀缓存、Kimi 自动缓存、
        // 通义 prompt cache 等），显式 prompt_cache_key 仅 DeepSeek 支持，其他供应商
        // 可能因未知参数报 400 —— 不再发送显式 key，保留 ContextCaching 字段仅作配置展示。
        _ = config.ContextCaching;

        // 推理力度参数（仅对已知支持 reasoning_effort 的供应商发送）：
        // - "disabled" 与 "auto" 均不发送（auto = 跟随模型默认，DeepSeek 等
        //   枚举不含 auto，发送会 400）
        // - 合法枚举因供应商而异：DeepSeek V4 = none/minimal/low/medium/high/xhigh/max，
        //   Kimi K3 = low/high/max——用户配置的值直接透传，由供应商校验
        // - 其他供应商（智谱 thinking 参数/通义 enable_thinking/讯飞等）不用
        //   reasoning_effort，一律不发送，避免未知参数 400
        var effort = config.ReasoningEffort;
        if (string.IsNullOrEmpty(effort))
        {
            // 配置未显式设置时兜底用全局（模型管理页）；disabled/auto 为显式意图，不覆盖
            var globalEffort = AgentService.GetReasoningEffort();
            if (!string.IsNullOrEmpty(globalEffort) && globalEffort is not ("disabled" or "auto"))
                effort = globalEffort;
            else
                effort = "";
        }
        if (!string.IsNullOrEmpty(effort) && effort is not ("disabled" or "auto"))
        {
            var provider = (config.Provider ?? "").ToLowerInvariant();
            if (provider is "deepseek" or "moonshot")
                body["reasoning_effort"] = effort;
        }

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

            // Agent 场景：允许一次响应并行调用多个工具（主流新模型均支持），
            // 多工具任务（如同时搜索+查库）显著减少轮次
            body["parallel_tool_calls"] = true;
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

                // 推理模型的思考过程：DeepSeek 等多数用 reasoning_content，部分模型用 reasoning
                if (message.TryGetProperty("reasoning_content", out var reasoning) && reasoning.ValueKind == JsonValueKind.String)
                    result.ReasoningContent = reasoning.GetString() ?? "";
                else if (message.TryGetProperty("reasoning", out var reasoning2) && reasoning2.ValueKind == JsonValueKind.String)
                    result.ReasoningContent = reasoning2.GetString() ?? "";

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

        Log.Debug("OpenAiCompatibleLlmClient", $"[CatClaw] AI ParseResponse (C# fallback): content='{Truncate(result.Content, 200)}', toolCalls={result.ToolCalls.Count}, finishReason={result.FinishReason}");
        return result;
    }

    /// <summary>截断字符串到指定长度，超出部分以 "..." 结尾</summary>
    /// <param name="s">原字符串</param>
    /// <param name="maxLen">最大长度</param>
    /// <returns>截断后的字符串</returns>
    private static string Truncate(string s, int maxLen) =>
        s.Length <= maxLen ? s : s[..maxLen] + "...";

    /// <summary>
    /// 未配置输出上限时按模型名自动选择默认 max_tokens（服务端会按实际上限截断，给大值安全）。
    /// 规则：已知小上限模型（DeepSeek 系列输出上限 ~8K，且推理 token 计入）取 8K；
    /// 其余主流模型（Qwen/Kimi/GLM/GPT/Claude/Gemini 等，普遍 200k+ 上下文、大输出上限）取大默认值。
    /// </summary>
    private static int GetDefaultMaxTokens(string model)
    {
        var m = (model ?? "").ToLowerInvariant();
        if (m.Contains("deepseek"))
            return 8192;
        return 65536;
    }
}
