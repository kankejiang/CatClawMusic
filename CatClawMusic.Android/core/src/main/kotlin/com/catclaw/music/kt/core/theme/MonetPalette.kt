package com.catclaw.music.kt.core.theme

import kotlin.math.abs
import kotlin.math.roundToInt

/**
 * 莫奈取色背景的配色数学，逐条对齐 MAUI `ThemeService.GetPaletteDesign`（:115-146）与 `MonetPalette`。
 *
 * 两点容易记错的语义，先写在这：
 * ① 「莫奈取色」取的是**系统壁纸色**（Android `WallpaperManager.getWallpaperColors(FLAG_SYSTEM)`），
 *    不是当前专辑封面 —— 封面取色是另一档「动态封面背景」；
 * ② 只拿 HSL 的 H，饱和度与亮度全部按固定值重铺，所以换壁纸只会换色调、不会把壁纸的明暗带进来。
 */
object MonetPalette {

    /** 饱和度夹在这个区间：太灰的壁纸会让整屏发白，太艳会盖过内容 */
    const val SAT_MIN = 0.15
    const val SAT_MAX = 0.60

    private const val DARK_L_0 = 0.075
    private const val DARK_L_1 = 0.105
    private const val DARK_L_2 = 0.145
    private const val LIGHT_L_0 = 0.97
    private const val LIGHT_L_1 = 0.945
    private const val LIGHT_L_2 = 0.91
    private const val DARK_GLOW_L = 0.62
    private const val LIGHT_GLOW_L = 0.70

    /** 壁纸没给出副色时，把主色色相转 60° 造一个（与 C# `RotateHue(primary, 60)` 同） */
    fun rotateHue(hueDeg: Double, deltaDeg: Double = 60.0): Double {
        val shifted = (hueDeg + deltaDeg) % 360.0
        return if (shifted < 0) shifted + 360.0 else shifted
    }

    fun clampSaturation(sat: Double): Double = sat.coerceIn(SAT_MIN, SAT_MAX)

    /**
     * 深色底：3 段线性渐变（0 / 0.55 / 1）+ 两团径向光晕。
     * 第一团用主色 @0x2A、第二团用副色 @0x14，位置与半径照抄 C#。
     */
    fun darkPalette(primaryHue: Double, secondaryHue: Double, sat: Double): CatClawPalette {
        val s = clampSaturation(sat)
        return CatClawPalette(
            primary = CatClawTheme.PINK_PRIMARY,
            primaryLight = CatClawTheme.PINK_LIGHT,
            primaryDark = CatClawTheme.PINK_DARK,
            accent = CatClawTheme.ACCENT_MINT,
            textPrimary = 0xFFFFFFFFL,
            textSecondary = 0xCCFFFFFFL,
            backgroundStops = listOf(
                BgStop(hslArgb(primaryHue, s * 0.55, DARK_L_0), 0f),
                BgStop(hslArgb(primaryHue, s * 0.70, DARK_L_1), 0.55f),
                BgStop(hslArgb(secondaryHue, s * 0.80, DARK_L_2), 1f),
            ),
            glows = listOf(
                BgGlow(0.80f, 0.18f, 0.52f, argbOf(0x2A, hslRgb(primaryHue, DARK_GLOW_L, s))),
                BgGlow(0.24f, 0.86f, 0.70f, argbOf(0x14, hslRgb(secondaryHue, DARK_GLOW_L, s))),
            ),
        )
    }

    /**
     * 浅色底：C# 第一团光晕是接近白的 `#F2FFFFFF`（不随取色变），第二团才用副色 @0x38。
     */
    fun lightPalette(primaryHue: Double, secondaryHue: Double, sat: Double): CatClawPalette {
        val s = clampSaturation(sat)
        return CatClawPalette(
            primary = CatClawTheme.PINK_PRIMARY,
            primaryLight = CatClawTheme.PINK_LIGHT,
            primaryDark = CatClawTheme.PINK_DARK,
            accent = CatClawTheme.ACCENT_MINT,
            textPrimary = 0xFF1A1D24L,
            textSecondary = 0x99000000L,
            backgroundStops = listOf(
                BgStop(hslArgb(primaryHue, s * 0.30, LIGHT_L_0), 0f),
                BgStop(hslArgb(primaryHue, s * 0.40, LIGHT_L_1), 0.55f),
                BgStop(hslArgb(secondaryHue, s * 0.50, LIGHT_L_2), 1f),
            ),
            glows = listOf(
                BgGlow(0.80f, 0.18f, 0.52f, 0xF2FFFFFFL),
                BgGlow(0.24f, 0.86f, 0.70f, argbOf(0x38, hslRgb(secondaryHue, LIGHT_GLOW_L, s))),
            ),
        )
    }

    /** HSL → ARGB（不透明） */
    fun hslArgb(hueDeg: Double, sat: Double, light: Double): Long = argbOf(0xFF, hslRgb(hueDeg, light, sat))

    private fun argbOf(alpha: Int, rgb: Triple<Int, Int, Int>): Long =
        ((alpha.toLong() and 0xFF) shl 24) or
            ((rgb.first.toLong() and 0xFF) shl 16) or
            ((rgb.second.toLong() and 0xFF) shl 8) or
            (rgb.third.toLong() and 0xFF)

    /** 标准 HSL→RGB；hue 归一到 [0,360)，sat/light 夹到 [0,1] */
    fun hslRgb(hueDeg: Double, light: Double, sat: Double): Triple<Int, Int, Int> {
        val h = ((hueDeg % 360.0) + 360.0) % 360.0
        val s = sat.coerceIn(0.0, 1.0)
        val l = light.coerceIn(0.0, 1.0)
        val c = (1.0 - abs(2.0 * l - 1.0)) * s
        val x = c * (1.0 - abs(((h / 60.0) % 2.0) - 1.0))
        val m = l - c / 2.0
        val (r1, g1, b1) = when {
            h < 60.0 -> Triple(c, x, 0.0)
            h < 120.0 -> Triple(x, c, 0.0)
            h < 180.0 -> Triple(0.0, c, x)
            h < 240.0 -> Triple(0.0, x, c)
            h < 300.0 -> Triple(x, 0.0, c)
            else -> Triple(c, 0.0, x)
        }
        return Triple(
            ((r1 + m) * 255.0).roundToInt().coerceIn(0, 255),
            ((g1 + m) * 255.0).roundToInt().coerceIn(0, 255),
            ((b1 + m) * 255.0).roundToInt().coerceIn(0, 255),
        )
    }
}
