package com.catclaw.music.kt.ui.theme

import android.app.Application
import android.content.Context
import android.content.Intent
import android.content.IntentFilter
import android.graphics.Bitmap
import androidx.lifecycle.ViewModel
import androidx.lifecycle.viewModelScope
import com.catclaw.music.kt.core.theme.AppearanceMode
import com.catclaw.music.kt.data.settings.AppearanceSettings
import dagger.hilt.android.lifecycle.HiltViewModel
import javax.inject.Inject
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.launch
import kotlinx.coroutines.withContext

@HiltViewModel
class ThemeViewModel @Inject constructor(
    private val store: ThemeStore,
    val appearance: AppearanceSettings,
    private val application: Application,
) : ViewModel() {
    val mode: StateFlow<AppearanceMode> = store.mode

    val startupIndex: StateFlow<Int> = appearance.startupIndex

    /** 壁纸换色时 +1，让上层的 remember 重算一次取色板 */
    private val _monetTick = MutableStateFlow(0)
    val monetTick: StateFlow<Int> = _monetTick.asStateFlow()

    private val _customBackground = MutableStateFlow<Bitmap?>(null)
    val customBackground: StateFlow<Bitmap?> = _customBackground.asStateFlow()

    private val wallpaperReceiver = object : android.content.BroadcastReceiver() {
        override fun onReceive(context: Context?, intent: Intent?) {
            _monetTick.value += 1
        }
    }

    init {
        runCatching {
            application.registerReceiver(
                wallpaperReceiver,
                IntentFilter(Intent.ACTION_WALLPAPER_CHANGED),
            )
        }
        viewModelScope.launch {
            appearance.customBgPath.collect { path ->
                _customBackground.value = withContext(Dispatchers.IO) {
                    loadCustomBackground(path, application.resources.displayMetrics.heightPixels)
                }
            }
        }
    }

    override fun onCleared() {
        runCatching { application.unregisterReceiver(wallpaperReceiver) }
        super.onCleared()
    }

    fun setMode(mode: AppearanceMode) = store.setMode(mode)
}
