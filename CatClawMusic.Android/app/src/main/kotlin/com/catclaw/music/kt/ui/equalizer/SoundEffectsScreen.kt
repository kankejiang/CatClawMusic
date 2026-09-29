package com.catclaw.music.kt.ui.equalizer

import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.ExperimentalLayoutApi
import androidx.compose.foundation.layout.FlowRow
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.verticalScroll
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Slider
import androidx.compose.material3.SliderDefaults
import androidx.compose.material3.Switch
import androidx.compose.material3.SwitchDefaults
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.remember
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import androidx.hilt.navigation.compose.hiltViewModel
import androidx.lifecycle.compose.collectAsStateWithLifecycle
import com.catclaw.music.kt.R
import com.catclaw.music.kt.core.equalizer.EqualizerRules
import com.catclaw.music.kt.core.playback.CrossfadeRules
import com.catclaw.music.kt.data.equalizer.AndroidAudioFx
import com.catclaw.music.kt.ui.theme.BackTile
import com.catclaw.music.kt.ui.theme.GlassChip
import com.catclaw.music.kt.ui.theme.GlassSwitch
import com.catclaw.music.kt.ui.theme.LocalCatClawPalette
import com.catclaw.music.kt.ui.theme.PillLabel
import com.catclaw.music.kt.ui.theme.glassCard
import com.catclaw.music.kt.ui.theme.highlightOnGlassCard

/** 预设 key → 文案。名字属于文案，所以走 strings.xml，不留在 :core 里当硬编码中文 */
internal fun presetNameRes(key: String): Int = when (key) {
    "custom" -> R.string.preset_custom
    "rock" -> R.string.preset_rock
    "jazz" -> R.string.preset_jazz
    "pop" -> R.string.preset_pop
    "classical" -> R.string.preset_classical
    "hiphop" -> R.string.preset_hiphop
    "blues" -> R.string.preset_blues
    "electronic" -> R.string.preset_electronic
    "metal" -> R.string.preset_metal
    else -> R.string.preset_flat
}

/**
 * 音效页 —— 对应 MAUI 的 `SoundEffectsPage` + `EqualizerPage`，两页合一。
 *
 * 合一的理由：这页在安卓侧只出现一次，中间多一跳不省版面，而频段曲线和低频/响度
 * 本来就在改同一只耳朵听到的东西。
 *
 * 能力没给的行**直接不画**，不摆一排拉了没反应的滑杆。没播过歌时 `probed=false`，
 * 这时只说"播放一首歌后自动检测"——把"还没试过"报成"不支持"是假话。
 * 设置照常落盘，会话就绪时由播放器那条接线一次性应用。
 */
@OptIn(ExperimentalLayoutApi::class)
@Composable
fun SoundEffectsScreen(
    onBack: () -> Unit,
    viewModel: SoundEffectsViewModel = hiltViewModel(),
) {
    val palette = LocalCatClawPalette.current
    val settings by viewModel.settings.collectAsStateWithLifecycle()
    val caps = remember { viewModel.capabilities() }
    val range = remember(caps) { viewModel.gainRange() }
    val labels = remember { viewModel.bandLabels() }

    Column(
        modifier = Modifier
            .fillMaxSize()
            .padding(horizontal = 16.dp),
    ) {
        Spacer(Modifier.height(12.dp))
        Row(verticalAlignment = Alignment.CenterVertically) {
            BackTile(onBack = onBack, description = stringResource(R.string.GoBack))
            Spacer(Modifier.width(12.dp))
            Text(
                text = stringResource(R.string.soundfx_title),
                fontSize = 24.sp,
                fontWeight = FontWeight.Bold,
                color = Color(palette.textPrimary),
                modifier = Modifier.weight(1f),
            )
            PillLabel(
                text = stringResource(R.string.soundfx_reset),
                onClick = { viewModel.reset() },
                filled = false,
            )
        }
        val capLine = capabilityLine(caps)
        if (capLine != null) {
            Spacer(Modifier.height(6.dp))
            Text(text = capLine, fontSize = 12.sp, color = Color(palette.textSecondary))
        }
        Spacer(Modifier.height(12.dp))
        // 页头钉住，只滚内容：这页比屏幕高，整列一起滚会把「返回」和「重置」滚出屏幕，
        // 真机上滑到频段滑杆之后就只能靠系统返回键离开一页（覆盖层没有自己的 back 处理）
        Column(modifier = Modifier.fillMaxWidth().weight(1f).verticalScroll(rememberScrollState())) {
            SectionCard(title = stringResource(R.string.soundfx_section_eq)) {
                GlassSwitch(
                    label = stringResource(R.string.soundfx_switch),
                    subtitle = stringResource(R.string.soundfx_switch_sub),
                    checked = settings.enabled,
                    onChange = { viewModel.setEnabled(it) },
                )
                if (caps.probed && !caps.supported) {
                    Text(
                        text = stringResource(R.string.soundfx_eq_unsupported),
                        fontSize = 13.sp,
                        color = Color(palette.textSecondary),
                        modifier = Modifier.padding(top = 10.dp),
                    )
                } else {
                    FlowRow(
                        modifier = Modifier.fillMaxWidth().padding(top = 12.dp),
                        horizontalArrangement = Arrangement.spacedBy(8.dp),
                        verticalArrangement = Arrangement.spacedBy(8.dp),
                    ) {
                        // 存的名字不在这 9 格里（例如「重置」后是 flat）就高亮「自定义」：
                        // 一条谁都没亮着的芯片组会让人以为重置没生效（C# 同规则）
                        val selectedKey = if (settings.preset in EqualizerRules.PRESET_KEYS) {
                            settings.preset
                        } else {
                            SoundEffectsViewModel.PRESET_CUSTOM
                        }
                        EqualizerRules.PRESET_KEYS.forEach { key ->
                            GlassChip(
                                label = stringResource(presetNameRes(key)),
                                selected = key == selectedKey,
                                onClick = { viewModel.selectPreset(key) },
                            )
                        }
                    }
                    labels.forEachIndexed { i, name ->
                        ValueSlider(
                            title = name,
                            valueLabel = String.format("%+.1f dB", settings.gains.getOrElse(i) { 0.0 }),
                            value = settings.gains.getOrElse(i) { 0.0 }.toFloat(),
                            valueRange = range.start.toFloat()..range.endInclusive.toFloat(),
                            steps = 24,
                            onValue = { viewModel.setGain(i, it.toDouble()) },
                        )
                    }
                }
            }
            Spacer(Modifier.height(12.dp))

            // 设备没给的能力就不画那一行；淡入淡出是纯音量曲线，任何设备都有，所以恒在
            val hasBass = !caps.probed || caps.bassSupported
            val hasLoudness = !caps.probed || caps.loudnessSupported
            val hasSpatial = !caps.probed || caps.spatialSupported
            SectionCard(title = stringResource(R.string.soundfx_section_other)) {
                if (hasBass) {
                    ValueSlider(
                        title = stringResource(R.string.soundfx_bass),
                        valueLabel = "${settings.bassBoost}",
                        value = settings.bassBoost.toFloat(),
                        valueRange = 0f..100f,
                        steps = 9,
                        onValue = { viewModel.setBassBoost(it.toInt()) },
                    )
                }
                if (hasLoudness) {
                    ValueSlider(
                        title = stringResource(R.string.soundfx_loudness),
                        valueLabel = "${settings.loudness}",
                        value = settings.loudness.toFloat(),
                        valueRange = 0f..100f,
                        steps = 9,
                        onValue = { viewModel.setLoudness(it.toInt()) },
                    )
                }
                // 虚拟环绕独立于总开关：它是空间效果，与频段曲线是两件事（C# 同规则）
                if (hasSpatial) {
                    GlassSwitch(
                        label = stringResource(R.string.soundfx_spatial),
                        subtitle = stringResource(R.string.soundfx_spatial_sub),
                        checked = settings.spatial,
                        onChange = { viewModel.setSpatial(it) },
                    )
                }
                GlassSwitch(
                    label = stringResource(R.string.soundfx_crossfade),
                    subtitle = stringResource(R.string.soundfx_crossfade_sub),
                    checked = settings.crossfade,
                    onChange = { viewModel.setCrossfade(it) },
                )
                ValueSlider(
                    title = stringResource(R.string.soundfx_crossfade_dur),
                    valueLabel = stringResource(R.string.soundfx_seconds, settings.crossfadeSec),
                    value = settings.crossfadeSec.toFloat(),
                    valueRange = 0f..CrossfadeRules.MAX_WINDOW_SEC.toFloat(),
                    steps = CrossfadeRules.MAX_WINDOW_SEC - 1,
                    onValue = { viewModel.setCrossfadeSec(it.toInt()) },
                )
            }
            // 这页在播放页覆盖层里，底部那两栏是浮着的，不留白最后一段滑杆就永远滚不出来
            Spacer(Modifier.height(120.dp))
        }
    }
}

@Composable
private fun SectionCard(title: String, content: @Composable () -> Unit) {
    val palette = LocalCatClawPalette.current
    Column(
        modifier = Modifier
            .fillMaxWidth()
            .glassCard(palette = palette, corner = 24)
            .padding(16.dp),
    ) {
        Text(text = title, fontSize = 15.sp, fontWeight = FontWeight.Bold, color = Color(palette.textPrimary))
        content()
    }
}


/**
 * 带当前值标签的滑杆。值标签落在玻璃卡上，所以走 highlightOnGlassCard 而不是次要色。
 *
 * 滑杆**不因总开关关闭而禁用**：调曲线这件事本来就该在听不到的时候先摆好，
 * 而且禁用态的 M3 滑杆画成一根竖条（真机截图里五根一排，看着像坏了）。
 */
@Composable
private fun ValueSlider(
    title: String,
    valueLabel: String,
    value: Float,
    valueRange: ClosedFloatingPointRange<Float>,
    steps: Int,
    onValue: (Float) -> Unit,
) {
    val palette = LocalCatClawPalette.current
    Column(modifier = Modifier.fillMaxWidth().padding(top = 10.dp)) {
        Row(modifier = Modifier.fillMaxWidth(), verticalAlignment = Alignment.CenterVertically) {
            Text(
                text = title,
                style = MaterialTheme.typography.bodyMedium,
                color = Color(palette.textPrimary),
                modifier = Modifier.weight(1f),
            )
            Text(
                text = valueLabel,
                style = MaterialTheme.typography.labelMedium,
                color = Color(highlightOnGlassCard(palette)),
            )
        }
        Slider(
            value = value.coerceIn(valueRange.start, valueRange.endInclusive),
            onValueChange = onValue,
            valueRange = valueRange,
            steps = steps,
            colors = SliderDefaults.colors(
                thumbColor = Color(palette.primary),
                activeTrackColor = Color(palette.primary),
            ),
        )
    }
}

/** 挂载成功给一行本机频段实况；没挂载只说"待检测"；明确不支持时交给卡片里那句话 */
@Composable
private fun capabilityLine(caps: AndroidAudioFx.Capabilities): String? = when {
    !caps.probed -> stringResource(R.string.soundfx_pending_probe)
    !caps.supported -> null
    else -> stringResource(
        R.string.soundfx_eq_bands,
        caps.frequencies.size,
        caps.frequencies.joinToString("/") { EqualizerRules.label(it) },
    )
}
