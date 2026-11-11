package com.catclaw.music.kt.core.netease

import org.junit.Assert.assertEquals
import org.junit.Assert.assertNull
import org.junit.Test

class SongMatcherTest {

    private val library = listOf(
        MatchCandidate(1, "七里香", "周杰伦", 299),
        MatchCandidate(2, "海阔天空", "Beyond", 326),
        MatchCandidate(3, "海阔天空", "信乐团", 271),
        MatchCandidate(4, "Hong Kong (feat. 王菲季候风)", "The Weeknd", 217),
        MatchCandidate(5, "Blue", "Troye Sivan", 211),
        MatchCandidate(6, "晴天", "周杰伦", 269),
    )
    private val matcher = SongMatcher(library)

    @Test
    fun `标题与艺术家全等`() {
        assertEquals(1, matcher.match("七里香", "周杰伦", 299)?.id)
    }

    @Test
    fun `括号里的 feat 不影响匹配`() {
        assertEquals(4, matcher.match("Hong Kong", "The Weeknd", 217)?.id)
        assertEquals(4, matcher.match("Hong Kong (feat. 王菲季候风)", "The Weeknd", 217)?.id)
    }

    @Test
    fun `全角字母数字折半角后仍相等`() {
        assertEquals(5, matcher.match("ＢＬＵＥ", "Troye Sivan", 211)?.id)
    }

    @Test
    fun `网易云多艺术家写成斜杠时按主艺术家命中`() {
        // 本地只有 "周杰伦"，远端写成 "周杰伦/费玉清"
        assertEquals(1, matcher.match("七里香", "周杰伦/费玉清", 299)?.id)
    }

    @Test
    fun `同名多版本靠时长收敛`() {
        assertEquals(2, matcher.match("海阔天空", "Beyond", 328)?.id)
        assertEquals(3, matcher.match("海阔天空", "信乐团", 270)?.id)
    }

    @Test
    fun `同名多版本且艺术家对不上时宁可放弃`() {
        // 时长差 1 秒也够像，但两首歌在本地同名、艺术家都不相关 —— 猜错就是放错歌
        assertNull(matcher.match("海阔天空", "某个翻唱歌手", 325))
    }

    @Test
    fun `单候选但时长差太大不认`() {
        assertNull(matcher.match("晴天", "周杰伦", 200))
    }

    @Test
    fun `本地缺时长时仍按标题与艺术家命中`() {
        assertNull(matcher.match("不知道的歌", "周杰伦", 299))
        // 本地扫描没读到时长（0）不该成为反证：标题+艺术家已经对上了
        val unknownDuration = SongMatcher(listOf(MatchCandidate(9, "无名", "某人", 0)))
        assertEquals(9, unknownDuration.match("无名", "某人", 200)?.id)
    }

    @Test
    fun `标题里的标点差异不影响`() {
        assertEquals(6, matcher.match("晴天！", "周杰伦", 269)?.id)
    }
}
