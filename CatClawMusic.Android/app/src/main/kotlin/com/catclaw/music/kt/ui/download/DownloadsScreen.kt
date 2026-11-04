package com.catclaw.music.kt.ui.download

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
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.verticalScroll
import androidx.compose.material3.Icon
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.res.painterResource
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextAlign
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
import com.catclaw.music.kt.ui.theme.LocalCatClawPalette
import com.catclaw.music.kt.ui.theme.PillLabel
import com.catclaw.music.kt.ui.theme.glassCard

/**
 * 下载管理 —— MAUI `DownloadsPage` 的对应物（磁力那半按既定判例不做）。
 *
 * 2026-11-04 版式重做：原来是「一张大卡里堆路径 + 两个胶囊」，空状态又是一整块大玻璃，
 * 整页看着稀碎。现在三段式：数字摘要 → 设置行（iOS 行式，标签/值/发丝线）→ 任务卡；
 * 空状态不再套卡，改成居中的图标 + 一句标题 + 一句指路。
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
                    text = stringResource(R.string.downloads_subtitle),
                    fontSize = 12.sp,
                    color = secondary,
                    maxLines = 1,
                    overflow = TextOverflow.Ellipsis,
                )
            }
        }
        Spacer(Modifier.height(16.dp))

        // ① 数字摘要：任务 / 进行中 / 已完成，三格等宽，中间一根发丝线
        Row(
            modifier = Modifier
                .fillMaxWidth()
                .glassCard(palette = palette, corner = 22)
                .padding(vertical = 14.dp),
            verticalAlignment = Alignment.CenterVertically,
        ) {
            StatCell(
                value = tasks.size.toString(),
                label = stringResource(R.string.downloads_stat_tasks),
                primary = primary,
                secondary = secondary,
                modifier = Modifier.weight(1f),
            )
            VerticalHairline(secondary)
            StatCell(
                value = tasks.count { it.state == DownloadState.Running }.toString(),
                label = stringResource(R.string.downloads_stat_running),
                primary = primary,
                secondary = secondary,
                modifier = Modifier.weight(1f),
            )
            VerticalHairline(secondary)
            StatCell(
                value = tasks.count { it.state == DownloadState.Done }.toString(),
                label = stringResource(R.string.downloads_stat_done),
                primary = primary,
                secondary = secondary,
                modifier = Modifier.weight(1f),
            )
        }
        Spacer(Modifier.height(12.dp))

        // ② 设置行：iOS 那种「左标签 / 右值」，行间发丝线
        Column(
            modifier = Modifier
                .fillMaxWidth()
                .glassCard(palette = palette, corner = 22),
        ) {
            // 下载位置：路径太长，值上面单独给一行 mid-ellipsis，别把标签挤没
            Column(modifier = Modifier.fillMaxWidth().padding(horizontal = 16.dp, vertical = 13.dp)) {
                Text(stringResource(R.string.downloads_dir), fontSize = 13.5.sp, color = primary)
                Text(
                    text = snapshot.directoryPath,
                    fontSize = 11.5.sp,
                    color = secondary.copy(alpha = 0.75f),
                    maxLines = 1,
                    overflow = TextOverflow.MiddleEllipsis,
                    modifier = Modifier.padding(top = 4.dp),
                )
            }
            Hairline(secondary)
            SettingsRow(
                label = stringResource(R.string.downloads_used),
                value = SongDetailFormat.bytes(snapshot.directoryBytes).ifBlank { stringResource(R.string.downloads_zero) },
                primary = primary,
                secondary = secondary,
            )
            Hairline(secondary)
            SettingsRow(
                label = stringResource(R.string.downloads_speed_limit),
                value = if (speedKbps > 0) "$speedKbps KB/s" else stringResource(R.string.downloads_speed_unlimited),
                primary = primary,
                secondary = secondary,
                onClick = viewModel::cycleSpeedLimit,
            )
            Hairline(secondary)
            SettingsRow(
                label = stringResource(R.string.downloads_concurrency),
                value = concurrent.toString(),
                primary = primary,
                secondary = secondary,
                onClick = viewModel::cycleConcurrent,
            )
        }
        Spacer(Modifier.height(14.dp))

        // ③ 任务列表 / 空状态
        if (tasks.isEmpty()) {
            EmptyDownloads(primary = primary, secondary = secondary)
        } else {
            tasks.forEach { task ->
                TaskCard(task = task, viewModel = viewModel)
                Spacer(Modifier.height(10.dp))
            }
        }
        Spacer(Modifier.height(28.dp))
    }
}

/** 摘要里的一格：大数字 + 小标签 */
@Composable
private fun StatCell(
    value: String,
    label: String,
    primary: Color,
    secondary: Color,
    modifier: Modifier = Modifier,
) {
    Column(modifier = modifier, horizontalAlignment = Alignment.CenterHorizontally) {
        Text(text = value, fontSize = 22.sp, fontWeight = FontWeight.Bold, color = primary)
        Text(text = label, fontSize = 11.sp, color = secondary, modifier = Modifier.padding(top = 2.dp))
    }
}

/** 设置行：左标签、右值，可点时行尾多一个排版箭头 */
@Composable
private fun SettingsRow(
    label: String,
    value: String,
    primary: Color,
    secondary: Color,
    onClick: (() -> Unit)? = null,
) {
    Row(
        modifier = Modifier
            .fillMaxWidth()
            .let { if (onClick != null) it.clickable(onClick = onClick) else it }
            .padding(horizontal = 16.dp, vertical = 14.dp),
        verticalAlignment = Alignment.CenterVertically,
    ) {
        Text(text = label, fontSize = 13.5.sp, color = primary)
        Spacer(Modifier.weight(1f))
        Text(text = value, fontSize = 13.sp, color = secondary)
        if (onClick != null) {
            Text("›", fontSize = 16.sp, color = secondary, modifier = Modifier.padding(start = 8.dp))
        }
    }
}

@Composable
private fun Hairline(secondary: Color) {
    Box(
        modifier = Modifier
            .fillMaxWidth()
            .padding(start = 16.dp)
            .height(0.5.dp)
            .background(secondary.copy(alpha = 0.14f)),
    )
}

@Composable
private fun VerticalHairline(secondary: Color) {
    Box(
        modifier = Modifier
            .width(0.5.dp)
            .height(26.dp)
            .background(secondary.copy(alpha = 0.14f)),
    )
}

/** 空状态：不套卡，居中图标 + 标题 + 一句指路 */
@Composable
private fun EmptyDownloads(primary: Color, secondary: Color) {
    Column(
        modifier = Modifier.fillMaxWidth().padding(top = 44.dp, bottom = 12.dp),
        horizontalAlignment = Alignment.CenterHorizontally,
    ) {
        Box(
            modifier = Modifier
                .size(58.dp)
                .clip(CircleShape)
                .background(secondary.copy(alpha = 0.10f)),
            contentAlignment = Alignment.Center,
        ) {
            Icon(
                painter = painterResource(R.drawable.ic_history),
                contentDescription = null,
                tint = secondary,
                modifier = Modifier.size(24.dp),
            )
        }
        Text(
            text = stringResource(R.string.downloads_empty_title),
            fontSize = 15.sp,
            fontWeight = FontWeight.Medium,
            color = primary,
            modifier = Modifier.padding(top = 16.dp),
        )
        Text(
            text = stringResource(R.string.downloads_empty),
            fontSize = 12.5.sp,
            lineHeight = 19.sp,
            color = secondary,
            textAlign = TextAlign.Center,
            modifier = Modifier.padding(top = 8.dp, start = 32.dp, end = 32.dp),
        )
    }
}

@Composable
private fun TaskCard(task: DownloadTask, viewModel: DownloadsViewModel) {
    val palette = LocalCatClawPalette.current
    val primary = Color(palette.textPrimary)
    val secondary = Color(palette.textSecondary)
    val fraction = if (task.total > 0) (task.downloaded.toFloat() / task.total).coerceIn(0f, 1f) else 0f
    val sizeText = if (task.total > 0) {
        stringResource(R.string.downloads_bytes, SongDetailFormat.bytes(task.downloaded), SongDetailFormat.bytes(task.total))
    } else {
        task.fileName
    }

    Column(
        modifier = Modifier
            .fillMaxWidth()
            .glassCard(palette = palette, corner = 22)
            .padding(horizontal = 14.dp, vertical = 13.dp),
    ) {
        Row(verticalAlignment = Alignment.CenterVertically) {
            // 封面位：下载任务只有标题（还没入库），所以给一个图标块，别留空洞
            Box(
                modifier = Modifier
                    .size(42.dp)
                    .clip(RoundedCornerShape(13.dp))
                    .background(secondary.copy(alpha = 0.10f)),
                contentAlignment = Alignment.Center,
            ) {
                Icon(
                    painter = painterResource(R.drawable.ic_download),
                    contentDescription = null,
                    tint = secondary,
                    modifier = Modifier.size(19.dp),
                )
            }
            Spacer(Modifier.width(12.dp))
            Column(modifier = Modifier.weight(1f)) {
                Text(
                    text = task.title,
                    fontSize = 14.sp,
                    fontWeight = FontWeight.Medium,
                    color = primary,
                    maxLines = 1,
                    overflow = TextOverflow.Ellipsis,
                )
                Text(
                    text = statusText(task.state) + " · " + sizeText,
                    fontSize = 11.5.sp,
                    color = secondary,
                    maxLines = 1,
                    overflow = TextOverflow.Ellipsis,
                    modifier = Modifier.padding(top = 2.dp),
                )
            }
            if (task.total > 0 && (task.state == DownloadState.Running || task.state == DownloadState.Paused)) {
                Text(
                    text = (fraction * 100).toInt().toString() + "%",
                    fontSize = 13.sp,
                    fontWeight = FontWeight.Medium,
                    color = primary,
                    modifier = Modifier.padding(start = 10.dp),
                )
            }
        }

        // 进度条：细、圆头、贴底，读完进度再读下面的动作
        Box(
            modifier = Modifier
                .fillMaxWidth()
                .padding(top = 11.dp)
                .height(4.dp)
                .clip(RoundedCornerShape(2.dp))
                .background(secondary.copy(alpha = 0.16f)),
        ) {
            Box(
                modifier = Modifier
                    .fillMaxWidth(fraction)
                    .height(4.dp)
                    .background(Color(palette.primaryDark)),
            )
        }

        Row(
            modifier = Modifier.padding(top = 10.dp),
            horizontalArrangement = Arrangement.spacedBy(8.dp),
        ) {
            when (task.state) {
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
