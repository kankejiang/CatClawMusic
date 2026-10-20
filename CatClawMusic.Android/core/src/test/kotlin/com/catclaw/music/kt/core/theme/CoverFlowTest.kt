package com.catclaw.music.kt.core.theme

import org.junit.Assert.assertEquals
import org.junit.Assert.assertNotEquals
import org.junit.Assert.assertTrue
import org.junit.Test

/**
 * 封面流背景的算术 —— MAUI `Services/Frosted/CoverFlowProcessor.cs` 的移植对证。
 *
 * 真机上只能看"亮不亮、偏不偏色"，所以能把关的是这些：采样缓冲尺寸、统一底色、
 * 下采样保比例、过饱和**不偏色相**（MAUI 明确踩过 ColorMatrix 把暖金变品红）、
 * 以及渲染出的帧尺寸/确定性/随时间变化。
 */
class CoverFlowTest {

    @Test
    fun `下采样缓冲按 dpi 分档`() {
        // Halcyon：高 dpi(≥420) 视口 /24、否则 /16；dpi≤0（Windows）用 /18
        assertEquals(45 to 98, CoverFlowProcessor.suggestBufferSize(1080, 2340, 480))  // 97.5 → 98
        assertEquals(68 to 146, CoverFlowProcessor.suggestBufferSize(1080, 2340, 320))  // 67.5 → 68
        assertEquals(60 to 130, CoverFlowProcessor.suggestBufferSize(1080, 2340, 0))
    }

    @Test
    fun `统一底色 深色压暗亮色提白 缺省色兜底`() {
        val dark = CoverFlowProcessor.middleColor(0xE6B43C, isDark = true)
        assertEquals((0xE6 * 0.34f).toInt(), dark[0])
        assertEquals((0xB4 * 0.34f).toInt(), dark[1])
        assertEquals((0x3C * 0.34f).toInt(), dark[2])

        val light = CoverFlowProcessor.middleColor(0xE6B43C, isDark = false)
        assertEquals((0xE6 + (255 - 0xE6) * 0.20f).toInt(), light[0])
        // 无色（0）时用 Halcyon 的深蓝兜底 —— 注意兜底值在**压暗之前**赋值，所以深色下是 11×0.34
        assertEquals((11 * 0.34f).toInt(), CoverFlowProcessor.middleColor(0, isDark = true)[0])
        assertEquals((13 * 0.34f).toInt(), CoverFlowProcessor.middleColor(0, isDark = true)[1])
        assertEquals((32 * 0.34f).toInt(), CoverFlowProcessor.middleColor(0, isDark = true)[2])
    }

    @Test
    fun `下采样保比例并压到长边以内`() {
        val src = IntArray(1024 * 512) { 0xFF3366CC.toInt() }
        val s = CoverFlowProcessor.scaleSource(src, 1024, 512)
        assertEquals(256, s.width)
        assertEquals(128, s.height)
        assertTrue("源本来就在上限内时应原样返回", CoverFlowProcessor.scaleSource(src, 200, 100).width == 200)
    }

    @Test
    fun `过饱和保色相 暖金不许变品红`() {
        // MAUI 的坑：ColorMatrix.setSaturation(2.5) 把暖金 (230,180,60) 打成 (255,0,99) 品红。
        // HSV 方案色相零漂移 —— 这条就是钉住那个结论。
        val gold = 0xFFE6B43C.toInt()
        val before = CoverFlowProcessor.rgbToHsv(0xE6, 0xB4, 0x3C)
        val after = CoverFlowProcessor.applySaturation(gold, CoverFlowProcessor.SATURATION)
        val hsv = CoverFlowProcessor.rgbToHsv(after shr 16 and 0xFF, after shr 8 and 0xFF, after and 0xFF)
        assertTrue("色相漂了：${before[0]} → ${hsv[0]}", kotlin.math.abs(hsv[0] - before[0]) <= 1.5f)
        assertTrue("饱和度没提高", hsv[1] >= before[1])
        assertTrue("红色通道被推满（品红特征）", after shr 16 and 0xFF < 255)
    }

    @Test
    fun `渲染的帧尺寸等于目标画布且不透明`() {
        val src = CoverFlowProcessor.scaleSource(IntArray(64 * 64) { 0xFF804020.toInt() }, 64, 64)
        val frame = CoverFlowProcessor.render(src, 48, 90, 1234L, blur = 42f, accentArgb = 0x804020, isDark = true)
        assertEquals(48, frame.width)
        assertEquals(90, frame.height)
        assertTrue("帧里出现了透明像素", frame.pixels.all { (it ushr 24) and 0xFF == 0xFF })
    }

    @Test
    fun `同一时刻可复现 不同时刻会转动`() {
        // 必须用**有结构**的封面：纯色图旋转多少度都是同一张，测不出"转动"
        val src = CoverFlowProcessor.scaleSource(
            IntArray(32 * 32) { i -> if ((i / 32 + i % 32) % 4 < 2) 0xFF2050A0.toInt() else 0xFFE0C040.toInt() },
            32,
            32,
        )
        val a = CoverFlowProcessor.render(src, 24, 40, 5000L, 42f, 0x2050A0, true)
        val b = CoverFlowProcessor.render(src, 24, 40, 5000L, 42f, 0x2050A0, true)
        assertTrue("同一时刻两次渲染应完全一致", a.pixels.contentEquals(b.pixels))
        val c = CoverFlowProcessor.render(src, 24, 40, 20_000L, 42f, 0x2050A0, true)
        assertNotEquals("三层反向慢转应让不同时刻的帧不同", a.pixels.toList(), c.pixels.toList())
    }

    @Test
    fun `盒式模糊把硬边揉开但保留均值`() {
        val w = 16; val h = 4
        val px = IntArray(w * h) { if (it % w < w / 2) 0xFF000000.toInt() else 0xFFFFFFFF.toInt() }
        val before = px.sumOf { it and 0xFF } / px.size
        CoverFlowProcessor.boxBlur(px, w, h, 3)
        val after = px.sumOf { it and 0xFF } / px.size
        // 黑区**内部**的像素模糊后仍是黑，要测的是边界附近那一格
        assertTrue("边界没被揉开：${px[7] and 0xFF}", px[7] and 0xFF in 1..254)
        assertTrue("均值漂移过大：$before → $after", kotlin.math.abs(after - before) <= 8)
    }
}
