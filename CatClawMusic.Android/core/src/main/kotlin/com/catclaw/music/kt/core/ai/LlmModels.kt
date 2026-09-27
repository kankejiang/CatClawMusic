package com.catclaw.music.kt.core.ai

import com.catclaw.music.kt.core.model.Song

/**
 * LLM 配置与对话数据模型，字段、默认值与语义对齐 C# `Core/Services/AI/ChatModels.cs` 与 `AiModels.cs`。
 * 这些默认值会直接进请求体，所以一个都不许凭感觉改。
 */
data class LlmConfig(
    val name: String = "默认配置",
    val provider: String = "deepseek",
    val apiUrl: String = "https://api.deepseek.com/v1",
    val apiKey: String = "",
    val model: String = "deepseek-v4-flash",
    /** 仅对非推理模型发送 */
    val temperature: Double = 0.7,
    /** 旧字段，保留兼容；新配置用 maxCompletionTokens */
    val maxTokens: Int = 2048,
    val enabled: Boolean = false,
    /** 当前模型失败时按顺序尝试下一个启用了退回的模型 */
    val fallbackEnabled: Boolean = false,
    /** auto/disabled/low/high/max，默认 disabled */
    val reasoningEffort: String = "disabled",
    /** 1.0 表示用模型默认，此时不发送该参数 */
    val topP: Double = 1.0,
    val frequencyPenalty: Double = 0.0,
    val presencePenalty: Double = 0.0,
    /** text / json_object */
    val responseFormat: String = "text",
    /** 含推理 token 的最大输出；0 表示用模型默认上限 */
    val maxCompletionTokens: Int = 0,
    /** C# 侧已不再发送显式 prompt_cache_key（只有 DeepSeek 认，其他家会 400），此字段仅作配置展示 */
    val contextCaching: Boolean = true,
) {
    fun displayName(): String = name.ifBlank { LlmProviderInfo.byId(provider)?.name ?: provider }
}

/** 一条对话消息。role 取 system/user/assistant/tool */
data class ChatMessage(
    val role: String,
    val content: String = "",
    /** assistant 发起的工具调用；序列化时只有 assistant 且非空才带 */
    val toolCalls: List<ToolCall>? = null,
    /** role=tool 时回填对应的调用 id */
    val toolCallId: String? = null,
    val reasoningContent: String? = null,
    /**
     * 随这条回复一起给出的可点歌卡片，只给 UI 用。
     * 出站请求里 [OpenAiCompatibleClient] 逐字段手拼 JSON，不读这一项，
     * 所以它不会污染对话协议体（C# 的 `ChatMessage.Songs` 同样是 UI-only 字段）。
     */
    val songs: List<Song> = emptyList(),
)

data class ToolCall(
    val id: String = "",
    val type: String = "function",
    val name: String = "",
    /** 原始 JSON 文本，流式下由多个分片拼出来 */
    val arguments: String = "",
)

/** 工具定义。parameters 走 JSON Schema 的子集 */
data class ToolDefinition(
    val type: String = "function",
    val name: String,
    val description: String,
    val properties: Map<String, ToolParamSchema> = emptyMap(),
    val required: List<String> = emptyList(),
)

data class ToolParamSchema(
    val type: String,
    val description: String = "",
    val enumValues: List<String> = emptyList(),
)

data class LlmResponse(
    val content: String = "",
    val reasoningContent: String = "",
    val toolCalls: List<ToolCall> = emptyList(),
    val finishReason: String = "",
)

/** 流式增量：正文与思考过程各自分开走，UI 才能分区渲染 */
data class LlmStreamDelta(
    val content: String = "",
    val reasoningContent: String = "",
    val finishReason: String = "",
)

/** Agent 全局运行设置，对齐 C# `AgentRunSettings` */
object AgentRunSettings {
    const val KEY_MAX_TOOL_ROUNDS = "agent_max_tool_rounds"
    const val KEY_MAX_PLAN_ROUNDS = "agent_max_plan_rounds"
    const val KEY_REASONING_EFFORT = "agent_reasoning_effort"

    /**
     * 联网类任务（搜索→抓取→下载）实测要 6-10 轮，8 轮偏紧容易误报「步骤过多」；
     * 死循环由「同工具 + 同参数连续 3 次」的检测兜底，所以这里放宽。
     */
    const val DEFAULT_MAX_TOOL_ROUNDS = 12
    const val DEFAULT_MAX_PLAN_ROUNDS = 0
    const val DEFAULT_REASONING_EFFORT = "disabled"

    val REASONING_EFFORT_OPTIONS = listOf("auto", "disabled", "low", "high", "max")
}

/** 服务商预设。地址与模型清单照抄 C# `LlmProviderInfo.GetAll()` */
data class LlmProviderInfo(
    val id: String,
    val name: String,
    val defaultApiUrl: String,
    val defaultModel: String = "",
    val presetModels: List<String> = emptyList(),
) {
    companion object {
        val ALL: List<LlmProviderInfo> = listOf(
            LlmProviderInfo("deepseek", "DeepSeek", "https://api.deepseek.com/v1", "deepseek-v4-flash",
                listOf("deepseek-v4-flash", "deepseek-v4-pro")),
            LlmProviderInfo("modelscope", "魔搭社区", "https://api-inference.modelscope.cn/v1", "",
                listOf("Qwen/Qwen3.5-32B-A3B-Instruct", "Qwen/Qwen3.5-235B-A22B-Instruct", "Qwen/Qwen3-235B-A22B",
                    "Qwen/Qwen2.5-Coder-32B-Instruct", "deepseek-ai/DeepSeek-V4-Flash", "deepseek-ai/DeepSeek-V4-Pro")),
            LlmProviderInfo("llamacpp", "llama.cpp (本地)", "http://127.0.0.1:8080/v1"),
            LlmProviderInfo("zhipu", "智谱 AI", "https://open.bigmodel.cn/api/paas/v4", "glm-4.7-flash",
                listOf("glm-5.2", "glm-5.1", "glm-5", "glm-5-turbo", "glm-4.7", "glm-4.7-flashx", "glm-4.6",
                    "glm-4.5-air", "glm-4.7-flash", "glm-4-long")),
            LlmProviderInfo("moonshot", "Moonshot (Kimi)", "https://api.moonshot.cn/v1", "kimi-k3",
                listOf("kimi-k3", "kimi-k2.7-code", "kimi-k2.6")),
            LlmProviderInfo("qwen", "通义千问", "https://dashscope.aliyuncs.com/compatible-mode/v1", "",
                listOf("qwen3.8-max", "qwen3.7-plus", "qwen3.7-flash")),
            LlmProviderInfo("spark", "讯飞星火", "https://spark-api-open.xf-yun.com/v1", "",
                listOf("spark-4.0-ultra", "spark-max", "spark-pro", "spark-lite")),
            LlmProviderInfo("nvidia", "NVIDIA NIM", "https://integrate.api.nvidia.com/v1",
                "nvidia/llama-3.1-nemotron-nano-8b-v1",
                listOf("nvidia/llama-3.1-nemotron-nano-8b-v1", "nvidia/llama-3.3-nemotron-super-49b-v1",
                    "meta/llama-3.3-70b-instruct", "meta/llama-3.1-8b-instruct", "microsoft/phi-4-mini-instruct",
                    "mistralai/mistral-nemotron")),
            LlmProviderInfo("opencodego", "OpenCode Go", "https://opencode.ai/zen/go/v1", "deepseek-v4-flash",
                listOf("glm-5.3", "glm-5.2", "glm-5.1", "kimi-k3", "kimi-k2.7-code", "kimi-k2.6",
                    "deepseek-v4-flash", "deepseek-v4-pro", "mimo-v2.5", "mimo-v2.5-pro", "hy3")),
            LlmProviderInfo("custom", "自定义 (OpenAI 兼容)", ""),
        )

        fun byId(id: String): LlmProviderInfo? = ALL.firstOrNull { it.id == id }
    }
}
