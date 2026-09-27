package com.catclaw.music.kt.core.ai

import org.junit.Assert.assertEquals
import org.junit.Assert.assertTrue
import org.junit.Test

/**
 * 分段裁剪与 tool_calls 配对修复。
 * 这些用例的意义在于：按条裁在短对话上看不出问题，跑到工具轮次多了才 400 ——
 * 所以必须有一组测试直接盯住「配对不被拆散」。
 */
class ConversationTest {

    private fun sys(text: String) = ChatMessage("system", text)
    private fun user(text: String) = ChatMessage("user", text)
    private fun bot(text: String) = ChatMessage("assistant", text)
    private fun call(id: String, name: String = "t") = ToolCall(id = id, name = name, arguments = "{}")
    private fun botCalls(id: String, text: String = "") = ChatMessage("assistant", text, toolCalls = listOf(call(id)))
    private fun tool(id: String, text: String = "r") = ChatMessage("tool", text, toolCallId = id)

    @Test
    fun eachSystemMessageIsItsOwnSegment() {
        val msgs = listOf(sys("a"), sys("b"), user("u"))
        assertEquals(listOf(0..0, 1..1, 2..2), Conversation.segmentBounds(msgs))
    }

    @Test
    fun assistantWithToolCallsGluesItsToolMessagesIntoOneSegment() {
        val msgs = listOf(sys("s"), user("u"), botCalls("c1"), tool("c1"), tool("c1"), bot("done"), user("again"))
        val bounds = Conversation.segmentBounds(msgs)
        assertEquals(listOf(0..0, 1..1, 2..4, 5..5, 6..6), bounds)
    }

    /** 不属于自己的 tool 消息不能被并进这一段 */
    @Test
    fun toolMessageWithForeignIdEndsTheSegment() {
        val msgs = listOf(botCalls("c1"), tool("other"), bot("x"))
        val bounds = Conversation.segmentBounds(msgs)
        assertEquals(listOf(0..0, 1..1, 2..2), bounds)
    }

    @Test
    fun multipleToolCallsInOneAssistantFormOneSegment() {
        val msgs = listOf(
            ChatMessage("assistant", "", toolCalls = listOf(call("a"), call("b"))),
            tool("a"),
            tool("b"),
        )
        assertEquals(listOf(0..2), Conversation.segmentBounds(msgs))
    }

    // ---- 配对修复 ----

    /** 孤立的 tool 消息（前面没有带对应 id 的 assistant）必须被删掉 */
    @Test
    fun orphanToolMessageIsDropped() {
        val msgs = mutableListOf(sys("s"), user("u"), tool("ghost"))
        Conversation.sanitizeToolMessagePairs(msgs)
        assertEquals(listOf("system", "user"), msgs.map { it.role })
    }

    @Test
    fun toolFollowedByNonAssistantIsDropped() {
        val msgs = mutableListOf(user("u"), bot("not a call"), tool("c1"))
        Conversation.sanitizeToolMessagePairs(msgs)
        assertEquals(2, msgs.size)
        assertTrue(msgs.none { it.role == "tool" })
    }

    /** assistant 声明了两个调用却只有一个被应答：整组删掉，否则 API 报 insufficient tool messages */
    @Test
    fun incompleteToolResponsesRemoveTheWholeAssistantGroup() {
        val msgs = mutableListOf(
            sys("s"),
            user("u"),
            ChatMessage("assistant", "", toolCalls = listOf(call("a"), call("b"))),
            tool("a"),
        )
        Conversation.sanitizeToolMessagePairs(msgs)
        assertEquals(listOf("system", "user"), msgs.map { it.role })
    }

    @Test
    fun completePairSurvivesSanitizing() {
        val msgs = mutableListOf(sys("s"), user("u"), botCalls("a"), tool("a"), bot("final"))
        Conversation.sanitizeToolMessagePairs(msgs)
        assertEquals(5, msgs.size)
        assertEquals(listOf("system", "user", "assistant", "tool", "assistant"), msgs.map { it.role })
    }

    // ---- 历史裁剪 ----

    @Test
    fun historyBelowTheThresholdIsUntouched() {
        val msgs = MutableList(Conversation.TRIM_THRESHOLD) { i -> if (i % 2 == 0) user("u$i") else bot("b$i") }
        val before = msgs.size
        Conversation.trimConversation(msgs)
        assertEquals(before, msgs.size)
    }

    @Test
    fun trimKeepsEverySystemMessageAndWholeSegmentsOnly() {
        val msgs = mutableListOf(sys("persona"))
        repeat(20) { i ->
            msgs.add(user("u$i"))
            msgs.add(botCalls("c$i"))
            msgs.add(tool("c$i", "r$i"))
        }
        Conversation.trimConversation(msgs)

        assertEquals(1, msgs.count { it.role == "system" })
        assertEquals("persona", msgs.first().content)
        // 每个 assistant(tool_calls) 后面必须紧跟它自己的 tool 应答
        msgs.forEachIndexed { index, m ->
            m.toolCalls?.forEach { tc ->
                val answered = msgs.drop(index + 1).takeWhile { it.role == "tool" }.any { it.toolCallId == tc.id }
                assertTrue("段被拆散：$tc", answered)
            }
        }
        assertTrue(msgs.count { it.role != "system" } <= Conversation.KEEP_SEGMENTS * 3)
    }

    // ---- token 估算：系数来自 C#，改了会让预算判断整体漂移 ----

    @Test
    fun tokenEstimationMatchesTheCsharpFormula() {
        assertEquals(1200, Conversation.estimateTokens(emptyList()))
        // 1 条：6 + 内容长度/2（8 字符 → 4）
        assertEquals(1200 + 6 + 4, Conversation.estimateTokens(listOf(user("abcdefgh"))))
        // 带一个调用：再 +80 + 参数长度/2（4 字符 → 2）
        val withCall = listOf(ChatMessage("assistant", "", toolCalls = listOf(ToolCall(id = "x", arguments = "{}{}"))))
        assertEquals(1200 + 6 + 80 + 2, Conversation.estimateTokens(withCall))
    }

    @Test
    fun requestLevelTrimStopsAtTheBudgetAndNeverTouchesSystem() {
        val filler = "x".repeat(2000)
        val msgs = mutableListOf(sys("s"), user(filler), bot(filler), user(filler), bot(filler), user("last"))
        val request = Conversation.buildRequestMessages(msgs)

        assertTrue(Conversation.estimateTokens(request) <= Conversation.TOKEN_BUDGET || request.size <= Conversation.MIN_MESSAGES)
        assertEquals(1, request.count { it.role == "system" })
        assertEquals("s", request.first().content)
        // 原始历史不被请求级裁剪改动
        assertEquals(6, msgs.size)
    }

    @Test
    fun requestLevelTrimLeavesNoDanglingToolPair() {
        val msgs = mutableListOf(
            sys("s"),
            user("u0"),
            botCalls("c1"),
            tool("c1"),
            user("u1"),
            bot("done"),
        )
        val request = Conversation.buildRequestMessages(msgs)
        val roles = request.map { it.role }
        // 幂等：再裁一次不该改变结构（否则说明配对状态不稳定）
        assertEquals(roles, Conversation.buildRequestMessages(request).map { it.role })
    }
}
