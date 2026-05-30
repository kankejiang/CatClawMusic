namespace CatClawMusic.UI.Services.AI;

public class ChatMessage
{
    public string Role { get; set; } = "user";
    public string Content { get; set; } = "";
    public List<ToolCall>? ToolCalls { get; set; }
    public string? ToolCallId { get; set; }
    public string? Name { get; set; }
    public List<CatClawMusic.Core.Models.Song>? Songs { get; set; }
}

public class ToolCall
{
    public string Id { get; set; } = "";
    public string Type { get; set; } = "function";
    public ToolCallFunction Function { get; set; } = new();
}

public class ToolCallFunction
{
    public string Name { get; set; } = "";
    public string Arguments { get; set; } = "";
}

public class ToolDefinition
{
    public string Type { get; set; } = "function";
    public ToolFunctionDef Function { get; set; } = new();
}

public class ToolFunctionDef
{
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public ToolParameterDef Parameters { get; set; } = new();
}

public class ToolParameterDef
{
    public string Type { get; set; } = "object";
    public Dictionary<string, ToolParameterProperty> Properties { get; set; } = new();
    public List<string> Required { get; set; } = new();
}

public class ToolParameterProperty
{
    public string Type { get; set; } = "string";
    public string Description { get; set; } = "";
    public List<string>? Enum { get; set; }
}

public class LlmConfig
{
    public string Name { get; set; } = "默认配置";
    public string Provider { get; set; } = "deepseek";
    public string ApiUrl { get; set; } = "https://api.deepseek.com/v1";
    public string ApiKey { get; set; } = "";
    public string Model { get; set; } = "deepseek-chat";
    public double Temperature { get; set; } = 0.7;
    public int MaxTokens { get; set; } = 2048;
    public bool Enabled { get; set; }
}

public class LlmResponse
{
    public string Content { get; set; } = "";
    public List<ToolCall> ToolCalls { get; set; } = new();
    public bool HasToolCalls => ToolCalls.Count > 0;
    public string FinishReason { get; set; } = "";
}

public class LlmProviderInfo
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string DefaultApiUrl { get; set; } = "";
    public string DefaultModel { get; set; } = "";
    public string[] PresetModels { get; set; } = Array.Empty<string>();
}
