package com.catclaw.music.kt.ui.lyrics

import org.junit.Assert.assertEquals
import org.junit.Assert.assertTrue
import org.junit.Test

/**
 * 非当前行雾化半径的曲线（[lyricBlurDp]）。
 *
 * 用户要求「离当前行越远效果越强」，所以这条曲线的两个性质必须钉住：
 * **单调递增**（越远越模糊）与**封顶**（再远也不能糊成一团、白费 GPU）。
 */
class LyricRowBlurTest {

    @Test
    fun `当前行不模糊`() {
        assertEquals(0f, lyricBlurDp(0), 0.001f)
        assertEquals(0f, lyricBlurDp(-3), 0.001f)
    }

    @Test
    fun `距离越远越强且严格递增`() {
        val values = (1..6).map { lyricBlurDp(it) }
        assertTrue("应当严格递增：$values", values.zipWithNext().all { (a, b) -> b > a })
        assertEquals(0.9f, lyricBlurDp(1), 0.001f)
        assertEquals(2.7f, lyricBlurDp(3), 0.001f)
    }

    @Test
    fun `远处封顶不无限糊`() {
        assertEquals(6f, lyricBlurDp(7), 0.001f)
        assertEquals(6f, lyricBlurDp(40), 0.001f)
    }
}
