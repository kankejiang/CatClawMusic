package com.catclaw.music.kt.core.lyrics

import org.junit.Assert.assertEquals
import org.junit.Assert.assertNull
import org.junit.Test

class LyricsStateTest {

    private val sampleLines = listOf(
        LyricLine(1_000, "一"),
        LyricLine(4_000, "二"),
        LyricLine(9_000, "三"),
    )

    private fun lyrics() = Lyrics().apply { this.lines = sampleLines.toMutableList() }

    @Test
    fun indexBeforeFirstLineIsMinusOne() {
        assertEquals(-1, LyricsState.findLineIndex(sampleLines, 999))
        assertNull(LyricsState.present(lyrics(), 500))
        assertEquals(-1, LyricsState.findLineIndex(emptyList(), 500))
    }

    @Test
    fun indexPicksLastLineAtOrBeforePosition() {
        assertEquals(0, LyricsState.findLineIndex(sampleLines, 1_000))
        assertEquals(0, LyricsState.findLineIndex(sampleLines, 3_999))
        assertEquals(1, LyricsState.findLineIndex(sampleLines, 4_000))
        assertEquals(2, LyricsState.findLineIndex(sampleLines, 60_000))
    }

    @Test
    fun fillProgressIsLinearWithoutWordTimestamps() {
        val p = LyricsState.present(lyrics(), 2_500)!!
        assertEquals(0, p.index)
        assertEquals(0.5, p.fillProgress, 0.001)
        assertEquals("二", p.nextLine?.text)
    }

    @Test
    fun lineModeAlwaysFullyFilled() {
        val p = LyricsState.present(lyrics(), 1_500, lineMode = true)!!
        assertEquals(1.0, p.fillProgress, 0.0)
    }

    @Test
    fun karaokeUsesSyllableTimestamps() {
        val karaoke = Lyrics().apply {
            lines = mutableListOf(
                LyricLine(
                    startMs = 10_000,
                    text = "你好",
                    wordTimestamps = listOf(
                        WordTimestamp("你", 10_000, 1_000),
                        WordTimestamp("好", 11_000, 1_000),
                    ),
                ),
                LyricLine(12_000, "下一句"),
            )
        }
        // 两个字符各占一半：唱完「你」的一半 → 0.25；「你」完 +「好」90% → 0.95
        assertEquals(0.25, LyricsState.present(karaoke, 10_500)!!.fillProgress, 0.001)
        assertEquals(0.95, LyricsState.present(karaoke, 11_900)!!.fillProgress, 0.001)
    }

    @Test
    fun lastLineFillsOverFiveSecondWindow() {
        val p = LyricsState.present(lyrics(), 11_000)!!
        assertEquals(2, p.index)
        assertEquals(0.4, p.fillProgress, 0.001)
        assertNull(p.nextLine)
    }
}
