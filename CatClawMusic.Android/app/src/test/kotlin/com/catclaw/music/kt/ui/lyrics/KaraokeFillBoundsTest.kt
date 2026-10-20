package com.catclaw.music.kt.ui.lyrics

import androidx.compose.ui.text.style.TextAlign
import java.io.File
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
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

    // ── 换行后的逐行填充（2026-10-18 真机投诉「两行一起着色」）──────────────

    private fun row(left: Float, right: Float, top: Float = 0f, bottom: Float = 10f) =
        KaraokeRow(left, top, right, bottom)

    @Test
    fun `单行时与旧的整块区间等价`() {
        val filled = karaokeFilledRows(listOf(row(150f, 250f)), 0.3f)
        assertEquals(1, filled.size)
        assertEquals(150f, filled[0].left, 0.01f)
        assertEquals(180f, filled[0].right, 0.01f)
    }

    @Test
    fun `两行等宽时先填满第一行再填第二行`() {
        val rows = listOf(row(0f, 100f, top = 0f, bottom = 10f), row(0f, 100f, top = 12f, bottom = 22f))
        val half = karaokeFilledRows(rows, 0.5f)
        assertEquals("半程只该填满第一行，第二行一点都不能有", 1, half.size)
        assertEquals(100f, half[0].right, 0.01f)

        val threeQuarters = karaokeFilledRows(rows, 0.75f)
        assertEquals(2, threeQuarters.size)
        assertEquals(100f, threeQuarters[0].right, 0.01f)
        assertEquals("第二行填到一半", 50f, threeQuarters[1].right, 0.01f)
    }

    @Test
    fun `份额按行宽加权，长行拿更多`() {
        // 第一行 100px、第二行 300px：总进度 25% 时正好唱完第一行
        val rows = listOf(row(0f, 100f), row(0f, 300f, top = 12f, bottom = 22f))
        val quarter = karaokeFilledRows(rows, 0.25f)
        assertEquals(1, quarter.size)
        assertEquals(100f, quarter[0].right, 0.01f)
        val halfWay = karaokeFilledRows(rows, 0.625f)
        assertEquals(2, halfWay.size)
        assertEquals("第二行已经唱掉一半", 150f, halfWay[1].right, 0.01f)
    }

    @Test
    fun `边界：0 一点不填、1 每行都填满、空列表不炸`() {
        val rows = listOf(row(0f, 100f), row(0f, 100f, top = 12f, bottom = 22f))
        assertTrue(karaokeFilledRows(rows, 0f).isEmpty())
        assertEquals(2, karaokeFilledRows(rows, 1f).size)
        assertEquals(100f, karaokeFilledRows(rows, 1f)[1].right, 0.01f)
        assertTrue(karaokeFilledRows(emptyList(), 0.5f).isEmpty())
    }

    /** 纯函数必须真的接在绘制路径上，否则测试守的是个没人调的公式 */
    @Test
    fun `裁切算术确实接在 drawWithContent 上`() {
        val src = source.readText()
        assertTrue("彩字原语没调用 karaokeFillBounds（首帧回退那条路）", src.contains("karaokeFillBounds("))
        assertTrue("逐行填充没接上绘制", src.contains("karaokeFilledRows("))
        assertTrue("逐行结果没按行裁切", src.contains("clipRect(left = row.left"))
        assertTrue("没读排版结果，拿不到每行边界", src.contains("onTextLayout = { layout = it }"))
        // 性能判据（2026-10-18 修卡顿）：进度必须在**绘制期**读，写成值就会每帧重组
        assertTrue("进度不是 lambda", src.contains("progress: () -> Float"))
        assertTrue("进度没在 drawWithContent 里读", src.contains("val p = progress()"))
        assertFalse("彩字层又开始每帧拼 Path 了（分配压力）", src.contains("Path()"))
        // 正控制：把接线删掉，判据必须变假
        assertTrue(
            "删掉调用后判据没反应",
            !src.replace("karaokeFillBounds(", "/* 拆掉了 */").contains("karaokeFillBounds("),
        )
    }

    /** 接线检查看的是**共用底料**：彩字原语与算术都在 KaraokeFill.kt（2026-10-18 两页拆开时挪过去的） */
    private val source: File by lazy {
        listOf(
            "src/main/kotlin/com/catclaw/music/kt/ui/lyrics/KaraokeFill.kt",
            "app/src/main/kotlin/com/catclaw/music/kt/ui/lyrics/KaraokeFill.kt",
        ).map(::File).firstOrNull { it.isFile } ?: error("找不到 KaraokeFill.kt（工作目录应为 :app 模块根）")
    }
}
