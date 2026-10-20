package com.catclaw.music.kt.core.theme

import kotlin.math.cos
import kotlin.math.exp
import kotlin.math.floor
import kotlin.math.max
import kotlin.math.roundToInt
import kotlin.math.sin
import kotlin.math.sqrt

/**
 * 流光喷发背景的共享算法内核 —— MAUI `Services/Frosted` 三件套的原样 Kotlin 移植
 * （FrostedFlowConfig.cs / FrostedFlowProcessor.cs / FrostedFlowAnimator.cs，2026-10-07）：
 * 把 Halcyon（Apple Music 风格）OS3 fragment shader 的数学用纯 Kotlin 移植，
 * 不依赖任何平台 API，渲染走低分辨率像素缓冲，放大铺满即得雾面流光。
 *
 * 渲染管线（移植自 Halcyon OS3BgFrag，与 C# `FrostedFlowProcessor.Render` 逐行对齐）：
 * 1) 像素坐标 → 归一化 uv（flipY）；
 * 2) 对全局 uv 叠加 Perlin 值噪声做扰动，得 noiseValue；
 * 3) 4 个光点随时间做 sin/cos 圆周漂移，按距离 smoothstep 径向混合出基础色/alpha；
 * 4) 用噪声把 RGB 做「反色降饱和」（HSV 明度保持的简化线性版），再叠加 lightOffset 提亮；
 * 5) 叠加极低幅度梯度噪声颗粒（胶片质感）。
 */
object FrostedFlowProcessor {

    // 静态 uniform（即 Halcyon BgEffectPainter 里写死的那批默认值）
    const val TRANSLATE_Y = 0f
    const val ALPHA_MULTI = 1f
    const val NOISE_SCALE = 1.5f
    const val POINT_RADIUS_MULTI = 1f
    const val GRAIN_AMPLITUDE = 1f          // 1/255 量级

    /** 根据「颜色阶段数」（可带小数）在预设的 4 个颜色阶段间往返插值，得 16 float RGBA（4 光点）。 */
    fun interpolateColors(preset: FrostedFlowPreset, stage: Float, output: FloatArray): FloatArray {
        val s = ((stage.toInt() % 4) + 4) % 4
        val frac = stage - floor(stage)
        val startIdx = colorStageIndex(s)
        val endIdx = colorStageIndex(s + 1)
        val start = preset.colorStages[startIdx]
        val end = preset.colorStages[endIdx]
        for (i in 0 until 16) output[i] = start[i] + (end[i] - start[i]) * frac
        return output
    }

    // 映射：0→colors2(下标1)、1→colors1(下标0)、2→colors2(下标1)、3→colors3(下标2)，其余循环
    private fun colorStageIndex(i: Int): Int = when ((i % 4 + 4) % 4) {
        0 -> 1
        1 -> 0
        2 -> 1
        else -> 2
    }

    /** 把一帧流光渲染进 ARGB 像素缓冲（预乘 alpha，与 C# 版逐位对齐）。 */
    fun render(
        argb: IntArray, w: Int, h: Int,
        preset: FrostedFlowPreset,
        colors: FloatArray,
        animTime: Float,
        boundX: Float, boundY: Float, boundW: Float, boundH: Float,
        baseArgb: Int,
    ) {
        val invW = 1f / w
        val invH = 1f / h
        val saturateOffset = preset.saturateOffset
        val lightOffset = preset.lightOffset
        val pointOffset = preset.pointOffset
        val points = preset.points

        // 光点先逐点做圆周漂移（一次算好，供逐像素复用）
        val px4 = FloatArray(4)
        val py4 = FloatArray(4)
        val pr4 = FloatArray(4)
        for (i in 0 until 4) {
            val ox = points[i * 3]
            val oy = points[i * 3 + 1]
            px4[i] = ox + sin(animTime + oy) * pointOffset
            py4[i] = oy + cos(animTime + ox) * pointOffset
            pr4[i] = points[i * 3 + 2] * POINT_RADIUS_MULTI
        }

        val nShift = -animTime
        val nScale = NOISE_SCALE

        for (y in 0 until h) {
            val vuvY = 1f - y * invH
            val rowBase = y * w
            for (x in 0 until w) {
                val vuvX = x * invW

                // 流光区外的像素直接用底色，跳过光照计算
                val uvx = (vuvX - boundX) / boundW
                val uvy = (vuvY - TRANSLATE_Y - boundY) / boundH
                if (uvx < 0f || uvx > 1f || uvy < 0f || uvy > 1f) {
                    argb[rowBase + x] = baseArgb
                    continue
                }

                val noiseValue = perlin(vuvX * nScale + nShift, vuvY * nScale + nShift)
                val oppositeNoise = smoothStep(0f, 1f, noiseValue)

                var colR = 0f; var colG = 0f; var colB = 0f; var colA = 0f
                for (i in 0 until 4) {
                    val ci = i * 4
                    val pA = colors[ci + 3]
                    val r = colors[ci] * pA
                    val g = colors[ci + 1] * pA
                    val b = colors[ci + 2] * pA
                    val ddx = uvx - px4[i]
                    val ddy = uvy - py4[i]
                    val d = sqrt(ddx * ddx + ddy * ddy)
                    val pct = smoothStep(pr4[i], 0f, d)
                    colR += (r - colR) * pct
                    colG += (g - colG) * pct
                    colB += (b - colB) * pct
                    colA += (pA - colA) * pct
                }

                if (colA > 1e-4f) {
                    // 颜色 / alpha（反 premultiply）
                    val invA = 1f / colA
                    colR *= invA; colG *= invA; colB *= invA

                    // 反色降饱和：desat = oppositeNoise * saturateOffset，对新饱和度做 mix(s, 0, desat)
                    val lum = colR * 0.299f + colG * 0.587f + colB * 0.114f
                    val desat = oppositeNoise * saturateOffset
                    val k = 1f - desat
                    colR = lum + (colR - lum) * k
                    colG = lum + (colG - lum) * k
                    colB = lum + (colB - lum) * k

                    // 噪声提亮
                    val light = oppositeNoise * lightOffset
                    colR += light; colG += light; colB += light

                    colA = if (colA < 0f) 0f else if (colA > 1f) 1f else colA
                    colA *= ALPHA_MULTI

                    // 胶片颗粒
                    val grain = hash(vuvX * 1e5f, vuvY * 1e5f) * GRAIN_AMPLITUDE / 255f - (0.5f / 255f)
                    colR += grain; colG += grain; colB += grain

                    argb[rowBase + x] = pack(
                        clampByte(colR * colA), clampByte(colG * colA), clampByte(colB * colA), clampByte(colA))
                } else {
                    argb[rowBase + x] = baseArgb
                }
            }
        }
    }

    fun pack(r: Int, g: Int, b: Int, a: Int): Int = (a shl 24) or (r shl 16) or (g shl 8) or b

    /** Halcyon 封面流的帧下采样系数：高 dpi(≥420) 视口 1/24、否则 1/16（dpi≤0 用 1/18）。
     *  流光/封面流本质是「放大铺满的重度模糊图」，超低分辨率即可（MAUI CoverFlowProcessor.SuggestBufferSize）。 */
    fun suggestBufferSize(viewW: Int, viewH: Int, densityDpi: Int): Pair<Int, Int> {
        val factor = when {
            densityDpi <= 0 -> 18f
            densityDpi >= 420 -> 24f
            else -> 16f
        }
        return max(1, (viewW / factor).roundToInt()) to max(1, (viewH / factor).roundToInt())
    }

    private fun clampByte(v: Float): Int = if (v <= 0f) 0 else if (v >= 1f) 255 else (v * 255f).toInt()

    private fun smoothStep(e0: Float, e1: Float, x: Float): Float {
        var t = (x - e0) / (e1 - e0)
        t = if (t < 0f) 0f else if (t > 1f) 1f else t
        return t * t * (3f - 2f * t)
    }

    /** hash(vec2)：位置抖动出的稳定 [0,1) 伪随机数，作为值噪声采样基底。 */
    private fun hash(x: Float, y: Float): Float {
        val hx = frac(x * 0.1031f)
        val hy = frac(y * 0.1031f)
        val p = frac(hx * hy + 0.1031f)
        val a = hx + hy + p
        val c = (hx * a + hy * a) * 7.17f
        return frac(c + a)
    }

    /** Perlin 值噪声：四角 hash 双线性插值 + smoothstep 平滑。 */
    private fun perlin(x: Float, y: Float): Float {
        val ix = floor(x).toInt()
        val iy = floor(y).toInt()
        val fx = x - ix
        val fy = y - iy
        val a = hash(ix.toFloat(), iy.toFloat())
        val b = hash((ix + 1).toFloat(), iy.toFloat())
        val c = hash(ix.toFloat(), (iy + 1).toFloat())
        val d = hash((ix + 1).toFloat(), (iy + 1).toFloat())
        val u = fx * fx * (3f - 2f * fx)
        val v = fy * fy * (3f - 2f * fy)
        val bottom = a + (b - a) * u
        val top = c + (d - c) * u
        return bottom + (top - bottom) * v
    }

    private fun frac(v: Float): Float = v - floor(v)
}

/**
 * Halcyon 流光喷发背景预设（移植自 Halcyon 的 BgEffectConfig，MAUI FrostedFlowConfig.cs）。
 * 浅色/深色两套，决定光点位置、光源半径与 4 个颜色阶段的 RGBA（每阶段 4 光点 × RGBA = 16 float），
 * 以及动画/调色参数。
 */
class FrostedFlowPreset(
    /** 4 个光点：[x, y, 半径] * 4（归一化坐标） */
    val points: FloatArray,
    /** 4 个颜色阶段（每阶段 16 float = 4 光点 RGBA），颜色随时间在这之间往返插值 */
    val colorStages: Array<FloatArray>,
    /** 颜色阶段停留/过渡的半周期（秒） */
    val colorInterpPeriod: Float,
    val lightOffset: Float,
    val saturateOffset: Float,
    val pointOffset: Float,
) {
    companion object {
        val Light = FrostedFlowPreset(
            points = floatArrayOf(
                0.8f, 0.2f, 1.0f,
                0.8f, 0.9f, 1.0f,
                0.2f, 0.9f, 1.0f,
                0.2f, 0.2f, 1.0f,
            ),
            colorStages = arrayOf(
                floatArrayOf(1.0f, 0.9f, 0.94f, 1.0f, 1.0f, 0.84f, 0.89f, 1.0f, 0.97f, 0.73f, 0.82f, 1.0f, 0.64f, 0.65f, 0.98f, 1.0f),
                floatArrayOf(0.58f, 0.74f, 1.0f, 1.0f, 1.0f, 0.9f, 0.93f, 1.0f, 0.74f, 0.76f, 1.0f, 1.0f, 0.97f, 0.77f, 0.84f, 1.0f),
                floatArrayOf(0.58f, 0.74f, 1.0f, 1.0f, 1.0f, 0.9f, 0.93f, 1.0f, 0.74f, 0.76f, 1.0f, 1.0f, 0.97f, 0.77f, 0.84f, 1.0f),
                floatArrayOf(0.98f, 0.86f, 0.9f, 1.0f, 0.6f, 0.73f, 0.98f, 1.0f, 0.92f, 0.93f, 1.0f, 1.0f, 0.56f, 0.69f, 1.0f, 1.0f),
            ),
            colorInterpPeriod = 5f,
            lightOffset = 0.1f,
            saturateOffset = 0.2f,
            pointOffset = 0.2f,
        )

        val Dark = FrostedFlowPreset(
            points = floatArrayOf(
                0.8f, 0.2f, 1.0f,
                0.8f, 0.9f, 1.0f,
                0.2f, 0.9f, 1.0f,
                0.2f, 0.2f, 1.0f,
            ),
            colorStages = arrayOf(
                floatArrayOf(0.2f, 0.06f, 0.88f, 0.4f, 0.3f, 0.14f, 0.55f, 0.5f, 0.0f, 0.64f, 0.96f, 0.5f, 0.11f, 0.16f, 0.83f, 0.4f),
                floatArrayOf(0.07f, 0.15f, 0.79f, 0.5f, 0.62f, 0.21f, 0.67f, 0.5f, 0.06f, 0.25f, 0.84f, 0.5f, 0.0f, 0.2f, 0.78f, 0.5f),
                floatArrayOf(0.07f, 0.15f, 0.79f, 0.5f, 0.62f, 0.21f, 0.67f, 0.5f, 0.06f, 0.25f, 0.84f, 0.5f, 0.0f, 0.2f, 0.78f, 0.5f),
                floatArrayOf(0.58f, 0.3f, 0.74f, 0.4f, 0.27f, 0.18f, 0.6f, 0.5f, 0.66f, 0.26f, 0.62f, 0.5f, 0.12f, 0.16f, 0.7f, 0.6f),
            ),
            colorInterpPeriod = 8f,
            lightOffset = 0.0f,
            saturateOffset = 0.17f,
            pointOffset = 0.4f,
        )

        fun choose(isDark: Boolean): FrostedFlowPreset = if (isDark) Dark else Light
    }
}

/**
 * 流光动画计时器（Halcyon 的 animTime + colorStage 简化版，MAUI FrostedFlowAnimator.cs）。
 * - animTime：累计动画秒，暂停时冻结（滑动/非激活时把 playing 置 false 即暂停）；
 * - colorStage：颜色阶段数，随 animTime/周期 前进并带一阶滞后，制造「停留后过渡」的感觉。
 */
class FrostedFlowAnimator(
    colorInterpPeriod: Float,
    private val lag: Float = 3.0f,   // 滞后系数：越大跟得越快
) {
    private val period = if (colorInterpPeriod > 0f) colorInterpPeriod else 5f

    /** 累计动画秒（可读给 render 做光点运动） */
    var animTime: Float = 0f
        private set

    /** 当前颜色阶段数（可带小数） */
    var colorStage: Float = 0f
        private set

    companion object {
        /**
         * 全局动画减速系数：光点漂移 / 色彩阶段 / 封面流旋转都乘它。
         * <1 时整体变慢（MAUI FrostedFlowAnimator.TimeScale，一处调两端生效）。
         */
        const val TIME_SCALE = 0.45f
    }

    /** 推进一帧。[dt] 为增量秒；[playing] 为 false 时动画冻结（colorStage 仍向目标滞后逼近）。 */
    fun advance(dt: Float, playing: Boolean) {
        if (dt <= 0f) return
        if (playing) animTime += dt * TIME_SCALE

        // 目标阶段 = 时间/周期 + 1（Halcyon 保持 target 领先一拍，用一阶滞后逼近）
        val target = animTime / period + 1f
        val coeff = 1f - exp(-lag * dt)
        colorStage += (target - colorStage) * coeff
    }

    /** 重置计时（可选的初始化同步）。 */
    fun reset() {
        animTime = 0f
        colorStage = 0f
    }
}

/** 全屏歌词页流光回退底色（MAUI Android handler OnAnimationTick：Pack(12,14,30,255) 的深夜蓝黑）。 */
const val FROSTED_FLOW_BASE_ARGB: Int = 0xFF0C0E1E.toInt()
