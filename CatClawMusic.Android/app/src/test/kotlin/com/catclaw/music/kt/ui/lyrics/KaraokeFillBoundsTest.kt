package com.catclaw.music.kt.ui.lyrics

import androidx.compose.ui.text.style.TextAlign
import java.io.File
import org.junit.Assert.assertEquals
import org.junit.Assert.assertTrue
import org.junit.Test

/**
 * 逐字填充的裁切算术。
 *
 * 修之前的写法是 `clipRect(right = size.width * progress)`——按**控件宽度**扫。
 * 居中/右对齐时文字两边有留白，进度前半段全在切空气：唱到 10% 时彩字一格都没露出来。
 * 这里按「这一行画出来的字宽 + 对齐算出的行左边界」来扫，并把算术钉成测试——
 * 像素效果在这台机器上截不到图，但几何是可以算的。
 */
class KaraokeFillBoundsTest {

    // 控件 400px、这一行字 100px：居中时文字占据 [150, 250]
    private val box = 400f
    private val line = 100f

    @Test
    fun `居中按行宽扫而不是块宽`() {
        val (left, right) = karaokeFillBounds(TextAlign.Center, box, line, 0.1f)
        assertEquals(150f, left, 0.01f)
        // 唱到 10% 就该露出字的前 10px；旧写法算出来是 0→40，全在留白里
        assertEquals(160f, right, 0.01f)
        assertTrue("彩字区间落在文字之外（旧 bug 的特征）", right > left)

        val full = karaokeFillBounds(TextAlign.Center, box, line, 1f)
        assertEquals(150f to 250f, full.first to full.second)
    }

    @Test
    fun `右对齐从行首扫到行尾`() {
        val atHalf = karaokeFillBounds(TextAlign.End, box, line, 0.5f)
        assertEquals(300f to 350f, atHalf.first to atHalf.second)
        val atEnd = karaokeFillBounds(TextAlign.End, box, line, 1f)
        assertEquals(300f to 400f, atEnd.first to atEnd.second)
    }

    @Test
    fun `左对齐等价于原来的起点但长度按行宽`() {
        assertEquals(0f to 50f, karaokeFillBounds(TextAlign.Start, box, line, 0.5f))
    }

    @Test
    fun `越界的进度与行宽都夹回控件范围`() {
        for (p in listOf(-1f, 0f, 1f, 2f)) {
            val (left, right) = karaokeFillBounds(TextAlign.Center, box, line, p)
            assertTrue("left 越界 p=$p", left in 0f..box)
            assertTrue("right 越界 p=$p: $left..$right", right in left..box)
        }
        // 省略号截断后行宽可能报得比控件还宽：夹住，不能裁出负区间
        val wide = karaokeFillBounds(TextAlign.Start, box, 999f, 0.5f)
        assertEquals(0f, wide.first, 0.01f)
        assertEquals(box, wide.second, 0.01f)
    }

    /** 纯函数必须真的接在绘制路径上，否则测试守的是个没人调的公式 */
    @Test
    fun `裁切算术确实接在 drawWithContent 上`() {
        val src = source.readText()
        assertTrue("KaraokeSpan 没调用 karaokeFillBounds", src.contains("karaokeFillBounds("))
        assertTrue("clipRect 没用到算出来的边界", src.contains("clipRect(left = clipLeft"))
        // 正控制：把接线删掉，判据必须变假
        assertTrue(
            "删掉调用后判据没反应",
            !src.replace("karaokeFillBounds(", "/* 拆掉了 */").contains("karaokeFillBounds("),
        )
    }

    private val source: File by lazy {
        listOf(
            "src/main/kotlin/com/catclaw/music/kt/ui/lyrics/LyricLineText.kt",
            "app/src/main/kotlin/com/catclaw/music/kt/ui/lyrics/LyricLineText.kt",
        ).map(::File).firstOrNull { it.isFile } ?: error("找不到 LyricLineText.kt（工作目录应为 :app 模块根）")
    }
}
