namespace CatClawMusic.UI.Services.AI;

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
}
