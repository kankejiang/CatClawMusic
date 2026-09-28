package com.catclaw.music.kt.ui.settings

import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.verticalScroll
import androidx.compose.material3.AlertDialog
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.collectAsState
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
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import androidx.hilt.navigation.compose.hiltViewModel
import androidx.compose.material3.LinearProgressIndicator
import com.catclaw.music.kt.R
import com.catclaw.music.kt.ui.theme.BackTile
import com.catclaw.music.kt.ui.theme.EmptyState
import com.catclaw.music.kt.ui.theme.LocalCatClawPalette
import com.catclaw.music.kt.ui.theme.PillLabel
import com.catclaw.music.kt.ui.theme.glassCard
import com.catclaw.music.kt.ui.theme.highlightOnGlassCard
import java.io.File

/**
 * 备份与恢复子页 —— MAUI `BackupRestorePage` 的对应物。
 * 视觉全走共享件（玻璃卡 / 胶囊 / 空状态 / 返回块），这里只写这一页特有的东西：
 * 进度条、备份文件列表、恢复前的确认框。
 */
@Composable
fun BackupRestoreScreen(
    onBack: () -> Unit,
    viewModel: BackupViewModel = hiltViewModel(),
) {
    val palette = LocalCatClawPalette.current
    val state by viewModel.state.collectAsState()
    // 每次回到这页都重新数一遍：备份是页外也会变的磁盘状态，缓存进 StateFlow 就会看到过期列表
    val files = remember(state.message, state.busy) { viewModel.backups() }
    var pending by remember { mutableStateOf<File?>(null) }

    Column(
        modifier = Modifier
            .fillMaxWidth()
            .verticalScroll(rememberScrollState())
            .padding(horizontal = 18.dp),
    ) {
        Row(verticalAlignment = Alignment.CenterVertically, modifier = Modifier.fillMaxWidth().padding(top = 10.dp)) {
            BackTile(onBack = onBack, description = stringResource(R.string.GoBack))
            Spacer(Modifier.width(12.dp))
            Text(stringResource(R.string.PageTitleBackupRestore), fontSize = 22.sp, fontWeight = FontWeight.Bold, color = Color(palette.textPrimary))
        }
        Text(
            stringResource(R.string.BackupRestoreSubtitle),
            fontSize = 13.sp,
            color = Color(palette.textSecondary),
            modifier = Modifier.padding(top = 8.dp, bottom = 4.dp),
        )

        Column(
            modifier = Modifier
                .fillMaxWidth()
                .padding(vertical = 10.dp)
                .glassCard(palette = palette, corner = 22)
                .padding(16.dp),
        ) {
            Text(stringResource(R.string.BackupData), fontSize = 15.sp, fontWeight = FontWeight.SemiBold, color = Color(palette.textPrimary))
            Text(stringResource(R.string.BackupDataDesc), fontSize = 12.sp, color = Color(palette.textSecondary), modifier = Modifier.padding(top = 2.dp))
            if (state.busy) {
                LinearProgressIndicator(
                    progress = { (state.percent / 100f).coerceIn(0f, 1f) },
                    modifier = Modifier.fillMaxWidth().padding(top = 12.dp),
                    // 进度条填充也走判据：浅色主题下 primary 压玻璃卡只有 2.28，够不到控件的 3.0
                    color = Color(highlightOnGlassCard(palette)),
                    trackColor = Color(palette.textPrimary).copy(alpha = 0.10f),
                )
            }
            state.message?.let {
                Text(it, fontSize = 12.sp, color = Color(palette.textSecondary), maxLines = 2, overflow = TextOverflow.Ellipsis, modifier = Modifier.padding(top = 10.dp))
            }
            Box(modifier = Modifier.padding(top = 12.dp)) {
                PillLabel(text = stringResource(R.string.CreateBackup), onClick = viewModel::createBackup, enabled = !state.busy)
            }
            Text(
                viewModel.backupRoot().absolutePath,
                fontSize = 11.sp,
                color = Color(palette.textSecondary),
                modifier = Modifier.padding(top = 10.dp),
            )
        }

        Column(
            modifier = Modifier
                .fillMaxWidth()
                .padding(vertical = 10.dp)
                .glassCard(palette = palette, corner = 22)
                .padding(16.dp),
        ) {
            Text(stringResource(R.string.RestoreData), fontSize = 15.sp, fontWeight = FontWeight.SemiBold, color = Color(palette.textPrimary))
            Text(stringResource(R.string.RestoreDataDesc), fontSize = 12.sp, color = Color(palette.textSecondary), modifier = Modifier.padding(top = 2.dp, bottom = 10.dp))
            if (files.isEmpty()) {
                EmptyState(hint = stringResource(R.string.SelectBackupFile), iconRes = R.drawable.ic_library)
            } else {
                files.forEach { file ->
                    Row(
                        modifier = Modifier.fillMaxWidth().padding(vertical = 8.dp),
                        verticalAlignment = Alignment.CenterVertically,
                        horizontalArrangement = Arrangement.spacedBy(12.dp),
                    ) {
                        Column(modifier = Modifier.weight(1f)) {
                            Text(file.name, fontSize = 13.sp, fontWeight = FontWeight.Medium, color = Color(palette.textPrimary), maxLines = 1, overflow = TextOverflow.Ellipsis)
                            Text(
                                "%d · %s".format(file.length() / 1024, "KB"),
                                fontSize = 11.sp,
                                color = Color(palette.textSecondary),
                            )
                        }
                        PillLabel(
                            text = stringResource(R.string.RestoreData),
                            onClick = { pending = file },
                            filled = false,
                            enabled = !state.busy,
                        )
                    }
                }
            }
        }
        Spacer(Modifier.height(24.dp))
    }

    pending?.let { file ->
        AlertDialog(
            onDismissRequest = { pending = null },
            containerColor = Color(palette.backgroundStops.last().argb),
            title = { Text(stringResource(R.string.SelectBackupFile)) },
            text = { Text("${file.name}\n${stringResource(R.string.RestoreDataDesc)}", color = Color(palette.textPrimary)) },
            confirmButton = {
                PillLabel(
                    text = stringResource(R.string.OK),
                    onClick = {
                        pending = null
                        viewModel.restore(file)
                    },
                )
            },
            dismissButton = {
                PillLabel(text = stringResource(R.string.Cancel), onClick = { pending = null }, filled = false)
            },
        )
    }
}

