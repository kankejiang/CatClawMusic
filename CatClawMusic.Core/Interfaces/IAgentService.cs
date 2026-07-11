using CatClawMusic.Core.Models;

namespace CatClawMusic.Core.Interfaces;

/// <summary>
/// 大语言模型（LLM）客户端接口，抽象对底层 LLM 服务（如 OpenAI 兼容接口）的访问
/// </summary>
public interface ILlmClient
{
    /// <summary>发起一次对话请求，支持传入工具定义以启用 Function Calling</summary>
    /// <param name="messages">对话消息列表</param>
    /// <param name="tools">可选的工具定义列表</param>
    /// <param name="ct">取消令牌</param>
    Task<LlmResponse> ChatAsync(List<ChatMessage> messages, List<ToolDefinition>? tools = null, CancellationToken ct = default);

    /// <summary>测试与 LLM 服务的连接是否正常</summary>
    Task<bool> TestConnectionAsync();

    /// <summary>获取 LLM 服务上可用的模型 ID 列表</summary>
    Task<List<string>> GetModelsAsync();
}

/// <summary>
/// AI 智能体工具接口，每个工具代表一个可被 LLM 调用的本地能力
/// </summary>
public interface IAgentTool
{
    /// <summary>工具名称（LLM 调用时使用的标识）</summary>
    string Name { get; }

    /// <summary>工具功能描述（提供给 LLM 用于判断何时调用）</summary>
    string Description { get; }

    /// <summary>获取工具的参数定义，用于 LLM Function Calling 协议</summary>
    ToolDefinition GetDefinition();

    /// <summary>执行工具并返回结果文本</summary>
    /// <param name="arguments">LLM 传入的参数 JSON 字符串</param>
    Task<string> ExecuteAsync(string arguments);
}

/// <summary>
/// AI 智能体服务接口，负责管理对话上下文并调用 LLM 完成用户请求
/// </summary>
public interface IAgentService
{
    /// <summary>发送用户消息并获取 AI 回复，支持流式部分消息回调</summary>
    /// <param name="userMessage">用户输入文本</param>
    /// <param name="onPartialMessage">流式输出时的部分消息回调</param>
    /// <param name="ct">取消令牌</param>
    Task<ChatMessage> SendMessageAsync(string userMessage, Action<ChatMessage>? onPartialMessage = null, CancellationToken ct = default);

    /// <summary>清空当前对话历史</summary>
    void ClearConversation();

    /// <summary>获取当前对话历史消息列表</summary>
    List<ChatMessage> GetConversationHistory();

    /// <summary>当前智能体是否已完成配置（具备可用的 LLM 客户端与工具）</summary>
    bool IsConfigured { get; }

    /// <summary>获取当前正在使用的内置智能体</summary>
    BuiltinAgent GetCurrentAgent();

    /// <summary>切换当前智能体</summary>
    /// <param name="agentId">智能体唯一标识</param>
    void SetCurrentAgent(string agentId);

    /// <summary>一次性快速问答：使用独立临时对话，不污染主对话历史</summary>
    /// <param name="systemPrompt">系统提示词</param>
    /// <param name="userPrompt">用户提示词</param>
    /// <param name="ct">取消令牌</param>
    Task<string> QuickAskAsync(string systemPrompt, string userPrompt, CancellationToken ct = default);
}

/// <summary>
/// 智能体配置存储接口，提供对配置项的简单键值读写访问
/// </summary>
public interface IAgentConfigStorage
{
    /// <summary>读取字符串配置项</summary>
    /// <param name="key">配置键</param>
    /// <param name="defaultValue">未找到时的默认值</param>
    string? GetString(string key, string? defaultValue = null);

    /// <summary>写入字符串配置项</summary>
    void SetString(string key, string value);

    /// <summary>读取整型配置项</summary>
    int GetInt(string key, int defaultValue = 0);

    /// <summary>写入整型配置项</summary>
    void SetInt(string key, int value);

    /// <summary>读取单精度浮点配置项</summary>
    float GetFloat(string key, float defaultValue = 0f);

    /// <summary>写入单精度浮点配置项</summary>
    void SetFloat(string key, float value);

    /// <summary>读取布尔配置项</summary>
    bool GetBool(string key, bool defaultValue = false);

    /// <summary>写入布尔配置项</summary>
    void SetBool(string key, bool value);
}
