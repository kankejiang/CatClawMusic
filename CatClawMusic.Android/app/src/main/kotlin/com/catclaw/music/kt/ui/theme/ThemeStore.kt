package com.catclaw.music.kt.ui.theme

import android.content.Context
import com.catclaw.music.kt.core.theme.AppearanceMode
import dagger.hilt.android.qualifiers.ApplicationContext
import javax.inject.Inject
import javax.inject.Singleton
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow

/** 外观模式持久化：MAUI 侧用 Preferences，这里用 SharedPreferences 顶位（M4 一并换成 DataStore）。 */
@Singleton
class ThemeStore @Inject constructor(
    @ApplicationContext context: Context,
) {
    private val prefs = context.getSharedPreferences("appearance", Context.MODE_PRIVATE)

    private val _mode = MutableStateFlow(read())
    val mode: StateFlow<AppearanceMode> = _mode.asStateFlow()

    fun setMode(mode: AppearanceMode) {
        prefs.edit().putString(KEY, mode.name).apply()
        _mode.value = mode
    }

    private fun read(): AppearanceMode =
        prefs.getString(KEY, null)?.let { runCatching { AppearanceMode.valueOf(it) }.getOrNull() }
            ?: AppearanceMode.System

    private companion object {
        const val KEY = "appearance_mode"
    }
}
