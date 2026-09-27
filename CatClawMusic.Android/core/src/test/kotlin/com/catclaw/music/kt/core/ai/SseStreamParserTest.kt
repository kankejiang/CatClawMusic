package com.catclaw.music.kt.core.ai

import org.junit.Assert.assertEquals
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Test

/**
 * SSE 分片聚合。这里覆盖的都是各家网关真实会发的形状，
 * 少一条都对应 C# 注释里点过的坑（`tool_calls: null`、只有首片带 name、reasoning 字段名不统一）。
 */
class SseStreamParserTest {

    private fun delta(chunk: String) = """data: {"choices":[{"index":0,"delta":$chunk,"finish_reason":null}]}"""

    @Test
    fun plainTextAccumulatesAndEmitsPerLineIncrements() {
        val parser = SseStreamParser()
        assertEquals("你", parser.feedLine(delta("""{"content":"你"}"""))?.content)
        assertEquals("好", parser.feedLine(delta("""{"content":"好"}"""))?.content)
        assertEquals("你好", parser.result().content)
    }

    /** 空行、心跳注释、非 data: 行都不该产生增量，也不该打断累加 */
    @Test
    fun blankAndCommentLinesAreIgnored() {
        val parser = SseStreamParser()
        assertNull(parser.feedLine(""))
        assertNull(parser.feedLine(": ping"))
        assertNull(parser.feedLine("event: message"))
        parser.feedLine(delta("""{"content":"a"}"""))
        assertNull(parser.feedLine("data:"))
        assertNull(parser.feedLine("data: not-json"))
        parser.feedLine(delta("""{"content":"b"}"""))
        assertEquals("ab", parser.result().content)
    }

    @Test
    fun reasoningContentAndTheFallbackFieldBothLandInReasoning() {
        val a = SseStreamParser()
        a.feedLine(delta("""{"reasoning_content":"先想","content":"再说"}"""))
        assertEquals("先想", a.result().reasoningContent)
        assertEquals("再说", a.result().content)

        val b = SseStreamParser()
        b.feedLine(delta("""{"reasoning":"另一种字段"}"""))
        assertEquals("另一种字段", b.result().reasoningContent)

        // 两个字段同时存在时按 C# 的顺序取 reasoning_content
        val c = SseStreamParser()
        c.feedLine(delta("""{"reasoning":"ignored","reasoning_content":"taken"}"""))
        assertEquals("taken", c.result().reasoningContent)
    }

    /** 有些服务商在 delta 里显式给 "tool_calls": null，直接遍历会抛类型异常 */
    @Test
    fun explicitNullToolCallsDoNotBreakTheStream() {
        val parser = SseStreamParser()
        parser.feedLine(delta("""{"content":"x","tool_calls":null}"""))
        parser.feedLine(delta("""{"content":"y"}"""))
        assertEquals("xy", parser.result().content)
        assertTrue(parser.result().toolCalls.isEmpty())
    }

    @Test
    fun fragmentedToolCallsMergeByIdNameAndArgumentPieces() {
        val parser = SseStreamParser()
        parser.feedLine(
            delta("""{"tool_calls":[{"index":0,"id":"call_9","type":"function","function":{"name":"play_song","arguments":""}}]}"""),
        )
        parser.feedLine(delta("""{"tool_calls":[{"index":0,"function":{"arguments":"{\"title\":\""}}]}"""))
        // 后续片不再带 name，也不该把已知名字盖成空
        parser.feedLine(delta("""{"tool_calls":[{"index":0,"function":{"name":"","arguments":"夜曲\"}"}}]}"""))
        parser.feedLine(
            delta("""{"tool_calls":[{"index":1,"id":"call_2","function":{"name":"set_mode","arguments":"{}"}}]}"""),
        )

        val result = parser.result()
        assertEquals(2, result.toolCalls.size)
        assertEquals("call_9", result.toolCalls[0].id)
        assertEquals("play_song", result.toolCalls[0].name)
        assertEquals("""{"title":"夜曲"}""", result.toolCalls[0].arguments)
        assertEquals("set_mode", result.toolCalls[1].name)
        assertEquals("{}", result.toolCalls[1].arguments)
    }

    /** 分片按到达顺序记录，最终结果要按 index 升序，否则多工具调用的 id 会与参数错位 */
    @Test
    fun outOfOrderIndexesAreSortedInResult() {
        val parser = SseStreamParser()
        parser.feedLine(delta("""{"tool_calls":[{"index":1,"id":"b","function":{"name":"second","arguments":"{}"}}]}"""))
        parser.feedLine(delta("""{"tool_calls":[{"index":0,"id":"a","function":{"name":"first","arguments":"{}"}}]}"""))
        assertEquals(listOf("first", "second"), parser.result().toolCalls.map { it.name })
        assertEquals(listOf("a", "b"), parser.result().toolCalls.map { it.id })
    }

    @Test
    fun finishReasonIsCapturedFromTheLastChunk() {
        val parser = SseStreamParser()
        parser.feedLine(delta("""{"content":"hi"}"""))
        parser.feedLine("""data: {"choices":[{"index":0,"delta":{},"finish_reason":"tool_calls"}]}""")
        assertEquals("tool_calls", parser.result().finishReason)
    }

    @Test
    fun doneSentinelStopsFurtherLines() {
        val parser = SseStreamParser()
        parser.feedLine(delta("""{"content":"keep"}"""))
        assertNull(parser.feedLine("data: [DONE]"))
        parser.feedLine(delta("""{"content":"dropped"}"""))
        assertEquals("keep", parser.result().content)
    }

    /**  choices 缺失/为空/不是数组：整块跳过，不能抛 */
    @Test
    fun malformedChoiceShapesAreSkipped() {
        val parser = SseStreamParser()
        assertNull(parser.feedLine("""data: {"choices":[]}"""))
        assertNull(parser.feedLine("""data: {"id":"chatcmpl-1"}"""))
        assertNull(parser.feedLine("""data: {"choices":"nope"}"""))
        assertNull(parser.feedLine("""data: []"""))
        parser.feedLine(delta("""{"content":"ok"}"""))
        assertEquals("ok", parser.result().content)
    }

    @Test
    fun emptyResultHasNoContentNoCallsAndBlankFinish() {
        val r = SseStreamParser().result()
        assertEquals("", r.content)
        assertEquals("", r.reasoningContent)
        assertEquals("", r.finishReason)
        assertTrue(r.toolCalls.isEmpty())
    }
}
