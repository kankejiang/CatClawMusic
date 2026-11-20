package com.catclaw.music.kt.lyrico.search

import org.junit.Assert.assertEquals
import org.junit.Assert.assertNotNull
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Test

/** 插件 searchSongs 返回值解析与选曲的单测。 */
class LyricoSongParserTest {

    private val payload = """
    [
      {"id":"111","title":"不浪漫罪名","artist":"王杰","album":"从今开始",
       "duration":0,"picUrl":"https://x/1.jpg",
       "fields":{"title":"不浪漫罪名","artist":"王杰"},
       "internal":{"lrcshare_id":"111"}},
      {"id":"222","title":"不浪漫罪名 (Live)","artist":"王杰","album":"演唱会",
       "duration":245,"internal":{"lrcshare_id":"222"}}
    ]
    """.trimIndent()

    private val target = LyricsTarget("不浪漫罪名", "王杰", "从今开始", 258_000)

    @Test
    fun parsesHitsAndKeepsRawJson() {
        val hits = LyricoSongParser.parse(payload)
        assertEquals(2, hits.size)
        assertEquals("111", hits[0].id)
        assertEquals("不浪漫罪名", hits[0].title)
        assertEquals("王杰", hits[0].artist)
        // 原文必须保留：getLyrics 要用 internal 里的真实 id
        assertTrue("应保留 internal", hits[0].json.contains("lrcshare_id"))
        // duration 秒 → 毫秒
        assertEquals(245_000L, hits[1].durationMs)
        // duration=0 保持 0
        assertEquals(0L, hits[0].durationMs)
    }

    @Test
    fun picksExactTitleOverLiveVersion() {
        val hits = LyricoSongParser.parse(payload)
        val best = LyricoSongMatcher.pickBest(target, hits)
        assertNotNull(best)
        assertEquals("应挑中非 Live 版本", "111", best!!.id)
    }

    @Test
    fun rejectsUnrelatedSongs() {
        val hits = LyricoSongParser.parse(payload)
        val other = LyricsTarget("夜曲", "周杰伦", "", 226_000)
        assertNull("歌名艺人全对不上时不应硬选", LyricoSongMatcher.pickBest(other, hits))
    }

    @Test
    fun toleratesGarbagePayloads() {
        assertTrue(LyricoSongParser.parse(null).isEmpty())
        assertTrue(LyricoSongParser.parse("").isEmpty())
        assertTrue(LyricoSongParser.parse("{}").isEmpty())
        assertTrue(LyricoSongParser.parse("not json").isEmpty())
    }

    @Test
    fun scorePrefersSameArtistAndDuration() {
        val hits = LyricoSongParser.parse(payload)
        val exact = LyricoSongMatcher.score(target, hits[0])
        val live = LyricoSongMatcher.score(target, hits[1])
        assertTrue("同专辑同时长应更高：exact=$exact live=$live", exact > live)
    }
}
