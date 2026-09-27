package com.catclaw.music.kt.data.settings

import android.content.Context
import com.catclaw.music.kt.core.audio.AudioCache
import dagger.hilt.android.qualifiers.ApplicationContext
import javax.inject.Inject
import javax.inject.Singleton
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow

/**
 * 音频缓存上限，键与默认值对齐 MAUI `GeneralSettingsViewModel` 用的 `audio_cache_size_mb`（默认 500）。
 * 钳位沿用 `AudioCache` 自己的 100..15000 MB，避免两处各写一套边界。
 */
@Singleton
class CacheSettings @Inject constructor(
    @ApplicationContext context: Context,
) {
    private val prefs = context.getSharedPreferences(PREFS, Context.MODE_PRIVATE)

    private val _sizeMb = MutableStateFlow(prefs.getInt(KEY, AudioCache.DEFAULT_CACHE_SIZE_MB))
    val sizeMb: StateFlow<Int> = _sizeMb.asStateFlow()

    fun setSizeMb(value: Int) {
        val v = clampToRange(value)
        _sizeMb.value = v
        prefs.edit().putInt(KEY, v).apply()
    }

    /** 供 DI 在构造 AudioCache 时读取；不经过 Flow，避免在图构建期订阅 */
    fun sizeMbSnapshot(): Int = prefs.getInt(KEY, AudioCache.DEFAULT_CACHE_SIZE_MB)

    companion object {
        const val MIN_MB = 100
        const val MAX_MB = 15000
        private const val PREFS = "catclaw_state"
        private const val KEY = "audio_cache_size_mb"

        fun clampToRange(value: Int): Int = value.coerceIn(MIN_MB, MAX_MB)
    }
}
