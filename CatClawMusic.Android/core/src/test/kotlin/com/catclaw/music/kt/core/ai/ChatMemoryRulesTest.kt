package com.catclaw.music.kt.core.ai

import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Test

/**
 * AI 长期记忆的规则。
 *
 * 这层之所以值得单独测：`AgentService` 只是把 `memoryProvider()` 的返回值原样塞进一条
 * system 消息，所以这里排序/裁剪/格式错一位，模型看到的就是另一份记忆 —— 而界面上什么都看不出来。
 */
class ChatMemoryRulesTest {

    private fun item(id: String, content: String = "喜欢周杰伦", importance: Int = 5, category: String = "preference", at: Long = 1000L) =
        ChatMemoryRules.Item(id = id, category = category, content = content, importance = importance, createdAt = at)

    @Test
    fun analysisThresholdsMatchTheConversationChunk() {
        assertFalse(ChatMemoryRules.shouldAnalyze(19))
        assertTrue("攒够 2 个 chunk 才提炼一次", ChatMemoryRules.shouldAnalyze(20))
        assertFalse("少于 4 句不值得问模型", ChatMemoryRules.worthAnalyzing(3))
        assertTrue(ChatMemoryRules.worthAnalyzing(4))
    }

    @Test
    fun mergeAppendsNewItemAndUpdatesSameIdInPlace() {
        val existing = listOf(item("a", "喜欢民谣", importance = 6))
        val merged = ChatMemoryRules.merge(
            existing = existing,
            extracted = listOf(item("a", "喜欢民谣和指弹", importance = 9), item("b", "晚上常听", at = 2000L)),
            now = 5000L,
        )
        assertEquals("同 id 是更新而不是新增一条", 2, merged.size)
        val a = merged.first { it.id == "a" }
        assertEquals("喜欢民谣和指弹", a.content)
        assertEquals(9, a.importance)
        assertEquals("原创建时间要留住", 1000L, a.createdAt)
        assertEquals(5000L, a.updatedAt)
        val b = merged.first { it.id == "b" }
        assertEquals("新条目用本次时间当创建时间", 5000L, b.createdAt)
        assertNull(b.updatedAt)
    }

    @Test
    fun mergeDropsBlankContentAndClampsImportance() {
        val merged = ChatMemoryRules.merge(
            existing = emptyList(),
            extracted = listOf(item("x", "   "), item("y", "importance 越界", importance = 99), item("z", "负数", importance = -3)),
            now = 1L,
        )
        assertEquals("空内容不占名额", listOf("y", "z"), merged.map { it.id })
        assertEquals(10, merged.first { it.id == "y" }.importance)
        assertEquals(1, merged.first { it.id == "z" }.importance)
    }

    @Test
    fun mergeKeepsTotalUnderCapByDroppingLeastImportant() {
        val existing = (1..60).map { item("id$it", "内容$it", importance = it % 5, at = it.toLong()) }
        val merged = ChatMemoryRules.merge(existing, extracted = listOf(item("new", "最重要", importance = 10)), now = 9L)
        assertEquals(ChatMemoryRules.MAX_ITEMS, merged.size)
        assertEquals("新加的这条重要性最高，必须活着", "new", merged.first().id)
        val kept = merged.map { it.id }.toSet()
        val dropped = (1..60).map { "id$it" }.filter { it !in kept }
        assertTrue(
            "被丢掉的那批，重要性一定不高于留下来的任何一条",
            dropped.all { d ->
                val importance = d.removePrefix("id").toInt() % 5
                merged.all { it.importance >= importance }
            },
        )
    }

    @Test
    fun promptRendersTopByImportanceWithCategoryLabels() {
        val text = ChatMemoryRules.renderMemoryForPrompt(
            listOf(
                item("a", "喜欢周杰伦", importance = 7),
                item("b", "在成都", importance = 8, category = "user_profile"),
                item("c", "神秘爱好", importance = 9, category = "unknown_kind"),
            ),
        )
        assertEquals(
            "顺序按重要性降序；标签是 prompt 契约的一部分",
            "- [其他] 神秘爱好 (重要性:9)\n- [用户画像] 在成都 (重要性:8)\n- [偏好] 喜欢周杰伦 (重要性:7)\n",
            text,
        )
        assertEquals("空表要给一句明确的话，不能留空", "（暂无记忆）", ChatMemoryRules.renderMemoryForPrompt(emptyList()))
    }

    @Test
    fun promptCapsAtTwentyItems() {
        val many = (1..30).map { item("i$it", "内容$it", importance = it, at = it.toLong()) }
        val lines = ChatMemoryRules.renderMemoryForPrompt(many).trim().lines()
        assertEquals(20, lines.size)
        assertTrue("留下的必须是最重要那批", lines.first().contains("(重要性:30)"))
        assertFalse("第 11 重要以外的不该出现", lines.any { it.contains("(重要性:10)") })
    }

    @Test
    fun agentMemoryUsesTheSameRenderingAsTheAnalyzerSees() {
        val items = listOf(item("a", "喜欢周杰伦", importance = 7))
        assertEquals(
            "模型在提炼时看到的\"已有记忆\"和对话里注入的记忆必须是同一份措辞",
            ChatMemoryRules.renderMemoryForPrompt(items),
            ChatMemoryRules.renderAgentMemory(items),
        )
    }

    @Test
    fun conversationWindowLabelsYukiAndUser() {
        val msgs = (1..25).map { if (it % 2 == 0) "assistant" to "回复$it" else "user" to "说$it" }
        val text = ChatMemoryRules.renderConversation(msgs)
        val lines = text.trim().lines()
        assertEquals("只带最近 20 条", 20, lines.size)
        assertEquals("窗口起点就是倒数第 20 条（第 6 条，是 Yuki 说的）", "Yuki: 回复6", lines.first())
        assertEquals("assistant 显示成 Yuki，其余算用户", "用户: 说25", lines.last())
    }

    @Test
    fun parseExtractedToleratesWhatModelsActuallyReturn() {
        val clean = ChatMemoryRules.parseExtracted(
            """[{"id":"a1","category":"habit","content":"深夜听摇滚","importance":8}]""",
            now = 1000L,
        ) { 1 }
        assertEquals(1, clean.size)
        assertEquals("a1", clean.first().id)
        assertEquals("habit", clean.first().category)
        assertEquals(8, clean.first().importance)

        // 模型常把 JSON 夹在解释文字与 ``` 围栏里：取第一个 [ 到最后一个 ]
        val fenced = ChatMemoryRules.parseExtracted(
            "好的，我提取到这些：\n```json\n[{\"content\":\"喜欢粤语歌\"}]\n```\n以上。",
            now = 2L,
        ) { 7 }
        assertEquals("围栏与前后解释都不该挡住解析", 1, fenced.size)
        assertEquals("缺 id 时要造一个 8 位十六进制", 8, fenced.first().id.length)
        assertEquals("缺 importance 用默认值", ChatMemoryRules.DEFAULT_IMPORTANCE, fenced.first().importance)
        assertEquals("缺 category 归到偏好", "preference", fenced.first().category)

        // 一次回答里出现两个数组时，"第一个 [ 到最后一个 ]"跨出来的不是合法 JSON —— 于是什么都不记。
        // 这是 C# 同一条取法的既有行为，宁可少记也不猜：模型吐两段数组本身就说明它没在按格式回答。
        assertTrue(
            ChatMemoryRules.parseExtracted("[] 先给个空的\n[{\"content\":\"这条其实读不到\"}]", 2L) { 7 }.isEmpty(),
        )
    }

    @Test
    fun parseExtractedDropsBadRowsInsteadOfTheWholeBatch() {
        val mixed = ChatMemoryRules.parseExtracted(
            """[{"content":"留着","importance":"9"},{"content":"   "},{"nope":1}]""",
            now = 3L,
        ) { 5 }
        assertEquals("空内容与没有 content 的条目丢掉，其余照收", listOf("留着"), mixed.map { it.content })
        assertEquals("importance 写成字符串也要认", 9, mixed.first().importance)

        assertTrue(ChatMemoryRules.parseExtracted("没有数组", 1L) { 1 }.isEmpty())
        assertTrue(ChatMemoryRules.parseExtracted("[{坏掉的 json", 1L) { 1 }.isEmpty())
        assertTrue(ChatMemoryRules.parseExtracted("[]", 1L) { 1 }.isEmpty())
    }

    @Test
    fun recordableAndIdRules() {
        assertTrue(ChatMemoryRules.isRecordable("有内容"))
        assertFalse(ChatMemoryRules.isRecordable("  "))
    }

    @Test
    fun newIdIsEightHexCharsAndAvoidsExistingOnes() {
        val first = ChatMemoryRules.newId(emptySet()) { 0x1234abcd }
        assertEquals("1234abcd", first)
        // 第二次仍抽到同一个值时必须换下一个，而不是交出一个重复 id
        val draws = intArrayOf(0x1234abcd, 0xffff).iterator()
        val second = ChatMemoryRules.newId(setOf(first)) { draws.next() }
        assertEquals("短值要补零到 8 位", "0000ffff", second)
    }
}
