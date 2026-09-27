package com.catclaw.music.kt.core.lyrics

import org.junit.Assert.assertEquals
import org.junit.Assert.assertNotSame
import org.junit.Assert.assertSame
import org.junit.Test

/** 「删除空行」这一档的过滤规则（MAUI 默认就是开的）。 */
class LyricsFilterTest {

    private fun lyrics(vararg texts: String) = Lyrics().apply {
        lines = texts.mapIndexed { i, t -> LyricLine(startMs = (i + 1) * 1000L, text = t) }.toMutableList()
    }

    @Test
    fun blankAndWhitespaceLinesAreDroppedOrderPreserved() {
        val out = lyrics("第一行", "", "   ", "第二行", "\t").withoutEmptyLines()
        assertEquals(listOf("第一行", "第二行"), out.lines.map { it.text })
    }

    @Test
    fun survivingLinesKeepTheirTimestampsSoSeekByLineStillWorks() {
        val out = lyrics("a", "", "b").withoutEmptyLines()
        assertEquals(listOf(1000L, 3000L), out.lines.map { it.startMs })
    }

    @Test
    fun nothingToRemoveReturnsTheSameInstance() {
        val src = lyrics("a", "b")
        assertSame("重建会让上层以为歌词换了、列表在滑动中自己跳一下", src, src.withoutEmptyLines())
    }

    @Test
    fun allBlankYieldsAnEmptyListNotNullNorTheOriginal() {
        val src = lyrics("", "  ")
        val out = src.withoutEmptyLines()
        assertNotSame(src, out)
        assertEquals(0, out.lines.size)
    }

    @Test
    fun metadataAndParseTimeSidecarsAreCarriedOver() {
        val src = lyrics("a", "", "b").apply {
            metadata = LyricsMetadata().apply { title = "稻香" }
            translationLines = listOf(LyricLine(1000L, "tr"))
            romaLines = listOf(LyricLine(1000L, "roma"))
        }
        val out = src.withoutEmptyLines()
        assertEquals("稻香", out.metadata.title)
        assertEquals(1, out.translationLines?.size)
        assertEquals(1, out.romaLines?.size)
    }

    @Test
    fun perLineAlignmentSurvivesTheRebuild() {
        // 这一条是提取函数之后才发现原来漏的：重建 Lyrics 时没带 hasPerLineAlignment，
        // 于是"真删掉了空行"会把逐行对齐退回成全局对齐。
        val src = lyrics("a", "", "b").apply { hasPerLineAlignment = true }
        assertEquals(true, src.withoutEmptyLines().hasPerLineAlignment)
    }

    @Test
    fun perLineTranslationAndRomaRideAlongWithTheirLine() {
        // 译文/罗马音是挂在每条 LyricLine 上的，translationLines/romaLines 只是解析期的中间态；
        // 所以删空行不会让它们错位 —— 这条钉住的是"别改成按位置索引配对"。
        val src = Lyrics().apply {
            lines = mutableListOf(
                LyricLine(1000L, "", translation = "空行的译文"),
                LyricLine(2000L, "正文", translation = "正文的译文", roma = "seibun"),
            )
        }
        val out = src.withoutEmptyLines()
        assertEquals(1, out.lines.size)
        assertEquals("正文的译文", out.lines[0].translation)
        assertEquals("seibun", out.lines[0].roma)
    }
}
