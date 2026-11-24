package com.catclaw.music.kt.ui.lyrics

import androidx.compose.foundation.background
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.navigationBarsPadding
import androidx.compose.foundation.layout.statusBarsPadding
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.items
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
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
import androidx.lifecycle.compose.collectAsStateWithLifecycle
import androidx.hilt.navigation.compose.hiltViewModel
import com.catclaw.music.kt.R
import com.catclaw.music.kt.lyrico.batch.BatchOperation
import com.catclaw.music.kt.lyrico.db.BatchItemEntity
import com.catclaw.music.kt.lyrico.db.BatchItemStatus
import com.catclaw.music.kt.lyrico.db.BatchStatus
import com.catclaw.music.kt.lyrico.db.BatchTaskEntity
import com.catclaw.music.kt.lyrico.ui.BatchViewModel
import com.catclaw.music.kt.ui.theme.BackTile
import com.catclaw.music.kt.ui.theme.LocalCatClawPalette
import com.catclaw.music.kt.ui.theme.PillLabel
import com.catclaw.music.kt.ui.theme.glassCard
import com.catclaw.music.kt.ui.theme.highlightOnGlassCard

/** 批量任务列表 + 详情（点一条展开逐首结果）。 */
@Composable
fun BatchTasksScreen(
    onBack: () -> Unit,
    viewModel: BatchViewModel = hiltViewModel(),
) {
    val palette = LocalCatClawPalette.current
    val state by viewModel.state.collectAsStateWithLifecycle()
    val ink = Color(palette.textPrimary)
    val dim = Color(palette.textSecondary)
    val accent = Color(highlightOnGlassCard(palette))
    var openTaskId by remember { mutableStateOf<Long?>(null) }

    openTaskId?.let { taskId ->
        BatchTaskDetail(
            taskId = taskId,
            viewModel = viewModel,
            onBack = { openTaskId = null },
        )
        return
    }

    Column(
        modifier = Modifier
            .fillMaxSize()
            .statusBarsPadding()
            // 与批量处理页一致：底栏与迷你条在这一层是隐藏的，只避让系统导航条
            .navigationBarsPadding()
            .padding(bottom = 8.dp)

            .padding(horizontal = 16.dp),
    ) {
        Spacer(Modifier.height(12.dp))
        Row(verticalAlignment = Alignment.CenterVertically) {
            BackTile(onBack = onBack, description = stringResource(R.string.GoBack))
            Spacer(Modifier.width(12.dp))
            Text(
                text = stringResource(R.string.batch_tasks),
                fontSize = 20.sp,
                fontWeight = FontWeight.Bold,
                color = ink,
                modifier = Modifier.weight(1f),
            )
            PillLabel(
                text = stringResource(R.string.batch_clear_finished),
                onClick = viewModel::clearFinished,
                filled = false,
            )
        }
        Spacer(Modifier.height(10.dp))

        if (state.tasks.isEmpty()) {
            Text(
                text = stringResource(R.string.batch_no_tasks),
                fontSize = 13.sp,
                color = dim,
                modifier = Modifier.padding(vertical = 20.dp),
            )
            return@Column
        }

        LazyColumn(
            modifier = Modifier.fillMaxSize(),
            verticalArrangement = Arrangement.spacedBy(8.dp),
        ) {
            items(state.tasks, key = { it.id }) { task ->
                TaskRow(
                    task = task,
                    accent = accent,
                    onClick = { openTaskId = task.id },
                    onDelete = { viewModel.deleteTask(task.id) },
                )
            }
        }
    }
}

@Composable
private fun TaskRow(
    task: BatchTaskEntity,
    accent: Color,
    onClick: () -> Unit,
    onDelete: () -> Unit,
) {
    val palette = LocalCatClawPalette.current
    val ink = Color(palette.textPrimary)
    val dim = Color(palette.textSecondary)
    val total = task.total.coerceAtLeast(1)
    val done = task.succeeded + task.failed + task.skipped
    Column(
        modifier = Modifier
            .fillMaxWidth()
            .glassCard(palette = palette, corner = 16)
            .clickable(onClick = onClick)
            .padding(horizontal = 12.dp, vertical = 10.dp),
    ) {
        Row(verticalAlignment = Alignment.CenterVertically) {
            Text(
                text = BatchOperation.fromKey(task.operation)?.label ?: task.operation,
                fontSize = 14.sp,
                fontWeight = FontWeight.SemiBold,
                color = ink,
                modifier = Modifier.weight(1f),
            )
            Text(text = statusLabel(task.status), fontSize = 11.sp, color = accent)
        }
        Text(
            text = task.title.ifBlank { "${done}/${task.total}" },
            fontSize = 11.sp,
            color = dim,
            maxLines = 1,
        )
        Spacer(Modifier.height(6.dp))
        // 进度条：不用 Material 的 LinearProgressIndicator，避免又引入一套 M3 配色
        androidx.compose.foundation.layout.Box(
            modifier = Modifier
                .fillMaxWidth()
                .height(4.dp)
                .clip(RoundedCornerShape(999.dp))
                .background(ink.copy(alpha = 0.10f)),
        ) {
            androidx.compose.foundation.layout.Box(
                modifier = Modifier
                    .fillMaxWidth((done.toFloat() / total).coerceIn(0f, 1f))
                    .height(4.dp)
                    .clip(RoundedCornerShape(999.dp))
                    .background(accent),
            )
        }
        Spacer(Modifier.height(4.dp))
        Row(verticalAlignment = Alignment.CenterVertically) {
            Text(
                text = "成功 ${task.succeeded} · 跳过 ${task.skipped} · 失败 ${task.failed}",
                fontSize = 10.sp,
                color = dim,
                modifier = Modifier.weight(1f),
            )
            if (task.status in setOf(BatchStatus.COMPLETED, BatchStatus.CANCELLED, BatchStatus.FAILED)) {
                Text(
                    text = stringResource(R.string.batch_delete),
                    fontSize = 10.sp,
                    color = Color(palette.textSecondary),
                    modifier = Modifier.clickable(onClick = onDelete),
                )
            }
        }
    }
}

@Composable
private fun BatchTaskDetail(
    taskId: Long,
    viewModel: BatchViewModel,
    onBack: () -> Unit,
) {
    val palette = LocalCatClawPalette.current
    val ink = Color(palette.textPrimary)
    val dim = Color(palette.textSecondary)
    val task by viewModel.observeTask(taskId).collectAsStateWithLifecycle(initialValue = null)
    val items by viewModel.observeItems(taskId).collectAsStateWithLifecycle(initialValue = emptyList())

    Column(
        modifier = Modifier
            .fillMaxSize()
            .statusBarsPadding()
            // 同批量处理：这一层也隐藏了底栏与迷你条，只避让系统导航条
            .navigationBarsPadding()
            .padding(bottom = 8.dp)
            .padding(horizontal = 16.dp),
    ) {
        Spacer(Modifier.height(12.dp))
        Row(verticalAlignment = Alignment.CenterVertically) {
            BackTile(onBack = onBack, description = stringResource(R.string.GoBack))
            Spacer(Modifier.width(12.dp))
            Column(modifier = Modifier.weight(1f)) {
                Text(
                    text = task?.let { BatchOperation.fromKey(it.operation)?.label ?: it.operation }
                        ?: stringResource(R.string.batch_tasks),
                    fontSize = 18.sp,
                    fontWeight = FontWeight.Bold,
                    color = ink,
                )
                task?.let {
                    Text(text = statusLabel(it.status), fontSize = 11.sp, color = dim)
                }
            }
        }
        Spacer(Modifier.height(10.dp))
        LazyColumn(
            modifier = Modifier.fillMaxSize(),
            verticalArrangement = Arrangement.spacedBy(6.dp),
        ) {
            items(items, key = { it.id }) { item ->
                ItemRow(item)
            }
        }
    }
}

@Composable
private fun ItemRow(item: BatchItemEntity) {
    val palette = LocalCatClawPalette.current
    val ink = Color(palette.textPrimary)
    val dim = Color(palette.textSecondary)
    val statusColor = when (item.status) {
        BatchItemStatus.SUCCESS -> Color(highlightOnGlassCard(palette))
        BatchItemStatus.FAILED -> Color(palette.textSecondary)
        else -> dim
    }
    Column(
        modifier = Modifier
            .fillMaxWidth()
            .glassCard(palette = palette, corner = 12)
            .padding(horizontal = 10.dp, vertical = 8.dp),
    ) {
        Row {
            Text(
                text = item.songTitle.ifBlank { "#${item.songId}" },
                fontSize = 13.sp,
                color = ink,
                maxLines = 1,
                modifier = Modifier.weight(1f),
            )
            Text(text = itemStatusLabel(item.status), fontSize = 10.sp, color = statusColor)
        }
        if (item.message.isNotBlank()) {
            Text(
                text = item.message,
                fontSize = 10.sp,
                color = dim,
                maxLines = 2,
            )
        }
    }
}

@Composable
private fun statusLabel(status: String): String = when (status) {
    BatchStatus.PENDING -> stringResource(R.string.batch_status_pending)
    BatchStatus.RUNNING -> stringResource(R.string.batch_status_running)
    BatchStatus.PAUSED -> stringResource(R.string.batch_status_paused)
    BatchStatus.COMPLETED -> stringResource(R.string.batch_status_completed)
    BatchStatus.CANCELLED -> stringResource(R.string.batch_status_cancelled)
    BatchStatus.FAILED -> stringResource(R.string.batch_status_failed)
    else -> status
}

@Composable
private fun itemStatusLabel(status: String): String = when (status) {
    BatchItemStatus.SUCCESS -> stringResource(R.string.batch_item_success)
    BatchItemStatus.FAILED -> stringResource(R.string.batch_item_failed)
    BatchItemStatus.SKIPPED -> stringResource(R.string.batch_item_skipped)
    else -> stringResource(R.string.batch_status_pending)
}
