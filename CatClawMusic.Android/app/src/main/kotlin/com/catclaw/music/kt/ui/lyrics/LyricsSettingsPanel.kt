package com.catclaw.music.kt.ui.lyrics

import android.graphics.Bitmap
import androidx.compose.animation.core.EaseInOutCubic
import androidx.compose.animation.core.animateFloatAsState
import androidx.compose.animation.core.tween
import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.clickable
import androidx.compose.foundation.gestures.animateScrollBy
import androidx.compose.foundation.interaction.DragInteraction
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.BoxWithConstraints
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.PaddingValues
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxHeight
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.statusBarsPadding
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.rememberLazyListState
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material3.Icon
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Slider
import androidx.compose.material3.SliderDefaults
import androidx.compose.material3.Switch
import androidx.compose.material3.SwitchDefaults
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.produceState
import androidx.compose.runtime.DisposableEffect
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableIntStateOf
import androidx.compose.runtime.derivedStateOf
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.State
import androidx.compose.runtime.remember
import androidx.compose.runtime.rememberUpdatedState
import androidx.compose.runtime.setValue
import androidx.compose.runtime.withFrameNanos
import androidx.compose.runtime.snapshotFlow
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.BlurredEdgeTreatment
import androidx.compose.ui.draw.blur
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.toArgb
import androidx.compose.ui.graphics.graphicsLayer
import androidx.compose.ui.layout.onSizeChanged
import androidx.compose.ui.platform.LocalDensity
import androidx.compose.ui.platform.LocalLifecycleOwner
import androidx.compose.ui.res.painterResource
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import androidx.hilt.navigation.compose.hiltViewModel
import androidx.lifecycle.Lifecycle
import androidx.lifecycle.LifecycleEventObserver
import androidx.lifecycle.compose.collectAsStateWithLifecycle
import com.catclaw.music.kt.R
import com.catclaw.music.kt.data.lyrics.LyricsSettings
import com.catclaw.music.kt.ui.lyrics.LyricLineText
import com.catclaw.music.kt.ui.lyrics.LyricsViewModel
import com.catclaw.music.kt.ui.lyrics.lyricBlurDp
import com.catclaw.music.kt.ui.player.CoverTile
import com.catclaw.music.kt.ui.player.rememberCover
import com.catclaw.music.kt.ui.theme.GlassChip
import com.catclaw.music.kt.core.theme.CoverFlowProcessor
import com.catclaw.music.kt.ui.theme.glassCard
import com.catclaw.music.kt.ui.theme.highlightOnGlassCard
import com.catclaw.music.kt.ui.theme.inkOnGradient
import com.catclaw.music.kt.ui.theme.LocalCatClawPalette
import com.catclaw.music.kt.ui.theme.artworkWash
import com.catclaw.music.kt.ui.theme.catClawBackground
import com.catclaw.music.kt.ui.theme.rememberArtworkTint
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.delay
import kotlinx.coroutines.withContext
import kotlinx.coroutines.flow.distinctUntilChanged
import kotlinx.coroutines.flow.drop
import kotlin.math.abs
import kotlin.math.roundToInt

/** 全屏歌词页的设置面板（从 FullLyricsScreen.kt 拆出，参数化无状态） */
@Composable
internal fun LyricsSettingsPanel(
    lineMode: Boolean,
    sourceMode: com.catclaw.music.kt.data.lyrics.LyricsSourceMode,
    alignment: Int,
    fontSizeSp: Float,
    removeEmpty: Boolean,
    translation: Boolean,
    roma: Boolean,
    onLineMode: () -> Unit,
    onSource: (com.catclaw.music.kt.data.lyrics.LyricsSourceMode) -> Unit,
    onAlignment: (Int) -> Unit,
    onFontSize: (Float) -> Unit,
    onRemoveEmpty: (Boolean) -> Unit,
    onTranslation: (Boolean) -> Unit,
    onRoma: (Boolean) -> Unit,
    onClose: () -> Unit,
) {
    val palette = LocalCatClawPalette.current
    val surface = Color(palette.backgroundStops.last().argb)
    val primary = Color(palette.textPrimary)

    val sheetShape = RoundedCornerShape(topStart = 26.dp, topEnd = 26.dp)
    Box(modifier = Modifier.fillMaxSize().clickable(onClick = onClose)) {
        Column(
            modifier = Modifier
                .align(Alignment.BottomCenter)
                .fillMaxWidth()
                .clip(sheetShape)
                .background(surface)
                .border(1.dp, primary.copy(alpha = 0.16f), sheetShape)
                .clickable(enabled = false) {}
                .padding(horizontal = 20.dp, vertical = 8.dp),
        ) {
            Box(
                modifier = Modifier.fillMaxWidth().padding(bottom = 8.dp),
                contentAlignment = Alignment.Center,
            ) {
                Box(
                    modifier = Modifier
                        .size(width = 38.dp, height = 4.dp)
                        .clip(RoundedCornerShape(999.dp))
                        .background(primary.copy(alpha = 0.22f)),
                )
            }
            Text(stringResource(R.string.lyric_panel), style = MaterialTheme.typography.titleMedium, color = primary)
            Spacer(Modifier.height(10.dp))
            Text(stringResource(R.string.lyric_mode), style = MaterialTheme.typography.labelMedium, color = Color(palette.textSecondary))
            ChipRow(
                labels = listOf(
                    stringResource(R.string.lyric_mode_word) to 1,
                    stringResource(R.string.lyric_mode_line) to 0,
                ),
                selected = if (lineMode) 1 else 0,
                onPick = { onLineMode() },
            )
            Text(stringResource(R.string.lyric_source), style = MaterialTheme.typography.labelMedium, color = Color(palette.textSecondary))
            ChipRow(
                labels = listOf(
                    stringResource(R.string.lyric_source_auto) to 0,
                    stringResource(R.string.lyric_source_embedded) to 1,
                    stringResource(R.string.lyric_source_external) to 2,
                ),
                selected = sourceMode.ordinal,
                onPick = { onSource(com.catclaw.music.kt.data.lyrics.LyricsSourceMode.entries[it]) },
            )
            Text(stringResource(R.string.lyric_align), style = MaterialTheme.typography.labelMedium, color = Color(palette.textSecondary))
            ChipRow(
                labels = listOf(
                    stringResource(R.string.lyric_align_left) to 0,
                    stringResource(R.string.lyric_align_center) to 1,
                    stringResource(R.string.lyric_align_right) to 2,
                ),
                selected = alignment,
                onPick = onAlignment,
            )
            // 字号滑杆对齐 MAUI BuildFontSizeSlider（FullLyricsPage.Settings.cs:224-280）：
            // 章节标题 + 右对齐主色粗体当前值独立一行 + 底部「A」/最大值两端标注
            Text(
                text = stringResource(R.string.lyric_font),
                style = MaterialTheme.typography.labelMedium,
                color = Color(palette.textSecondary),
            )
            Text(
                text = fontSizeSp.toInt().toString(),
                style = MaterialTheme.typography.labelLarge,
                fontWeight = FontWeight.Bold,
                // 主色当字色必须过可读性判据（VisualLanguageTest）：不足 3:1 退回正文色
                color = Color(highlightOnGlassCard(palette)),
                textAlign = TextAlign.End,
                modifier = Modifier.fillMaxWidth(),
            )
            Slider(
                value = fontSizeSp.coerceIn(LyricsSettings.MIN_FONT, LyricsSettings.MAX_FONT),
                onValueChange = onFontSize,
                valueRange = LyricsSettings.MIN_FONT..LyricsSettings.MAX_FONT,
                steps = (LyricsSettings.MAX_FONT - LyricsSettings.MIN_FONT).toInt() - 1,
                // 这一页在浅色主题下也钉成深色，所以滑杆不能沿用环境色
                colors = SliderDefaults.colors(
                    thumbColor = Color(palette.primary),
                    activeTrackColor = Color(palette.primary),
                    inactiveTrackColor = Color(palette.textPrimary).copy(alpha = 0.18f),
                ),
            )
            // MAUI rangeGrid：左「A」右最大值（textHint 11sp；本端调色板无 textHint 档，取 textSecondary 降透明）
            Row(modifier = Modifier.fillMaxWidth()) {
                Text(text = "A", fontSize = 11.sp, color = Color(palette.textSecondary).copy(alpha = 0.6f))
                Spacer(Modifier.weight(1f))
                Text(
                    text = LyricsSettings.MAX_FONT.toInt().toString(),
                    fontSize = 11.sp,
                    color = Color(palette.textSecondary).copy(alpha = 0.6f),
                )
            }
            ToggleRow(
                label = stringResource(R.string.lyric_remove_empty),
                checked = removeEmpty,
                onChecked = onRemoveEmpty,
                description = stringResource(R.string.lyric_remove_empty_desc),
            )
            ToggleRow(stringResource(R.string.lyric_show_translation), translation, onTranslation)
            ToggleRow(stringResource(R.string.lyric_show_roma), roma, onRoma)
        }
    }
}

@Composable
internal fun ChipRow(labels: List<Pair<String, Int>>, selected: Int, onPick: (Int) -> Unit) {
    Row(horizontalArrangement = Arrangement.spacedBy(8.dp), modifier = Modifier.padding(vertical = 6.dp)) {
        labels.forEach { (label, value) ->
            GlassChip(label = label, selected = value == selected, onClick = { onPick(value) })
        }
    }
}

@Composable
internal fun ToggleRow(label: String, checked: Boolean, onChecked: (Boolean) -> Unit, description: String? = null) {
    val palette = LocalCatClawPalette.current
    Row(verticalAlignment = Alignment.CenterVertically, modifier = Modifier.fillMaxWidth()) {
        Column(modifier = Modifier.weight(1f)) {
            Text(
                text = label,
                style = MaterialTheme.typography.bodyMedium,
                color = Color(palette.textPrimary),
            )
            // 灰色副描述（MAUI BuildToggleSwitch 的 descLabel：13sp textSecondary）
            if (description != null) {
                Text(
                    text = description,
                    style = MaterialTheme.typography.bodySmall,
                    color = Color(palette.textSecondary),
                )
            }
        }
        Switch(
            checked = checked,
            onCheckedChange = onChecked,
            // 这一页在浅色主题下也钉成深色，沿用环境色会让开关拿浅色方案的底色，落在深底上像别人的组件
            colors = SwitchDefaults.colors(
                checkedThumbColor = Color(palette.primary),
                checkedTrackColor = Color(palette.primary).copy(alpha = 0.32f),
                uncheckedThumbColor = Color(palette.textSecondary),
                uncheckedTrackColor = Color(palette.textPrimary).copy(alpha = 0.18f),
            ),
        )
    }
}

