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
import androidx.compose.foundation.horizontalScroll
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
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.text.style.TextAlign
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
            // 覆盖层底部压着「迷你播放栏 + 底部导航」，不预留会被挡住
            // （真机验收时点内容实际点到了播放栏上）
            .padding(bottom = 120.dp)

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

        // 来源筛选：多个源同时有结果时，用户常想只看某一个源。
        // 来源集合动态生成 —— 没返回结果的源不该出现（避免点进去是空列表）。
        if (state.availableSources.size > 1) {
            Row(
                modifier = Modifier
                    .fillMaxWidth()
                    .horizontalScroll(rememberScrollState()),
                horizontalArrangement = Arrangement.spacedBy(6.dp),
            ) {
                FormatChip(
                    label = stringResource(R.string.lyrics_match_filter_all),
                    selected = state.sourceFilter == null,
                    onClick = { viewModel.setSourceFilter(null) },
                )
                state.availableSources.forEach { source ->
                    FormatChip(
                        label = source,
                        selected = state.sourceFilter == source,
                        onClick = { viewModel.setSourceFilter(source) },
                    )
                }
            }
            Spacer(Modifier.height(8.dp))
        }

        // 候选列表（占上半部分）
        LazyColumn(
            modifier = Modifier
                .fillMaxWidth()
                .weight(1f),
            verticalArrangement = Arrangement.spacedBy(8.dp),
        ) {
            items(state.visibleCandidates, key = { it.index }) { item ->
                CandidateRow(
                    item = item,
                    selected = item.index == state.selectedIndex,
                    onClick = { viewModel.select(item.index) },
                )
            }
            if (!state.searching && state.visibleCandidates.isEmpty()) {
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
            // 标题：这排胶囊是「选择写入文件时用哪种格式」，不写出来看不出用途。
            Row(
                modifier = Modifier.fillMaxWidth(),
                verticalAlignment = Alignment.CenterVertically,
            ) {
                Text(
                    text = stringResource(R.string.lyrics_match_format_title),
                    fontSize = 11.sp,
                    fontWeight = FontWeight.SemiBold,
                    color = dim,
                )
                Spacer(Modifier.weight(1f))
                // 当前格式的适用性：一句话说清「写进去之后什么效果」
                Text(
                    text = stringResource(
                        when (state.format) {
                            LyricoLyricsFormat.PLAIN -> R.string.lyrics_match_format_hint_plain
                            LyricoLyricsFormat.ENHANCED -> R.string.lyrics_match_format_hint_enhanced
                            LyricoLyricsFormat.VERBATIM -> R.string.lyrics_match_format_hint_verbatim
                            LyricoLyricsFormat.TTML -> R.string.lyrics_match_format_hint_ttml
                        },
                    ),
                    fontSize = 10.sp,
                    color = dim.copy(alpha = 0.8f),
                    maxLines = 1,
                    overflow = TextOverflow.Ellipsis,
                    modifier = Modifier.weight(1.6f),
                    textAlign = TextAlign.End,
                )
            }
            Spacer(Modifier.height(6.dp))
            // 四个格式标签的总宽超过屏宽，塞在固定 Row 里会把最后一个压成「T...」。
            // 改成横向滚动：每个 chip 保持自然宽度，放不下就滑。
            Row(
                modifier = Modifier
                    .fillMaxWidth()
                    .horizontalScroll(rememberScrollState()),
                horizontalArrangement = Arrangement.spacedBy(8.dp),
            ) {
                LyricoLyricsFormat.entries.forEach { format ->
                    FormatChip(
                        label = format.label,
                        selected = state.format == format,
                        onClick = { viewModel.setFormat(format) },
                    )
                }
            }
            Spacer(Modifier.height(10.dp))
            // 预览框也加标题：否则会和上面的格式行连成一片，看不出这块是「结果预览」
            Text(
                text = stringResource(R.string.lyrics_match_preview_title),
                fontSize = 11.sp,
                fontWeight = FontWeight.SemiBold,
                color = dim,
            )
            Spacer(Modifier.height(6.dp))
            // 固定高度而不是 weight(1f)：预览只需要看清几行，
            // 用权重会和候选列表平分空间，把候选压成一条半（真机确认过）。
            Box(
                modifier = Modifier
                    .fillMaxWidth()
                    .height(150.dp)
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
            // 之前这里漏了 clickable：onClick 参数传进来却没用上，
            // 于是点任何候选都没反应，「只能选第一个」其实是一个都没选中，
            // 只是搜索完默认把 selectedIndex 设成 0 看着像选中了。
            .clickable(onClick = onClick)
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
            // 来源从这里移走，改到卡片右上角做徽章：与「同步/逐字」「行数」这些
            // 属性信息混在一行时，来源读起来只是个普通标签，看不出「这条来自哪个源」。
            Row(horizontalArrangement = Arrangement.spacedBy(6.dp)) {
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
        // 右侧：来源徽章（上）+ 得分（下）。
        // 徽章常驻，不做省略 —— 来源名都很短，且这是这条候选最关键的身份信息。
        Column(horizontalAlignment = Alignment.End) {
            Text(
                text = item.sourceName,
                fontSize = 9.5.sp,
                fontWeight = FontWeight.Medium,
                color = accent,
                maxLines = 1,
                modifier = Modifier
                    .clip(RoundedCornerShape(999.dp))
                    .background(accent.copy(alpha = 0.14f))
                    .padding(horizontal = 7.dp, vertical = 2.dp),
            )
            Spacer(Modifier.height(4.dp))
            Text(
                text = "%.2f".format(item.score),
                fontSize = 11.sp,
                color = dim,
            )
        }
    }
}

@Composable
private fun FormatChip(label: String, selected: Boolean, onClick: () -> Unit) {
    // 混用 theme 里的唯一实现：圆角/底色/字级只该有一处定义（VisualLanguageTest 钉着）
    GlassChip(label = label, selected = selected, onClick = onClick, compact = true)
}
