package com.catclaw.music.kt.core.theme

import kotlin.math.abs
import kotlin.math.cos
import kotlin.math.floor
import kotlin.math.max
import kotlin.math.min
import kotlin.math.roundToInt
import kotlin.math.sin

/**
 * 「封面流」背景帧渲染 —— MAUI `Services/Frosted/CoverFlowProcessor.cs` 的逐行移植。
 *
 * 流程（与 Halcyon `createAppleFlowFrameBitmap` 一致）：
 * 源封面 → 过饱和 1.6x → 三份**反向慢速旋转、位置错开**的平铺 → 两层 wash 洗色 →
 * 两趟盒式模糊 → 中心裁剪。全部像素运算都在超低分辨率画布（视口 /16~24，像素量约 1/576），
 * 由 UI 层放大铺满 —— 封面本身就是背景，所以观感鲜亮而不是灰暗。
 *
 * 为什么单独一个文件：MAUI 那边是 Android/Windows 共用的一份纯计算（`Platforms/Android/
 * FrostedBackgroundHandler.cs` 只负责调度）。本端同样放在 `:core`（纯 JVM、可单测），
 * Compose 侧（`ui/theme/FrostedFlowBackground.kt`）只把帧位图放大贴出去。
 *
 * 两个移植注意（都是 MAUI 那边踩过的坑，照抄别改）：
 * - **过饱和用 HSV 而不是 ColorMatrix.setSaturation(2.5)**：后者保亮度矩阵在 2.5x 时绿通道先被压到 0、
 *   红推满，暖金 (230,180,60) 会直接变品红 (255,0,99)（CoverFlowProcessor.cs:16-20）；
 * - **帧底色用 palette.middle**（深色 = accent×0.34，亮色 = 向白提亮 20%），封面透明地盖在其上，
 *   模糊混合后整体向统一色调收拢 —— 这是 Apple Music 那种沉浸感的关键（同文件 :26-41）。
 */
object CoverFlowProcessor {

    /** 流描绘制的源封面长边上限（Halcyon `scaledForFlowSource` 256） */
    const val SOURCE_MAX_DIM = 256

    /** 封面过饱和度系数（HSV 增强，保色相、保明度） */
    const val SATURATION = 1.6f

    /** 旋转时不露边的外扩系数 */
    private const val OVERSCAN = 1.3f

    private const val PI = 3.14159265f

    private const val OPAQUE = 0xFF shl 24

    /** 规整后的封面源（≤256 长边，ARGB 不透明） */
    class CoverSource(val pixels: IntArray, val width: Int, val height: Int) {
        val isEmpty: Boolean get() = pixels.isEmpty() || width <= 0 || height <= 0

        override fun equals(other: Any?): Boolean = this === other

        override fun hashCode(): Int = System.identityHashCode(this)
    }

    /** 渲染出的一帧（ARGB 不透明，尺寸 = 中心裁剪后的画布） */
    class CoverFrame(val pixels: IntArray, val width: Int, val height: Int)

    /** 空源：调用方用它表示「没有封面」（此时背景退回流光模式） */
    val EMPTY_SOURCE = CoverSource(IntArray(0), 0, 0)

    /**
     * Halcyon palette.middle：封面流帧的不透明底色。
     * 深色 = accent 压暗到 34%（浓都暗调）；亮色 = 向白提亮 20%。
     * 提白比例是用户实测定的：原本 72% 会把高调封面冲成一片灰白、歌词白字不可读（:36-37）。
     */
    fun middleColor(accentArgb: Int, isDark: Boolean): IntArray {
        var r = accentArgb shr 16 and 0xFF
        var g = accentArgb shr 8 and 0xFF
        var b = accentArgb and 0xFF
        if (accentArgb and 0xFFFFFF == 0) { r = 11; g = 13; b = 32 }
        return if (isDark) {
            intArrayOf((r * 0.34f).toInt(), (g * 0.34f).toInt(), (b * 0.34f).toInt())
        } else {
            intArrayOf(
                (r + (255 - r) * 0.20f).toInt(),
                (g + (255 - g) * 0.20f).toInt(),
                (b + (255 - b) * 0.20f).toInt(),
            )
        }
    }

    /**
     * Halcyon 帧下采样：高 dpi(≥420) 视口 /24、否则 /16；dpi≤0（Windows）用 /18。
     * 封面流本质是「放大铺满的重度模糊图」，超低分辨率即可 —— 模糊块反而更接近原版。
     */
    fun suggestBufferSize(viewW: Int, viewH: Int, densityDpi: Int): Pair<Int, Int> {
        val factor = if (densityDpi <= 0) 18f else if (densityDpi >= 420) 24f else 16f
        return max(1, (viewW / factor).roundToInt()) to max(1, (viewH / factor).roundToInt())
    }

    /** 把任意分辨率 ARGB 封面双线性下采样到 ≤maxDim 长边，作为流描绘制的源 */
    fun scaleSource(src: IntArray, srcW: Int, srcH: Int, maxDim: Int = SOURCE_MAX_DIM): CoverSource {
        if (src.isEmpty() || srcW <= 0 || srcH <= 0) return EMPTY_SOURCE
        val longest = max(srcW, srcH)
        if (longest <= maxDim) return CoverSource(src, srcW, srcH)
        val scale = maxDim.toFloat() / longest
        val tw = max(1, (srcW * scale).roundToInt())
        val th = max(1, (srcH * scale).roundToInt())
        val out = IntArray(tw * th)
        for (y in 0 until th) {
            val sy = (y + 0.5f) * srcH / th - 0.5f
            val sy0 = floor(sy).toInt().coerceIn(0, srcH - 1)
            val sy1 = min(sy0 + 1, srcH - 1)
            val fy = clamp01(sy - sy0)
            for (x in 0 until tw) {
                val sx = (x + 0.5f) * srcW / tw - 0.5f
                val sx0 = floor(sx).toInt().coerceIn(0, srcW - 1)
                val sx1 = min(sx0 + 1, srcW - 1)
                val fx = clamp01(sx - sx0)
                val c00 = src[sy0 * srcW + sx0]
                val c01 = src[sy0 * srcW + sx1]
                val c10 = src[sy1 * srcW + sx0]
                val c11 = src[sy1 * srcW + sx1]
                out[y * tw + x] = blend(blerp(c00, c01, fx), blerp(c10, c11, fx), fy)
            }
        }
        return CoverSource(out, tw, th)
    }

    // ===== 过饱和源缓存（按源数组身份识别；换歌才重算，MAUI :108-127）=====
    private var satSrcPixels: IntArray? = null
    private var satWidth = 0
    private var satPixels: IntArray? = null

    @Synchronized
    private fun saturated(src: CoverSource): IntArray {
        val cached = satPixels
        if (cached != null && satSrcPixels === src.pixels && satWidth == src.width) return cached
        val n = src.width * src.height
        val s = IntArray(n)
        for (i in 0 until n) s[i] = applySaturation(src.pixels[i], SATURATION)
        satPixels = s
        satSrcPixels = src.pixels
        satWidth = src.width
        return s
    }

    /**
     * 渲染一帧封面流到目标低分辨率画布。
     * [accentArgb] = 封面强调色（0xRRGGBB，缺省 0）；[timeMs] 驱动三层反向慢转。
     */
    fun render(
        src: CoverSource,
        outW: Int,
        outH: Int,
        timeMs: Long,
        blur: Float,
        accentArgb: Int,
        isDark: Boolean,
    ): CoverFrame {
        if (src.isEmpty || outW <= 0 || outH <= 0) return CoverFrame(IntArray(0), 0, 0)
        val sat = saturated(src)

        // 工作画布 = 目标画布 × 外扩（旋转不露边）
        val workW = max(1, (outW * OVERSCAN).roundToInt())
        val workH = max(1, (outH * OVERSCAN).roundToInt())
        val frame = IntArray(workW * workH)
        val middle = middleColor(accentArgb, isDark)
        val middleArgb = OPAQUE or (middle[0] shl 16) or (middle[1] shl 8) or middle[2]
        frame.fill(middleArgb)

        val diagonal = max(workW, workH) * OVERSCAN
        val coverScale = diagonal / max(src.height, 1)
        val translateX = -(diagonal - workW) / 2f
        val translateY = -(diagonal - workH) / 2f
        val rotatePivot = diagonal / 2f
        val cx = workW / 2f
        val cy = workH / 2f
        val rot = (timeMs % 70_000L) / 70_000f * 360f

        drawLayer(frame, workW, workH, src, sat, coverScale, rotatePivot, translateX, translateY, cx, cy,
            rotation = (timeMs % 120_000L) / 120_000f * -360f, offsetX = 0f, offsetY = 0f, extraRotation = null)
        drawLayer(frame, workW, workH, src, sat, coverScale, rotatePivot, translateX, translateY, cx, cy,
            rotation = (timeMs % 90_000L) / 90_000f * 360f, offsetX = -0.95f, offsetY = -0.7f, extraRotation = null)
        drawLayer(frame, workW, workH, src, sat, coverScale, rotatePivot, translateX, translateY, cx, cy,
            rotation = rot, offsetX = -0.5f, offsetY = 0.7f, extraRotation = rot)

        // 洗色：主洗把过饱和封面压进统一氛围调性（深色沉浸感的主要来源）
        val primary = wash(isDark, true, middle[0], middle[1], middle[2])
        val secondary = wash(isDark, false, middle[0], middle[1], middle[2])
        for (i in frame.indices) {
            frame[i] = blendOver(frame[i], primary[0].toInt(), primary[1].toInt(), primary[2].toInt(), primary[3])
            frame[i] = blendOver(frame[i], secondary[0].toInt(), secondary[1].toInt(), secondary[2].toInt(), secondary[3])
        }

        var radius = (((clampBlur(blur) - 30f) / 70f) * 17f + 8f).roundToInt()
        radius = radius.coerceIn(8, 25)
        boxBlur(frame, workW, workH, radius)

        var cropW = (workW / OVERSCAN).roundToInt().coerceIn(1, workW)
        var cropH = (workH / OVERSCAN).roundToInt().coerceIn(1, workH)
        val ox = max(0, (workW - cropW) / 2)
        val oy = max(0, (workH - cropH) / 2)
        val result = IntArray(cropW * cropH)
        for (y in 0 until cropH) {
            frame.copyInto(result, y * cropW, (oy + y) * workW + ox, (oy + y) * workW + ox + cropW)
        }
        return CoverFrame(result, cropW, cropH)
    }
    // ===== 三层平铺绘制（CoverFlowProcessor.cs:197-250 的逐行移植）=====

    private fun drawLayer(
        frame: IntArray,
        workW: Int,
        workH: Int,
        src: CoverSource,
        sat: IntArray,
        coverScale: Float,
        pivot: Float,
        tx: Float,
        ty: Float,
        cx: Float,
        cy: Float,
        rotation: Float,
        offsetX: Float,
        offsetY: Float,
        extraRotation: Float?,
    ) {
        // 与 Halcyon 相同的正向变换：Scale → Rot(pivot) → Translate → Translate(offset) → Rot(center)
        var f = M2D(coverScale.toDouble(), 0.0, 0.0, 0.0, coverScale.toDouble(), 0.0)
        f = mul(rot(rotation, pivot, pivot), f)
        f = mul(M2D(1.0, 0.0, tx.toDouble(), 0.0, 1.0, ty.toDouble()), f)
        if (offsetX != 0f || offsetY != 0f) {
            f = mul(M2D(1.0, 0.0, (workW * offsetX).toDouble(), 0.0, 1.0, (workH * offsetY).toDouble()), f)
        }
        if (extraRotation != null) f = mul(rot(extraRotation, cx, cy), f)

        // 求逆，供「画布像素 → 封面像素」采样
        val det = f.a * f.e - f.b * f.d
        if (abs(det) < 1e-9) return

        val sw = src.width.toDouble()
        val sh = src.height.toDouble()
        val stride = src.width
        for (fy in 0 until workH) {
            val row = fy * workW
            val dyf = fy - f.f
            for (fx in 0 until workW) {
                val dxc = fx - f.c
                val sx = (f.e * dxc - f.b * dyf) / det
                val sy = (-f.d * dxc + f.a * dyf) / det
                if (sx < 0 || sy < 0 || sx >= sw || sy >= sh) continue
                val sx0 = min(sx.toInt(), stride - 1)
                val sy0 = min(sy.toInt(), src.height - 1)
                val sx1 = min(sx0 + 1, stride - 1)
                val sy1 = min(sy0 + 1, src.height - 1)
                val fxw = (sx - sx0).toFloat()
                val fyw = (sy - sy0).toFloat()
                val c00 = sat[sy0 * stride + sx0]
                val c01 = sat[sy0 * stride + sx1]
                val c10 = sat[sy1 * stride + sx0]
                val c11 = sat[sy1 * stride + sx1]
                frame[row + fx] = blend(blerp(c00, c01, fxw), blerp(c10, c11, fxw), fyw)
            }
        }
    }

    /** 2×3 仿射：fx = a*x + b*y + c ; fy = d*x + e*y + f */
    private class M2D(
        val a: Double, val b: Double, val c: Double,
        val d: Double, val e: Double, val f: Double,
    )

    /** 左乘：返回 A∘B（先 B 后 A） */
    private fun mul(ma: M2D, mb: M2D): M2D = M2D(
        ma.a * mb.a + ma.b * mb.d,
        ma.a * mb.b + ma.b * mb.e,
        ma.a * mb.c + ma.b * mb.f + ma.c,
        ma.d * mb.a + ma.e * mb.d,
        ma.d * mb.b + ma.e * mb.e,
        ma.d * mb.c + ma.e * mb.f + ma.f,
    )

    /** 绕 (px,py) 旋转 deg 度 */
    private fun rot(deg: Float, px: Float, py: Float): M2D {
        val rad = deg * PI / 180f
        val cs = cos(rad).toDouble()
        val sn = sin(rad).toDouble()
        val pxd = px.toDouble()
        val pyd = py.toDouble()
        return M2D(cs, -sn, pxd - pxd * cs + pyd * sn, sn, cs, pyd - pyd * cs - pxd * sn)
    }

    // ===== 颜色辅助 =====（CoverFlowProcessor.cs:279-320）

    private fun clamp01(v: Float): Float = if (v < 0f) 0f else if (v > 1f) 1f else v

    private fun blerp(ca: Int, cb: Int, t: Float): Int {
        val ar = ca shr 16 and 0xFF
        val ag = ca shr 8 and 0xFF
        val ab = ca and 0xFF
        val br = cb shr 16 and 0xFF
        val bg = cb shr 8 and 0xFF
        val bb = cb and 0xFF
        return OPAQUE or
            ((ar + (br - ar) * t).toInt() shl 16) or
            ((ag + (bg - ag) * t).toInt() shl 8) or
            (ab + (bb - ab) * t).toInt()
    }

    private fun blend(ca: Int, cb: Int, t: Float): Int = blerp(ca, cb, t)

    /** 把半透明色以 SRC_OVER 叠到 dst 之上 */
    private fun blendOver(dst: Int, sr: Int, sg: Int, sb: Int, a: Float): Int {
        val inv = 1f - a
        val dr = dst shr 16 and 0xFF
        val dg = dst shr 8 and 0xFF
        val db = dst and 0xFF
        val r = (dr * inv + sr * a).toInt()
        val g = (dg * inv + sg * a).toInt()
        val b = (db * inv + sb * a).toInt()
        return OPAQUE or (r shl 16) or (g shl 8) or b
    }

    /**
     * HSV 饱和度增强（保色相、保明度）。
     * 色相零漂移 → 暖金/肤色/橙色系封面不会被过饱和打成品红或灰；饱和 clamp 到 1 自然封顶。
     * 同时不做「近中性去色」：那是 MAUI 明确否掉的 hack（会把封面里大片暖白/肤色打成死灰）。
     */
    internal fun applySaturation(argb: Int, s: Float): Int {
        val r = argb shr 16 and 0xFF
        val g = argb shr 8 and 0xFF
        val b = argb and 0xFF
        val hsv = rgbToHsv(r, g, b)
        val sat = min(hsv[1] * s, 1f)
        val rgb = hsvToRgb(hsv[0], sat, hsv[2])
        return OPAQUE or (rgb[0] shl 16) or (rgb[1] shl 8) or rgb[2]
    }

    /** 返回 [h, s, v]（h 单位度） */
    internal fun rgbToHsv(r: Int, g: Int, b: Int): FloatArray {
        val rf = r / 255f
        val gf = g / 255f
        val bf = b / 255f
        val mx = max(rf, max(gf, bf))
        val mn = min(rf, min(gf, bf))
        val d = mx - mn
        val v = mx
        val s = if (mx == 0f) 0f else d / mx
        if (d < 1e-6f) return floatArrayOf(0f, s, v)
        val h = if (mx == rf) {
            60f * ((gf - bf) / d + (if (gf < bf) 6f else 0f))
        } else if (mx == gf) {
            60f * ((bf - rf) / d + 2f)
        } else {
            60f * ((rf - gf) / d + 4f)
        }
        return floatArrayOf(h, s, v)
    }

    /** 返回 [r, g, b]（0..255） */
    internal fun hsvToRgb(h: Float, s: Float, v: Float): IntArray {
        if (s <= 0f) {
            val g = (v * 255f).roundToInt()
            return intArrayOf(g, g, g)
        }
        val hh = ((h % 360f) + 360f) % 360f
        val hi = floor(hh / 60f)
        val fr = hh / 60f - hi
        val p = v * (1f - s)
        val q = v * (1f - s * fr)
        val t = v * (1f - s * (1f - fr))
        val (rr, gg, bb) = when (hi.toInt()) {
            0 -> Triple(v, t, p)
            1 -> Triple(q, v, p)
            2 -> Triple(p, v, t)
            3 -> Triple(p, q, v)
            4 -> Triple(t, p, v)
            else -> Triple(v, p, q)
        }
        return intArrayOf((rr * 255f).roundToInt(), (gg * 255f).roundToInt(), (bb * 255f).roundToInt())
    }

    /**
     * wash 洗色（Halcyon 原版）。入参为 palette.middle 底色：
     * 深色：主洗 = middle×0.72、α0.34；次洗 = 黑 α0.18。
     * 亮色：主洗 = middle×0.78 + 白×0.22、α0.26；次洗 = 白 α0.14。
     */
    private fun wash(isDark: Boolean, primary: Boolean, mr: Int, mg: Int, mb: Int): FloatArray {
        if (isDark) {
            return if (primary) {
                floatArrayOf(mr * 0.72f, mg * 0.72f, mb * 0.72f, 0.34f)
            } else {
                floatArrayOf(0f, 0f, 0f, 0.18f)
            }
        }
        return if (primary) {
            floatArrayOf(
                mr * 0.78f + 255 * 0.22f,
                mg * 0.78f + 255 * 0.22f,
                mb * 0.78f + 255 * 0.22f,
                0.26f,
            )
        } else {
            floatArrayOf(255f, 255f, 255f, 0.14f)
        }
    }

    private fun clampBlur(blur: Float): Float = if (blur < 30f) 30f else if (blur > 100f) 100f else blur

    /** 两趟分离盒式模糊（Halcyon blurBitmapFast），就地修改 */
    internal fun boxBlur(argb: IntArray, width: Int, height: Int, radius: Int) {
        if (width <= 1 || height <= 1 || argb.size < width * height) return
        val temp = IntArray(argb.size)
        val rr = radius.coerceIn(1, 25)
        val window = rr * 2 + 1

        for (y in 0 until height) {
            val row = y * width
            var a = 0L; var red = 0L; var grn = 0L; var blu = 0L
            for (k in -rr..rr) {
                val p = argb[row + k.coerceIn(0, width - 1)]
                a += p shr 24 and 0xFF; red += p shr 16 and 0xFF; grn += p shr 8 and 0xFF; blu += p and 0xFF
            }
            for (x in 0 until width) {
                temp[row + x] = pack(a / window, red / window, grn / window, blu / window)
                val xo = (x - rr).coerceIn(0, width - 1)
                val xi = (x + rr + 1).coerceIn(0, width - 1)
                val pOut = argb[row + xo]
                val pIn = argb[row + xi]
                a += (pIn shr 24 and 0xFF) - (pOut shr 24 and 0xFF)
                red += (pIn shr 16 and 0xFF) - (pOut shr 16 and 0xFF)
                grn += (pIn shr 8 and 0xFF) - (pOut shr 8 and 0xFF)
                blu += (pIn and 0xFF) - (pOut and 0xFF)
            }
        }

        for (x in 0 until width) {
            var a = 0L; var red = 0L; var grn = 0L; var blu = 0L
            for (k in -rr..rr) {
                val p = temp[k.coerceIn(0, height - 1) * width + x]
                a += p shr 24 and 0xFF; red += p shr 16 and 0xFF; grn += p shr 8 and 0xFF; blu += p and 0xFF
            }
            for (y in 0 until height) {
                argb[y * width + x] = pack(a / window, red / window, grn / window, blu / window)
                val yo = (y - rr).coerceIn(0, height - 1)
                val yi = (y + rr + 1).coerceIn(0, height - 1)
                val pOut = temp[yo * width + x]
                val pIn = temp[yi * width + x]
                a += (pIn shr 24 and 0xFF) - (pOut shr 24 and 0xFF)
                red += (pIn shr 16 and 0xFF) - (pOut shr 16 and 0xFF)
                grn += (pIn shr 8 and 0xFF) - (pOut shr 8 and 0xFF)
                blu += (pIn and 0xFF) - (pOut and 0xFF)
            }
        }
    }

    private fun pack(a: Long, r: Long, g: Long, b: Long): Int =
        ((a and 0xFF).toInt() shl 24) or ((r and 0xFF).toInt() shl 16) or
            ((g and 0xFF).toInt() shl 8) or (b and 0xFF).toInt()
}
