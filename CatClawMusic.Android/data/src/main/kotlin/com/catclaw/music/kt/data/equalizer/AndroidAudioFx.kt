package com.catclaw.music.kt.data.equalizer

import android.media.audiofx.BassBoost
import android.media.audiofx.Equalizer
import android.media.audiofx.LoudnessEnhancer
import com.catclaw.music.kt.core.diagnostic.AppLog
import com.catclaw.music.kt.core.equalizer.EqualizerRules
import javax.inject.Inject
import javax.inject.Singleton

/**
 * 安卓原生音效：`Equalizer` + `BassBoost` + `LoudnessEnhancer` 挂到播放器的音频会话上。
 * 对应 C# `AndroidEqualizerService`（那份还带 Virtualizer 与 FFmpeg 烘焙模式，见下）。
 *
 * 两处按平台事实做的处理：
 * 1. **设备给的频段才是真相**。我们存的曲线是 5 段（60/250/1k/4k/12k），
 *    但不同 DSP 给的段数与中心频率都不一样 —— 应用前先把曲线重采样到设备实际频段上，
 *    否则"把第 3 段推到 1kHz"可能推到设备的任意一个频点。
 * 2. 挂不上就是挂不上。模拟器与部分定制 ROM 没有 Equalizer 效果，
 *    这时 `supported=false`，页面要老实显示"本设备不支持"，而不是摆一排拉了没反应的滑杆。
 *
 * 没移植的两项：Virtualizer（C# 挂了但 UI 上没有对应控件）与 FFmpeg 烘焙 10 段（本端没有那台转码引擎）。
 */
@Singleton
class AndroidAudioFx @Inject constructor() {

    /** 设备到底给不给这些效果 —— 页面据此决定画滑杆还是画「本设备不支持」 */
    data class Capabilities(
        val supported: Boolean = false,
        val frequencies: IntArray = IntArray(0),
        val minDb: Double = EqualizerRules.MIN_GAIN_DB,
        val maxDb: Double = EqualizerRules.MAX_GAIN_DB,
        val bassSupported: Boolean = false,
        val loudnessSupported: Boolean = false,
    ) {
        override fun equals(other: Any?): Boolean = other is Capabilities &&
            supported == other.supported && frequencies.contentEquals(other.frequencies) &&
            minDb == other.minDb && maxDb == other.maxDb &&
            bassSupported == other.bassSupported && loudnessSupported == other.loudnessSupported

        override fun hashCode(): Int =
            (((supported.hashCode() * 31 + frequencies.contentHashCode()) * 31 + minDb.hashCode()) * 31 +
                maxDb.hashCode()) * 31 + bassSupported.hashCode()
    }

    private var equalizer: Equalizer? = null
    private var bass: BassBoost? = null
    private var loudness: LoudnessEnhancer? = null
    private var attachedSession = -1

    @Volatile
    private var caps = Capabilities()

    fun capabilities(): Capabilities = caps

    /** 重复调用安全：会话没变就不重建（ExoPlayer 的 sessionId 在整个播放生命周期内是稳定的） */
    @Suppress("DEPRECATION")
    fun attach(sessionId: Int) {
        if (sessionId <= 0) return
        if (sessionId == attachedSession && equalizer != null) return
        release()
        attachedSession = sessionId
        var bands = 0
        var freqs = IntArray(0)
        runCatching {
            val eq = Equalizer(0, sessionId)
            bands = eq.numberOfBands.toInt()
            // 中心频率单位是毫赫，原生给的是「这台设备有几段、分别在哪」，页面要按这个画滑杆
            freqs = IntArray(bands) { i -> runCatching { eq.getCenterFreq(i.toShort()) / 1000 }.getOrDefault(0) }
            val range = eq.bandLevelRange
            equalizer = eq
            AppLog.i(TAG, "attached session=$sessionId bands=$bands freqs=${freqs.joinToString(",")}")
        }.onFailure { AppLog.w(TAG, "均衡器不可用（设备没有该效果）", it) }
        runCatching { bass = BassBoost(0, sessionId) }.onFailure { bass = null }
        runCatching { loudness = LoudnessEnhancer(sessionId) }.onFailure { loudness = null }
        val bassOk = bass?.let { b -> runCatching { b.strengthSupported }.getOrDefault(false) } == true
        val range = runCatching { equalizer?.bandLevelRange }.getOrNull()
        caps = Capabilities(
            supported = equalizer != null && bands > 0,
            frequencies = freqs,
            minDb = (range?.getOrNull(0) ?: -1200) / 100.0,
            maxDb = (range?.getOrNull(1) ?: 1200) / 100.0,
            bassSupported = bassOk,
            loudnessSupported = loudness != null,
        )
        AppLog.i(TAG, "capabilities supported=${caps.supported} bass=${caps.bassSupported} loud=${caps.loudnessSupported}")
    }

    @Suppress("DEPRECATION")
    fun apply(settings: AudioFxSettings) {
        equalizer?.let { eq ->
            val deviceFreqs = if (caps.frequencies.isEmpty()) EqualizerRules.NATIVE_FREQUENCIES else caps.frequencies
            val curve = EqualizerRules.resample(settings.gains, EqualizerRules.NATIVE_FREQUENCIES, deviceFreqs)
            runCatching {
                eq.enabled = settings.enabled
                if (settings.enabled) {
                    curve.forEachIndexed { i, db -> eq.setBandLevel(i.toShort(), toMillibels(db)) }
                }
            }.onFailure { AppLog.w(TAG, "应用均衡器失败", it) }
        }
        // 低频增强：原生强度是 0..1000（千分比），UI 上是 0..100；设备不支持时这两个调用都落不到实例上
        runCatching { bass?.enabled = settings.enabled && settings.bassBoost > 0 }
        runCatching { bass?.setStrength((settings.bassBoost * 10).coerceIn(0, 1000).toShort()) }
        // 响度增益单位是毫贝；总开关关着时给 0（= 不增强）
        runCatching {
            loudness?.enabled = settings.enabled && settings.loudness > 0
            loudness?.setTargetGain(if (settings.enabled) (settings.loudness * 12).coerceIn(0, 1200) else 0)
        }
    }

    fun release() {
        runCatching { equalizer?.release() }
        runCatching { bass?.release() }
        runCatching { loudness?.release() }
        equalizer = null
        bass = null
        loudness = null
        attachedSession = -1
    }

    /** dB → 设备毫贝：夹在设备自己给的范围内，越界的部分直接丢掉，不让原生层报错 */
    private fun toMillibels(db: Double): Short {
        val lo = (caps.minDb * 100).toInt()
        val hi = (caps.maxDb * 100).toInt()
        return EqualizerRules.toMillibels(db, lo.toShort(), hi.toShort())
    }

    private companion object {
        const val TAG = "CatClawAudioFx"
    }
}
