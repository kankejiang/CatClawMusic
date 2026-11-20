package com.catclaw.music.kt.lyrico.search

import com.catclaw.music.kt.lyrico.lyrics.LyricoLyrics
import com.catclaw.music.kt.lyrico.lyrics.LyricoLine
import com.catclaw.music.kt.lyrico.lyrics.LyricoPayloadType
import com.catclaw.music.kt.lyrico.lyrics.LyricoWord
import org.junit.Assert.assertEquals
import org.junit.Assert.assertNotNull
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Test

/** 候选排序规则的单测。 */
class LyricsRankingTest {

    private val target = LyricsTarget(
        title = "晴天",
        artist = "周杰伦",
        album = "叶惠美",
        durationMs = 269_000,
    )

    private fun candidate(
        title: String = "晴天",
        artist: String = "周杰伦",
        album: String = "叶惠美",
        durationMs: Long = 269_000,
        wordByWord: Boolean = false,
        source: String = "src",
    ): LyricsCandidate {
        val words = if (wordByWord) {
            listOf(LyricoWord(0, 500, "故"), LyricoWord(500, 1000, "事"))
        } else {
            listOf(LyricoWord(0, 1000, "故事的小黄花"))
        }
        return LyricsCandidate(
            sourceId = source,
            sourceName = source,
            trackTitle = title,
            artist = artist,
            album = album,
            durationMs = durationMs,
            lyrics = LyricoLyrics(
                payloadType = LyricoPayloadType.STRUCTURED,
                original = listOf(LyricoLine(0, 1000, words)),
            ),
        )
    }

    @Test
    fun exactMatchScoresHigherThanPartial() {
        val exact = LyricsRanking.score(target, candidate())
        val wrongArtist = LyricsRanking.score(target, candidate(artist = "某翻唱歌手"))
        val wrongAlbum = LyricsRanking.score(target, candidate(album = "别的专辑"))
        assertNotNull(exact)
        assertNotNull(wrongArtist)
        assertTrue("同艺术家应比不同艺术家分高", exact!! > wrongArtist!!)
        assertTrue("同专辑应比不同专辑分高", exact > wrongAlbum!!)
    }

    @Test
    fun rejectsDurationMismatchWhenTitleNotExact() {
        // 歌名相近（包含）但时长差 60s：应被丢弃
        val bad = candidate(title = "晴天 (Live)", durationMs = 329_000)
        assertNull("时长差过大且歌名不精确时应淘汰", LyricsRanking.score(target, bad))
    }

    @Test
    fun keepsDurationMismatchWhenTitleExact() {
        // 歌名完全一致，即使时长差 10s 也保留（只是分数降低）
        val same = candidate(durationMs = 279_000)
        assertNotNull(LyricsRanking.score(target, same))
    }

    @Test
    fun wordByWordBeatsPlainAtSameMetadata() {
        val word = LyricsRanking.score(target, candidate(wordByWord = true, source = "a"))
        val plain = LyricsRanking.score(target, candidate(wordByWord = false, source = "b"))
        assertTrue("逐字应比整行分高", word!! > plain!!)
    }

    @Test
    fun rankSortsByScoreAndDropsRejected() {
        val list = listOf(
            candidate(artist = "翻唱甲", durationMs = 300_000, source = "s1"),
            candidate(source = "s2"),
            candidate(wordByWord = true, source = "s3"),
            candidate(title = "晴天 (Live)", durationMs = 400_000, source = "s4"),
        )
        val ranked = LyricsRanking.rank(target, list)
        assertEquals("被淘汰的那条不应出现", 3, ranked.size)
        assertTrue("逐字版本应排第一", ranked.first().sourceId == "s3")
        assertTrue("分数应递减", ranked[0].score >= ranked[1].score && ranked[1].score >= ranked[2].score)
    }

    @Test
    fun normalizesPunctuationAndCase() {
        val noisy = candidate(title = "【晴天】", artist = "周杰伦 Jay Chou")
        val score = LyricsRanking.score(target, noisy)
        assertNotNull("括号/空格/英文别名不应导致淘汰", score)
        assertTrue("分数应明显高于 0", score!! > 0.4)
    }
}
