package com.catclaw.music.kt.core.ai

import com.catclaw.music.kt.core.model.Song
import kotlinx.coroutines.CancellationException
import kotlinx.coroutines.test.runTest
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Test

/**
 * 聊天会话状态机：倒序列表、分页锚点、流式增量、中断保留半句。
 * 全部用函数缝注入，不碰 Room 也不碰网络。
 */
class ChatSessionTest {

    private class Harness(
        private var reply: ChatMessage = ChatMessage(role = "assistant", content = "好的喵"),
        private val configured: Boolean = true,
        private val recent: List<StoredMessage> = emptyList(),
        private val older: List<StoredMessage> = emptyList(),
        private val local: suspend (String) -> ChatMessage? = { null },
        private val stream: List<ChatMessage> = emptyList(),
        private val failure: (suspend () -> ChatMessage)? = null,
    ) {
        val persisted = mutableListOf<Pair<String, String>>()
        var agentCalls = 0

        val session = ChatSession(
            agentSend = { _, onPartial ->
                agentCalls++
                stream.forEach { onPartial(it) }
                failure?.invoke() ?: reply
            },
            localCommand = local,
            isConfigured = { configured },
            persist = { role, content -> persisted += role to content },
            loadRecent = { recent },
            loadOlder = { _, _ -> older },
        )
    }

    private fun song(id: Int) = Song(id = id, title = "歌$id", artist = "A", filePath = "/m/$id.mp3")

    @Test
    fun firstOpenGreetsAccordingToConfigState() = runTest {
        val configured = Harness().session
        configured.open()
        assertEquals(1, configured.messages.value.size)
        assertTrue(configured.messages.value[0].content.contains("可以帮你找歌"))

        val bare = Harness(configured = false).session
        bare.open()
        assertTrue(bare.messages.value[0].content.contains("还没配置"))
    }

    @Test
    fun historyIsNewestFirstAndAnchorsOldestId() = runTest {
        val h = Harness(
            recent = listOf(
                StoredMessage(1, "user", "最早"),
                StoredMessage(2, "assistant", "中间"),
                StoredMessage(3, "user", "最新"),
            ),
        )
        h.session.open()
        assertEquals(listOf(3, 2, 1), h.session.messages.value.map { it.id })
        assertEquals("最新", h.session.messages.value[0].content)
        assertEquals(1, h.session.oldestLoadedId)
        assertFalse("短页说明已经到头", h.session.hasMoreHistory)
    }

    @Test
    fun loadMoreAppendsOlderToTheTailAndMovesTheAnchor() = runTest {
        // 首屏必须**取满一页（30）**，否则按"短页即到底"的规则根本不会再发起加载更多 —— 这条规则本身也是被测对象
        val h = Harness(
            recent = (11..40).map { StoredMessage(it, "user", "m$it") },
            older = listOf(StoredMessage(5, "user", "m5"), StoredMessage(6, "user", "m6")),
        )
        h.session.open()
        assertTrue(h.session.hasMoreHistory)
        h.session.loadMore()
        val ids = h.session.messages.value.map { it.id }
        assertEquals(listOf(40, 39), ids.take(2))
        assertEquals(listOf(11, 6, 5), ids.takeLast(3))
        assertEquals(5, h.session.oldestLoadedId)
        assertFalse("不满一页 → 到底了", h.session.hasMoreHistory)
    }

    @Test
    fun streamingDeltasFillBubbleAndToolStepsStackInOrder() = runTest {
        val h = Harness(
            reply = ChatMessage(role = "assistant", content = "你好"),
            stream = listOf(
                ChatMessage(role = "assistant", content = "你"),
                ChatMessage(role = "assistant", content = "好"),
                ChatMessage(
                    role = "assistant",
                    toolCalls = listOf(ToolCall(id = "c1", name = "search_music")),
                ),
                ChatMessage(role = "tool", content = "{}", toolCallId = "c1"),
            ),
        )
        h.session.open()
        h.session.send("找点歌")

        val top = h.session.messages.value[0]
        assertEquals("assistant", top.role)
        assertEquals("你好", top.content)
        assertEquals(listOf("🔧 调用工具: search_music", "✅ search_music 完成"), top.steps)
        assertFalse(top.thinking)
        assertEquals(
            listOf("user" to "找点歌", "assistant" to "你好"),
            h.persisted,
        )
        assertFalse(h.session.thinking.value)
    }

    @Test
    fun blankInputSendsNothing() = runTest {
        val h = Harness()
        h.session.send("   ")
        assertEquals(0, h.agentCalls)
        assertTrue(h.persisted.isEmpty())
    }

    @Test
    fun unconfiguredPathPrefersLocalCommandAndFallsBackToHint() = runTest {
        val local = Harness(
            configured = false,
            local = { ChatMessage(role = "assistant", content = "已暂停播放", songs = listOf(song(3))) },
        )
        local.session.open()
        local.session.send("暂停")
        val bubble = local.session.messages.value[0]
        assertEquals("已暂停播放", bubble.content)
        assertEquals(listOf(3), bubble.songs.map { it.id })
        assertEquals(0, local.agentCalls)

        val hint = Harness(configured = false).session
        hint.open()
        hint.send("讲个笑话")
        assertTrue(hint.messages.value[0].content.contains("AI 还没有配置好喵"))
    }

    @Test
    fun stopKeepsTheHalfSentenceAndStillPersistsIt() = runTest {
        val h = Harness(
            stream = listOf(ChatMessage(role = "assistant", content = "说到一半")),
            failure = { throw CancellationException("user stopped") },
        )
        h.session.open()
        h.session.send("长一点")

        val bubble = h.session.messages.value[0]
        assertEquals("说到一半\n\n（已停止回复）", bubble.content)
        assertFalse(bubble.thinking)
        assertEquals("assistant" to "说到一半\n\n（已停止回复）", h.persisted.last())
        assertFalse(h.session.thinking.value)
    }

    @Test
    fun stopBeforeAnyWordSaysSoPlainly() = runTest {
        val h = Harness(failure = { throw CancellationException("user stopped") })
        h.session.open()
        h.session.send("停")
        assertEquals("已停止回复喵。", h.session.messages.value[0].content)
    }

    @Test
    fun modelErrorBecomesBubbleTextInsteadOfAnCrash() = runTest {
        val h = Harness(failure = { throw IllegalStateException("连接被重置") })
        h.session.open()
        h.session.send("在吗")
        assertEquals("出错了喵：连接被重置", h.session.messages.value[0].content)
    }

    @Test
    fun emptyReplyFallsBackToSongCountOrDone() = runTest {
        val withSongs = Harness(reply = ChatMessage(role = "assistant", songs = listOf(song(1), song(2))))
        withSongs.session.open()
        withSongs.session.send("来点歌")
        assertEquals("帮你找到 2 首相关歌曲喵。", withSongs.session.messages.value[0].content)

        val empty = Harness(reply = ChatMessage(role = "assistant"))
        empty.session.open()
        empty.session.send("在吗")
        assertEquals("处理完成喵。", empty.session.messages.value[0].content)
    }
}
