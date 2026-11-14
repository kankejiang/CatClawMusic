package com.catclaw.music.kt.ui.equalizer

import androidx.compose.foundation.Canvas
import androidx.compose.foundation.gestures.awaitEachGesture
import androidx.compose.foundation.gestures.awaitFirstDown
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.BoxWithConstraints
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.ExperimentalLayoutApi
import androidx.compose.foundation.layout.FlowRow
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxHeight
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.offset
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
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.setValue
import androidx.compose.runtime.remember
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.geometry.Offset
import androidx.compose.ui.graphics.Brush
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.Path
import androidx.compose.ui.graphics.PathEffect
import androidx.compose.ui.graphics.StrokeCap
import androidx.compose.ui.graphics.drawscope.Stroke
import androidx.compose.ui.input.pointer.pointerInput
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.Dp
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
import java.util.Locale

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
 * 版式照 2026-11 的原型重做：
 * - 总开关提到页头（不再占卡内一行）；
 * - 5 根**竖向推子**与响应曲线共用一套坐标系，曲线节点就是推子中心；
 * - 预设分「常用 / 更多」两组，全部可见，不再横向滚动；
 * - 与频段无关的环绕声 / 淡入淡出单独成「播放效果」，低音与响度归「音效增强」。
 *
 * 能力没给的行**直接不画**，不摆一排拉了没反应的滑杆。没播过歌时 `probed=false`，
 * 这时只说"播放一首歌后自动检测"——把"还没试过"报成"不支持"是假话。
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

        // 页头钉住，只滚内容：这页比屏幕高，整列一起滚会把「返回」和「重置」滚出屏幕。
        // 拖推子时临时关掉外层滚动：verticalScroll 会抢垂直手势，不关就变成「拖推子只滚页面」
        var faderDragging by remember { mutableStateOf(false) }
        Column(
            modifier = Modifier
                .fillMaxWidth()
                .weight(1f)
                .verticalScroll(rememberScrollState(), enabled = !faderDragging),
        ) {

            // ── 均衡器页头：标题 + 总开关
            Row(verticalAlignment = Alignment.CenterVertically) {
                Column(modifier = Modifier.weight(1f), verticalArrangement = Arrangement.spacedBy(2.dp)) {
                    Text(
                        text = stringResource(R.string.soundfx_section_eq),
                        fontSize = 26.sp,
                        fontWeight = FontWeight.Bold,
                        color = Color(palette.textPrimary),
                    )
                    Text(
                        text = stringResource(R.string.soundfx_switch_sub),
                        fontSize = 11.5.sp,
                        color = Color(palette.textSecondary),
                    )
                }
                Switch(
                    checked = settings.enabled,
                    onCheckedChange = { viewModel.setEnabled(it) },
                    colors = SwitchDefaults.colors(
                        checkedThumbColor = Color(palette.backgroundStops.last().argb),
                        checkedTrackColor = Color(palette.primary),
                        uncheckedThumbColor = Color(palette.textSecondary),
                        uncheckedTrackColor = Color(palette.textPrimary).copy(alpha = 0.12f),
                    ),
                )
            }
            Spacer(Modifier.height(14.dp))

            if (caps.probed && !caps.supported) {
                SectionCard(title = stringResource(R.string.soundfx_section_eq)) {
                    Text(
                        text = stringResource(R.string.soundfx_eq_unsupported),
                        fontSize = 13.sp,
                        color = Color(palette.textSecondary),
                    )
                }
            } else {
                FaderPanel(
                    labels = labels,
                    gains = settings.gains,
                    range = range,
                    enabled = settings.enabled,
                    summary = stringResource(
                        presetNameRes(
                            if (settings.preset in EqualizerRules.PRESET_KEYS) settings.preset
                            else SoundEffectsViewModel.PRESET_CUSTOM,
                        ),
                    ),
                    onGain = { i, db -> viewModel.setGain(i, db) },
                    onDragStateChange = { faderDragging = it },
                )

                // ── 预设：两行分组（常用 4 / 更多 5），全部可见，不横滑
                val selectedKey = if (settings.preset in EqualizerRules.PRESET_KEYS) {
                    settings.preset
                } else {
                    SoundEffectsViewModel.PRESET_CUSTOM
                }
                Spacer(Modifier.height(14.dp))
                PresetGroup(
                    title = stringResource(R.string.soundfx_preset_common),
                    keys = EqualizerRules.PRESET_KEYS.take(4),
                    selected = selectedKey,
                    onPick = viewModel::selectPreset,
                )
                Spacer(Modifier.height(8.dp))
                PresetGroup(
                    title = stringResource(R.string.soundfx_preset_more),
                    keys = EqualizerRules.PRESET_KEYS.drop(4),
                    selected = selectedKey,
                    onPick = viewModel::selectPreset,
                )
            }

            Spacer(Modifier.height(18.dp))
            val hasBass = !caps.probed || caps.bassSupported
            val hasLoudness = !caps.probed || caps.loudnessSupported
            val hasSpatial = !caps.probed || caps.spatialSupported

            // ── 播放效果：环绕声 / 淡入淡出 / 时长
            SectionCard(title = stringResource(R.string.soundfx_section_playback)) {
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
                    valueLabel = if (settings.crossfade) {
                        stringResource(R.string.soundfx_seconds, settings.crossfadeSec)
                    } else {
                        "—"
                    },
                    value = settings.crossfadeSec.toFloat(),
                    valueRange = 0f..CrossfadeRules.MAX_WINDOW_SEC.toFloat(),
                    steps = CrossfadeRules.MAX_WINDOW_SEC - 1,
                    enabled = settings.crossfade,
                    onValue = { viewModel.setCrossfadeSec(it.toInt()) },
                )
            }

            Spacer(Modifier.height(12.dp))

            // ── 音效增强：低音 / 响度
            SectionCard(title = stringResource(R.string.soundfx_section_enhance)) {
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
            }
            // 这页在播放页覆盖层里，底部那两栏是浮着的，不留白最后一段滑杆就永远滚不出来
            Spacer(Modifier.height(120.dp))
        }
    }
}

/**
 * 均衡器主体：响应曲线 + 5 根竖向推子共用同一坐标系。
 *
 * 曲线节点就是推子中心，0 dB 与虚线基准重合；推子只改增益，重绘交给 Compose。
 * 关闭时曲线整条不画、推子降透明度，但**保留位置**（下次打开即恢复）。
 */
@Composable
private fun FaderPanel(
    labels: List<String>,
    gains: DoubleArray,
    range: ClosedFloatingPointRange<Double>,
    enabled: Boolean,
    summary: String,
    onGain: (Int, Double) -> Unit,
    onDragStateChange: (Boolean) -> Unit,
) {
    val palette = LocalCatClawPalette.current
    val accent = Color(palette.primary)
    val lo = range.start.toFloat()
    val hi = range.endInclusive.toFloat()
    val total = gains.sum()
    val ink = Color(palette.textPrimary)
    val sub = Color(palette.textSecondary)

    Column(
        modifier = Modifier
            .fillMaxWidth()
            .glassCard(palette = palette, corner = 26)
            .padding(16.dp),
    ) {
        Row(modifier = Modifier.fillMaxWidth(), verticalAlignment = Alignment.CenterVertically) {
            Text(
                text = stringResource(R.string.soundfx_eq_curve),
                fontSize = 10.5.sp,
                letterSpacing = 1.1.sp,
                color = sub,
            )
            Spacer(Modifier.weight(1f))
            Text(
                text = String.format(Locale.US, "%+.1f dB · %s", total, summary),
                fontSize = 10.5.sp,
                color = if (enabled) sub else sub.copy(alpha = 0.5f),
            )
        }
        Spacer(Modifier.height(10.dp))

        Row(modifier = Modifier.fillMaxWidth().height(224.dp)) {
            // 刻度列
            Column(
                modifier = Modifier.width(30.dp).fillMaxHeight().padding(vertical = 16.dp),
                verticalArrangement = Arrangement.SpaceBetween,
                horizontalAlignment = Alignment.End,
            ) {
                listOf(hi, hi / 2f, 0f, lo / 2f, lo).forEach { v ->
                    Text(
                        text = when {
                            v == 0f -> "0"
                            v % 1f == 0f -> if (v > 0f) "+${v.toInt()}" else "−${(-v).toInt()}"
                            else -> String.format(Locale.US, "%+.1f", v).replace("-", "−")
                        },
                        fontSize = 9.sp,
                        color = sub.copy(alpha = 0.75f),
                    )
                }
            }
            Spacer(Modifier.width(6.dp))

            BoxWithConstraints(
                modifier = Modifier
                    .weight(1f)
                    .fillMaxHeight()
                    .padding(vertical = 16.dp),
            ) {
                val plotH: Dp = maxHeight
                Canvas(modifier = Modifier.fillMaxSize()) {
                    val h = size.height
                    val w = size.width
                    listOf(0f, 0.25f, 0.5f, 0.75f, 1f).forEach { f ->
                        val y = h * f
                        if (f == 0.5f) {
                            drawLine(
                                color = Color.White.copy(alpha = 0.16f),
                                start = Offset(0f, y),
                                end = Offset(w, y),
                                strokeWidth = 1f,
                                pathEffect = PathEffect.dashPathEffect(floatArrayOf(6f, 8f)),
                            )
                        } else {
                            drawLine(Color.White.copy(alpha = 0.05f), Offset(0f, y), Offset(w, y), 1f)
                        }
                    }
                    if (!enabled || gains.size < 2) return@Canvas
                    // x 取每根推子的中心（Row 里是等宽 weight，中心在 (i+0.5)/n 处），
                    // 不能用 i/(n-1)：那样端点会贴到画布左右边缘，看着「线没落点上」
                    val pts = gains.mapIndexed { i, g ->
                        val t = ((g.toFloat() - lo) / (hi - lo)).coerceIn(0f, 1f)
                        Offset(x = w * (i + 0.5f) / gains.size, y = (1f - t) * h)
                    }
                    // Catmull-Rom → 三次贝塞尔：每段起终点都是真实节点。
                    // 之前用「二次贝塞尔到中点」的写法，曲线只被控制点拉扯、**不经过节点**，
                    // 看起来就是「线没落点上」。控制点 y 夹在画布内，避免极值时冲出卡片。
                    val path = Path().apply {
                        moveTo(pts.first().x, pts.first().y)
                        for (i in 0 until pts.size - 1) {
                            val p0 = pts.getOrElse(i - 1) { pts[i] }
                            val p1 = pts[i]
                            val p2 = pts[i + 1]
                            val p3 = pts.getOrElse(i + 2) { p2 }
                            val c1 = Offset(
                                p1.x + (p2.x - p0.x) / 6f,
                                (p1.y + (p2.y - p0.y) / 6f).coerceIn(0f, h),
                            )
                            val c2 = Offset(
                                p2.x - (p3.x - p1.x) / 6f,
                                (p2.y - (p3.y - p1.y) / 6f).coerceIn(0f, h),
                            )
                            cubicTo(c1.x, c1.y, c2.x, c2.y, p2.x, p2.y)
                        }
                    }
                    val area = Path().apply {
                        addPath(path)
                        lineTo(w, h)
                        lineTo(0f, h)
                        close()
                    }
                    drawPath(
                        path = area,
                        brush = Brush.verticalGradient(
                            listOf(accent.copy(alpha = 0.26f), accent.copy(alpha = 0f)),
                        ),
                    )
                    drawPath(path, color = accent, style = Stroke(width = 2.4.dp.toPx()))
                }

                Row(modifier = Modifier.fillMaxSize()) {
                    labels.forEachIndexed { index, label ->
                        val g = gains.getOrElse(index) { 0.0 }.toFloat()
                        val t = ((g - lo) / (hi - lo)).coerceIn(0f, 1f)
                        val thumbY: Dp = plotH * (1f - t)
                        BoxWithConstraints(
                            modifier = Modifier
                                .weight(1f)
                                .fillMaxHeight()
                                .pointerInput(index, lo, hi, enabled) {
                                    if (!enabled) return@pointerInput
                                    fun update(y: Float) {
                                        val tt = (y / size.height).coerceIn(0f, 1f)
                                        val db = hi - tt * (hi - lo)
                                        onGain(index, (db * 2f).toInt() / 2.0)
                                    }
                                    // 主动消费按下事件：否则外层 verticalScroll 会把手势抢走，
                                    // 表现成「拖推子只滚动页面、增益不动」
                                    awaitEachGesture {
                                        val down = awaitFirstDown(requireUnconsumed = false)
                                        down.consume()
                                        onDragStateChange(true)
                                        update(down.position.y)
                                        while (true) {
                                            val event = awaitPointerEvent()
                                            val change = event.changes.firstOrNull { it.id == down.id } ?: break
                                            if (!change.pressed) break
                                            update(change.position.y)
                                            change.consume()
                                        }
                                        onDragStateChange(false)
                                    }
                                },
                        ) {
                            Canvas(modifier = Modifier.fillMaxSize()) {
                                val cx = size.width / 2f
                                val zeroY = size.height * (hi / (hi - lo))
                                val ty = size.height * (1f - t)
                                drawLine(
                                    color = Color.White.copy(alpha = 0.08f),
                                    start = Offset(cx, 0f),
                                    end = Offset(cx, size.height),
                                    strokeWidth = 4.dp.toPx(),
                                    cap = StrokeCap.Round,
                                )
                                drawLine(
                                    color = accent.copy(alpha = if (enabled) 0.85f else 0.25f),
                                    start = Offset(cx, zeroY),
                                    end = Offset(cx, ty),
                                    strokeWidth = 4.dp.toPx(),
                                    cap = StrokeCap.Round,
                                )
                                drawCircle(
                                    Color.White.copy(alpha = if (enabled) 1f else 0.55f),
                                    radius = 9.dp.toPx(),
                                    center = Offset(cx, ty),
                                )
                                drawCircle(
                                    accent,
                                    radius = 9.dp.toPx(),
                                    center = Offset(cx, ty),
                                    style = Stroke(width = 2.dp.toPx()),
                                )
                            }
                            Text(
                                text = String.format(Locale.US, "%+.1f", g),
                                fontSize = 9.5.sp,
                                color = if (enabled) ink.copy(alpha = 0.72f) else sub.copy(alpha = 0.45f),
                                modifier = Modifier
                                    .align(Alignment.TopCenter)
                                    .offset(y = (thumbY - 30.dp).coerceAtLeast(0.dp)),
                            )
                            Text(
                                text = label,
                                fontSize = 9.sp,
                                color = sub.copy(alpha = 0.85f),
                                modifier = Modifier.align(Alignment.BottomCenter).offset(y = 12.dp),
                            )
                        }
                    }
                }
            }
        }

        Spacer(Modifier.height(18.dp))
        Row(modifier = Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.SpaceBetween) {
            listOf(
                stringResource(R.string.soundfx_band_low) to (gains.getOrElse(0) { 0.0 } + gains.getOrElse(1) { 0.0 }) / 2,
                stringResource(R.string.soundfx_band_mid) to gains.getOrElse(2) { 0.0 },
                stringResource(R.string.soundfx_band_high) to (gains.getOrElse(3) { 0.0 } + gains.getOrElse(4) { 0.0 }) / 2,
            ).forEach { (name, v) ->
                Text(
                    text = "$name ${String.format(Locale.US, "%+.1f", v)}",
                    fontSize = 10.sp,
                    color = sub,
                )
            }
        }
    }
}

@Composable
private fun PresetGroup(
    title: String,
    keys: List<String>,
    selected: String,
    onPick: (String) -> Unit,
) {
    val palette = LocalCatClawPalette.current
    Column(verticalArrangement = Arrangement.spacedBy(6.dp)) {
        Text(
            text = title,
            fontSize = 10.sp,
            letterSpacing = 0.8.sp,
            color = Color(palette.textSecondary).copy(alpha = 0.8f),
        )
        FlowRow(
            modifier = Modifier.fillMaxWidth(),
            horizontalArrangement = Arrangement.spacedBy(8.dp),
            verticalArrangement = Arrangement.spacedBy(8.dp),
        ) {
            keys.forEach { key ->
                GlassChip(
                    label = stringResource(presetNameRes(key)),
                    selected = key == selected,
                    onClick = { onPick(key) },
                )
            }
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
 * 只有「淡入淡出时长」这种依赖另一个开关的项才传 enabled=false。
 */
@Composable
private fun ValueSlider(
    title: String,
    valueLabel: String,
    value: Float,
    valueRange: ClosedFloatingPointRange<Float>,
    steps: Int,
    enabled: Boolean = true,
    onValue: (Float) -> Unit,
) {
    val palette = LocalCatClawPalette.current
    Column(modifier = Modifier.fillMaxWidth().padding(top = 10.dp)) {
        Row(modifier = Modifier.fillMaxWidth(), verticalAlignment = Alignment.CenterVertically) {
            Text(
                text = title,
                style = MaterialTheme.typography.bodyMedium,
                color = Color(palette.textPrimary).copy(alpha = if (enabled) 1f else 0.45f),
                modifier = Modifier.weight(1f),
            )
            Text(
                text = valueLabel,
                style = MaterialTheme.typography.labelMedium,
                color = if (enabled) Color(highlightOnGlassCard(palette)) else Color(palette.textSecondary).copy(alpha = 0.5f),
            )
        }
        Slider(
            value = value.coerceIn(valueRange.start, valueRange.endInclusive),
            onValueChange = onValue,
            valueRange = valueRange,
            steps = steps,
            enabled = enabled,
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
