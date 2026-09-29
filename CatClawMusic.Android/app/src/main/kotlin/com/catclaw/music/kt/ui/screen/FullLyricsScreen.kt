package com.catclaw.music.kt.ui.screen
import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.clickable
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.draw.clip

import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.rememberLazyListState
import androidx.compose.material3.Icon
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.res.painterResource
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import androidx.hilt.navigation.compose.hiltViewModel
import androidx.lifecycle.compose.collectAsStateWithLifecycle
import com.catclaw.music.kt.R
import com.catclaw.music.kt.data.lyrics.LyricsSettings
import com.catclaw.music.kt.ui.lyrics.LyricLineText
import com.catclaw.music.kt.ui.lyrics.LyricsViewModel
import com.catclaw.music.kt.ui.player.rememberCover
import com.catclaw.music.kt.ui.theme.GlassChip
import com.catclaw.music.kt.ui.theme.LocalCatClawPalette
import com.catclaw.music.kt.ui.theme.EmptyState
import com.catclaw.music.kt.ui.theme.PillLabel
import com.catclaw.music.kt.ui.theme.artworkWash
import com.catclaw.music.kt.ui.theme.catClawBackground

@Composable
fun FullLyricsScreen(onClose: () -> Unit, viewModel: LyricsViewModel = hiltViewModel()) {
    val palette = LocalCatClawPalette.current
    val lyrics by viewModel.lyrics.collectAsStateWithLifecycle()
    val loading by viewModel.loading.collectAsStateWithLifecycle()
    val lineMode by viewModel.lineMode.collectAsStateWithLifecycle()
    val positionMs by viewModel.positionMs.collectAsStateWithLifecycle()
    val fontSize by viewModel.fontSizeSp.collectAsStateWithLifecycle()
    val alignment by viewModel.alignment.collectAsStateWithLifecycle()
    val showTranslation by viewModel.showTranslation.collectAsStateWithLifecycle()
    val showRoma by viewModel.showRoma.collectAsStateWithLifecycle()
    var panelOpen by remember { mutableStateOf(false) }

    val shown = viewModel.visibleLyrics()
    val lines = shown?.lines.orEmpty()
    val presentation = com.catclaw.music.kt.core.lyrics.LyricsState.present(
        lyrics = shown,
        positionMs = positionMs,
        lineMode = lineMode,
    )
    val currentIndex = presentation?.index ?: -1
    val listState = rememberLazyListState()

    LaunchedEffect(currentIndex) {
        if (currentIndex >= 0 && lines.isNotEmpty()) {
            listState.animateScrollToItem(currentIndex.coerceAtMost(lines.lastIndex))
        }
    }

    // 与播放页同一套语言：底色由当前这首歌的封面染出来（[artworkWash] 单点定义），
    // 头部只留文字与纯图标——原来那颗玻璃卡返回方块是另一套语言，跟播放页的图标行摆在一起会分家
    val song = viewModel.currentSong()
    val cover = rememberCover(song?.coverArtPath, song?.filePath, maxPx = 256)

    Box(modifier = Modifier.fillMaxSize().catClawBackground().artworkWash(cover)) {
        Column(modifier = Modifier.fillMaxSize()) {
            Row(
                modifier = Modifier.fillMaxWidth().padding(horizontal = 26.dp, vertical = 12.dp),
                verticalAlignment = Alignment.CenterVertically,
                horizontalArrangement = Arrangement.spacedBy(12.dp),
            ) {
                Column(modifier = Modifier.weight(1f)) {
                    Text(
                        text = viewModel.currentSongTitle().ifEmpty { stringResource(R.string.m1_nothing) },
                        fontSize = 17.sp,
                        fontWeight = FontWeight.SemiBold,
                        color = Color(palette.textPrimary),
                        maxLines = 1,
                        overflow = TextOverflow.Ellipsis,
                    )
                    Text(
                        text = viewModel.currentSongArtist(),
                        fontSize = 12.5.sp,
                        color = Color(palette.textSecondary),
                        maxLines = 1,
                        overflow = TextOverflow.Ellipsis,
                    )
                }
                PillLabel(
                    text = stringResource(if (lineMode) R.string.lyric_mode_line else R.string.lyric_mode_word),
                    onClick = { viewModel.toggleLineMode() },
                    filled = false,
                )
                Icon(
                    painter = painterResource(R.drawable.ic_settings),
                    contentDescription = stringResource(R.string.Settings),
                    tint = Color(palette.textSecondary),
                    modifier = Modifier
                        .size(44.dp)
                        .clip(CircleShape)
                        .clickable { panelOpen = !panelOpen }
                        .padding(11.dp),
                )
                Icon(
                    painter = painterResource(R.drawable.ic_close),
                    contentDescription = stringResource(R.string.lyric_close),
                    tint = Color(palette.textPrimary),
                    modifier = Modifier
                        .size(44.dp)
                        .clip(CircleShape)
                        .clickable(onClickLabel = stringResource(R.string.lyric_close)) { onClose() }
                        .padding(11.dp),
                )
            }

            Spacer(Modifier.height(4.dp))

            if (lines.isEmpty()) {
                Box(modifier = Modifier.fillMaxSize().padding(horizontal = 22.dp), contentAlignment = Alignment.Center) {
                    EmptyState(
                        hint = stringResource(if (loading) R.string.lyric_loading else R.string.lyric_none),
                        iconRes = R.drawable.ic_lyrics,
                    )
                }
            } else {
                LazyColumn(
                    state = listState,
                    modifier = Modifier.fillMaxSize(),
                    contentPadding = androidx.compose.foundation.layout.PaddingValues(vertical = 120.dp),
                ) {
                    items(count = lines.size) { index ->
                        LyricLineText(
                            line = lines[index],
                            isCurrent = index == currentIndex,
                            progress = if (index == currentIndex) presentation?.fillProgress ?: 0.0 else 0.0,
                            showExtras = index == currentIndex,
                            onClick = { viewModel.seekTo(lines[index].startMs) },
                            fontSizeSp = fontSize,
                            alignmentOverride = alignment,
                            showTranslation = showTranslation,
                            showRoma = showRoma,
                        )
                    }
                }
            }
        }

        if (panelOpen) {
            LyricsSettingsPanel(
                sourceMode = viewModel.sourceMode.collectAsStateWithLifecycle().value,
                alignment = alignment,
                fontSizeSp = fontSize,
                removeEmpty = viewModel.removeEmptyLines.collectAsStateWithLifecycle().value,
                translation = showTranslation,
                roma = showRoma,
                onSource = viewModel::setSourceMode,
                onAlignment = viewModel::setAlignment,
                onFontSize = viewModel::setFontSize,
                onRemoveEmpty = viewModel::setRemoveEmptyLines,
                onTranslation = viewModel::setShowTranslation,
                onRoma = viewModel::setShowRoma,
                onClose = { panelOpen = false },
            )
        }
    }
}

/** 全屏歌词页的设置面板：档位与默认值对齐 MAUI `LyricsSettingsService` */
@Composable
private fun LyricsSettingsPanel(
    sourceMode: com.catclaw.music.kt.data.lyrics.LyricsSourceMode,
    alignment: Int,
    fontSizeSp: Float,
    removeEmpty: Boolean,
    translation: Boolean,
    roma: Boolean,
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
            Text(
                text = stringResource(R.string.lyric_font) + "  " + fontSizeSp.toInt().toString(),
                style = MaterialTheme.typography.labelMedium,
                color = Color(palette.textSecondary),
            )
            androidx.compose.material3.Slider(
                value = fontSizeSp.coerceIn(LyricsSettings.MIN_FONT, LyricsSettings.MAX_FONT),
                onValueChange = onFontSize,
                valueRange = LyricsSettings.MIN_FONT..LyricsSettings.MAX_FONT,
                steps = (LyricsSettings.MAX_FONT - LyricsSettings.MIN_FONT).toInt() - 1,
                // 这一页在浅色主题下也钉成深色，所以滑杆不能沿用环境色
                colors = androidx.compose.material3.SliderDefaults.colors(
                    thumbColor = Color(palette.primary),
                    activeTrackColor = Color(palette.primary),
                    inactiveTrackColor = Color(palette.textPrimary).copy(alpha = 0.18f),
                ),
            )
            ToggleRow(stringResource(R.string.lyric_remove_empty), removeEmpty, onRemoveEmpty)
            ToggleRow(stringResource(R.string.lyric_show_translation), translation, onTranslation)
            ToggleRow(stringResource(R.string.lyric_show_roma), roma, onRoma)
        }
    }
}

@Composable
private fun ChipRow(labels: List<Pair<String, Int>>, selected: Int, onPick: (Int) -> Unit) {
    Row(horizontalArrangement = Arrangement.spacedBy(8.dp), modifier = Modifier.padding(vertical = 6.dp)) {
        labels.forEach { (label, value) ->
            GlassChip(label = label, selected = value == selected, onClick = { onPick(value) })
        }
    }
}

@Composable
private fun ToggleRow(label: String, checked: Boolean, onChecked: (Boolean) -> Unit) {
    val palette = LocalCatClawPalette.current
    Row(verticalAlignment = Alignment.CenterVertically, modifier = Modifier.fillMaxWidth()) {
        Text(
            text = label,
            style = MaterialTheme.typography.bodyMedium,
            color = Color(palette.textPrimary),
            modifier = Modifier.weight(1f),
        )
        androidx.compose.material3.Switch(
            checked = checked,
            onCheckedChange = onChecked,
            // 这一页在浅色主题下也钉成深色，沿用环境色会让开关拿浅色方案的底色，落在深底上像别人的组件
            colors = androidx.compose.material3.SwitchDefaults.colors(
                checkedThumbColor = Color(palette.primary),
                checkedTrackColor = Color(palette.primary).copy(alpha = 0.32f),
                uncheckedThumbColor = Color(palette.textSecondary),
                uncheckedTrackColor = Color(palette.textPrimary).copy(alpha = 0.18f),
            ),
        )
    }
}
