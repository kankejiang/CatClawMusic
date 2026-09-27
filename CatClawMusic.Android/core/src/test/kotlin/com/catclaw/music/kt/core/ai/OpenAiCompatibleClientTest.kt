package com.catclaw.music.kt.core.ai

import kotlinx.serialization.json.Json
import kotlinx.serialization.json.JsonObject
import kotlinx.serialization.json.JsonPrimitive
import kotlinx.serialization.json.contentOrNull
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Test

/**
 * LLM 传输层：端点拼接、请求体的「不确定就不发」纪律、SSE 分片聚合、退回链。
 * 全部走假 transport，不需要网络。
 */
class OpenAiCompatibleClientTest {

    private val JSON = Json { ignoreUnknownKeys = true }

    private fun obj(body: String): JsonObject = JSON.parseToJsonElement(body) as JsonObject

    private fun key(body: String, key: String): Boolean = obj(body).containsKey(key)

    private fun str(body: String, key: String): String? =
        (obj(body)[key] as? JsonPrimitive)?.contentOrNull

    // ---- BuildChatUrl：C# 里那条「/v4 不能再补 /v1」的规则最容易写错 ----

    @Test
    fun chatUrlAppendsPerApiVersionShape() {
        assertEquals("https://x/v1/chat/completions", OpenAiCompatibleClient.buildChatUrl("https://x/v1"))
        assertEquals("https://x/v1/chat/completions", OpenAiCompatibleClient.buildChatUrl("https://x/v1/"))
        assertEquals("https://x/v1/chat/completions", OpenAiCompatibleClient.buildChatUrl("https://x/v1///"))
        // 已经拼全的不重复拼
        assertEquals(
            "https://x/v1/chat/completions",
            OpenAiCompatibleClient.buildChatUrl("https://x/v1/chat/completions"),
        )
        // 智谱 v4：必须是 /v4/chat/completions，拼成 /v4/v1/... 会被拒
        assertEquals(
            "https://open.bigmodel.cn/api/paas/v4/chat/completions",
            OpenAiCompatibleClient.buildChatUrl("https://open.bigmodel.cn/api/paas/v4"),
        )
        // 没有版本段才补 /v1
        assertEquals("https://x/v1/chat/completions", OpenAiCompatibleClient.buildChatUrl("https://x"))
    }

    @Test
    fun defaultMaxTokensFollowsTheModelFamily() {
        assertEquals(8192, OpenAiCompatibleClient.defaultMaxTokens("deepseek-v4-flash"))
        assertEquals(8192, OpenAiCompatibleClient.defaultMaxTokens("DeepSeek-V4-Pro"))
        assertEquals(65536, OpenAiCompatibleClient.defaultMaxTokens("kimi-k3"))
    }

    // ---- 请求体：省略规则是重点，多发一个参数某些网关就 400 ----

    private fun bodyFor(config: LlmConfig, messages: List<ChatMessage> = listOf(ChatMessage("user", "hi")), tools: List<ToolDefinition>? = null) =
        OpenAiCompatibleClient.buildRequestBody(messages, tools, config, stream = false)

    @Test
    fun neutralSamplingParamsAreOmitted() {
        val body = bodyFor(LlmConfig())
        assertEquals(0.7, str(body, "temperature")!!.toDouble(), 1e-9)
        assertFalse(key(body, "top_p"))
        assertFalse(key(body, "frequency_penalty"))
        assertFalse(key(body, "presence_penalty"))
        assertFalse(key(body, "response_format"))
        assertFalse(key(body, "reasoning_effort"))
        // 默认 maxTokens=2048 走 max_tokens，而不是 max_completion_tokens
        assertEquals(2048, str(body, "max_tokens")!!.toInt())
        assertFalse(key(body, "max_completion_tokens"))
    }

    @Test
    fun nonNeutralSamplingParamsAreSent() {
        val body = bodyFor(
            LlmConfig(topP = 0.8, frequencyPenalty = -0.5, presencePenalty = 1.25, responseFormat = "json_object"),
        )
        assertEquals(0.8, str(body, "top_p")!!.toDouble(), 1e-9)
        assertEquals(-0.5, str(body, "frequency_penalty")!!.toDouble(), 1e-9)
        assertEquals(1.25, str(body, "presence_penalty")!!.toDouble(), 1e-9)
        assertEquals("json_object", (obj(body)["response_format"] as JsonObject)["type"]!!.jsonText())
    }

    @Test
    fun maxCompletionTokensWinsOverLegacyMaxTokens() {
        val body = bodyFor(LlmConfig(maxTokens = 2048, maxCompletionTokens = 9000))
        assertEquals(9000, str(body, "max_completion_tokens")!!.toInt())
        assertFalse(key(body, "max_tokens"))
    }

    @Test
    fun bothTokenFieldsZeroFallsBackToPerModelDefault() {
        val body = bodyFor(LlmConfig(maxTokens = 0, maxCompletionTokens = 0, model = "kimi-k3"))
        assertEquals(65536, str(body, "max_tokens")!!.toInt())
    }

    @Test
    fun reasoningEffortOnlyGoesToProvidersThatKnowIt() {
        assertEquals(
            "high",
            str(bodyFor(LlmConfig(provider = "deepseek", reasoningEffort = "high")), "reasoning_effort"),
        )
        assertTrue(key(bodyFor(LlmConfig(provider = "moonshot", reasoningEffort = "max")), "reasoning_effort"))
        // 智谱/通义用的是自己的 thinking 参数，发 reasoning_effort 会 400
        assertFalse(key(bodyFor(LlmConfig(provider = "zhipu", reasoningEffort = "high")), "reasoning_effort"))
        // NVIDIA 只对 deepseek-ai/ 系列透传
        assertFalse(
            key(
                bodyFor(LlmConfig(provider = "nvidia", model = "meta/llama-3.3-70b-instruct", reasoningEffort = "high")),
                "reasoning_effort",
            ),
        )
        assertTrue(
            key(
                bodyFor(LlmConfig(provider = "nvidia", model = "deepseek-ai/deepseek-v3", reasoningEffort = "high")),
                "reasoning_effort",
            ),
        )
    }

    /** auto 与 disabled 都是「显式表示不干预」，不能发出去（DeepSeek 枚举里没有 auto，发了就 400） */
    @Test
    fun autoAndDisabledEffortAreNeverSent() {
        assertFalse(key(bodyFor(LlmConfig(provider = "deepseek", reasoningEffort = "auto")), "reasoning_effort"))
        assertFalse(key(bodyFor(LlmConfig(provider = "deepseek", reasoningEffort = "disabled")), "reasoning_effort"))
    }

    @Test
    fun globalEffortFillsInOnlyWhenConfigLeavesItEmpty() {
        val body = OpenAiCompatibleClient.buildRequestBody(
            listOf(ChatMessage("user", "hi")), null, LlmConfig(provider = "deepseek", reasoningEffort = ""),
            stream = false, globalReasoningEffort = "medium",
        )
        assertEquals("medium", str(body, "reasoning_effort"))

        val neutral = OpenAiCompatibleClient.buildRequestBody(
            listOf(ChatMessage("user", "hi")), null, LlmConfig(provider = "deepseek", reasoningEffort = ""),
            stream = false, globalReasoningEffort = "disabled",
        )
        assertFalse(key(neutral, "reasoning_effort"))
    }

    /** 推理模型不支持 temperature，发了会被拒 */
    @Test
    fun temperatureIsSkippedForReasoningModels() {
        assertFalse(key(bodyFor(LlmConfig(provider = "deepseek", reasoningEffort = "high")), "temperature"))
    }

    @Test
    fun toolRoleAndAssistantToolCallsRoundTrip() {
        val body = bodyFor(
            LlmConfig(),
            messages = listOf(
                ChatMessage("user", "播放周杰伦"),
                ChatMessage(
                    "assistant", "",
                    toolCalls = listOf(ToolCall(id = "call_1", name = "search_song", arguments = """{"q":"周杰伦"}""")),
                ),
                ChatMessage("tool", """{"hits":3}""", toolCallId = "call_1"),
            ),
        )
        val messages = obj(body)["messages"]!!.jsonArray()
        assertEquals(3, messages.size)
        val assistant = messages[1] as JsonObject
        val call = (assistant["tool_calls"]!!.jsonArray()[0] as JsonObject)
        assertEquals("call_1", call["id"]!!.jsonText())
        assertEquals("function", call["type"]!!.jsonText())
        assertEquals("search_song", (call["function"] as JsonObject)["name"]!!.jsonText())
        val toolMsg = messages[2] as JsonObject
        assertEquals("call_1", toolMsg["tool_call_id"]!!.jsonText())
        assertEquals("tool", toolMsg["role"]!!.jsonText())
    }

    @Test
    fun toolDefinitionsSerializeAsJsonSchema() {
        val body = bodyFor(
            LlmConfig(),
            tools = listOf(
                ToolDefinition(
                    name = "play_song",
                    description = "播放指定歌曲",
                    properties = mapOf(
                        "title" to ToolParamSchema("string", "歌名"),
                        "mode" to ToolParamSchema("string", enumValues = listOf("random", "order")),
                    ),
                    required = listOf("title"),
                ),
            ),
        )
        val fn = ((obj(body)["tools"]!!.jsonArray()[0]) as JsonObject)["function"] as JsonObject
        assertEquals("play_song", fn["name"]!!.jsonText())
        val params = fn["parameters"] as JsonObject
        assertEquals("object", params["type"]!!.jsonText())
        val props = params["properties"] as JsonObject
        assertEquals("歌名", (props["title"] as JsonObject)["description"]!!.jsonText())
        assertEquals(
            listOf("random", "order"),
            ((props["mode"] as JsonObject)["enum"]!!.jsonArray()).map { it.jsonText() },
        )
        assertEquals(listOf("title"), (params["required"]!!.jsonArray()).map { it.jsonText() })
    }

    // ---- 退回链 ----

    private class RecordingTransport(
        val responses: Map<String, LlmHttpResult> = emptyMap(),
        var default: LlmHttpResult = LlmHttpResult(200, """{"choices":[{"message":{"content":"ok"},"finish_reason":"stop"}]}"""),
    ) : LlmTransport {
        val posted = mutableListOf<String>()
        var onLine: ((String) -> Unit)? = null

        override fun post(url: String, headers: Map<String, String>, body: String, forEachLine: (String) -> Unit): LlmHttpResult {
            posted += url
            onLine = forEachLine
            return responses[url] ?: default
        }

        override fun get(url: String, headers: Map<String, String>): LlmHttpResult =
            responses[url] ?: LlmHttpResult(404, "")
    }

    @Test
    fun missingCredentialsFailFastWithoutTouchingTheNetwork() {
        val transport = RecordingTransport()
        val client = OpenAiCompatibleClient(transport, configProvider = { LlmConfig(apiKey = "") })
        val error = runCatching { client.chat(listOf(ChatMessage("user", "hi"))) }.exceptionOrNull()
        assertTrue(error is OpenAiCompatibleClient.LlmApiException)
        assertTrue(error!!.message!!.contains("未配置"))
        assertTrue(transport.posted.isEmpty())
    }

    @Test
    fun fallbackModelIsTriedAndReportedTogetherWithThePrimary() {
        val transport = RecordingTransport(
            responses = mutableMapOf(
                "https://a.example/v1/chat/completions" to LlmHttpResult(500, "primary boom"),
                "https://b.example/v1/chat/completions" to LlmHttpResult(200, """{"choices":[{"message":{"content":"from fallback"},"finish_reason":"stop"}]}"""),
            ),
        )
        val client = OpenAiCompatibleClient(
            transport,
            configProvider = { LlmConfig(name = "主", apiUrl = "https://a.example/v1", apiKey = "k") },
            fallbackProvider = { listOf(LlmConfig(name = "备", apiUrl = "https://b.example/v1", apiKey = "k", fallbackEnabled = true)) },
        )
        val response = client.chat(listOf(ChatMessage("user", "hi")))
        assertEquals("from fallback", response.content)
        assertEquals(2, transport.posted.size)
    }

    @Test
    fun whenEveryFallbackFailsTheMessageNamesBothTiers() {
        val transport = RecordingTransport(default = LlmHttpResult(503, "down"))
        val client = OpenAiCompatibleClient(
            transport,
            configProvider = { LlmConfig(name = "主", apiUrl = "https://a/v1", apiKey = "k") },
            fallbackProvider = { listOf(LlmConfig(name = "备", apiUrl = "https://b/v1", apiKey = "k", fallbackEnabled = true)) },
        )
        val error = runCatching { client.chat(listOf(ChatMessage("user", "hi"))) }.exceptionOrNull()
        assertTrue(error!!.message!!.contains("主模型"))
        assertTrue(error.message!!.contains("1 个备用模型"))
    }

    @Test
    fun nonStreamingErrorBodyIsTruncatedIntoTheMessage() {
        val transport = RecordingTransport(
            responses = mapOf("https://a/v1/chat/completions" to LlmHttpResult(400, "x".repeat(900))),
        )
        val client = OpenAiCompatibleClient(transport, configProvider = { LlmConfig(apiUrl = "https://a/v1", apiKey = "k") })
        val error = runCatching { client.chat(listOf(ChatMessage("user", "hi"))) }.exceptionOrNull()
        assertTrue(error!!.message!!.contains("(400)"))
        assertTrue(error.message!!.length < 600)
    }

    // ---- 小助手 ----

    private fun kotlinx.serialization.json.JsonElement.jsonText(): String = (this as JsonPrimitive).content
    private fun kotlinx.serialization.json.JsonElement.jsonArray() = (this as kotlinx.serialization.json.JsonArray).toList()
}
