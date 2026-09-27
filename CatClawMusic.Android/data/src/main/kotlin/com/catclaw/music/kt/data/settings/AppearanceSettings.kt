package com.catclaw.music.kt.data.settings

import android.content.Context
import android.app.WallpaperManager
import android.graphics.Color
import androidx.core.graphics.blue
import androidx.core.graphics.green
import androidx.core.graphics.red
import kotlin.math.abs
import kotlin.math.roundToInt
import dagger.hilt.android.qualifiers.ApplicationContext
import javax.inject.Inject
import javax.inject.Singleton
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow

/**
 * 外观页的持久项，键与默认值对齐 MAUI `ThemeService.cs:20-31,787-791`：
 * 启动页索引默认 **2（播放）**、自定义背景图路径、背景透明度默认 0.5、
 * 雾面动态背景默认 **开**、莫奈取色默认关、动态封面背景默认关。
 *
 * 互斥规则照抄 `AppearanceSettingsViewModel.cs:98-123` + `ThemeService.cs:275-307`：
 * 莫奈与动态封面互斥；开动态封面必须先开雾面；关雾面会连带关掉动态封面。
 */
@Singleton
class AppearanceSettings @Inject constructor(
    @ApplicationContext private val context: Context,
) {
    private val prefs = context.getSharedPreferences(PREFS, Context.MODE_PRIVATE)

    private val _startupIndex = MutableStateFlow(prefs.getInt(KEY_STARTUP, DEFAULT_STARTUP))
    val startupIndex: StateFlow<Int> = _startupIndex.asStateFlow()

    private val _customBgPath = MutableStateFlow(prefs.getString(KEY_BG_PATH, null)?.takeIf { it.isNotEmpty() })
    val customBgPath: StateFlow<String?> = _customBgPath.asStateFlow()

    private val _bgOpacity = MutableStateFlow(prefs.getFloat(KEY_BG_OPACITY, DEFAULT_BG_OPACITY))
    val bgOpacity: StateFlow<Float> = _bgOpacity.asStateFlow()

    private val _frosted = MutableStateFlow(prefs.getBoolean(KEY_FROSTED, true))
    val frostedEnabled: StateFlow<Boolean> = _frosted.asStateFlow()

    private val _monet = MutableStateFlow(prefs.getBoolean(KEY_MONET, false))
    val monetEnabled: StateFlow<Boolean> = _monet.asStateFlow()

    private val _cover = MutableStateFlow(prefs.getBoolean(KEY_COVER, false))
    val coverEnabled: StateFlow<Boolean> = _cover.asStateFlow()

    fun setStartupIndex(value: Int) {
        val v = value.coerceIn(0, 2)
        _startupIndex.value = v
        prefs.edit().putInt(KEY_STARTUP, v).apply()
    }

    /** 透明度夹在 0.1..1.0（`ThemeService.cs:217`）；注意 MAUI 侧这个值只存不渲染，见下方注释 */
    fun setBackgroundOpacity(value: Float) {
        val v = value.coerceIn(0.1f, 1f)
        _bgOpacity.value = v
        prefs.edit().putFloat(KEY_BG_OPACITY, v).apply()
    }

    fun setCustomBackground(path: String?, opacity: Float = _bgOpacity.value) {
        val v = opacity.coerceIn(0.1f, 1f)
        _customBgPath.value = path
        _bgOpacity.value = v
        prefs.edit()
            .putString(KEY_BG_PATH, path.orEmpty())
            .putFloat(KEY_BG_OPACITY, v)
            .apply()
    }

    fun setFrostedEnabled(value: Boolean) {
        _frosted.value = value
        if (!value && _cover.value) {
            _cover.value = false
            prefs.edit().putBoolean(KEY_COVER, false).apply()
        }
        prefs.edit().putBoolean(KEY_FROSTED, value).apply()
    }

    fun setMonetEnabled(value: Boolean) {
        _monet.value = value
        if (value && _cover.value) {
            _cover.value = false
            prefs.edit().putBoolean(KEY_COVER, false).apply()
        }
        prefs.edit().putBoolean(KEY_MONET, value).apply()
    }

    /** 动态封面要雾面打底：雾面关着时这个开不了（与 C# 的拒绝式校验同形） */
    fun setCoverEnabled(value: Boolean): Boolean {
        if (value && !_frosted.value) return false
        _cover.value = value
        if (value && _monet.value) {
            _monet.value = false
            prefs.edit().putBoolean(KEY_MONET, false).apply()
        }
        prefs.edit().putBoolean(KEY_COVER, value).apply()
        return true
    }

    /**
     * 系统壁纸主/副色的 HSL（度, 度, 饱和度）。
     * 与 C# `MonetPalette.cs:30-45` 同源：`FLAG_SYSTEM` 的 Primary/Secondary；
     * 副色缺失时把主色色相转 60°。取不到壁纸（无权限/无壁纸服务）返回 null，调用方回落到标准主题。
     */
    fun wallpaperHues(): Triple<Double, Double, Double>? {
        val colors = runCatching {
            context.getSystemService(WallpaperManager::class.java)
                .getWallpaperColors(WallpaperManager.FLAG_SYSTEM)
        }.getOrNull() ?: return null
        val primary = colors.primaryColor ?: return null
        val (ph, ps, _) = toHsl(to255(primary.red()), to255(primary.green()), to255(primary.blue()))
        val secondary = colors.secondaryColor
        val sh = if (secondary != null) {
            toHsl(to255(secondary.red()), to255(secondary.green()), to255(secondary.blue())).first
        } else {
            ((ph + 60.0) % 360.0 + 360.0) % 360.0
        }
        return Triple(ph, sh, ps)
    }

    private fun to255(channel: Float): Int = (channel * 255.0f).roundToInt().coerceIn(0, 255)

    private fun toHsl(r: Int, g: Int, b: Int): Triple<Double, Double, Double> {
        val rf = r / 255.0
        val gf = g / 255.0
        val bf = b / 255.0
        val max = maxOf(rf, gf, bf)
        val min = minOf(rf, gf, bf)
        val delta = max - min
        val l = (max + min) / 2.0
        val s = if (delta == 0.0) 0.0 else delta / (1.0 - abs(2.0 * l - 1.0)).coerceAtLeast(1e-9)
        val h = when {
            delta == 0.0 -> 0.0
            max == rf -> 60.0 * (((gf - bf) / delta) % 6.0)
            max == gf -> 60.0 * (((bf - rf) / delta) + 2.0)
            else -> 60.0 * (((rf - gf) / delta) + 4.0)
        }
        return Triple((h + 360.0) % 360.0, s.coerceIn(0.0, 1.0), l)
    }

    companion object {
        const val DEFAULT_STARTUP = 2
        const val DEFAULT_BG_OPACITY = 0.5f
        val STARTUP_LABELS = listOf("音乐库", "探索", "播放")

        /** 启动页选项 → ViewPager 页号；0 音乐库→4、1 探索→2、2 播放→1（歌单一档在 C# 里到不了） */
        fun startupToPagerPage(index: Int): Int = when (index) {
            0 -> 4
            1 -> 2
            2 -> 1
            else -> 1
        }

        private const val PREFS = "appearance"
        private const val KEY_STARTUP = "startup_page_index"
        private const val KEY_BG_PATH = "custom_bg_path"
        private const val KEY_BG_OPACITY = "custom_bg_opacity"
        private const val KEY_FROSTED = "frosted_bg_enabled"
        private const val KEY_MONET = "monet_bg_enabled"
        private const val KEY_COVER = "cover_bg_enabled"
    }
}
