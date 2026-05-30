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
    private readonly IMusicLibraryService? _musicLibrary;

    private const string SystemPrompt = @"你是猫爪音乐的 AI 助手，可以帮助用户管理音乐库和播放音乐。你可以：
1. 搜索音乐库中的歌曲
2. 创建、查看、删除播放列表（歌单）
3. 将歌曲添加到歌单或从歌单移除
4. 播放指定歌曲
5. 当用户询问需要网络信息的问题时（如：最新音乐资讯、艺术家介绍、歌曲背景等），使用网络搜索工具获取信息

请用中文回复用户。当用户请求涉及音乐操作时，请使用提供的工具来完成。回复要简洁友好。

重要规则：
- 创建歌单前不需要确认，直接创建
- 添加歌曲到歌单时，先搜索歌曲获取 ID，再添加
- 如果用户说""帮我创建一个XX歌单""，直接调用创建歌单工具
- 如果用户说""把XX添加到歌单""，先搜索歌曲，再添加到歌单
- 当用户询问需要网络信息的问题时，主动使用 web_search 工具搜索";

    private static readonly LlmProviderInfo[] Providers = new[]
    {
        new LlmProviderInfo { Id = "deepseek", Name = "DeepSeek", DefaultApiUrl = "https://api.deepseek.com/v1", DefaultModel = "", PresetModels = new[] { "deepseek-chat", "deepseek-reasoner" } },
        new LlmProviderInfo { Id = "modelscope", Name = "魔搭社区", DefaultApiUrl = "https://api-inference.modelscope.cn/v1", DefaultModel = "", PresetModels = new[] { "Qwen/Qwen3.5-35B-A3B", "Qwen/Qwen3-235B-A22B", "Qwen/Qwen2.5-Coder-32B-Instruct", "Qwen/Qwen2.5-72B-Instruct", "deepseek-ai/DeepSeek-V3", "deepseek-ai/DeepSeek-R1" } },
        new LlmProviderInfo { Id = "llamacpp", Name = "llama.cpp (本地)", DefaultApiUrl = "http://127.0.0.1:8080/v1", DefaultModel = "" },
        new LlmProviderInfo { Id = "zhipu", Name = "智谱 AI", DefaultApiUrl = "https://open.bigmodel.cn/api/paas/v1", DefaultModel = "", PresetModels = new[] { "glm-4-flash", "glm-4-plus", "glm-4-air", "glm-4-long", "glm-4" } },
        new LlmProviderInfo { Id = "moonshot", Name = "Moonshot (Kimi)", DefaultApiUrl = "https://api.moonshot.cn/v1", DefaultModel = "", PresetModels = new[] { "moonshot-v1-8k", "moonshot-v1-32k", "moonshot-v1-128k" } },
        new LlmProviderInfo { Id = "qwen", Name = "通义千问", DefaultApiUrl = "https://dashscope.aliyuncs.com/compatible-mode/v1", DefaultModel = "", PresetModels = new[] { "qwen-turbo", "qwen-plus", "qwen-max", "qwen-long" } },
        new LlmProviderInfo { Id = "spark", Name = "讯飞星火", DefaultApiUrl = "https://spark-api-open.xf-yun.com/v1", DefaultModel = "", PresetModels = new[] { "generalv3.5", "generalv3", "4.0Ultra" } },
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

    public AgentService(ILlmClient llmClient, IEnumerable<IAgentTool> tools, ILogService logService, IMusicLibraryService? musicLibrary = null)
    {
        _llmClient = llmClient;
        _tools = tools;
        _logService = logService;
        _musicLibrary = musicLibrary;
    }

    public static LlmProviderInfo[] GetProviders() => Providers;

    public static List<LlmConfig> LoadAllConfigs()
    {
        try
        {
            var ctx = global::Android.App.Application.Context;
            var prefs = ctx.GetSharedPreferences("catclaw_ai", FileCreationMode.Private);
            var json = prefs.GetString("all_configs", "[]") ?? "[]";
            var configs = JsonSerializer.Deserialize<List<LlmConfig>>(json) ?? new List<LlmConfig>();
            return configs;
        }
        catch
        {
            return new List<LlmConfig>();
        }
    }

    public static void SaveAllConfigs(List<LlmConfig> configs)
    {
        var ctx = global::Android.App.Application.Context;
        var prefs = ctx.GetSharedPreferences("catclaw_ai", FileCreationMode.Private);
        var json = JsonSerializer.Serialize(configs);
        prefs.Edit().PutString("all_configs", json).Apply();
    }

    public static LlmConfig LoadConfig()
    {
        try
        {
            var ctx = global::Android.App.Application.Context;
            var prefs = ctx.GetSharedPreferences("catclaw_ai", FileCreationMode.Private);
            var currentName = prefs.GetString("current_config_name", "默认配置") ?? "默认配置";
            var allConfigs = LoadAllConfigs();
            var config = allConfigs.FirstOrDefault(c => c.Name == currentName);
            
            if (config != null)
                return config;
            
            return new LlmConfig
            {
                Name = currentName,
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
        var allConfigs = LoadAllConfigs();
        var existingIndex = allConfigs.FindIndex(c => c.Name == config.Name);
        
        if (existingIndex >= 0)
        {
            allConfigs[existingIndex] = config;
        }
        else
        {
            allConfigs.Add(config);
        }
        
        SaveAllConfigs(allConfigs);
        
        var ctx = global::Android.App.Application.Context;
        var prefs = ctx.GetSharedPreferences("catclaw_ai", FileCreationMode.Private);
        prefs.Edit()
            .PutString("current_config_name", config.Name)
            .PutString("provider", config.Provider)
            .PutString("api_url", config.ApiUrl)
            .PutString("api_key", config.ApiKey)
            .PutString("model", config.Model)
            .PutFloat("temperature", (float)config.Temperature)
            .PutInt("max_tokens", config.MaxTokens)
            .PutBoolean("enabled", config.Enabled)
            .Apply();
    }

    public static void DeleteConfig(string configName)
    {
        var allConfigs = LoadAllConfigs();
        var toRemove = allConfigs.FirstOrDefault(c => c.Name == configName);
        if (toRemove != null)
        {
            allConfigs.Remove(toRemove);
            SaveAllConfigs(allConfigs);
        }
    }

    public static string GetCurrentConfigName()
    {
        var ctx = global::Android.App.Application.Context;
        var prefs = ctx.GetSharedPreferences("catclaw_ai", FileCreationMode.Private);
        return prefs.GetString("current_config_name", "默认配置") ?? "默认配置";
    }

    public static void SetCurrentConfigName(string name)
    {
        var ctx = global::Android.App.Application.Context;
        var prefs = ctx.GetSharedPreferences("catclaw_ai", FileCreationMode.Private);
        prefs.Edit().PutString("current_config_name", name).Apply();
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
                List<Core.Models.Song>? songs = null;
                if (toolMap.TryGetValue(toolCall.Function.Name, out var tool))
                {
                    try
                    {
                        toolResult = await tool.ExecuteAsync(toolCall.Function.Arguments);
                        _logService.Info("Agent", $"Agent 工具 {toolCall.Function.Name} 执行成功");

                        if (toolCall.Function.Name == "search_music" && _musicLibrary != null)
                        {
                            try
                            {
                                var keyword = NativeInterop.AiExtractStringArg(toolCall.Function.Arguments, "keyword")
                                    ?? ArgHelper.ExtractStringArgFallback(toolCall.Function.Arguments, "keyword");
                                if (!string.IsNullOrWhiteSpace(keyword))
                                    songs = await _musicLibrary.SearchAsync(keyword);
                            }
                            catch { }
                        }
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
                    Name = toolCall.Function.Name,
                    Songs = songs
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
