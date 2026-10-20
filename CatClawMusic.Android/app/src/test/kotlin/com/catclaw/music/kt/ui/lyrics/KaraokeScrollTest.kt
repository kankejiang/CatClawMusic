package com.catclaw.music.kt.ui.lyrics

import org.junit.Assert.assertEquals
import org.junit.Assert.assertTrue
import org.junit.Test

/**
 * 长句「跟着逐字着色左移」的算术（[karaokeScrollOffset]）。
 *
 * 2026-10-19 用户反馈：播放页长歌词会压到收藏按钮上，要在 ♡ 左边设安全区；
 * 但设了安全区后后半句会被挡住，所以要求「已唱到边缘时整句跟着左移，速度和逐字着色一致」。
 *
 * 速度一致这件事可以被数学钉死：offset 对 sungX 的斜率必须是 1（线性、无缓动），
 * 这样每唱过一个像素，整句就左移一个像素 —— 多一个动画曲线都会让两者不同步。
 */
class KaraokeScrollTest {

    @Test
    fun `短句不位移`() {
        assertEquals(0f, karaokeScrollOffset(textWidth = 300f, visibleWidth = 500f, sungX = 300f, tailPx = 16f), 0.001f)
    }

    @Test
    fun `未唱到右缘前不位移`() {
        // 阈值 = visible - tail = 484；已唱 400 还没到
        assertEquals(0f, karaokeScrollOffset(900f, 500f, sungX = 400f, tailPx = 16f), 0.001f)
    }

    @Test
    fun `越过右缘后整句跟着走`() {
        // 600 - (500 - 16) = 116
        assertEquals(116f, karaokeScrollOffset(900f, 500f, sungX = 600f, tailPx = 16f), 0.001f)
    }

    @Test
    fun `位移上限是句尾贴住右缘`() {
        // max = 900 - 500 = 400；已唱到末尾也只移 400，不会把整句拖空
        assertEquals(400f, karaokeScrollOffset(900f, 500f, sungX = 900f, tailPx = 16f), 0.001f)
    }

    @Test
    fun `位移速度与逐字速度一致`() {
        // 线性区里每多唱 10px，整句就多移 10px —— 斜率必须是 1（不是 0.5/2/缓动）
        val a = karaokeScrollOffset(900f, 500f, sungX = 600f, tailPx = 16f)
        val b = karaokeScrollOffset(900f, 500f, sungX = 610f, tailPx = 16f)
        assertEquals(10f, b - a, 0.001f)
        val c = karaokeScrollOffset(900f, 500f, sungX = 700f, tailPx = 16f)
        assertTrue("线性区应严格线性", kotlin.math.abs((c - a) - 100f) < 0.001f)
    }

    @Test
    fun `退化宽度不崩`() {
        assertEquals(0f, karaokeScrollOffset(900f, 0f, 500f, 16f), 0.001f)
        assertEquals(0f, karaokeScrollOffset(0f, 500f, 0f, 16f), 0.001f)
    }
}
