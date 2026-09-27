package com.catclaw.music.kt.core.theme

import org.junit.Assert.assertEquals
import org.junit.Assert.assertNotEquals
import org.junit.Assert.assertTrue
import org.junit.Test

/** 莫奈取色的配色数学：这些数都是 C# `ThemeService.GetPaletteDesign` 里写死的量，钉住不许漂 */
class MonetPaletteTest {

    private fun argb(value: Long) = Triple(
        ((value shr 16) and 0xFF).toInt(),
        ((value shr 8) and 0xFF).toInt(),
        (value and 0xFF).toInt(),
    )

    @Test
    fun hslKnownConversions() {
        assertEquals(Triple(255, 255, 255), MonetPalette.hslRgb(0.0, 1.0, 0.5))
        assertEquals(Triple(0, 0, 0), MonetPalette.hslRgb(0.0, 0.0, 0.5))
        assertEquals(Triple(128, 128, 128), MonetPalette.hslRgb(0.0, 0.5, 0.0))
        assertEquals(Triple(0, 255, 0), MonetPalette.hslRgb(120.0, 0.5, 1.0))
        assertEquals(Triple(255, 0, 0), MonetPalette.hslRgb(0.0, 0.5, 1.0))
        assertEquals(Triple(0, 0, 255), MonetPalette.hslRgb(240.0, 0.5, 1.0))
    }

    @Test
    fun hueWrapsBothWays() {
        assertEquals(10.0, MonetPalette.rotateHue(350.0, 20.0), 1e-9)
        assertEquals(350.0, MonetPalette.rotateHue(10.0, -20.0), 1e-9)
        assertEquals(60.0, MonetPalette.rotateHue(0.0), 1e-9)
    }

    @Test
    fun saturationIsClampedToTheUsableBand() {
        assertEquals(MonetPalette.SAT_MIN, MonetPalette.clampSaturation(0.0), 1e-9)
        assertEquals(MonetPalette.SAT_MIN, MonetPalette.clampSaturation(0.02), 1e-9)
        assertEquals(0.40, MonetPalette.clampSaturation(0.40), 1e-9)
        assertEquals(MonetPalette.SAT_MAX, MonetPalette.clampSaturation(0.99), 1e-9)
    }

    @Test
    fun darkPaletteHasThreeAscendingStopsAndTwoGlowsAtTheCsharpGeometry() {
        val p = MonetPalette.darkPalette(210.0, 270.0, 0.4)
        assertEquals(3, p.backgroundStops.size)
        assertEquals(listOf(0f, 0.55f, 1f), p.backgroundStops.map { it.offset })
        val lightness = p.backgroundStops.map { luminance(argb(it.argb)) }
        assertEquals(true, lightness[0] < lightness[1] && lightness[1] < lightness[2])
        assertEquals(2, p.glows.size)
        assertEquals(0.80f, p.glows[0].cx, 1e-6f)
        assertEquals(0.18f, p.glows[0].cy, 1e-6f)
        assertEquals(0.52f, p.glows[0].radius, 1e-6f)
        assertEquals(0.24f, p.glows[1].cx, 1e-6f)
        assertEquals(0.86f, p.glows[1].cy, 1e-6f)
        assertEquals(0.70f, p.glows[1].radius, 1e-6f)
        // 光晕是半透明叠加：alpha 0x2A / 0x14，不是实心
        assertEquals(0x2AL, (p.glows[0].centerArgb ushr 24) and 0xFF)
        assertEquals(0x14L, (p.glows[1].centerArgb ushr 24) and 0xFF)
    }

    @Test
    fun lightPaletteKeepsTheFirstGlowWhiteAndTintsOnlyTheSecond() {
        val p = MonetPalette.lightPalette(210.0, 270.0, 0.4)
        assertEquals(0xF2FFFFFFL, p.glows[0].centerArgb)
        assertEquals(0x38L, (p.glows[1].centerArgb ushr 24) and 0xFF)
        val lightness = p.backgroundStops.map { luminance(argb(it.argb)) }
        assertTrue("浅色底三段应当越来越暗", lightness[0] > lightness[1] && lightness[1] > lightness[2])
    }

    @Test
    fun changingTheWallpaperHueChangesTheStopsButNotTheAccentOrLightness() {
        val warm = MonetPalette.darkPalette(30.0, 90.0, 0.4)
        val cool = MonetPalette.darkPalette(210.0, 270.0, 0.4)
        assertNotEquals(warm.backgroundStops[1].argb, cool.backgroundStops[1].argb)
        assertEquals(warm.primary, cool.primary)
        assertEquals(warm.accent, cool.accent)
        assertEquals(warm.textPrimary, cool.textPrimary)
        assertEquals(
            luminance(argb(warm.backgroundStops[1].argb)),
            luminance(argb(cool.backgroundStops[1].argb)),
            0.02,
        )
    }

    private fun luminance(rgb: Triple<Int, Int, Int>): Double =
        (rgb.first * 0.299 + rgb.second * 0.587 + rgb.third * 0.114) / 255.0
}
