package com.catclaw.music.kt.core.ai

import kotlinx.coroutines.test.runTest
import org.junit.Assert.assertEquals
import org.junit.Assert.assertTrue
import org.junit.Test

/** Agent 主循环：用脚本化的假 LLM 响应驱动，不需要网络也不需要真工具。 */
class AgentServiceTest {

    private class FakeTool(
        override val name: String,
        override val isReadOnly: Boolean = true,
        private val reply: String = """{"ok":true}""",
        private val onStart: (() -> Unit)? = null,
        private val onFinish: (() -> Unit)? = null,
    ) : AgentTool {
        override val description: String = "test tool"
        override val properties: Map<String, ToolParamSchema> = emptyMap()
        override val required: List<String> = emptyList()
        var calls = 0

        override suspend fun execute(argumentsJson: String): String {
            calls++
            onStart?.invoke()
            kotlinx.coroutines.delay(5)
            onFinish?.invoke()
            return reply
        }
    }

    private class Script(vararg val responses: LlmResponse) {
        val requests = mutableListOf<List<ChatMessage>>()
        val deltas = mutableListOf<LlmStreamDelta>()

        fun chat(messages: List<ChatMessage>, tools: List<ToolDefinition>?, onDelta: ((LlmStreamDelta) -> Unit)?): LlmResponse {
            requests += messages
            val response = responses[minOf(requests.lastIndex, responses.lastIndex)]
            if (response.content.isNotEmpty()) onDelta?.invoke(LlmStreamDelta(content = response.content))
            onDelta?.let { deltas += LlmStreamDelta(content = response.content) }
            return response
        }
    }

    private fun text(content: String, reasoning: String = "") =
        LlmResponse(content = content, reasoningContent = reasoning, finishReason = "stop")

    private fun toolCall(name: String, arguments: String = "{}", id: String = "c1") =
        LlmResponse(toolCalls = listOf(ToolCall(id = id, name = name, arguments = arguments)), finishReason = "tool_calls")

    private fun service(script: Script, tools: List<AgentTool>, rounds: Int = 12) = AgentService(
        chat = script::chat,
        tools = tools,
        systemPrompt = { "你是猫爪助手" },
        maxToolRounds = { rounds },
    )

    /** 模型偶发写出大小写变体（Web_Search / PLAY_SONG），查找必须忽略大小写，否则白烧一轮 */
    @Test
    fun toolLookupIgnoresCaseInToolNames() = runTest {
        val script = Script(toolCall("PLAY_MUSIC", id = "c1"), text("ok"))
        val tool = FakeTool("play_music")
        val agent = service(script, listOf(tool))

        agent.sendMessage("go")

        assertEquals(1, tool.calls)
        assertTrue(agent.history.first { it.role == "tool" }.content.contains("ok"))
    }

    @Test
    fun plainAnswerShortCircuitsWithoutCallingAnyTool() = runTest {
        val script = Script(text("你好喵"))
        val tool = FakeTool("search_music")
        val agent = service(script, listOf(tool))

        val reply = agent.sendMessage("在吗")

        assertEquals("你好喵", reply.content)
        assertEquals("assistant", reply.role)
        assertEquals(0, tool.calls)
    }

    @Test
    fun fixedPrefixOrderIsPersonaThenMemoryThenLibrary() = runTest {
        val script = Script(text("ok"))
        val agent = AgentService(
            chat = script::chat,
            tools = emptyList(),
            systemPrompt = { "SYS" },
            personalityKnowledge = { "可爱语气词库" },
            memoryProvider = { "m1\nm2" },
            librarySnapshotProvider = { "616 首" },
        )
        agent.sendMessage("hi")

        val systems = agent.history.filter { it.role == "system" }.map { it.content }
        assertEquals(listOf("SYS", "可爱语气词库", "[记忆]\nm1\nm2", "[音乐库]\n616 首"), systems)
        assertEquals("user", agent.history[4].role)
    }

    @Test
    fun memoryKeepsOnlyTheLastLinesAndLibraryIsClipped() = runTest {
        val script = Script(text("ok"))
        val agent = AgentService(
            chat = script::chat,
            tools = emptyList(),
            systemPrompt = { "SYS" },
            memoryProvider = { (1..40).joinToString("\n") { "line$it" } },
            librarySnapshotProvider = { "L".repeat(900) },
        )
        agent.sendMessage("hi")
        val memory = agent.history.first { it.content.startsWith("[记忆]") }.content
        assertTrue("应只留最后 15 行", memory.contains("line40") && !memory.contains("line25"))
        val library = agent.history.first { it.content.startsWith("[音乐库]") }.content
        assertTrue(library.length <= "[音乐库]\n".length + 600 + 2)
        assertTrue(library.endsWith(".."))
    }

    @Test
    fun toolRoundAppendsAssistantThenToolMessageInOrder() = runTest {
        val script = Script(toolCall("search_music", """{"keyword":"夜曲"}""", id = "call_7"), text("找到了"))
        val tool = FakeTool("search_music", reply = """{"hits":2}""")
        val agent = service(script, listOf(tool))

        val reply = agent.sendMessage("找夜曲")

        assertEquals("找到了", reply.content)
        assertEquals(1, tool.calls)
        val roles = agent.history.map { it.role }
        assertEquals(listOf("system", "user", "assistant", "tool", "assistant"), roles)
        val assistantWithCalls = agent.history[2]
        assertEquals("call_7", assistantWithCalls.toolCalls!!.single().id)
        assertEquals("call_7", agent.history[3].toolCallId)
        assertEquals("""{"hits":2}""", agent.history[3].content)
    }

    /** 同一工具 + 完全相同参数连续 3 次就该中断，而不是把 12 轮烧光再报「步骤过多」 */
    @Test
    fun identicalToolCallThreeTimesInARowBreaksTheLoop() = runTest {
        val script = Script(toolCall("fetch_page", """{"url":"https://x"}""", id = "c"))
        val tool = FakeTool("fetch_page", reply = """{"error":"反爬"}""")
        val agent = service(script, listOf(tool), rounds = 12)

        val reply = agent.sendMessage("抓一下")

        // 第 3 轮在分派工具「之前」就中断（C# 同序：先判重复再执行），所以只真跑了 2 次
        assertEquals(2, tool.calls)
        assertTrue(reply.content.contains("连续多次没有返回有效结果"))
        assertTrue(reply.content.contains("fetch_page"))
    }

    /** 参数变了就不算重复 —— 这是「有效重试」，不能误杀 */
    @Test
    fun changingArgumentsResetsTheRepeatCounter() = runTest {
        val responses = listOf(
            toolCall("fetch_page", """{"url":"https://a"}""", id = "c1"),
            toolCall("fetch_page", """{"url":"https://b"}""", id = "c2"),
            toolCall("fetch_page", """{"url":"https://c"}""", id = "c3"),
            text("好了"),
        )
        val script = Script(*responses.toTypedArray())
        val tool = FakeTool("fetch_page")
        val agent = service(script, listOf(tool), rounds = 12)

        assertEquals("好了", agent.sendMessage("抓").content)
        assertEquals(3, tool.calls)
    }

    @Test
    fun exhaustingRoundsReportsTooManySteps() = runTest {
        var n = 0
        val agent = AgentService(
            chat = { _, _, _ ->
                n++
                toolCall("probe", """{"n":$n}""", id = "c$n")
            },
            tools = listOf(FakeTool("probe")),
            systemPrompt = { "s" },
            maxToolRounds = { 4 },
        )
        val reply = agent.sendMessage("go")
        assertTrue(reply.content.contains("操作步骤过多"))
    }

    @Test
    fun zeroMeansUnlimitedRounds() = runTest {
        var n = 0
        val agent = AgentService(
            chat = { _, _, _ ->
                n++
                if (n < 30) toolCall("probe", """{"n":$n}""", id = "c$n") else LlmResponse(content = "done", finishReason = "stop")
            },
            tools = listOf(FakeTool("probe")),
            systemPrompt = { "s" },
            maxToolRounds = { 0 },
        )
        assertEquals("done", agent.sendMessage("go").content)
        assertEquals(30, n)
    }

    @Test
    fun unknownToolGetsTheAvailableListBackAsItsResult() = runTest {
        val script = Script(toolCall("no_such", id = "cX"), text("换个办法"))
        val agent = service(script, listOf(FakeTool("play_song")))

        agent.sendMessage("go")

        val toolMsg = agent.history.first { it.role == "tool" }
        assertTrue(toolMsg.content.contains("未知工具: no_such"))
        assertTrue(toolMsg.content.contains("play_song"))
    }

    @Test
    fun toolExceptionBecomesAnErrorResultInsteadOfKillingTheRun() = runTest {
        val boom = object : AgentTool {
            override val name = "explode"
            override val description = "x"
            override val properties = emptyMap<String, ToolParamSchema>()
            override val required = emptyList<String>()
            override val isReadOnly = true
            override suspend fun execute(argumentsJson: String): String = throw IllegalStateException("炸了")
        }
        val script = Script(toolCall("explode", id = "c"), text("恢复了"))
        val agent = service(script, listOf(boom))

        assertEquals("恢复了", agent.sendMessage("go").content)
        val toolMsg = agent.history.first { it.role == "tool" }
        assertTrue(toolMsg.content.contains("工具执行失败"))
        assertTrue(toolMsg.content.contains("炸了"))
    }

    /** 只读工具并行跑，写/控制类必须串行 —— 这里验「非只读的不与其他并发」 */
    @Test
    fun readOnlyToolsRunConcurrentlyButWriteToolsStaySequential() = runTest {
        var concurrent = 0
        var maxConcurrent = 0
        var writeSawOverlap = false

        fun tracker() = object : AgentTool {
            override val name = "ro"
            override val description = "x"
            override val properties = emptyMap<String, ToolParamSchema>()
            override val required = emptyList<String>()
            override val isReadOnly = true
            override suspend fun execute(argumentsJson: String): String {
                concurrent++
                maxConcurrent = maxOf(maxConcurrent, concurrent)
                kotlinx.coroutines.delay(20)
                concurrent--
                return "{}"
            }
        }

        val write = object : AgentTool {
            override val name = "wr"
            override val description = "x"
            override val properties = emptyMap<String, ToolParamSchema>()
            override val required = emptyList<String>()
            override val isReadOnly = false
            override suspend fun execute(argumentsJson: String): String {
                writeSawOverlap = concurrent > 0
                return "{}"
            }
        }

        val responses = listOf(
            LlmResponse(
                toolCalls = listOf(
                    ToolCall(id = "a", name = "ro", arguments = "{}"),
                    ToolCall(id = "b", name = "ro", arguments = "{}"),
                    ToolCall(id = "w", name = "wr", arguments = "{}"),
                ),
                finishReason = "tool_calls",
            ),
            text("done"),
        )
        val script = Script(*responses.toTypedArray())
        val agent = AgentService(
            chat = script::chat,
            tools = listOf(tracker().let { object : AgentTool by it { override val name = "ro" } }, write),
            systemPrompt = { "s" },
        )
        agent.sendMessage("go")

        assertTrue("只读工具应并行", maxConcurrent >= 2)
        assertTrue("写工具执行时不该还有只读工具在跑", !writeSawOverlap)
    }

    @Test
    fun llmFailureReturnsAnApologyAndKeepsItInHistory() = runTest {
        val agent = AgentService(
            chat = { _, _, _ -> throw OpenAiCompatibleClient.LlmApiException("网关 503") },
            tools = emptyList(),
            systemPrompt = { "s" },
        )
        val reply = agent.sendMessage("hi")
        assertTrue(reply.content.contains("AI 服务请求失败"))
        assertTrue(reply.content.contains("网关 503"))
        assertEquals(reply, agent.history.last())
    }

    @Test
    fun reasoningFromEveryRoundIsAccumulatedOntoTheFinalReply() = runTest {
        val script = Script(
            LlmResponse(content = "", reasoningContent = "先查库", toolCalls = listOf(ToolCall(id = "c", name = "probe", arguments = "{}")), finishReason = "tool_calls"),
            text("答案", reasoning = "再回答"),
        )
        val agent = service(script, listOf(FakeTool("probe")))
        val reply = agent.sendMessage("q")

        assertEquals("先查库\n再回答", reply.reasoningContent)
    }

    @Test
    fun partialCallbacksCoverTextToolCallAndToolResult() = runTest {
        val script = Script(toolCall("search_music", id = "c1"), text("完成"))
        val agent = service(script, listOf(FakeTool("search_music")))
        val seen = mutableListOf<String>()
        agent.sendMessage("go") { msg -> seen += "${msg.role}:${msg.content.take(1)}" }

        assertTrue(seen.contains("assistant:完"))
        assertTrue(seen.any { it.startsWith("tool:") })
        assertTrue(seen.any { it.startsWith("assistant:") })
    }

    @Test
    fun clearConversationForcesThePrefixToBeRebuilt() = runTest {
        val script = Script(text("a"), text("b"))
        val agent = service(script, emptyList())
        agent.sendMessage("one")
        // 人格/记忆/音乐库三个 provider 都返回空，所以固定前缀只有系统提示这一条
        assertEquals(1, agent.history.count { it.role == "system" })

        agent.clearConversation()
        assertEquals(0, agent.history.size)

        agent.sendMessage("two")
        assertEquals(1, agent.history.count { it.role == "system" })
    }

    @Test
    fun longToolResultsAreTruncatedWithAMarker() = runTest {
        val huge = (1..900).joinToString("\n") { "line $it" }
        val script = Script(toolCall("big", id = "c"), text("ok"))
        val agent = service(script, listOf(FakeTool("big", reply = huge)))
        agent.sendMessage("go")

        val toolMsg = agent.history.first { it.role == "tool" }
        assertTrue(toolMsg.content.endsWith("[结果已截断]"))
        assertTrue(toolMsg.content.lines().size <= Conversation.KEEP_SEGMENTS * 100)
    }
}
