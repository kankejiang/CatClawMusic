package com.catclaw.music.kt.ui.theme

import androidx.compose.foundation.isSystemInDarkTheme
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.darkColorScheme
import androidx.compose.material3.lightColorScheme
import androidx.compose.runtime.Composable
import androidx.compose.runtime.CompositionLocalProvider
import androidx.compose.runtime.ProvidableCompositionLocal
import androidx.compose.runtime.staticCompositionLocalOf
import androidx.compose.ui.graphics.Color
import androidx.hilt.navigation.compose.hiltViewModel
import androidx.lifecycle.compose.collectAsStateWithLifecycle
import com.catclaw.music.kt.core.theme.AppearanceMode
import com.catclaw.music.kt.core.theme.CatClawPalette
import com.catclaw.music.kt.core.theme.CatClawTheme

val LocalCatClawPalette: ProvidableCompositionLocal<CatClawPalette> =
    staticCompositionLocalOf { CatClawTheme.light }

/** 解析后的深浅色（App 内外观设置优先于系统设置）。默认封面等绘制用它。 */
val LocalIsDarkTheme = staticCompositionLocalOf { true }

@Composable
fun CatClawTheme(
    themeViewModel: ThemeViewModel = hiltViewModel(),
    content: @Composable () -> Unit,
) {
    val mode = themeViewModel.mode.collectAsStateWithLifecycle().value
    CatClawTheme(mode = mode, content = content)
}

@Composable
fun CatClawTheme(
    mode: AppearanceMode,
    content: @Composable () -> Unit,
) {
    val systemDark = isSystemInDarkTheme()
    val isDark = when (mode) {
        AppearanceMode.Light -> false
        AppearanceMode.Dark -> true
        AppearanceMode.System -> systemDark
    }
    val palette = CatClawTheme.paletteFor(mode, systemDark)
    val base = if (isDark) darkColorScheme() else lightColorScheme()

    MaterialTheme(
        colorScheme = base.copy(
            primary = Color(palette.primary),
            secondary = Color(palette.accent),
            tertiary = Color(palette.primaryLight),
            onPrimary = Color(palette.textPrimary),
            surface = Color(palette.backgroundStops.first().argb),
            background = Color(palette.backgroundStops.first().argb),
            onSurface = Color(palette.textPrimary),
            onBackground = Color(palette.textPrimary),
        ),
        content = {
            CompositionLocalProvider(
                LocalCatClawPalette provides palette,
                // 解析后的深浅色：默认封面这类「跟随主题」的绘制要用它。
                // 这里不能用 isSystemInDarkTheme()，那是系统设置，会和 App 内的外观设置打架。
                LocalIsDarkTheme provides isDark,
                content = content,
            )
        },
    )
}
