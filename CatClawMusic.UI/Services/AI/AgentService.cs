using System.Text.Json;
using Android.Content;
using CatClawMusic.Core.Interfaces;
using CatClawMusic.Core.Models;

namespace CatClawMusic.UI.Services.AI;

public class AgentService : IAgentService
{
    private readonly ILlmClient _llmClient;
    private readonly IEnumerable<IAgentTool> _tools;
    private readonly List<ChatMessage> _conversationHistory = new();
    private readonly ILogService _logService;

    private const string SystemPrompt = @"你是猫爪音乐的 AI 助手，可以帮助用户管理音乐库和播放音乐。你可以：
1. 搜索音乐库中的歌曲
2. 创建、查看、删除播放列表（歌单）
3. 将歌曲添加到歌单或从歌单中移除
4. 播放指定歌曲

请用中文回复用户。当用户请求涉及音乐操作时，请使用提供的工具来完成。回复要简洁友好。

重要规则：
- 创建歌单前不需要确认，直接创建
- 添加歌曲到歌单时，先搜索歌曲获取 ID，再添加
- 如果用户说""帮我创建一个XX歌单""，直接调用创建歌单工具
- 如果用户说""把XX添加到歌单""，先搜索歌曲，再添加到歌单";

    private static readonly LlmProviderInfo[] Providers = new[]
    {
        new LlmProviderInfo { Id = "deepseek", Name = "DeepSeek", DefaultApiUrl = "https://api.deepseek.com/v1", DefaultModel = "deepseek-chat" },
        new LlmProviderInfo { Id = "modelscope", Name = "魔搭社区", DefaultApiUrl = "https://dashscope.aliyuncs.com/compatible-mode/v1", DefaultModel = "qwen-turbo" },
        new LlmProviderInfo { Id = "llamacpp", Name = "llama.cpp (本地)", DefaultApiUrl = "http://127.0.0.1:8080/v1", DefaultModel = "default" },
        new LlmProviderInfo { Id = "zhipu", Name = "智谱 AI", DefaultApiUrl = "https://open.bigmodel.cn/api/paas/v1", DefaultModel = "glm-4-flash" },
        new LlmProviderInfo { Id = "moonshot", Name = "Moonshot (Kimi)", DefaultApiUrl = "https://api.moonshot.cn/v1", DefaultModel = "moonshot-v1-8k" },
        new LlmProviderInfo { Id = "qwen", Name = "通义千问", DefaultApiUrl = "https://dashscope.aliyuncs.com/compatible-mode/v1", DefaultModel = "qwen-turbo" },
        new LlmProviderInfo { Id = "spark", Name = "讯飞星火", DefaultApiUrl = "https://spark-api-open.xf-yun.com/v1", DefaultModel = "generalv3.5" },
        new LlmProviderInfo { Id = "custom", Name = "自定义 (OpenAI 兼容)", DefaultApiUrl = "", DefaultModel = "" },
    };

    public bool IsConfigured
    {
        get
        {
            var config = LoadConfig();
            return config.Enabled && !string.IsNullOrWhiteSpace(config.ApiUrl) && !string.IsNullOrWhiteSpace(config.ApiKey);
        }
    }

    public AgentService(ILlmClient llmClient, IEnumerable<IAgentTool> tools, ILogService logService)
    {
        _llmClient = llmClient;
        _tools = tools;
        _logService = logService;
    }

    public static LlmProviderInfo[] GetProviders() => Providers;

    public static LlmConfig LoadConfig()
    {
        try
        {
            var ctx = global::Android.App.Application.Context;
            var prefs = ctx.GetSharedPreferences("catclaw_ai", FileCreationMode.Private);
            return new LlmConfig
            {
                Provider = prefs.GetString("provider", "deepseek") ?? "deepseek",
                ApiUrl = prefs.GetString("api_url", "https://api.deepseek.com/v1") ?? "",
                ApiKey = prefs.GetString("api_key", "") ?? "",
                Model = prefs.GetString("model", "deepseek-chat") ?? "deepseek-chat",
                Temperature = (double)(prefs.GetFloat("temperature", 0.7f)),
                MaxTokens = prefs.GetInt("max_tokens", 2048),
                Enabled = prefs.GetBoolean("enabled", false)
            };
        }
        catch
        {
            return new LlmConfig();
        }
    }

    public static void SaveConfig(LlmConfig config)
    {
        var ctx = global::Android.App.Application.Context;
        var prefs = ctx.GetSharedPreferences("catclaw_ai", FileCreationMode.Private);
        prefs.Edit()
            .PutString("provider", config.Provider)
            .PutString("api_url", config.ApiUrl)
            .PutString("api_key", config.ApiKey)
            .PutString("model", config.Model)
            .PutFloat("temperature", (float)config.Temperature)
            .PutInt("max_tokens", config.MaxTokens)
            .PutBoolean("enabled", config.Enabled)
            .Apply();
    }

    public async Task<ChatMessage> SendMessageAsync(string userMessage, Action<ChatMessage>? onPartialMessage = null, CancellationToken ct = default)
    {
        if (_conversationHistory.Count == 0)
        {
            _conversationHistory.Add(new ChatMessage { Role = "system", Content = SystemPrompt });
        }

        _conversationHistory.Add(new ChatMessage { Role = "user", Content = userMessage });

        var toolDefs = _tools.Select(t => t.GetDefinition()).ToList();
        var toolMap = _tools.ToDictionary(t => t.Name);

        const int maxToolRounds = 5;

        for (int round = 0; round < maxToolRounds; round++)
        {
            LlmResponse response;
            try
            {
                response = await _llmClient.ChatAsync(_conversationHistory, toolDefs, ct);
            }
            catch (Exception ex)
            {
                _logService.Warn("Agent", $"Agent LLM 请求失败: {ex.Message}");
                var errorMsg = new ChatMessage { Role = "assistant", Content = $"抱歉，AI 服务请求失败：{ex.Message}" };
                _conversationHistory.Add(errorMsg);
                return errorMsg;
            }

            if (!response.HasToolCalls)
            {
                var assistantMsg = new ChatMessage { Role = "assistant", Content = response.Content };
                _conversationHistory.Add(assistantMsg);
                return assistantMsg;
            }

            var assistantToolCallMsg = new ChatMessage
            {
                Role = "assistant",
                Content = response.Content ?? "",
                ToolCalls = response.ToolCalls
            };
            _conversationHistory.Add(assistantToolCallMsg);
            onPartialMessage?.Invoke(assistantToolCallMsg);

            foreach (var toolCall in response.ToolCalls)
            {
                string toolResult;
                if (toolMap.TryGetValue(toolCall.Function.Name, out var tool))
                {
                    try
                    {
                        toolResult = await tool.ExecuteAsync(toolCall.Function.Arguments);
                        _logService.Info("Agent", $"Agent 工具 {toolCall.Function.Name} 执行成功");
                    }
                    catch (Exception ex)
                    {
                        toolResult = JsonSerializer.Serialize(new { error = $"工具执行失败: {ex.Message}" });
                        _logService.Warn("Agent", $"Agent 工具 {toolCall.Function.Name} 执行失败: {ex.Message}");
                    }
                }
                else
                {
                    toolResult = JsonSerializer.Serialize(new { error = $"未知工具: {toolCall.Function.Name}" });
                }

                _conversationHistory.Add(new ChatMessage
                {
                    Role = "tool",
                    Content = toolResult,
                    ToolCallId = toolCall.Id,
                    Name = toolCall.Function.Name
                });
            }
        }

        var finalMsg = new ChatMessage { Role = "assistant", Content = "操作步骤过多，已停止执行。请尝试简化你的请求。" };
        _conversationHistory.Add(finalMsg);
        return finalMsg;
    }

    public void ClearConversation()
    {
        _conversationHistory.Clear();
    }

    public List<ChatMessage> GetConversationHistory() => _conversationHistory.ToList();
}
