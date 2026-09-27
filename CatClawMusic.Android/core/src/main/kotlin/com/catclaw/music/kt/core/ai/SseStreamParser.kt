package com.catclaw.music.kt.core.ai

import kotlinx.serialization.json.Json
import kotlinx.serialization.json.JsonArray
import kotlinx.serialization.json.JsonObject
import kotlinx.serialization.json.JsonPrimitive
import kotlinx.serialization.json.intOrNull
import kotlinx.serialization.json.jsonArray
import kotlinx.serialization.json.jsonObject
import kotlinx.serialization.json.jsonPrimitive

/**
 * OpenAI 兼容 SSE 流的累加解析器，逐条对齐 C# `OpenAiCompatibleLlmClient.ReadStreamAsync`。
 *
 * 每次请求新建一个实例（不能共享，否则并发请求的 tool_calls 分片会互相串）。
 *
 * 三处必须记住的容错，都是 C# 注释里点名的坑：
 * ① 推理模型的思考过程字段各家不同：`reasoning_content`（DeepSeek/智谱/Kimi）优先，回落 `reasoning`；
 * ② 有些服务商会在 delta 里显式给 `"tool_calls": null`，必须先确认是数组再遍历，否则类型异常；
 * ③ 解析不了的块直接忽略（服务端会夹杂注释行与空块），不能让一坏块把整个回答打掉。
 */
class SseStreamParser {

    private val content = StringBuilder()
    private val reasoning = StringBuilder()
    private var finishReason: String = ""
    private var done = false

    /** index → (id, name, arguments 累加器)。工具调用按 index 分片，最后按 index 升序合并 */
    private val slots = LinkedHashMap<Int, ToolSlot>()

    private class ToolSlot(var id: String = "", var name: String = "", val args: StringBuilder = StringBuilder())

    /**
     * 喂进一行 SSE 文本。
     * @return 本行产生的增量（正文/思考/结束原因），无增量时返回 null
     */
    fun feedLine(line: String): LlmStreamDelta? {
        if (done) return null
        if (line.isBlank() || !line.startsWith("data:")) return null

        val data = line.substring("data:".length).trim()
        if (data == "[DONE]") {
            done = true
            return null
        }

        val root = runCatching { JSON.parseToJsonElement(data).jsonObject }.getOrNull() ?: return null
        val choices = root["choices"] as? JsonArray ?: return null
        if (choices.isEmpty()) return null
        val choice = (choices[0] as? JsonObject) ?: return null

        (choice["finish_reason"] as? JsonPrimitive)
            ?.takeIf { it.isStringNotSentinel() }
            ?.let { finishReason = it.content }

        val delta = choice["delta"] as? JsonObject ?: return null

        val text = delta.stringOrNull("content")
        val think = delta.stringOrNull("reasoning_content") ?: delta.stringOrNull("reasoning")
        if (!text.isNullOrEmpty()) content.append(text)
        if (!think.isNullOrEmpty()) reasoning.append(think)

        (delta["tool_calls"] as? JsonArray)?.forEach { element ->
            val tc = element as? JsonObject ?: return@forEach
            val index = (tc["index"] as? JsonPrimitive)?.intOrNull ?: 0
            val slot = slots.getOrPut(index) { ToolSlot() }
            (tc["id"] as? JsonPrimitive)?.takeIf { it.isStringNotSentinel() }?.let { slot.id = it.content }
            (tc["function"] as? JsonObject)?.let { fn ->
                val name = fn.stringOrNull("name")
                // 只有第一片带 name，后续片是空串或缺省 —— 别用空串把已知的名字盖掉
                if (!name.isNullOrEmpty() && slot.name.isEmpty()) slot.name = name
                (fn["arguments"] as? JsonPrimitive)?.takeIf { it.isStringNotSentinel() }?.let { slot.args.append(it.content) }
            }
        }

        return LlmStreamDelta(content = text.orEmpty(), reasoningContent = think.orEmpty(), finishReason = finishReason)
    }

    fun result(): LlmResponse = LlmResponse(
        content = content.toString(),
        reasoningContent = reasoning.toString(),
        toolCalls = slots.entries.sortedBy { it.key }
            .map { (_, slot) -> ToolCall(id = slot.id, type = "function", name = slot.name, arguments = slot.args.toString()) },
        finishReason = finishReason,
    )

    private companion object {
        val JSON = Json { ignoreUnknownKeys = true }

        fun JsonObject.stringOrNull(key: String): String? =
            (this[key] as? JsonPrimitive)?.takeIf { it.isStringNotSentinel() }?.content

        /**
         * JsonNull 在 kotlinx 里也是 JsonPrimitive，直接读 content 会拿到字面 "null"。
         * isString 只有在源文本确实是带引号的字符串时为 true，正好把 null/数字/布尔都挡掉。
         */
        fun JsonPrimitive.isStringNotSentinel(): Boolean = isString
    }
}
