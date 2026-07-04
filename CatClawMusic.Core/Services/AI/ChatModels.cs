using CatClawMusic.Core.Models;

namespace CatClawMusic.Core.Services.AI;

/// <summary>
/// LLM 配置信息，保存服务提供商、API 地址、密钥、模型、采样参数等。
/// </summary>
public class LlmConfig
{
    /// <summary>配置名称，用于在多个配置间区分</summary>
    public string Name { get; set; } = "默认配置";
    /// <summary>服务商 ID（如 deepseek、qwen、zhipu 等）</summary>
    public string Provider { get; set; } = "deepseek";
    /// <summary>API 基础地址（OpenAI 兼容）</summary>
    public string ApiUrl { get; set; } = "https://api.deepseek.com/v1";
    /// <summary>API 密钥</summary>
    public string ApiKey { get; set; } = "";
    /// <summary>模型名称（如 deepseek-chat）</summary>
    public string Model { get; set; } = "deepseek-chat";
    /// <summary>采样温度（0.0 - 2.0，值越大回复越发散）</summary>
    public double Temperature { get; set; } = 0.7;
    /// <summary>单次响应最大 token 数</summary>
    public int MaxTokens { get; set; } = 2048;
    /// <summary>是否启用该配置</summary>
    public bool Enabled { get; set; }
    /// <summary>是否作为退回模型（当前模型失败时按顺序尝试下一个启用了退回的模型）</summary>
    public bool FallbackEnabled { get; set; }
}

/// <summary>
/// LLM 配置条目，扩展自 <see cref="LlmConfig"/>，增加唯一标识、激活状态与创建时间等元数据。
/// </summary>
public class LlmConfigEntry : LlmConfig
{
    /// <summary>条目唯一 ID（Guid 前 8 位）</summary>
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];
    /// <summary>条目显示名称（隐藏继承自基类的 Name 字段）</summary>
    public new string Name { get; set; } = "";
    /// <summary>是否为当前激活的配置</summary>
    public bool IsActive { get; set; }
    /// <summary>创建时间</summary>
    public DateTime CreatedAt { get; set; } = DateTime.Now;

    /// <summary>
    /// 获取显示名称：若 Name 非空则直接返回，否则按 Provider 查找对应服务商名称
    /// </summary>
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

/// <summary>
/// LLM 服务商预设信息，包含 ID、显示名称、默认 API 地址与预设模型列表。
/// </summary>
public class LlmProviderInfo
{
    /// <summary>服务商 ID</summary>
    public string Id { get; set; } = "";
    /// <summary>服务商显示名称</summary>
    public string Name { get; set; } = "";
    /// <summary>默认 API 地址</summary>
    public string DefaultApiUrl { get; set; } = "";
    /// <summary>默认模型名称</summary>
    public string DefaultModel { get; set; } = "";
    /// <summary>预设模型列表，用于前端选择</summary>
    public string[] PresetModels { get; set; } = Array.Empty<string>();

    /// <summary>
    /// 获取所有内置支持的 LLM 服务商列表
    /// </summary>
    /// <returns>包含 DeepSeek、魔搭、llama.cpp、智谱、Moonshot、通义千问、讯飞星火、自定义的数组</returns>
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
