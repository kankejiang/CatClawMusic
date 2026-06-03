using CatClawMusic.Core.Services.AI;

namespace CatClawMusic.Core.Interfaces;

public interface ILlmClient
{
    Task<LlmResponse> ChatAsync(List<ChatMessage> messages, List<ToolDefinition>? tools = null, CancellationToken ct = default);
    Task<bool> TestConnectionAsync();
    Task<List<string>> GetModelsAsync();
}

public interface IAgentTool
{
    string Name { get; }
    string Description { get; }
    ToolDefinition GetDefinition();
    Task<string> ExecuteAsync(string arguments);
}

public interface IAgentService
{
    Task<ChatMessage> SendMessageAsync(string userMessage, Action<ChatMessage>? onPartialMessage = null, CancellationToken ct = default);
    void ClearConversation();
    List<ChatMessage> GetConversationHistory();
    bool IsConfigured { get; }
    BuiltinAgent GetCurrentAgent();
    void SetCurrentAgent(string agentId);
}

public interface IAgentConfigStorage
{
    string? GetString(string key, string? defaultValue = null);
    void SetString(string key, string value);
    int GetInt(string key, int defaultValue = 0);
    void SetInt(string key, int value);
    float GetFloat(string key, float defaultValue = 0f);
    void SetFloat(string key, float value);
    bool GetBool(string key, bool defaultValue = false);
    void SetBool(string key, bool value);
}
