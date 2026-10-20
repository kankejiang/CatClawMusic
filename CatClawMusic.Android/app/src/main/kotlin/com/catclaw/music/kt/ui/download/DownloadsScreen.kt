package com.catclaw.music.kt.ui.download

import androidx.compose.foundation.background
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
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
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import androidx.hilt.navigation.compose.hiltViewModel
import androidx.lifecycle.compose.collectAsStateWithLifecycle
import com.catclaw.music.kt.R
import com.catclaw.music.kt.core.model.SongDetailFormat
import com.catclaw.music.kt.data.download.DownloadState
import com.catclaw.music.kt.data.download.DownloadTask
import com.catclaw.music.kt.ui.theme.BackTile
import com.catclaw.music.kt.ui.theme.EmptyState
import com.catclaw.music.kt.ui.theme.LocalCatClawPalette
import androidx.compose.foundation.clickable
import com.catclaw.music.kt.ui.theme.PillLabel
import com.catclaw.music.kt.ui.theme.glassCard

/**
 * 下载管理 —— MAUI `DownloadsPage` 的对应物：下载位置 + 任务计数 + 每条任务的进度与动作。
 *
 * 磁力那半按既定判例不做（libtorrent4j 让包体 +113%），所以右上角那个「＋ 输入地址」
 * 也不摆 —— 本端能下的只有远程曲库里的歌，那有它自己的下载入口。
 */
@Composable
fun DownloadsScreen(onBack: () -> Unit, viewModel: DownloadsViewModel = hiltViewModel()) {
    val palette = LocalCatClawPalette.current
    val tasks by viewModel.tasks.collectAsStateWithLifecycle()
    val snapshot by viewModel.snapshot.collectAsStateWithLifecycle()
    val speedKbps by viewModel.speedKbps.collectAsStateWithLifecycle()
    val concurrent by viewModel.concurrent.collectAsStateWithLifecycle()
    val primary = Color(palette.textPrimary)
    val secondary = Color(palette.textSecondary)
    LaunchedEffect(Unit) { viewModel.refresh() }

    Column(
        modifier = Modifier
            .fillMaxSize()
            .verticalScroll(rememberScrollState())
            .padding(horizontal = 16.dp),
    ) {
        Spacer(Modifier.height(12.dp))
        Row(verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(12.dp)) {
            BackTile(onBack = onBack, description = stringResource(R.string.GoBack))
            Column(modifier = Modifier.weight(1f)) {
                Text(
                    text = stringResource(R.string.downloads_title),
                    fontSize = 24.sp,
                    fontWeight = FontWeight.Bold,
                    color = primary,
                )
                Text(
                    text = stringResource(
                        R.string.downloads_counts,
                        tasks.size,
                        tasks.count { it.state == DownloadState.Running },
                        tasks.count { it.state == DownloadState.Done },
                    ),
                    fontSize = 12.sp,
                    color = secondary,
                )
            }
        }
        Spacer(Modifier.height(14.dp))

        // 下载位置：路径可能很长，给两行 + 中间省略，别让它把标题挤没
        Column(
            modifier = Modifier
                .fillMaxWidth()
                .glassCard(palette = palette, corner = 24)
                .padding(horizontal = 16.dp, vertical = 14.dp),
            verticalArrangement = Arrangement.spacedBy(6.dp),
        ) {
            Text(
                text = stringResource(R.string.downloads_dir),
                fontSize = 14.sp,
                fontWeight = FontWeight.SemiBold,
                color = primary,
            )
            Text(
                text = snapshot.directoryPath,
                fontSize = 12.sp,
                color = secondary,
                maxLines = 2,
                overflow = TextOverflow.MiddleEllipsis,
            )
            Text(
                text = stringResource(
                    R.string.downloads_size,
                    SongDetailFormat.bytes(snapshot.directoryBytes).ifBlank { stringResource(R.string.downloads_zero) },
                ),
                fontSize = 12.sp,
                color = secondary,
            )
            // 限速（对应 C# `DownloadsPage` 设置面板里那一档）：点一下切下一档。
            // 用循环档位而不是滑杆：档位是离散的几档，滑杆在 0 与 256 之间会滑出无穷多个没人要的值
            Row(
                modifier = Modifier
                    .fillMaxWidth()
                    .padding(top = 2.dp)
                    .clickable { viewModel.cycleSpeedLimit() },
                verticalAlignment = Alignment.CenterVertically,
            ) {
                Text(
                    text = stringResource(R.string.downloads_speed_limit),
                    fontSize = 12.sp,
                    color = secondary,
                    modifier = Modifier.weight(1f),
                )
                PillLabel(
                    text = if (speedKbps > 0) "$speedKbps KB/s" else stringResource(R.string.downloads_speed_unlimited),
                    onClick = { viewModel.cycleSpeedLimit() },
                    filled = false,
                )
            }
            // 并发数：与 C# `DownloadsPage` 设置面板里那一档同义（默认 2、可调 1..5）
            Row(
                modifier = Modifier
                    .fillMaxWidth()
                    .clickable { viewModel.cycleConcurrent() },
                verticalAlignment = Alignment.CenterVertically,
            ) {
                Text(
                    text = stringResource(R.string.downloads_concurrency),
                    fontSize = 12.sp,
                    color = secondary,
                    modifier = Modifier.weight(1f),
                )
                PillLabel(
                    text = concurrent.toString(),
                    onClick = { viewModel.cycleConcurrent() },
                    filled = false,
                )
            }
        }
        Spacer(Modifier.height(12.dp))

        if (tasks.isEmpty()) {
            EmptyState(hint = stringResource(R.string.downloads_empty), iconRes = R.drawable.ic_history)
            return
        }
        tasks.forEach { task ->
            TaskCard(task = task, viewModel = viewModel)
            Spacer(Modifier.height(10.dp))
        }
        Spacer(Modifier.height(24.dp))
    }
}

@Composable
private fun TaskCard(task: DownloadTask, viewModel: DownloadsViewModel) {
    val palette = LocalCatClawPalette.current
    val primary = Color(palette.textPrimary)
    val secondary = Color(palette.textSecondary)
    val fraction = if (task.total > 0) (task.downloaded.toFloat() / task.total).coerceIn(0f, 1f) else 0f

    Column(
        modifier = Modifier
            .fillMaxWidth()
            .glassCard(palette = palette, corner = 24)
            .padding(horizontal = 16.dp, vertical = 14.dp),
        verticalArrangement = Arrangement.spacedBy(8.dp),
    ) {
        Row(verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(10.dp)) {
            Text(
                text = task.title,
                fontSize = 14.sp,
                fontWeight = FontWeight.SemiBold,
                color = primary,
                maxLines = 1,
                overflow = TextOverflow.Ellipsis,
                modifier = Modifier.weight(1f),
            )
            Text(text = statusText(task.state), fontSize = 12.sp, color = secondary)
        }
        Text(
            text = if (task.total > 0) {
                stringResource(
                    R.string.downloads_bytes,
                    SongDetailFormat.bytes(task.downloaded),
                    SongDetailFormat.bytes(task.total),
                )
            } else {
                task.fileName
            },
            fontSize = 12.sp,
            color = secondary,
            maxLines = 1,
            overflow = TextOverflow.Ellipsis,
        )
        Row(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
            when (task.state) {
                // 排队中的任务与正在下的给同一组动作：暂停/取消对它一样有效
                // （取消会把还在等名额的协程一起收掉）
                DownloadState.Queued,
                DownloadState.Running -> {
                    PillLabel(text = stringResource(R.string.downloads_pause), onClick = { viewModel.pause(task.songId) }, filled = false)
                    PillLabel(text = stringResource(R.string.download_cancel), onClick = { viewModel.cancel(task.songId) }, filled = false)
                }
                DownloadState.Paused -> {
                    PillLabel(text = stringResource(R.string.downloads_resume), onClick = { viewModel.resume(task.songId) }, filled = false)
                    PillLabel(text = stringResource(R.string.download_cancel), onClick = { viewModel.cancel(task.songId) }, filled = false)
                }
                DownloadState.Failed -> {
                    PillLabel(text = stringResource(R.string.downloads_retry), onClick = { viewModel.resume(task.songId) }, filled = false)
                    PillLabel(text = stringResource(R.string.Delete), onClick = { viewModel.dismiss(task.songId) }, filled = false)
                }
                DownloadState.Done -> {
                    PillLabel(
                        text = stringResource(R.string.downloads_delete_file),
                        onClick = { viewModel.deleteFile(task.songId) },
                        filled = false,
                    )
                }
            }
        }
        // 进度条放在整张卡最后：它是没有前景的纯色块，
        // 摆在字色行之间会被 inkOn 闸门的窗口当成"粉面上写着字"
        Box(
            modifier = Modifier
                .fillMaxWidth()
                .height(5.dp)
                .clip(RoundedCornerShape(3.dp))
                .background(secondary.copy(alpha = 0.18f)),
        ) {
            Box(
                modifier = Modifier
                    .fillMaxWidth(fraction)
                    .height(5.dp)
                    .background(Color(palette.primaryDark)),
            )
        }
    }
}

@Composable
private fun statusText(state: DownloadState): String = stringResource(
    when (state) {
        DownloadState.Queued -> R.string.downloads_state_queued
        DownloadState.Running -> R.string.downloads_state_running
        DownloadState.Paused -> R.string.downloads_state_paused
        DownloadState.Done -> R.string.download_done
        DownloadState.Failed -> R.string.download_failed
    },
)
