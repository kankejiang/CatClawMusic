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
            CompositionLocalProvider(LocalCatClawPalette provides palette, content = content)
        },
    )
}
