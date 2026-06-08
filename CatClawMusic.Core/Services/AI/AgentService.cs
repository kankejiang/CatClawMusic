using System.Text.Json;
using CatClawMusic.Core.Interfaces;
using CatClawMusic.Core.Models;
using CatClawMusic.Core.Services;

namespace CatClawMusic.Core.Services.AI;

public class AgentService : IAgentService
{
    private readonly ILlmClient _llmClient;
    private readonly IEnumerable<IAgentTool> _tools;
    private readonly List<ChatMessage> _conversationHistory = new();
    private readonly ILogService _logService;
    private readonly IMusicLibraryService? _musicLibrary;

    private string _currentAgentId;

    private string CurrentSystemPrompt => BuiltinAgent.GetById(_currentAgentId).SystemPrompt;

    /// <summary>静态配置存储实例，在 DI 初始化时设置</summary>
    private static IAgentConfigStorage? _staticConfigStorage;

    /// <summary>初始化静态配置存储（由 DI 容器在启动时调用）</summary>
    public static void Initialize(IAgentConfigStorage configStorage)
    {
        _staticConfigStorage = configStorage;
    }

    private static IAgentConfigStorage ConfigStorage =>
        _staticConfigStorage ?? throw new InvalidOperationException("AgentService 未初始化，请先调用 AgentService.Initialize()");

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
        _currentAgentId = LoadCurrentAgentId();
    }

    public static LlmProviderInfo[] GetProviders() => LlmProviderInfo.GetAll();

    public static List<LlmConfig> LoadAllConfigs()
    {
        try
        {
            var json = ConfigStorage.GetString("all_configs", "[]") ?? "[]";
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
        var json = JsonSerializer.Serialize(configs);
        ConfigStorage.SetString("all_configs", json);
    }

    public static LlmConfig LoadConfig()
    {
        try
        {
            var currentName = ConfigStorage.GetString("current_config_name", "默认配置") ?? "默认配置";
            var allConfigs = LoadAllConfigs();
            var config = allConfigs.FirstOrDefault(c => c.Name == currentName);

            if (config != null)
                return config;

            return new LlmConfig
            {
                Name = currentName,
                Provider = ConfigStorage.GetString("provider", "deepseek") ?? "deepseek",
                ApiUrl = ConfigStorage.GetString("api_url", "https://api.deepseek.com/v1") ?? "",
                ApiKey = ConfigStorage.GetString("api_key", "") ?? "",
                Model = ConfigStorage.GetString("model", "deepseek-chat") ?? "deepseek-chat",
                Temperature = (double)ConfigStorage.GetFloat("temperature", 0.7f),
                MaxTokens = ConfigStorage.GetInt("max_tokens", 2048),
                Enabled = ConfigStorage.GetBool("enabled", false)
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

        ConfigStorage.SetString("current_config_name", config.Name);
        ConfigStorage.SetString("provider", config.Provider);
        ConfigStorage.SetString("api_url", config.ApiUrl);
        ConfigStorage.SetString("api_key", config.ApiKey);
        ConfigStorage.SetString("model", config.Model);
        ConfigStorage.SetFloat("temperature", (float)config.Temperature);
        ConfigStorage.SetInt("max_tokens", config.MaxTokens);
        ConfigStorage.SetBool("enabled", config.Enabled);
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
        return ConfigStorage.GetString("current_config_name", "默认配置") ?? "默认配置";
    }

    public static void SetCurrentConfigName(string name)
    {
        ConfigStorage.SetString("current_config_name", name);
    }

    public async Task<ChatMessage> SendMessageAsync(string userMessage, Action<ChatMessage>? onPartialMessage = null, CancellationToken ct = default)
    {
        if (_conversationHistory.Count == 0)
        {
            _conversationHistory.Add(new ChatMessage { Role = "system", Content = CurrentSystemPrompt });
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
                _logService.Info("Agent", $"Agent LLM 响应: content='{Truncate(response.Content, 200)}', toolCalls={response.ToolCalls.Count}, finishReason={response.FinishReason}");
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
                List<Song>? songs = null;
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
                                var keyword = ArgHelper.ExtractStringArgFallback(toolCall.Function.Arguments, "keyword");
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

                var toolResultMsg = new ChatMessage
                {
                    Role = "tool",
                    Content = toolResult,
                    ToolCallId = toolCall.Id,
                    Name = toolCall.Function.Name,
                    Songs = songs
                };
                _conversationHistory.Add(toolResultMsg);
                onPartialMessage?.Invoke(toolResultMsg);
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

    public BuiltinAgent GetCurrentAgent() => BuiltinAgent.GetById(_currentAgentId);

    public void SetCurrentAgent(string agentId)
    {
        _currentAgentId = agentId;
        SaveCurrentAgentId(agentId);
        _conversationHistory.Clear();
    }

    public static string LoadCurrentAgentId()
    {
        try
        {
            return ConfigStorage.GetString("current_agent_id", "yuki") ?? "yuki";
        }
        catch
        {
            return "yuki";
        }
    }

    public static void SaveCurrentAgentId(string agentId)
    {
        ConfigStorage.SetString("current_agent_id", agentId);
    }

    private static string Truncate(string s, int maxLen) =>
        s.Length <= maxLen ? s : s[..maxLen] + "...";
}
