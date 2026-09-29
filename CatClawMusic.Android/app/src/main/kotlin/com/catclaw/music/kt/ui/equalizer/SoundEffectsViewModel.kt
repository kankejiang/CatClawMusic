package com.catclaw.music.kt.ui.equalizer

import androidx.lifecycle.ViewModel
import com.catclaw.music.kt.core.equalizer.EqualizerRules
import com.catclaw.music.kt.data.equalizer.AndroidAudioFx
import com.catclaw.music.kt.data.equalizer.AudioFxPreferences
import com.catclaw.music.kt.data.equalizer.AudioFxSettings
import dagger.hilt.android.lifecycle.HiltViewModel
import javax.inject.Inject
import kotlinx.coroutines.flow.StateFlow

/**
 * 音效页状态。
 *
 * 每个写方法写完立刻 `push()` 一次把设置打到硬件上 —— 这页要的是"拖着滑杆就能听见"，
 * 等离开页面再生效等于没有实时反馈。`AndroidAudioFx.apply` 只在主线程安全，
 * `viewModelScope` 就是 Main.immediate，这里不再切线程。
 *
 * 会话还没就绪时 apply 是空操作（效果还没挂上），设置已经落盘，
 * 等播放器 STATE_READY 时由 DI 那条接线一次性应用，不会丢。
 */
@HiltViewModel
class SoundEffectsViewModel @Inject constructor(
    private val prefs: AudioFxPreferences,
    private val fx: AndroidAudioFx,
) : ViewModel() {

    val settings: StateFlow<AudioFxSettings> = prefs.settings

    /** 进页面时读一次。设备能力只在挂载那一刻探测过，没有"何时就绪"的事件可订阅，读快照够用 */
    fun capabilities(): AndroidAudioFx.Capabilities = fx.capabilities()

    /** 曲线上限：设备给了范围就用设备的，没挂上时退回 ±12dB（与存储层同一套判据） */
    fun gainRange(): ClosedFloatingPointRange<Double> {
        val caps = fx.capabilities()
        val lo = minOf(caps.minDb, caps.maxDb)
        val hi = maxOf(caps.minDb, caps.maxDb)
        return lo..hi
    }

    fun setEnabled(value: Boolean) {
        prefs.setEnabled(value)
        push()
    }

    fun selectPreset(key: String) {
        // 点「自定义」不是选一个预设，而是承认"这条曲线是我手调的"——没有曲线可套，所以只改名字
        if (key == PRESET_CUSTOM) prefs.markCustom() else prefs.setPreset(key)
        push()
    }

    fun setGain(index: Int, db: Double) {
        prefs.setGain(index, db)
        push()
    }

    fun setBassBoost(value: Int) {
        prefs.setBassBoost(value)
        push()
    }

    fun setLoudness(value: Int) {
        prefs.setLoudness(value)
        push()
    }

    fun setSpatial(value: Boolean) {
        prefs.setSpatial(value)
        push()
    }

    /**
     * 淡入淡出不经过 audiofx（它是"跟着进度改音量"，归播放器管），
     * 所以这里只写设置，由 PlaybackModule 那条收集流同步进控制器 —— 不 push。
     */
    fun setCrossfade(value: Boolean) {
        prefs.setCrossfade(value)
    }

    fun setCrossfadeSec(value: Int) {
        prefs.setCrossfadeSec(value)
    }

    fun reset() {
        prefs.reset()
        push()
    }

    /** 页面显示用的频段标签：滑杆编辑的是存下来的 5 段曲线，标签要和存储同源 */
    fun bandLabels(): List<String> = EqualizerRules.NATIVE_FREQUENCIES.map { EqualizerRules.label(it) }

    private fun push() = fx.apply(prefs.settings.value)

    companion object {
        const val PRESET_CUSTOM = "custom"
    }
}
