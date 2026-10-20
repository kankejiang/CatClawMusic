package com.catclaw.music.kt.core.theme

import org.junit.Assert.assertEquals
import org.junit.Assert.assertTrue
import org.junit.Test

/**
 * 流光喷发内核的算术钉子：全部是 C# `FrostedFlowProcessor/Animator/Config` 里写死的量，
 * 移植不许漂。像素效果没法截图对证，能钉的只有这些纯算术（与 karaokeFillBounds 同理）。
 */
class FrostedFlowTest {

    @Test
    fun colorStageMappingMatchesCSharp() {
        // C# ColorStageIndex：0→1、1→0、2→1、3→2（整数阶段恰好落在阶段表上，frac=0 取 start）
        val preset = FrostedFlowPreset.Dark
        val out = FloatArray(16)
        for (stage in 0..3) {
            FrostedFlowProcessor.interpolateColors(preset, stage.toFloat(), out)
            val expected = when (stage) {
                0 -> preset.colorStages[1]
                1 -> preset.colorStages[0]
                2 -> preset.colorStages[1]
                else -> preset.colorStages[2]
            }
            for (i in 0 until 16) assertEquals(expected[i], out[i], 1e-6f)
        }
    }

    @Test
    fun colorStageInterpolatesHalfway() {
        val preset = FrostedFlowPreset.Light
        val out = FloatArray(16)
        // stage = 0.5：start=stages[1]、end=stages[0]，中点即两表均值
        FrostedFlowProcessor.interpolateColors(preset, 0.5f, out)
        for (i in 0 until 16) {
            assertEquals(
                (preset.colorStages[1][i] + preset.colorStages[0][i]) / 2f,
                out[i], 1e-6f,
            )
        }
    }

    @Test
    fun animatorFreezesWhenNotPlaying() {
        val animator = FrostedFlowAnimator(5f)
        animator.advance(0.5f, playing = true)
        val t = animator.animTime
        val s = animator.colorStage
        // playing=false：animTime 冻结，colorStage 仍带滞后逼近目标（C# Advance 同款）
        animator.advance(0.5f, playing = false)
        assertEquals(t, animator.animTime, 1e-6f)
        assertTrue(animator.colorStage > s)
        // TimeScale=0.45：1s 推进只累计 0.45s
        animator.advance(1f, playing = true)
        assertEquals(t + 0.45f, animator.animTime, 1e-4f)
    }

    @Test
    fun renderWritesBaseColorOutsideBound() {
        // boundW=0：所有像素都在流光区外 → 整帧等于底色
        val w = 8; val h = 8
        val argb = IntArray(w * h)
        val preset = FrostedFlowPreset.Dark
        val colors = FloatArray(16)
        FrostedFlowProcessor.interpolateColors(preset, 0f, colors)
        FrostedFlowProcessor.render(argb, w, h, preset, colors, 0f, 0f, 0f, 0f, 1f, FROSTED_FLOW_BASE_ARGB)
        for (p in argb) assertEquals(FROSTED_FLOW_BASE_ARGB, p)
    }

    @Test
    fun renderProducesPremultipliedFrame() {
        val w = 32; val h = 32
        val argb = IntArray(w * h)
        val preset = FrostedFlowPreset.Dark
        val colors = FloatArray(16)
        FrostedFlowProcessor.interpolateColors(preset, 1f, colors)
        FrostedFlowProcessor.render(argb, w, h, preset, colors, 1.3f, 0f, 0f, 1f, 1f, FROSTED_FLOW_BASE_ARGB)
        for (p in argb) {
            val a = (p ushr 24) and 0xFF
            val r = (p ushr 16) and 0xFF
            val g = (p ushr 8) and 0xFF
            val b = p and 0xFF
            // 预乘约束 r,g,b ≤ alpha（±1：颗粒噪声在 alpha 量化边缘可顶开 1/255，C# 同序）；
            // 光照覆盖不到的像素 = 满格底色
            assertTrue(r <= a + 1 && g <= a + 1 && b <= a + 1)
        }
    }

    @Test
    fun suggestBufferSizeHalvesByDpi() {
        // 高 dpi(≥420) 1/24、否则 1/16（MAUI CoverFlowProcessor.SuggestBufferSize）
        assertEquals(45 to 80, FrostedFlowProcessor.suggestBufferSize(1080, 1920, 440))
        assertEquals(45 to 80, FrostedFlowProcessor.suggestBufferSize(1080, 1920, 420))
        assertEquals(68 to 120, FrostedFlowProcessor.suggestBufferSize(1080, 1920, 419))
    }
}
