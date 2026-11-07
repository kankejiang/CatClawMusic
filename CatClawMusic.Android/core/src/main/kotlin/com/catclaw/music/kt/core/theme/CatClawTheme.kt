package com.catclaw.music.kt.core.theme

/**
 * 猫爪「樱粉奶油」单主题调色板，取值直接对齐 MAUI 侧 ThemeService：
 * ThemeMap[Pink] = (#FF8FB8, #FFD9E7, #F56FA0)、GetAccentColor = #7ED8C3、
 * 深浅两套背景渐变各 3 停靠 + 2 层径向光晕。
 *
 * 颜色以 ARGB Long 承载，:core 因此不依赖 Android/Compose。
 */
enum class AppearanceMode { System, Light, Dark }

data class BgStop(val argb: Long, val offset: Float)

/** 径向光晕：中心归一化坐标 (cx, cy)、归一化半径 radius、中心色（含透明度）→ 全透明边缘。 */
data class BgGlow(val cx: Float, val cy: Float, val radius: Float, val centerArgb: Long)

data class CatClawPalette(
    val primary: Long,
    val primaryLight: Long,
    val primaryDark: Long,
    val accent: Long,
    val textPrimary: Long,
    val textSecondary: Long,
    val backgroundStops: List<BgStop>,
    val glows: List<BgGlow>,
)

object CatClawTheme {
    // 2026-11-07 用户要求：删掉所有粉色元素，改用液态玻璃语言。
    // 主色从粉色换成**冷灰蓝**（玻璃上的一点冷调），浅色主题用同族的深一档。
    const val PINK_PRIMARY = 0xFF8FA3CCL   // 保留常量名避免大面积改名，值已中性化
    const val PINK_LIGHT = 0xFFDDE4F2L
    const val PINK_DARK = 0xFF9FB4D9L
    const val ACCENT_MINT = 0xFF7ED8C3L

    val dark = CatClawPalette(
        primary = PINK_PRIMARY,
        primaryLight = PINK_LIGHT,
        primaryDark = PINK_DARK,
        accent = ACCENT_MINT,
        textPrimary = 0xFFFFFFFFL,
        textSecondary = 0xCCFFFFFFL,
        backgroundStops = listOf(
            BgStop(0xFF11141DL, 0f),
            BgStop(0xFF171C2AL, 0.55f),
            BgStop(0xFF1E2536L, 1f),
        ),
        glows = listOf(
            BgGlow(0.80f, 0.18f, 0.52f, 0x2A55D6FFL),
            BgGlow(0.24f, 0.86f, 0.70f, 0x147ED8C3L),
        ),
    )

    val light = CatClawPalette(
        primary = PINK_DARK,
        primaryLight = PINK_LIGHT,
        primaryDark = PINK_PRIMARY,
        accent = ACCENT_MINT,
        textPrimary = 0xFF4A3A44L,
        textSecondary = 0xFF866B77L,
        backgroundStops = listOf(
            BgStop(0xFFF7F8FCL, 0f),
            BgStop(0xFFEFF2F9L, 0.55f),
            BgStop(0xFFE6EAF4L, 1f),
        ),
        glows = listOf(
            BgGlow(0.80f, 0.14f, 0.70f, 0xF2FFFFFFL),
            BgGlow(0.20f, 0.88f, 0.60f, 0x387ED8C3L),
        ),
    )

    fun paletteFor(mode: AppearanceMode, systemDark: Boolean): CatClawPalette = when (mode) {
        AppearanceMode.Light -> light
        AppearanceMode.Dark -> dark
        AppearanceMode.System -> if (systemDark) dark else light
    }
}
