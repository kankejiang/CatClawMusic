using CatClawMusic.Core.Models;

namespace CatClawMusic.Core.Services.AI;

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
    /// <summary>是否作为退回模型（当前模型失败时按顺序尝试下一个启用了退回的模型）</summary>
    public bool FallbackEnabled { get; set; }
}

public class LlmConfigEntry : LlmConfig
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];
    public string Name { get; set; } = "";
    public bool IsActive { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.Now;

    public string DisplayName
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(Name)) return Name;
            var providers = LlmProviderInfo.GetAll();
            var provider = Array.Find(providers, p => p.Id == Provider);
            return provider?.Name ?? Provider;
        }
    }
}

public class LlmProviderInfo
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string DefaultApiUrl { get; set; } = "";
    public string DefaultModel { get; set; } = "";
    public string[] PresetModels { get; set; } = Array.Empty<string>();

    public static LlmProviderInfo[] GetAll() => new[]
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
}
