using System.Text.Json;
using CatClawMusic.Core.Interfaces;
using CatClawMusic.Core.Models;
using CatClawMusic.Core.Services;

using CatClawMusic.Core.Models;

namespace CatClawMusic.Core.Services.AI;

/// <summary>
/// AI 智能体服务实现，负责管理对话上下文、调度 LLM 客户端与工具，完成多轮工具调用循环。
/// </summary>
public class AgentService : IAgentService
{
    /// <summary>底层 LLM 客户端，用于实际发起对话请求</summary>
    private readonly ILlmClient _llmClient;

    /// <summary>可被 LLM 调用的本地工具集合</summary>
    private readonly IEnumerable<IAgentTool> _tools;

    /// <summary>当前对话历史消息列表（包含 system / user / assistant / tool 角色）</summary>
    private readonly List<ChatMessage> _conversationHistory = new();

    /// <summary>日志服务，用于记录 LLM 请求/响应与工具调用情况</summary>
    private readonly ILogService _logService;

    /// <summary>音乐库服务（可选），用于在搜索音乐时附加歌曲上下文</summary>
    private readonly IMusicLibraryService? _musicLibrary;

    /// <summary>当前智能体 ID，决定使用的系统提示词</summary>
    private string _currentAgentId;

    /// <summary>根据当前智能体 ID 获取对应的系统提示词</summary>
    private string CurrentSystemPrompt => BuiltinAgent.GetById(_currentAgentId).SystemPrompt;

    /// <summary>静态配置存储实例，在 DI 初始化时设置</summary>
    private static IAgentConfigStorage? _staticConfigStorage;

    /// <summary>初始化静态配置存储（由 DI 容器在启动时调用）</summary>
    /// <param name="configStorage">配置存储实现</param>
    public static void Initialize(IAgentConfigStorage configStorage)
    {
        _staticConfigStorage = configStorage;
    }

    /// <summary>获取已注入的配置存储实例，未初始化时抛出异常</summary>
    private static IAgentConfigStorage ConfigStorage =>
        _staticConfigStorage ?? throw new InvalidOperationException("AgentService 未初始化，请先调用 AgentService.Initialize()");

    /// <summary>当前智能体是否已完成配置（启用且填入了 ApiUrl 与 ApiKey）</summary>
    public bool IsConfigured
    {
        get
        {
            var config = LoadConfig();
            return config.Enabled && !string.IsNullOrWhiteSpace(config.ApiUrl) && !string.IsNullOrWhiteSpace(config.ApiKey);
        }
    }

    /// <summary>
    /// 构造 AgentService 实例
    /// </summary>
    /// <param name="llmClient">LLM 客户端</param>
    /// <param name="tools">可用的工具集合</param>
    /// <param name="logService">日志服务</param>
    /// <param name="musicLibrary">音乐库服务（可选）</param>
    public AgentService(ILlmClient llmClient, IEnumerable<IAgentTool> tools, ILogService logService, IMusicLibraryService? musicLibrary = null)
    {
        _llmClient = llmClient;
        _tools = tools;
        _logService = logService;
        _musicLibrary = musicLibrary;
        _currentAgentId = LoadCurrentAgentId();
    }

    /// <summary>获取所有支持的 LLM 服务商列表</summary>
    public static LlmProviderInfo[] GetProviders() => LlmProviderInfo.GetAll();

    /// <summary>从配置存储加载全部 LLM 配置列表</summary>
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

    /// <summary>保存全部 LLM 配置列表到配置存储</summary>
    /// <param name="configs">待保存的配置列表</param>
    public static void SaveAllConfigs(List<LlmConfig> configs)
    {
        var json = JsonSerializer.Serialize(configs);
        ConfigStorage.SetString("all_configs", json);
    }

    /// <summary>加载当前生效的 LLM 配置（按 current_config_name 索引，未找到则按旧字段读取）</summary>
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

    /// <summary>保存单个 LLM 配置，并同步更新当前生效配置与兼容字段</summary>
    /// <param name="config">待保存的配置</param>
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

    /// <summary>删除指定名称的 LLM 配置</summary>
    /// <param name="configName">配置名称</param>
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

    /// <summary>获取当前生效的 LLM 配置名称</summary>
    public static string GetCurrentConfigName()
    {
        return ConfigStorage.GetString("current_config_name", "默认配置") ?? "默认配置";
    }

    /// <summary>设置当前生效的 LLM 配置名称</summary>
    /// <param name="name">配置名称</param>
    public static void SetCurrentConfigName(string name)
    {
        ConfigStorage.SetString("current_config_name", name);
    }

    /// <summary>
    /// 发送用户消息并获取 AI 回复，支持多轮工具调用与流式部分消息回调。
    /// <para>最多进行 5 轮工具调用循环，超出后将返回提示消息。</para>
    /// </summary>
    /// <param name="userMessage">用户输入文本</param>
    /// <param name="onPartialMessage">流式输出或工具调用阶段的回调</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>最终助手回复消息</returns>
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

    /// <summary>清空当前对话历史</summary>
    public void ClearConversation()
    {
        _conversationHistory.Clear();
    }

    /// <summary>获取当前对话历史消息列表的副本</summary>
    public List<ChatMessage> GetConversationHistory() => _conversationHistory.ToList();

    /// <summary>获取当前正在使用的内置智能体</summary>
    public BuiltinAgent GetCurrentAgent() => BuiltinAgent.GetById(_currentAgentId);

    /// <summary>切换当前智能体，并清空对话历史</summary>
    /// <param name="agentId">智能体 ID</param>
    public void SetCurrentAgent(string agentId)
    {
        _currentAgentId = agentId;
        SaveCurrentAgentId(agentId);
        _conversationHistory.Clear();
    }

    /// <summary>从配置存储加载当前智能体 ID（默认 yuki）</summary>
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

    /// <summary>持久化当前智能体 ID 到配置存储</summary>
    /// <param name="agentId">智能体 ID</param>
    public static void SaveCurrentAgentId(string agentId)
    {
        ConfigStorage.SetString("current_agent_id", agentId);
    }

    /// <summary>截断字符串到指定长度，超出部分以 "..." 结尾（用于日志输出）</summary>
    /// <param name="s">原字符串</param>
    /// <param name="maxLen">最大长度</param>
    private static string Truncate(string s, int maxLen) =>
        s.Length <= maxLen ? s : s[..maxLen] + "...";
}
