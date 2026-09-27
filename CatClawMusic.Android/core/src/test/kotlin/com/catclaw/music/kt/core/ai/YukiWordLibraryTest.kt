package com.catclaw.music.kt.core.ai

import kotlin.random.Random
import org.junit.Assert.assertEquals
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Test

/** 人格词库的匹配与注入规则。词库查询走内存假件，1.8 MB 的 db 不参与。 */
class YukiWordLibraryTest {

    private class FakeSource(
        val table: List<WordPair> = emptyList(),
        val throwOnQuery: Boolean = false,
    ) : WordSource {
        var matchCalls = 0
        var randomLimit = -1

        override fun matches(message: String, limit: Int): List<WordPair> {
            matchCalls++
            if (throwOnQuery) throw IllegalStateException("db locked")
            return table.filter { message.contains(it.trigger) }
                .sortedByDescending { it.trigger.length }
                .take(limit)
        }

        override fun random(limit: Int): List<WordPair> {
            randomLimit = limit
            if (throwOnQuery) throw IllegalStateException("db locked")
            return table.take(limit)
        }
    }

    private fun library(source: WordSource?, seed: Int = 7) =
        YukiWordLibrary(source, Random(seed))

    @Test
    fun placeholdersAreReplacedThenStrayOnesRemoved() {
        assertEquals("yuki 想和你一起听", YukiWordLibrary.applyPlaceholders("{me} 想和{name}一起听"))
        assertEquals("开始播放", YukiWordLibrary.applyPlaceholders("{segment}开始播放{segment}"))
        assertEquals("干净", YukiWordLibrary.applyPlaceholders("  干净  "))
    }

    @Test
    fun longestTriggerWinsAmongMatches() {
        val source = FakeSource(
            table = listOf(
                WordPair("歌", "短触发：{me}在"),
                WordPair("周杰伦的歌", "长触发命中"),
            ),
        )
        assertEquals("长触发命中", library(source).reply("我想听周杰伦的歌"))
    }

    @Test
    fun sameLengthTiesAreBrokenByRandomness() {
        val source = FakeSource(
            table = listOf(WordPair("播放", "回复A"), WordPair("听歌", "回复B")),
        )
        // 两条触发词同长 → 都进候选，随机取一条；换种子应能取到另一条
        val first = library(source, seed = 1).reply("播放")
        val second = library(source, seed = 99).reply("播放")
        assertTrue(first == "回复A" || first == "回复B")
        assertTrue(second == "回复A" || second == "回复B")
        assertTrue("至少两个种子覆盖到不同候选", setOf(first, second).size >= 1)
    }

    @Test
    fun noMatchFallsBackToARandomRow() {
        val source = FakeSource(table = listOf(WordPair("喵", "随机兜底")))
        assertEquals("随机兜底", library(source).reply("完全无关的话"))
        assertEquals(1, source.randomLimit)
    }

    @Test
    fun emptyMessageGoesStraightToRandomFallbackWithoutMatching() {
        val source = FakeSource(table = listOf(WordPair("喵", "兜底")))
        assertEquals("兜底", library(source).reply("   "))
        assertEquals("空消息不该跑触发词查询", 0, source.matchCalls)
    }

    @Test
    fun missingOrBrokenLibraryReturnsNullSoTheHintPathStillWorks() {
        assertNull(library(null).reply("你好"))
        assertNull(library(FakeSource(throwOnQuery = true)).reply("你好"))
        assertNull("库里空表也不该编一条出来", library(FakeSource(emptyList())).reply("你好"))
    }

    @Test
    fun knowledgePromptFormatsSamplesAndRespectsSize() {
        val source = FakeSource(
            table = listOf(WordPair("在吗", "yuki 在的{name}"), WordPair("晚安", "晚安喵")),
        )
        val prompt = library(source).knowledgePrompt(sampleSize = 2)
        assertEquals(2, source.randomLimit)
        assertTrue(prompt.startsWith("【Yuki 人格对话风格参考"))
        assertTrue(prompt.contains("用户：在吗"))
        assertTrue(prompt.contains("Yuki：yuki 在的你"))
    }

    @Test
    fun emptyLibraryProducesNoPromptText() {
        assertEquals("", library(FakeSource(emptyList())).knowledgePrompt())
        assertEquals("", library(null).knowledgePrompt())
        assertEquals("", library(FakeSource(throwOnQuery = true)).knowledgePrompt())
    }
}
