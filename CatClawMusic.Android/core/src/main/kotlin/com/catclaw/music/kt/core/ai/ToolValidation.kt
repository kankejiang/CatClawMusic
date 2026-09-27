package com.catclaw.music.kt.core.ai

import kotlinx.serialization.json.Json
import kotlinx.serialization.json.JsonElement
import kotlinx.serialization.json.JsonNull
import kotlinx.serialization.json.JsonObject
import kotlinx.serialization.json.JsonPrimitive
import kotlinx.serialization.json.buildJsonObject

/**
 * 必填参数与类型校验，对齐 C# `AgentToolDispatch.ValidateArguments`（:27-87）。
 * 通过返回 null，否则返回**给模型看**的错误 JSON。
 *
 * 错误体里一定带 `expected`：模型收到"参数无效"但没有可照抄的 schema 时，
 * 通常只是换个写法再错一次，白烧一轮；把该工具要哪些参数回给它才纠得回来。
 */
private val VALIDATION_JSON = Json { ignoreUnknownKeys = true }

fun validateArguments(tool: AgentTool, arguments: String): String? {
    if (arguments.isBlank()) {
        return errorJson(tool, "工具 ${tool.name} 缺少参数。请提供符合 ${tool.name} 参数定义的完整 JSON 参数后重新调用")
    }

    val args = runCatching { VALIDATION_JSON.parseToJsonElement(arguments) as? JsonObject }.getOrNull()
        ?: return errorJson(tool, "工具 ${tool.name} 的参数不是合法 JSON，请重写参数使其为 JSON 对象")

    val missing = mutableListOf<String>()
    val wrongType = mutableListOf<String>()
    val properties = tool.definition().properties

    for (name in tool.required) {
        val value = args[name]
        if (value == null || value is JsonNull) {
            missing += name
            continue
        }
        when (properties[name]?.type) {
            "string" -> if (value !is JsonPrimitive || !value.isString) wrongType += "$name(应为 string)"
            "number", "integer" ->
                if (value !is JsonPrimitive || value.isString || (value.content.toDoubleOrNull() == null)) {
                    wrongType += "$name(应为 number)"
                }
            "boolean" -> if (value !is JsonPrimitive || value.content != "true" && value.content != "false") {
                wrongType += "$name(应为 boolean)"
            }
        }
    }

    if (missing.isEmpty() && wrongType.isEmpty()) return null
    val detail = "缺少必填参数 [" + missing.joinToString(", ") + "]" +
        if (wrongType.isEmpty()) "" else "，类型不符 [" + wrongType.joinToString(", ") + "]"
    return errorJson(tool, "工具 ${tool.name} 参数无效：$detail。请按工具定义重写参数后重新调用")
}

/**
 * C# 只判 `prop.Type == "number"`，而本端多数工具声明的是 `"integer"`（与 C# 各工具的
 * `Type = "integer"` 一致），那些参数在 C# 里其实**没被类型校验**，字符串 "12" 会一路走到
 * `GetInt32()` 才炸。上面那个 when 把 integer 一起判掉 —— 有意比 C# 严，
 * 让模型在分派前就拿到可纠正的错误，而不是工具抛异常后的模糊报错。
 */
private fun errorJson(tool: AgentTool, error: String): String {
    val required = tool.required
    val expected = if (required.isEmpty()) {
        "${tool.name} 不需要参数（空对象 {}）"
    } else {
        "${tool.name} 必填参数: " + required.joinToString(", ")
    }
    val body = buildJsonObject {
        put("error", JsonPrimitive(error))
        put("expected", JsonPrimitive(expected))
    }
    return VALIDATION_JSON.encodeToString(JsonElement.serializer(), body)
}
