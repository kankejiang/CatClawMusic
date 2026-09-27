package com.catclaw.music.kt.data.lyrics

import android.content.Context
import dagger.hilt.android.qualifiers.ApplicationContext
import javax.inject.Inject
import javax.inject.Singleton
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow

/** 歌词来源，对齐 C# `LyricsSourceMode`：自动 / 仅内嵌 / 仅外挂 */
enum class LyricsSourceMode { Auto, Embedded, External }

/**
 * 全屏歌词显示设置，键名、默认值与钳位逐条对齐 MAUI `Services/LyricsSettingsService.cs`：
 * 模式默认逐行、来源默认自动、**对齐默认左**（C# 有一次性迁移把老用户的 Center 改回 Left，见 :84-96；
 * 这里对齐沿用 `LyricLine.alignment` 的 0 左 / 1 中 / 2 右，不再另造枚举）、
 * 字号 22（18..38）、删除空行 / 译文 / 罗马音 三个开关默认都开。
 */
@Singleton
class LyricsSettings @Inject constructor(
    @ApplicationContext context: Context,
) {
    private val prefs = context.getSharedPreferences(PREFS, Context.MODE_PRIVATE)

    private val _lineMode = MutableStateFlow(prefs.getBoolean(KEY_MODE, false))
    val lineMode: StateFlow<Boolean> = _lineMode.asStateFlow()

    private val _sourceMode = MutableStateFlow(
        runCatching {
            LyricsSourceMode.entries[prefs.getInt(KEY_SOURCE, LyricsSourceMode.Auto.ordinal)]
        }.getOrDefault(LyricsSourceMode.Auto),
    )
    val sourceMode: StateFlow<LyricsSourceMode> = _sourceMode.asStateFlow()

    /** 0 左 / 1 中 / 2 右，与 `LyricLine.alignment` 同一套编码 */
    private val _alignment = MutableStateFlow(migrateAlignment())
    val alignment: StateFlow<Int> = _alignment.asStateFlow()

    private val _fontSizeSp = MutableStateFlow(prefs.getFloat(KEY_FONT, DEFAULT_FONT))
    val fontSizeSp: StateFlow<Float> = _fontSizeSp.asStateFlow()

    private val _removeEmptyLines = MutableStateFlow(prefs.getBoolean(KEY_REMOVE_EMPTY, true))
    val removeEmptyLines: StateFlow<Boolean> = _removeEmptyLines.asStateFlow()

    private val _showTranslation = MutableStateFlow(prefs.getBoolean(KEY_SHOW_TL, true))
    val showTranslation: StateFlow<Boolean> = _showTranslation.asStateFlow()

    private val _showRoma = MutableStateFlow(prefs.getBoolean(KEY_SHOW_ROMA, true))
    val showRoma: StateFlow<Boolean> = _showRoma.asStateFlow()

    fun setLineMode(value: Boolean) {
        _lineMode.value = value
        prefs.edit().putBoolean(KEY_MODE, value).apply()
    }

    fun setSourceMode(value: LyricsSourceMode) {
        _sourceMode.value = value
        prefs.edit().putInt(KEY_SOURCE, value.ordinal).apply()
    }

    fun setAlignment(value: Int) {
        _alignment.value = value.coerceIn(0, 2)
        prefs.edit().putInt(KEY_ALIGNMENT, _alignment.value).apply()
    }

    fun setFontSize(value: Float) {
        val v = value.coerceIn(MIN_FONT, MAX_FONT)
        _fontSizeSp.value = v
        prefs.edit().putFloat(KEY_FONT, v).apply()
    }

    fun setRemoveEmptyLines(value: Boolean) {
        _removeEmptyLines.value = value
        prefs.edit().putBoolean(KEY_REMOVE_EMPTY, value).apply()
    }

    fun setShowTranslation(value: Boolean) {
        _showTranslation.value = value
        prefs.edit().putBoolean(KEY_SHOW_TL, value).apply()
    }

    fun setShowRoma(value: Boolean) {
        _showRoma.value = value
        prefs.edit().putBoolean(KEY_SHOW_ROMA, value).apply()
    }

    /**
     * C# 的同名迁移：老版本默认居中，新版本默认左对齐，
     * 首次升级时把已存成居中的用户改回左，之后不再干预用户的手动选择。
     */
    private fun migrateAlignment(): Int {
        if (!prefs.getBoolean(KEY_ALIGNMENT_MIGRATED, false)) {
            prefs.edit()
                .putBoolean(KEY_ALIGNMENT_MIGRATED, true)
                .putInt(KEY_ALIGNMENT, ALIGN_LEFT)
                .apply()
            return ALIGN_LEFT
        }
        return prefs.getInt(KEY_ALIGNMENT, ALIGN_LEFT).coerceIn(0, 2)
    }

    companion object {
        const val DEFAULT_FONT = 22f
        const val MIN_FONT = 18f
        const val MAX_FONT = 38f
        const val ALIGN_LEFT = 0

        private const val PREFS = "lyrics"
        private const val KEY_MODE = "lyrics_mode"
        private const val KEY_SOURCE = "lyrics_source_mode"
        private const val KEY_ALIGNMENT = "lyrics_alignment"
        private const val KEY_ALIGNMENT_MIGRATED = "lyrics_alignment_migrated_v2"
        private const val KEY_FONT = "lyrics_font_size"
        private const val KEY_REMOVE_EMPTY = "lyrics_remove_empty_lines"
        private const val KEY_SHOW_TL = "lyrics_show_translation"
        private const val KEY_SHOW_ROMA = "lyrics_show_roma"
    }
}
