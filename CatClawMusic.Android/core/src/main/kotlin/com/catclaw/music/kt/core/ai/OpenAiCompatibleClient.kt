package com.catclaw.music.kt.core.ai

import kotlinx.serialization.json.Json
import kotlinx.serialization.json.JsonArray
import kotlinx.serialization.json.JsonElement
import kotlinx.serialization.json.JsonNull
import kotlinx.serialization.json.JsonObject
import kotlinx.serialization.json.JsonPrimitive
import kotlinx.serialization.json.buildJsonArray
import kotlinx.serialization.json.buildJsonObject
import kotlinx.serialization.json.intOrNull
import kotlinx.serialization.json.jsonObject
import kotlinx.serialization.json.jsonPrimitive
import kotlinx.serialization.json.put

/** 一次 HTTP 调用的结果：状态码 + 响应体；流式时 [forEachLine] 逐行回调 SSE */
data class LlmHttpResult(val status: Int, val body: String)

/**
 * 传输口子：单测塞假实现即可覆盖 URL/请求体/SSE/退回链，真机走 :data 的 okhttp 实现。
 * [forEachLine] 每收到一行就回调（含空行），实现方负责按 UTF-8 解。
 */
interface LlmTransport {
    fun post(url: String, headers: Map<String, String>, body: String, forEachLine: (String) -> Unit): LlmHttpResult
    fun get(url: String, headers: Map<String, String>): LlmHttpResult
}

private val LlmJson = Json { ignoreUnknownKeys = true }

class OpenAiCompatibleClient(
    private val transport: LlmTransport,
    private val configProvider: () -> LlmConfig,
    private val fallbackProvider: () -> List<LlmConfig> = { emptyList() },
    /** C# 里由 AgentService.GetReasoningEffort() 提供的全局档位 */
    private val globalReasoningEffort: () -> String = { "" },
) {

    class LlmApiException(message: String) : RuntimeException(message)


    fun chat(
        messages: List<ChatMessage>,
        tools: List<ToolDefinition>? = null,
        onDelta: ((LlmStreamDelta) -> Unit)? = null,
        effortOverride: String? = null,
    ): LlmResponse {
        var config = configProvider()
        if (!effortOverride.isNullOrEmpty()) config = config.copy(reasoningEffort = effortOverride)
        if (config.apiUrl.isBlank() || config.apiKey.isBlank()) {
            throw LlmApiException("AI 服务未配置，请先在设置中配置 API 信息")
        }

        return try {
            call(config, messages, tools, onDelta)
        } catch (primary: Exception) {
            val fallbacks = fallbackProvider().filter { it.fallbackEnabled }
            if (fallbacks.isEmpty()) throw primary
            var last = primary
            for (fb in fallbacks) {
                try {
                    return call(fb, messages, tools, onDelta)
                } catch (fbEx: Exception) {
                    last = fbEx
                }
            }
            // 聚合抛出：只报主模型异常的话，上层无法确认备用是否真被试过
            throw LlmApiException(
                "主模型 ${config.displayName()} 及 ${fallbacks.size} 个备用模型均调用失败，最后错误: ${last.message}",
            )
        }
    }

    private fun call(
        config: LlmConfig,
        messages: List<ChatMessage>,
        tools: List<ToolDefinition>?,
        onDelta: ((LlmStreamDelta) -> Unit)?,
    ): LlmResponse {
        val url = buildChatUrl(config.apiUrl)
        val body = buildRequestBody(messages, tools, config, stream = onDelta != null, globalReasoningEffort = globalReasoningEffort())
        val parser = SseStreamParser()
        val result = transport.post(
            url,
            mapOf(
                "Authorization" to "Bearer ${config.apiKey}",
                "Content-Type" to "application/json",
            ),
            body,
        ) { line ->
            parser.feedLine(line)?.let { delta -> onDelta?.invoke(delta) }
        }
        if (result.status !in 200..299) {
            throw LlmApiException("API 请求失败 (${result.status}): ${truncate(result.body, 500)}")
        }
        return if (onDelta != null) {
            // 流式时响应体已被逐行消费掉；若传输层没回调过（例如服务端没带 SSE 头），
            // 退回按普通 JSON 解一次，行为与 C# 的「有 onDelta 才走流」等价
            parser.result().takeIf { it.content.isNotEmpty() || it.toolCalls.isNotEmpty() }
                ?: parseResponse(result.body)
        } else {
            parseResponse(result.body)
        }
    }

    /** 非流式响应解析：choices[0].message 里的 content / reasoning / tool_calls */
    fun parseResponse(body: String): LlmResponse {
        val root = runCatching { LlmJson.parseToJsonElement(body).jsonObject }.getOrNull()
            ?: return LlmResponse()
        val choices = root["choices"] as? JsonArray ?: return LlmResponse()
        val choice = choices.firstOrNull() as? JsonObject ?: return LlmResponse()
        val message = choice["message"] as? JsonObject ?: return LlmResponse()
        val calls = (message["tool_calls"] as? JsonArray)?.mapNotNull { element ->
            val tc = element as? JsonObject ?: return@mapNotNull null
            val fn = tc["function"] as? JsonObject
            ToolCall(
                id = tc.str("id").orEmpty(),
                type = tc.str("type") ?: "function",
                name = fn?.str("name").orEmpty(),
                arguments = fn?.str("arguments").orEmpty(),
            )
        } ?: emptyList()
        return LlmResponse(
            content = message.str("content").orEmpty(),
            reasoningContent = message.str("reasoning_content") ?: message.str("reasoning") ?: "",
            toolCalls = calls,
            finishReason = choice.str("finish_reason").orEmpty(),
        )
    }

    private fun JsonObject.str(key: String): String? =
        (this[key] as? kotlinx.serialization.json.JsonPrimitive)?.takeIf { it.isString }?.content

    fun testConnection(): Boolean = runCatching {
        val config = configProvider()
        if (config.apiUrl.isBlank() || config.apiKey.isBlank()) return false
        val url = buildChatUrl(config.apiUrl)
        val body = buildRequestBody(
            listOf(ChatMessage(role = "user", content = "ping")),
            null,
            config.copy(maxTokens = 1, temperature = 0.0),
            stream = false,
        )
        transport.post(url, mapOf("Authorization" to "Bearer ${config.apiKey}", "Content-Type" to "application/json"), body) {}.status in 200..299
    }.getOrDefault(false)

    /** 拉取该账号可用的模型 id 列表（NVIDIA 等目录会随账号变化，设置页用它做「获取模型列表」） */
    fun listModels(): List<String> = runCatching {
        val config = configProvider()
        val base = config.apiUrl.trimEnd('/')
        val url = if (base.endsWith("/v1", true) || VERSION_TAIL.matches(base)) "$base/models" else "$base/v1/models"
        val res = transport.get(url, mapOf("Authorization" to "Bearer ${config.apiKey}"))
        if (res.status !in 200..299) return emptyList()
        val root = LlmJson.parseToJsonElement(res.body).jsonObject
        (root["data"] as? JsonArray)?.mapNotNull { item ->
            (item as? JsonObject)?.get("id")?.jsonPrimitive?.contentOrNullSafe()
        } ?: emptyList()
    }.getOrDefault(emptyList())

    companion object {
        private val VERSION_TAIL = Regex("/v\\d+$")

        /**
         * 端点拼接规则照抄 C# `BuildChatUrl`：
         * 已带 /chat/completions 原样；/v1 或 /v1/ 直接接；
         * **其他版本段（智谱 /v4 等）接 /chat/completions 而不是 /v1/chat/completions**，
         * 否则会被拼成 /v4/v1/chat/completions；其余情况才补 /v1。
         */
        fun buildChatUrl(apiUrl: String): String {
            val url = apiUrl.trimEnd('/')
            return when {
                url.endsWith("/chat/completions", true) -> url
                url.endsWith("/v1", true) -> "$url/chat/completions"
                url.endsWith("/v1/", true) -> "${url}chat/completions"
                VERSION_TAIL.containsMatchIn(url) -> "$url/chat/completions"
                else -> "$url/v1/chat/completions"
            }
        }

        /** 与 C# `GetDefaultMaxTokens` 同：deepseek 8192，其余 65536 */
        fun defaultMaxTokens(model: String): Int =
            if (model.lowercase().contains("deepseek")) 8192 else 65536

        /**
         * 请求体构造，逐条对齐 C# `BuildRequestBody`。
         * 关键点是「**不确定的参数一律不发**」：top_p=1.0、两个 penalty=0、response_format=text、
         * reasoning_effort 的 disabled/auto 都会省略，因为各家网关对未知参数会直接 400。
         */
        fun buildRequestBody(
            messages: List<ChatMessage>,
            tools: List<ToolDefinition>?,
            config: LlmConfig,
            stream: Boolean,
            globalReasoningEffort: String = "",
        ): String {
            val msgList = buildJsonArray {
                for (m in messages) {
                    add(buildJsonObject {
                        put("role", m.role)
                        if (m.role == "assistant" && !m.toolCalls.isNullOrEmpty()) {
                            put("content", m.content)
                            put("tool_calls", buildJsonArray {
                                m.toolCalls.forEach { tc ->
                                    add(buildJsonObject {
                                        put("id", tc.id)
                                        put("type", tc.type)
                                        put("function", buildJsonObject {
                                            put("name", tc.name)
                                            put("arguments", tc.arguments)
                                        })
                                    })
                                }
                            })
                        } else if (m.role == "tool") {
                            put("tool_call_id", m.toolCallId.orEmpty())
                            put("content", m.content)
                        } else {
                            put("content", m.content)
                        }
                    })
                }
            }

            val body = buildJsonObject {
                put("model", config.model)
                put("messages", msgList)
                if (stream) put("stream", true)

                val isReasoningModel = config.reasoningEffort.isNotEmpty() && config.reasoningEffort != "disabled"
                if (!isReasoningModel) put("temperature", config.temperature)

                when {
                    config.maxCompletionTokens > 0 -> put("max_completion_tokens", config.maxCompletionTokens)
                    config.maxTokens > 0 -> put("max_tokens", config.maxTokens)
                    else -> put("max_tokens", defaultMaxTokens(config.model))
                }
                if (config.maxCompletionTokens == 0 && config.maxTokens == 0) {
                    // 上面已按 C# 顺序落默认值，这里不再重复
                }

                if (config.topP > 0.0 && config.topP < 1.0) put("top_p", config.topP)
                if (config.frequencyPenalty != 0.0) put("frequency_penalty", config.frequencyPenalty)
                if (config.presencePenalty != 0.0) put("presence_penalty", config.presencePenalty)
                if (config.responseFormat.isNotEmpty() && config.responseFormat != "text") {
                    put("response_format", buildJsonObject { put("type", config.responseFormat) })
                }

                var effort = config.reasoningEffort
                if (effort.isEmpty()) {
                    val global = globalReasoningEffort
                    effort = if (global.isNotEmpty() && global != "disabled" && global != "auto") global else ""
                }
                if (effort.isNotEmpty() && effort != "disabled" && effort != "auto") {
                    when (config.provider.lowercase()) {
                        // 只有已知支持该参数的供应商才发；枚举值原样透传，由供应商自己校验
                        "deepseek", "moonshot" -> put("reasoning_effort", effort)
                        "nvidia" -> if (config.model.contains("deepseek-ai/", true)) put("reasoning_effort", effort)
                    }
                }

                if (!tools.isNullOrEmpty()) {
                    put("tools", buildJsonArray {
                        tools.forEach { t ->
                            add(buildJsonObject {
                                put("type", t.type)
                                put("function", buildJsonObject {
                                    put("name", t.name)
                                    put("description", t.description)
                                    put("parameters", buildJsonObject {
                                        put("type", "object")
                                        put("properties", buildJsonObject {
                                            t.properties.forEach { (key, schema) ->
                                                put(key, buildJsonObject {
                                                    put("type", schema.type)
                                                    if (schema.description.isNotEmpty()) put("description", schema.description)
                                                    if (schema.enumValues.isNotEmpty()) {
                                                        put("enum", buildJsonArray { schema.enumValues.forEach { add(JsonPrimitive(it)) } })
                                                    }
                                                })
                                            }
                                        })
                                        put("required", buildJsonArray { t.required.forEach { add(JsonPrimitive(it)) } })
                                    })
                                })
                            })
                        }
                    })
                }
            }
            return LlmJson.encodeToString(JsonElement.serializer(), body)
        }

        private fun truncate(value: String, max: Int) = if (value.length <= max) value else value.substring(0, max)

        private fun JsonPrimitive.contentOrNullSafe(): String? = if (this is JsonNull) null else content
    }
}
