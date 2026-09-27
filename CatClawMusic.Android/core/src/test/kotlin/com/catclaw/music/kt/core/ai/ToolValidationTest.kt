package com.catclaw.music.kt.core.ai

import kotlinx.coroutines.test.runTest
import org.junit.Assert.assertEquals
import org.junit.Assert.assertNotNull
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Test

/** 工具分派前的必填/类型校验，对齐 C# `AgentToolDispatch.ValidateArguments` */
class ToolValidationTest {

    private class Tool(
        override val name: String = "search_music",
        override val required: List<String> = listOf("keyword"),
        override val properties: Map<String, ToolParamSchema> =
            mapOf("keyword" to ToolParamSchema("string", "关键词")),
    ) : AgentTool {
        override val description = "t"
        override val isReadOnly = true
        var calls = 0

        override suspend fun execute(argumentsJson: String): String {
            calls++
            return """{"success":true}"""
        }
    }

    private val intTool = Tool(
        name = "play_song",
        required = listOf("song_id"),
        properties = mapOf("song_id" to ToolParamSchema("integer", "歌曲 ID")),
    )

    @Test
    fun validArgumentsPassThrough() {
        assertNull(validateArguments(Tool(), """{"keyword":"周杰伦"}"""))
    }

    @Test
    fun blankArgumentsAreRejectedBeforeTheToolSeesThem() {
        val error = validateArguments(Tool(), "   ")
        assertNotNull(error)
        assertTrue(error!!.contains("缺少参数"))
        assertTrue(error.contains("search_music 必填参数: keyword"))
    }

    @Test
    fun malformedAndNonObjectArgumentsGetDistinctMessages() {
        assertTrue(validateArguments(Tool(), "{keyword:周杰伦")!!.contains("不是合法 JSON"))
        assertTrue(validateArguments(Tool(), """["周杰伦"]""")!!.contains("不是合法 JSON"))
    }

    @Test
    fun missingAndNullValuesBothCountAsMissing() {
        val missing = validateArguments(Tool(), """{"other":1}""")!!
        assertTrue(missing.contains("缺少必填参数 [keyword]"))
        assertTrue(validateArguments(Tool(), """{"keyword":null}""")!!.contains("缺少必填参数 [keyword]"))
    }

    @Test
    fun wrongTypesAreNamedWithTheirExpectedType() {
        val error = validateArguments(Tool(), """{"keyword":123}""")!!
        assertTrue(error.contains("keyword(应为 string)"))
        assertTrue(error.contains("类型不符"))
    }

    /**
     * 有意比 C# 严：C# 只判 `type == "number"`，`integer` 参数直接放行到 `GetInt32()` 才炸。
     * 这里把带引号的数字在分派前拦下，模型拿到的是可照抄的 schema 而不是工具异常。
     */
    @Test
    fun quotedNumberIsRejectedForIntegerParam() {
        assertTrue(validateArguments(intTool, """{"song_id":"12"}""")!!.contains("song_id(应为 number)"))
        assertNull(validateArguments(intTool, """{"song_id":12}"""))
    }

    @Test
    fun toolWithoutRequiredParamsDescribesItselfAsNeedingNone() {
        val noArgs = Tool(name = "get_current_song", required = emptyList(), properties = emptyMap())
        assertNull(validateArguments(noArgs, "{}"))
        assertTrue(validateArguments(noArgs, "")!!.contains("不需要参数"))
    }

    @Test
    fun agentDoesNotExecuteAToolWithInvalidArguments() = runTest {
        val tool = Tool()
        val script = mutableListOf(
            LlmResponse(toolCalls = listOf(ToolCall(id = "c1", name = "search_music", arguments = "{}")), finishReason = "tool_calls"),
            LlmResponse(content = "好了", finishReason = "stop"),
        )
        val service = AgentService(
            chat = { _, _, _ -> script.removeAt(0) },
            tools = listOf(tool),
            systemPrompt = { "s" },
        )

        val reply = service.sendMessage("找点歌")

        assertEquals("好了", reply.content)
        assertEquals("校验失败就不该真跑工具", 0, tool.calls)
        val fedBackToModel = service.history.last { it.role == "tool" }.content
        assertTrue(fedBackToModel.contains("缺少必填参数 [keyword]"))
    }
}
