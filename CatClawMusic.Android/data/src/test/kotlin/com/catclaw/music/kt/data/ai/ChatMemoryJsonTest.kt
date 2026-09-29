package com.catclaw.music.kt.data.ai

import com.catclaw.music.kt.core.ai.ChatMemoryRules
import org.junit.Assert.assertEquals
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Test

/**
 * `ai_memory.json` 的格式。
 *
 * 这个文件是**跨端交换格式**：MAUI 备份里的记忆要靠键名对上，键名写错一个字母的表现是
 * "读回来是空的" —— 不报错、不崩，只在用户身上显形。所以键名本身要钉住，而不只钉行为。
 */
class ChatMemoryJsonTest {

    private val sample = ChatMemoryRules.Item(
        id = "ab12cd34",
        category = "habit",
        content = "深夜听摇滚",
        importance = 8,
        createdAt = 1_700_000_000_000L,
        updatedAt = 1_700_000_005_000L,
    )

    @Test
    fun roundTripKeepsEveryField() {
        val back = ChatMemoryJson.decode(ChatMemoryJson.encode(listOf(sample)))
        assertEquals(1, back.size)
        assertEquals(sample, back.first())
    }

    @Test
    fun nullUpdatedAtIsOmittedRatherThanWrittenAsNull() {
        val text = ChatMemoryJson.encode(listOf(sample.copy(updatedAt = null)))
        assertTrue("省略键才和 C# 的可空字段输出一致", !text.contains("UpdatedAt"))
        assertNull(ChatMemoryJson.decode(text).first().updatedAt)
    }

    @Test
    fun keyNamesAreTheCrossEndContract() {
        val text = ChatMemoryJson.encode(listOf(sample))
        listOf("Id", "Category", "Content", "Importance", "CreatedAt").forEach {
            assertTrue("少了 $it 就对不上 MAUI 的字段", it in text)
        }
    }

    @Test
    fun decodeIsLenientOnUnknownKeysAndStrictOnUnusableRows() {
        val text = """
            [
              {"Id":"a","Content":"留着","Importance":"9","FutureField":123},
              {"Id":"b","Content":"   "},
              {"Content":"没有 id"},
              {"Id":"d","Content":"重要性越界","Importance":99},
              "不是对象"
            ]
        """.trimIndent()
        val rows = ChatMemoryJson.decode(text)
        assertEquals(listOf("a", "d"), rows.map { it.id })
        assertEquals("importance 写成字符串也要认", 9, rows.first().importance)
        assertEquals("缺 category 归到默认档", ChatMemoryRules.CATEGORY_PREFERENCE, rows.first().category)
        assertEquals(10, rows.last().importance)
    }

    @Test
    fun decodeGivesUpQuietlyOnUnparseableFiles() {
        assertTrue(ChatMemoryJson.decode("").isEmpty())
        assertTrue(ChatMemoryJson.decode("   ").isEmpty())
        assertTrue(ChatMemoryJson.decode("{不是数组}").isEmpty())
        assertTrue(ChatMemoryJson.decode("[坏掉的").isEmpty())
    }
}
