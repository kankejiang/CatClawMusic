package com.catclaw.music.kt.ui.theme

import com.catclaw.music.kt.core.theme.AppearanceMode
import com.catclaw.music.kt.core.theme.CatClawPalette
import com.catclaw.music.kt.core.theme.CatClawTheme
import com.catclaw.music.kt.core.theme.MonetPalette
import com.catclaw.music.kt.data.settings.AppearanceSettings

/**
 * 把「外观模式 + 莫奈取色档位」折成一张实际用的调色板。
 * 优先级与 C# `ThemeService.CurrentBgKey()` 一致：莫奈开且拿得到壁纸色 → 取色版；否则标准主题。
 * 动态封面背景需要封面取色管线（`AccentColorPicker` + 雾面三层），本档尚未接入，先按标准主题渲染。
 */
object AppearancePalette {

    /**
     * [monet] 显式指定是否套用莫奈取色：外壳只在**发现页**用莫奈档位（用户 2026-11-07 的要求），
     * 其它页传 false 即可，不必改用户设置。
     */
    fun resolve(
        mode: AppearanceMode,
        isSystemDark: Boolean,
        appearance: AppearanceSettings,
        monet: Boolean = appearance.monetEnabled.value,
    ): CatClawPalette {
        val base = CatClawTheme.paletteFor(mode, isSystemDark)
        val dark = mode.darkFor(isSystemDark)
        if (!monet) return base
        val hues = appearance.wallpaperHues() ?: return base
        return if (dark) {
            MonetPalette.darkPalette(hues.first, hues.second, hues.third)
        } else {
            MonetPalette.lightPalette(hues.first, hues.second, hues.third)
        }
    }

    private fun AppearanceMode.darkFor(isSystemDark: Boolean): Boolean = when (this) {
        AppearanceMode.Dark -> true
        AppearanceMode.Light -> false
        AppearanceMode.System -> isSystemDark
    }
}
