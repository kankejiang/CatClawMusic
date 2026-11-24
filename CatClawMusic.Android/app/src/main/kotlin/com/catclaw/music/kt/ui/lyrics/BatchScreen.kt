package com.catclaw.music.kt.ui.lyrics

import androidx.compose.foundation.background
import androidx.compose.foundation.horizontalScroll
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.imePadding
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.navigationBarsPadding
import androidx.compose.foundation.layout.statusBarsPadding
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.items
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
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
import com.catclaw.music.kt.lyrico.batch.BatchOperation
import com.catclaw.music.kt.lyrico.lyrics.LyricoLyricsFormat
import com.catclaw.music.kt.lyrico.ui.BatchSongItem
import com.catclaw.music.kt.lyrico.ui.BatchViewModel
import com.catclaw.music.kt.ui.theme.BackTile
import com.catclaw.music.kt.ui.theme.GlassChip
import com.catclaw.music.kt.ui.theme.GlassSearchField
import com.catclaw.music.kt.ui.theme.LocalCatClawPalette
import com.catclaw.music.kt.ui.theme.PillLabel
import com.catclaw.music.kt.ui.theme.glassCard
import com.catclaw.music.kt.ui.theme.highlightOnGlassCard

/**
 * 批量处理：选歌 → 选操作 → 执行。
 *
 * 入口在音乐库首页。任务列表单独一屏（[BatchTasksScreen]），避免把「选择」和「历史」
 * 挤在同一页里——批量任务是长操作，用户选完就想走，回来再看进度。
 */
@Composable
fun BatchScreen(
    onBack: () -> Unit,
    viewModel: BatchViewModel = hiltViewModel(),
) {
    val palette = LocalCatClawPalette.current
    val state by viewModel.state.collectAsStateWithLifecycle()
    val ink = Color(palette.textPrimary)
    val dim = Color(palette.textSecondary)
    val accent = Color(highlightOnGlassCard(palette))
    var tasksOpen by remember { mutableStateOf(false) }

    if (tasksOpen) {
        BatchTasksScreen(onBack = { tasksOpen = false }, viewModel = viewModel)
        return
    }

    Column(
        modifier = Modifier
            .fillMaxSize()
            .statusBarsPadding()
            // 这一页会隐藏底栏与迷你播放条（外壳统一处理），所以只需避让系统导航条；
            // 原来的 120dp 是为那条被压住的 chrome 预留的
            .navigationBarsPadding()
            .padding(bottom = 8.dp)
            // 搜索框聚焦后键盘会盖住下半屏：不处理的话「选择操作/开始」被压到键盘下面，
            // 用户根本点不到（真机验收时踩到）。imePadding 让内容随键盘上推。
            .imePadding()
            .padding(horizontal = 16.dp),
    ) {
        Spacer(Modifier.height(12.dp))
        Row(verticalAlignment = Alignment.CenterVertically) {
            BackTile(onBack = onBack, description = stringResource(R.string.GoBack))
            Spacer(Modifier.width(12.dp))
            Column(modifier = Modifier.weight(1f)) {
                Text(
                    text = stringResource(R.string.batch_title),
                    fontSize = 20.sp,
                    fontWeight = FontWeight.Bold,
                    color = ink,
                )
                Text(
                    text = stringResource(R.string.batch_selected, state.selectedIds.size),
                    fontSize = 12.sp,
                    color = dim,
                )
            }
            PillLabel(
                text = stringResource(R.string.batch_tasks),
                onClick = { tasksOpen = true },
                filled = false,
            )
        }

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
                kotlinx.coroutines.delay(2800)
                viewModel.consumeMessage()
            }
        }

        Spacer(Modifier.height(10.dp))
        GlassSearchField(
            query = state.query,
            onChange = viewModel::setQuery,
            placeholder = stringResource(R.string.batch_search_placeholder),
            modifier = Modifier.fillMaxWidth(),
        )
        Spacer(Modifier.height(8.dp))
        Row(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
            PillLabel(
                text = stringResource(R.string.batch_select_all),
                onClick = viewModel::selectAllVisible,
                filled = false,
            )
            PillLabel(
                text = stringResource(R.string.batch_clear),
                onClick = viewModel::clearSelection,
                filled = false,
            )
        }

        Spacer(Modifier.height(8.dp))
        if (state.loading) {
            Text(text = stringResource(R.string.batch_loading), fontSize = 13.sp, color = dim)
        } else {
            LazyColumn(
                modifier = Modifier.fillMaxWidth().weight(1f),
                verticalArrangement = Arrangement.spacedBy(6.dp),
            ) {
                items(state.visibleSongs, key = { it.id }) { song ->
                    SongPickRow(
                        song = song,
                        checked = song.id in state.selectedIds,
                        onClick = { viewModel.toggle(song.id) },
                    )
                }
            }
        }

        // 操作区
        Spacer(Modifier.height(8.dp))
        Text(
            text = stringResource(R.string.batch_choose_operation),
            fontSize = 11.sp,
            color = dim,
        )
        // 横向滚动而不是 FlowRow：FlowRow 是实验 API，多点一次 opt-in 不划算
        Row(
            modifier = Modifier
                .fillMaxWidth()
                .padding(vertical = 6.dp)
                .horizontalScroll(rememberScrollState()),
            horizontalArrangement = Arrangement.spacedBy(6.dp),
        ) {
            BatchOperation.entries.forEach { op ->
                GlassChip(
                    label = op.label,
                    selected = state.operation == op,
                    onClick = { viewModel.chooseOperation(op) },
                    compact = true,
                )
            }
        }

        // 操作参数：只显示当前操作真正用得上的
        when (state.operation) {
            BatchOperation.LYRICS_FORMAT,
            BatchOperation.MATCH_LYRICS,
            -> {
                Row(
                    modifier = Modifier.fillMaxWidth().padding(bottom = 4.dp),
                    horizontalArrangement = Arrangement.spacedBy(6.dp),
                ) {
                    LyricoLyricsFormat.entries.forEach { format ->
                        GlassChip(
                            label = format.label,
                            selected = state.options.lyricsFormat == format,
                            onClick = { viewModel.updateOptions { o -> o.copy(lyricsFormat = format) } },
                            compact = true,
                        )
                    }
                }
            }
            BatchOperation.RENAME_FILES -> {
                GlassSearchField(
                    query = state.options.renamePattern,
                    onChange = { v -> viewModel.updateOptions { o -> o.copy(renamePattern = v) } },
                    placeholder = stringResource(R.string.batch_rename_pattern),
                    modifier = Modifier.fillMaxWidth().padding(bottom = 4.dp),
                    compact = true,
                )
            }
            else -> Unit
        }

        Row(
            modifier = Modifier.fillMaxWidth().padding(vertical = 8.dp),
            horizontalArrangement = Arrangement.spacedBy(10.dp),
            verticalAlignment = Alignment.CenterVertically,
        ) {
            GlassChip(
                label = stringResource(R.string.batch_overwrite),
                selected = state.options.overwrite,
                onClick = { viewModel.updateOptions { o -> o.copy(overwrite = !o.overwrite) } },
                compact = true,
            )
            Spacer(Modifier.weight(1f))
            PillLabel(
                text = if (state.busy) {
                    stringResource(R.string.batch_starting)
                } else {
                    stringResource(R.string.batch_start)
                },
                onClick = viewModel::start,
                filled = true,
                enabled = !state.busy && state.selectedIds.isNotEmpty() && state.operation != null,
            )
            if (state.runningTaskId != null) {
                PillLabel(
                    text = stringResource(R.string.batch_cancel),
                    onClick = viewModel::cancelRunning,
                    filled = false,
                )
            }
        }
        Spacer(Modifier.height(8.dp))
    }
}

@Composable
private fun SongPickRow(song: BatchSongItem, checked: Boolean, onClick: () -> Unit) {
    val palette = LocalCatClawPalette.current
    val ink = Color(palette.textPrimary)
    val dim = Color(palette.textSecondary)
    Row(
        modifier = Modifier
            .fillMaxWidth()
            .glassCard(palette = palette, corner = 14, selected = checked)
            .clickable(onClick = onClick)
            .padding(horizontal = 12.dp, vertical = 10.dp),
        verticalAlignment = Alignment.CenterVertically,
    ) {
        // 勾选指示：不用 chip 组件（那是筛选语义），这里就是一个勾选框
        Box(
            modifier = Modifier
                .size(18.dp)
                .clip(CircleShape)
                .background(
                    if (checked) Color(palette.primary) else ink.copy(alpha = 0.10f),
                ),
            contentAlignment = Alignment.Center,
        ) {
            if (checked) {
                Text(text = "✓", fontSize = 11.sp, color = Color(palette.backgroundStops.last().argb))
            }
        }
        Spacer(Modifier.width(10.dp))
        Column(modifier = Modifier.weight(1f)) {
            Text(text = song.title, fontSize = 14.sp, color = ink, maxLines = 1)
            Text(
                text = listOfNotNull(
                    song.artist.takeIf { it.isNotBlank() },
                    song.album.takeIf { it.isNotBlank() },
                ).joinToString(" · "),
                fontSize = 11.sp,
                color = dim,
                maxLines = 1,
            )
        }
        if (song.remote) {
            Text(text = stringResource(R.string.batch_remote_tag), fontSize = 10.sp, color = dim)
        }
    }
}
