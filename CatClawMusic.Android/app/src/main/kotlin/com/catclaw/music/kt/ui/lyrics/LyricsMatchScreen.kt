package com.catclaw.music.kt.ui.lyrics

import androidx.compose.foundation.background
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.statusBarsPadding
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.items
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.verticalScroll
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import androidx.hilt.navigation.compose.hiltViewModel
import androidx.lifecycle.compose.collectAsStateWithLifecycle
import com.catclaw.music.kt.R
import com.catclaw.music.kt.lyrico.lyrics.LyricoLyricsFormat
import com.catclaw.music.kt.lyrico.ui.CandidateUi
import com.catclaw.music.kt.lyrico.ui.LyricsMatchTarget
import com.catclaw.music.kt.lyrico.ui.LyricsMatchViewModel
import com.catclaw.music.kt.ui.theme.BackTile
import com.catclaw.music.kt.ui.theme.GlassChip
import com.catclaw.music.kt.ui.theme.GlassSearchField
import com.catclaw.music.kt.ui.theme.LocalCatClawPalette
import com.catclaw.music.kt.ui.theme.PillLabel
import com.catclaw.music.kt.ui.theme.glassCard
import com.catclaw.music.kt.ui.theme.highlightOnGlassCard

/**
 * 歌词匹配页：搜词 → 选候选 → 预览 → 写入音频内嵌标签。
 *
 * 入口挂在歌曲详情页（`SongDetailScreen` 覆盖层），所以各页的「歌曲信息」都能进。
 * 该页只负责交互，搜索/排序/写回全在 `:lyrico` 的 `LyricsMatchViewModel` 里。
 */
@Composable
fun LyricsMatchScreen(
    target: LyricsMatchTarget,
    onBack: () -> Unit,
    viewModel: LyricsMatchViewModel = hiltViewModel(),
) {
    val palette = LocalCatClawPalette.current
    val state by viewModel.state.collectAsStateWithLifecycle()

    LaunchedEffect(target.songId) { viewModel.prepare(target) }

    val ink = Color(palette.textPrimary)
    val dim = Color(palette.textSecondary)
    val accent = Color(highlightOnGlassCard(palette))

    Column(
        modifier = Modifier
            .fillMaxSize()
            .statusBarsPadding()
            .padding(horizontal = 16.dp),
    ) {
        Spacer(Modifier.height(12.dp))
        Row(verticalAlignment = Alignment.CenterVertically) {
            BackTile(onBack = onBack, description = stringResource(R.string.GoBack))
            Spacer(Modifier.width(12.dp))
            Column {
                Text(
                    text = stringResource(R.string.lyrics_match_title),
                    fontSize = 20.sp,
                    fontWeight = FontWeight.Bold,
                    color = ink,
                )
                Text(
                    text = target.title,
                    fontSize = 12.sp,
                    color = dim,
                    maxLines = 1,
                )
            }
        }
        Spacer(Modifier.height(12.dp))

        // 搜索条件
        GlassSearchField(
            query = state.queryTitle,
            onChange = viewModel::setQueryTitle,
            placeholder = stringResource(R.string.lyrics_match_query_title),
            modifier = Modifier.fillMaxWidth(),
        )
        Spacer(Modifier.height(8.dp))
        GlassSearchField(
            query = state.queryArtist,
            onChange = viewModel::setQueryArtist,
            placeholder = stringResource(R.string.lyrics_match_query_artist),
            modifier = Modifier.fillMaxWidth(),
        )
        Spacer(Modifier.height(10.dp))
        Row(horizontalArrangement = Arrangement.spacedBy(10.dp)) {
            PillLabel(
                text = if (state.searching) {
                    stringResource(R.string.lyrics_match_searching)
                } else {
                    stringResource(R.string.lyrics_match_search)
                },
                onClick = viewModel::search,
                filled = true,
                enabled = !state.searching && !state.writing,
            )
            if (state.preview.isNotEmpty()) {
                PillLabel(
                    text = if (state.writing) {
                        stringResource(R.string.lyrics_match_writing)
                    } else {
                        stringResource(R.string.lyrics_match_write)
                    },
                    onClick = viewModel::write,
                    filled = true,
                    enabled = !state.writing && state.selectedIndex >= 0,
                )
            }
        }

        // 一次性提示
        state.message?.let { msg ->
            Spacer(Modifier.height(8.dp))
            Text(
                text = msg,
                fontSize = 12.sp,
                color = accent,
                modifier = Modifier
                    .fillMaxWidth()
                    .clip(RoundedCornerShape(12.dp))
                    .background(ink.copy(alpha = 0.08f))
                    .padding(horizontal = 12.dp, vertical = 8.dp),
            )
            LaunchedEffect(msg) {
                kotlinx.coroutines.delay(2600)
                viewModel.consumeMessage()
            }
        }

        if (state.sourceErrors.isNotEmpty()) {
            Spacer(Modifier.height(6.dp))
            Text(
                text = state.sourceErrors.joinToString("；").take(160),
                fontSize = 11.sp,
                color = dim,
            )
        }

        Spacer(Modifier.height(10.dp))

        // 候选列表（占上半部分）
        LazyColumn(
            modifier = Modifier
                .fillMaxWidth()
                .weight(1f),
            verticalArrangement = Arrangement.spacedBy(8.dp),
        ) {
            items(state.candidates, key = { it.index }) { item ->
                CandidateRow(
                    item = item,
                    selected = item.index == state.selectedIndex,
                    onClick = { viewModel.select(item.index) },
                )
            }
            if (!state.searching && state.candidates.isEmpty()) {
                item {
                    Text(
                        text = stringResource(R.string.lyrics_match_empty),
                        fontSize = 13.sp,
                        color = dim,
                        modifier = Modifier
                            .fillMaxWidth()
                            .padding(vertical = 24.dp),
                    )
                }
            }
        }

        // 预览 + 格式切换（占下半部分）
        if (state.preview.isNotEmpty()) {
            Spacer(Modifier.height(6.dp))
            Row(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                LyricoLyricsFormat.entries.forEach { format ->
                    FormatChip(
                        label = format.label,
                        selected = state.format == format,
                        onClick = { viewModel.setFormat(format) },
                    )
                }
            }
            Spacer(Modifier.height(8.dp))
            Box(
                modifier = Modifier
                    .fillMaxWidth()
                    .weight(1f)
                    .glassCard(palette = palette, corner = 18)
                    .padding(12.dp),
            ) {
                Text(
                    text = state.preview,
                    fontSize = 12.sp,
                    lineHeight = 18.sp,
                    color = ink,
                    modifier = Modifier
                        .fillMaxSize()
                        .verticalScroll(rememberScrollState()),
                )
            }
        }
        Spacer(Modifier.height(12.dp))
    }
}

@Composable
private fun CandidateRow(
    item: CandidateUi,
    selected: Boolean,
    onClick: () -> Unit,
) {
    val palette = LocalCatClawPalette.current
    val ink = Color(palette.textPrimary)
    val dim = Color(palette.textSecondary)
    val accent = Color(highlightOnGlassCard(palette))

    Row(
        modifier = Modifier
            .fillMaxWidth()
            .glassCard(palette = palette, corner = 16, selected = selected)
            .padding(horizontal = 12.dp, vertical = 10.dp),
        verticalAlignment = Alignment.CenterVertically,
    ) {
        Column(modifier = Modifier.weight(1f)) {
            Text(
                text = item.title.ifBlank { "—" },
                fontSize = 14.sp,
                fontWeight = FontWeight.SemiBold,
                color = ink,
                maxLines = 1,
            )
            Text(
                text = listOfNotNull(
                    item.artist.takeIf { it.isNotBlank() },
                    item.album.takeIf { it.isNotBlank() },
                    // 时长差比绝对时长更有用
                    if (item.durationMs > 0) "${item.durationMs / 1000}s" else null,
                ).joinToString(" · "),
                fontSize = 11.sp,
                color = dim,
                maxLines = 1,
            )
            Row(horizontalArrangement = Arrangement.spacedBy(6.dp)) {
                Text(text = item.sourceName, fontSize = 10.sp, color = accent)
                Text(
                    text = when {
                        item.wordByWord -> stringResource(R.string.lyrics_match_tag_word)
                        item.synced -> stringResource(R.string.lyrics_match_tag_synced)
                        else -> stringResource(R.string.lyrics_match_tag_plain)
                    },
                    fontSize = 10.sp,
                    color = dim,
                )
                Text(
                    text = stringResource(R.string.lyrics_match_lines, item.lineCount),
                    fontSize = 10.sp,
                    color = dim,
                )
            }
        }
        Text(
            text = "%.2f".format(item.score),
            fontSize = 11.sp,
            color = dim,
        )
    }
}

@Composable
private fun FormatChip(label: String, selected: Boolean, onClick: () -> Unit) {
    // 混用 theme 里的唯一实现：圆角/底色/字级只该有一处定义（VisualLanguageTest 钉着）
    GlassChip(label = label, selected = selected, onClick = onClick, compact = true)
}
