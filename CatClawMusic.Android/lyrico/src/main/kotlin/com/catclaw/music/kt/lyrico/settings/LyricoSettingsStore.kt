package com.catclaw.music.kt.lyrico.settings

import android.content.Context
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow

/**
 * Lyrico 设置的持久化。
 *
 * 用 SharedPreferences 而不是 Room 加表：这些是「一台设备一份」的开关，
 * 不需要按歌曲/任务查询，也就不该为它动 `lyrico.db` 的 schema（那边已被测试冻结）。
 * 也没引入 DataStore —— 为了几个布尔开关加一个依赖不划算。
 *
 * 内存里保留一份 [StateFlow]：设置页与歌词匹配页要同时看同一份值，
 * 每次读盘会让 UI 在滚动时反复 I/O。
 */
class LyricoSettingsStore(context: Context) {

    private val prefs = context.applicationContext
        .getSharedPreferences(PREFS_NAME, Context.MODE_PRIVATE)

    private val _settings = MutableStateFlow(load())

    /** 供 Compose / ViewModel 直接订阅。 */
    val settings: StateFlow<LyricoSettings> = _settings.asStateFlow()

    /** 同步读取当前值（不需要协程的场合）。 */
    fun current(): LyricoSettings = _settings.value

    /** 增量修改并落盘。 */
    @Synchronized
    fun update(transform: (LyricoSettings) -> LyricoSettings): LyricoSettings {
        val next = transform(_settings.value)
        _settings.value = next
        prefs.edit().putString(KEY_SETTINGS, LyricoSettingsCodec.encode(next)).apply()
        return next
    }

    @Synchronized
    fun save(settings: LyricoSettings) {
        _settings.value = settings
        prefs.edit().putString(KEY_SETTINGS, LyricoSettingsCodec.encode(settings)).apply()
    }

    private fun load(): LyricoSettings {
        val raw = prefs.getString(KEY_SETTINGS, null) ?: return LyricoSettings()
        // 解析失败退回默认：设置坏了不该让整个功能不可用
        return runCatching { LyricoSettingsCodec.decode(raw) }.getOrDefault(LyricoSettings())
    }

    private companion object {
        const val PREFS_NAME = "lyrico_settings"
        const val KEY_SETTINGS = "settings_json"
    }
}
