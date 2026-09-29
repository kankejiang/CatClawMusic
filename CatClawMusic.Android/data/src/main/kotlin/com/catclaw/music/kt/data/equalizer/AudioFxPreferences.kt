package com.catclaw.music.kt.data.equalizer

import android.content.Context
import com.catclaw.music.kt.core.equalizer.EqualizerRules
import dagger.hilt.android.qualifiers.ApplicationContext
import javax.inject.Inject
import javax.inject.Singleton
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow

/** 均衡器与音效的持久状态。键名与 C# `EqualizerSettings` 对齐，便于同一份 prefs 互通 */
data class AudioFxSettings(
    val enabled: Boolean = false,
    val preset: String = "flat",
    val gains: DoubleArray = DoubleArray(EqualizerRules.NATIVE_FREQUENCIES.size),
    /** 0..100 */
    val bassBoost: Int = 0,
    /** 0..100，映射到响度增益 */
    val loudness: Int = 0,
    /** 虚拟环绕声（原生 Virtualizer），开关型 */
    val spatial: Boolean = false,
) {
    override fun equals(other: Any?): Boolean = other is AudioFxSettings &&
        enabled == other.enabled && preset == other.preset && bassBoost == other.bassBoost &&
        loudness == other.loudness && spatial == other.spatial && gains.contentEquals(other.gains)

    override fun hashCode(): Int =
        ((enabled.hashCode() * 31 + preset.hashCode()) * 31 + bassBoost) * 31 + loudness
}

/**
 * 音效设置的读写。
 *
 * 默认**关**：均衡器一旦默认生效，用户听到的是"被改过的声音"却不知道为什么，
 * 而且不同设备的增益范围不一样，默认拉满会直接削波。C# 同样是 `Get(KeyEnabled, false)`。
 */
@Singleton
class AudioFxPreferences @Inject constructor(
    @ApplicationContext context: Context,
) {

    private val prefs = context.getSharedPreferences(PREFS, Context.MODE_PRIVATE)
    private val bandCount = EqualizerRules.NATIVE_FREQUENCIES.size

    private val _settings = MutableStateFlow(read())
    val settings: StateFlow<AudioFxSettings> = _settings.asStateFlow()

    fun setEnabled(value: Boolean) = write(_settings.value.copy(enabled = value))

    fun setPreset(key: String) {
        val gains = EqualizerRules.gainsForPreset(key, EqualizerRules.NATIVE_FREQUENCIES)
        write(_settings.value.copy(preset = key, gains = gains))
    }

    fun setGain(index: Int, db: Double) {
        val gains = _settings.value.gains.copyOf()
        if (index !in gains.indices) return
        gains[index] = EqualizerRules.clampGain(db)
        // 手改曲线之后就不再是任何预设 —— 留着旧名字会让"当前预设"这个高亮说谎
        write(_settings.value.copy(gains = gains, preset = "custom"))
    }

    fun setBassBoost(value: Int) = write(_settings.value.copy(bassBoost = value.coerceIn(0, 100)))

    fun setLoudness(value: Int) = write(_settings.value.copy(loudness = value.coerceIn(0, 100)))

    fun setSpatial(value: Boolean) = write(_settings.value.copy(spatial = value))

    /** 对齐 C# 的「重置」：曲线回平、空间/低频/响度归零，但**不动总开关** —— 用户刚关掉的东西不该被"重置"重新打开 */
    fun reset() = write(
        AudioFxSettings(
            enabled = _settings.value.enabled,
            preset = "flat",
            gains = DoubleArray(bandCount),
        ),
    )

    private fun read(): AudioFxSettings = AudioFxSettings(
        enabled = prefs.getBoolean(KEY_ENABLED, false),
        preset = prefs.getString(KEY_PRESET, "flat") ?: "flat",
        gains = EqualizerRules.decodeGains(prefs.getString(KEY_GAINS, "").orEmpty(), bandCount),
        bassBoost = prefs.getInt(KEY_BASS, 0).coerceIn(0, 100),
        loudness = prefs.getInt(KEY_LOUDNESS, 0).coerceIn(0, 100),
        spatial = prefs.getBoolean(KEY_SPATIAL, false),
    )

    private fun write(next: AudioFxSettings) {
        _settings.value = next
        prefs.edit()
            .putBoolean(KEY_ENABLED, next.enabled)
            .putString(KEY_PRESET, next.preset)
            .putString(KEY_GAINS, EqualizerRules.encodeGains(next.gains))
            .putInt(KEY_BASS, next.bassBoost)
            .putInt(KEY_LOUDNESS, next.loudness)
            .putBoolean(KEY_SPATIAL, next.spatial)
            .apply()
    }

    private companion object {
        const val PREFS = "equalizer"   // 与 desktop_lyric / lyrics 同规则：一个功能一个文件
        const val KEY_ENABLED = "eq_enabled"
        const val KEY_PRESET = "eq_preset"
        const val KEY_GAINS = "eq_band_gains"
        const val KEY_BASS = "eq_bass_boost"
        const val KEY_LOUDNESS = "eq_loudness"
        const val KEY_SPATIAL = "eq_spatial"
    }
}
