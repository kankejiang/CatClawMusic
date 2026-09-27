package com.catclaw.music.kt.data.lyrics

import android.content.Context
import dagger.hilt.android.qualifiers.ApplicationContext
import javax.inject.Inject
import javax.inject.Singleton
import kotlin.math.roundToInt
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow

/**
 * 桌面歌词设置。默认值、取值范围与钳位规则逐条对齐 MAUI `Services/LyricsSettingsService.cs`：
 * 字号 20（14..32）、正文 #B3FFFFFF、高亮 #FFFFE082、背景不透明度 0.3（0..1）、
 * 纵向位置比例 0.75（0.1..0.95，自屏幕顶部起算）。
 * 单行/双行只在 Windows 侧生效（C# `DesktopLyricService.cs:146-148` 明写 Android 悬浮窗忽略 nextText），故此处不落这个键。
 */
@Singleton
class DesktopLyricSettings @Inject constructor(
    @ApplicationContext context: Context,
) {
    private val prefs = context.getSharedPreferences("desktop_lyric", Context.MODE_PRIVATE)

    private val _fontSizeSp = MutableStateFlow(readClamped(KEY_FONT, DEFAULT_FONT, 14f, 32f))
    val fontSizeSp: StateFlow<Float> = _fontSizeSp.asStateFlow()

    private val _textColor = MutableStateFlow(prefs.getInt(KEY_TEXT, DEFAULT_TEXT))
    val textColor: StateFlow<Int> = _textColor.asStateFlow()

    private val _highlightColor = MutableStateFlow(prefs.getInt(KEY_HL, DEFAULT_HL))
    val highlightColor: StateFlow<Int> = _highlightColor.asStateFlow()

    private val _bgOpacity = MutableStateFlow(prefs.getFloat(KEY_BG, DEFAULT_BG))
    val bgOpacity: StateFlow<Float> = _bgOpacity.asStateFlow()

    /** 纵向位置：自屏幕顶部起算的高度比例，与 C# `DesktopPosY` 同义 */
    private val _posYRatio = MutableStateFlow(readClamped(KEY_Y, DEFAULT_Y, 0.1f, 0.95f))
    val posYRatio: StateFlow<Float> = _posYRatio.asStateFlow()

    /**
     * 读取时也要钳位 —— 只在写入时钳位修不了已经躺在盘上的老值。
     * 真机 desktop_lyric.xml 里就留着一条 pos_y_ratio=0.0（钳位下界还是 0f 的时代写进去的），
     * 于是老装机用户的悬浮窗永远贴在屏幕最顶上 —— 正是 M3-3 追过的那个悬案。
     * 越界就地写回合法值，等于一次性自愈，不需要额外的迁移标记。
     */
    private fun readClamped(key: String, default: Float, lo: Float, hi: Float): Float {
        val raw = prefs.getFloat(key, default)
        val fixed = raw.coerceIn(lo, hi)
        if (fixed != raw) prefs.edit().putFloat(key, fixed).apply()
        return fixed
    }

    private val _locked = MutableStateFlow(prefs.getBoolean(KEY_LOCKED, false))
    val locked: StateFlow<Boolean> = _locked.asStateFlow()

    /** 用户是否开启了桌面歌词（开关状态，与悬浮窗实际是否在场区分开） */
    private val _enabled = MutableStateFlow(prefs.getBoolean(KEY_ENABLED, false))
    val enabled: StateFlow<Boolean> = _enabled.asStateFlow()

    fun setEnabled(value: Boolean) {
        _enabled.value = value
        prefs.edit().putBoolean(KEY_ENABLED, value).apply()
    }

    fun setFontSize(value: Float) = write(KEY_FONT, _fontSizeSp, value.coerceIn(MIN_FONT, MAX_FONT))

    fun setTextColor(value: Int) {
        _textColor.value = value
        prefs.edit().putInt(KEY_TEXT, value).apply()
        onStyleChanged?.invoke()
    }

    fun setHighlightColor(value: Int) {
        _highlightColor.value = value
        prefs.edit().putInt(KEY_HL, value).apply()
        onStyleChanged?.invoke()
    }

    fun setBgOpacity(value: Float) = write(KEY_BG, _bgOpacity, value.coerceIn(0f, 1f))

    /** 存比例而不是存像素，换屏幕/转屏后位置仍然一致（与 C# 同做法） */
    fun savePosYRatio(ratio: Float, screenHeightPx: Int) {
        val v = ratio.coerceIn(MIN_Y, MAX_Y)
        _posYRatio.value = v
        prefs.edit().putFloat(KEY_Y, v).apply()
        if (screenHeightPx > 0) onPosYChanged?.invoke((v * screenHeightPx).roundToInt())
    }

    fun setLocked(value: Boolean) {
        _locked.value = value
        prefs.edit().putBoolean(KEY_LOCKED, value).apply()
        onLockChanged?.invoke(value)
    }

    private fun <T> write(key: String, field: MutableStateFlow<T>, value: T) {
        field.value = value
        onStyleChanged?.invoke()
        when (value) {
            is Float -> prefs.edit().putFloat(key, value).apply()
            is Int -> prefs.edit().putInt(key, value).apply()
            else -> Unit
        }
    }

    var onPosYChanged: ((Int) -> Unit)? = null
    var onLockChanged: ((Boolean) -> Unit)? = null
    var onStyleChanged: (() -> Unit)? = null

    companion object {
        const val DEFAULT_FONT = 20f
        const val MIN_FONT = 14f
        const val MAX_FONT = 32f
        const val DEFAULT_TEXT = 0xB3FFFFFF.toInt()
        const val DEFAULT_HL = 0xFFFFE082.toInt()
        const val DEFAULT_BG = 0.3f
        const val DEFAULT_Y = 0.75f
        const val MIN_Y = 0.1f
        const val MAX_Y = 0.95f

        private const val KEY_FONT = "font_size_sp"
        private const val KEY_TEXT = "text_color"
        private const val KEY_HL = "highlight_color"
        private const val KEY_BG = "bg_opacity"
        private const val KEY_Y = "pos_y_ratio"
        private const val KEY_LOCKED = "locked"
        private const val KEY_ENABLED = "enabled"
    }
}
