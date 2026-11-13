package com.catclaw.music.kt.ui.settings

import android.net.Uri
import androidx.activity.compose.rememberLauncherForActivityResult
import androidx.activity.result.contract.ActivityResultContracts
import androidx.compose.foundation.background
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.ColumnScope
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.verticalScroll
import androidx.compose.material3.Checkbox
import androidx.compose.material3.CheckboxDefaults
import androidx.compose.material3.Icon
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
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
import com.catclaw.music.kt.ui.theme.BackTile
import com.catclaw.music.kt.ui.theme.LocalCatClawPalette
import com.catclaw.music.kt.ui.theme.glassCard
import com.catclaw.music.kt.ui.theme.highlightOnGlassCard

/**
 * 本地音乐设置 —— 版式直接移植 MAUI `LocalMusicSettingsPage.xaml`：
 * 三张卡「扫描方式 / 音乐文件夹 / 扫描状态」，勾选框 + 满宽主色按钮 + 文件夹行（名称/路径/删除）。
 *
 * 视觉沿用原生玻璃语言（深色玻璃卡 + 主色点缀）；MAUI 那页没有消费者的开关不搬。
 * 文件夹选择仍走系统 SAF 树选择器（与 MAUI `FolderPicker` 同一个 Intent）。
 */
@Composable
fun LocalMusicSettingsScreen(
    onBack: () -> Unit,
    viewModel: LocalMusicSettingsViewModel = hiltViewModel(),
) {
    val palette = LocalCatClawPalette.current
    val view by viewModel.view.collectAsStateWithLifecycle()
    val primary = Color(palette.textPrimary)
    val secondary = Color(palette.textSecondary)
    val accent = Color(palette.primary)
    val duplicateHint = stringResource(R.string.local_scan_folder_duplicate)
    val canScan = !view.scanning && (view.useMediaStore || view.useSafScan)
    val picker = rememberLauncherForActivityResult(ActivityResultContracts.OpenDocumentTree()) { uri ->
        uri?.let { viewModel.addFolder(it) }
    }

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
                    text = stringResource(R.string.local_music_settings),
                    fontSize = 24.sp,
                    fontWeight = FontWeight.Bold,
                    color = primary,
                )
                Text(text = stringResource(R.string.local_music_subtitle), fontSize = 12.sp, color = secondary)
            }
        }
        Spacer(Modifier.height(14.dp))

        // ① 扫描方式（MAUI：两个 CheckBox + 标题/描述 + 并集提示）
        SectionCard(stringResource(R.string.local_scan_method)) {
            CheckRow(
                checked = view.useMediaStore,
                label = stringResource(R.string.local_scan_media_store),
                subtitle = stringResource(R.string.local_scan_media_store_sub),
                onChange = viewModel::setMediaStore,
            )
            CheckRow(
                checked = view.useSafScan,
                label = stringResource(R.string.local_scan_saf),
                subtitle = stringResource(R.string.local_scan_saf_sub),
                onChange = viewModel::setSafScan,
            )
            Text(
                text = stringResource(
                    if (!view.useMediaStore && !view.useSafScan) R.string.local_scan_none else R.string.local_scan_tip,
                ),
                fontSize = 11.sp,
                color = if (!view.useMediaStore && !view.useSafScan) {
                    Color(highlightOnGlassCard(palette))
                } else {
                    secondary
                },
                lineHeight = 15.sp,
            )
        }
        Spacer(Modifier.height(12.dp))

        // ② 音乐文件夹（满宽添加按钮 → 计数 → 文件夹行 + 红色删除）
        SectionCard(stringResource(R.string.local_scan_folders)) {
            PrimaryButton(
                text = stringResource(R.string.local_scan_add_folder),
                accent = accent,
                onClick = { picker.launch(null) },
            )
            Text(
                text = if (view.folders.isEmpty()) {
                    stringResource(R.string.local_scan_no_folder)
                } else {
                    stringResource(R.string.local_scan_folder_count, view.folders.size)
                },
                fontSize = 12.sp,
                color = secondary,
            )
            view.folders.forEach { uri ->
                Row(
                    modifier = Modifier.fillMaxWidth(),
                    verticalAlignment = Alignment.CenterVertically,
                    horizontalArrangement = Arrangement.spacedBy(8.dp),
                ) {
                    Icon(
                        painter = painterResource(R.drawable.ic_nav_folder),
                        contentDescription = null,
                        tint = secondary.copy(alpha = 0.8f),
                        modifier = Modifier.size(18.dp),
                    )
                    Column(Modifier.weight(1f)) {
                        Text(
                            text = folderLabel(uri),
                            fontSize = 13.sp,
                            color = primary,
                            maxLines = 1,
                            overflow = TextOverflow.Ellipsis,
                        )
                        Text(
                            text = folderPath(uri),
                            fontSize = 10.sp,
                            color = secondary.copy(alpha = 0.8f),
                            maxLines = 1,
                            overflow = TextOverflow.Ellipsis,
                        )
                    }
                    // 红色「删除」（MAUI：50×30 / 圆角 12 / 11sp）
                    Box(
                        modifier = Modifier
                            .clip(RoundedCornerShape(12.dp))
                            .background(Color(0xFFE0525F))
                            .clickable(onClickLabel = stringResource(R.string.local_scan_remove_folder)) {
                                viewModel.removeFolder(uri)
                            }
                            .padding(horizontal = 12.dp, vertical = 6.dp),
                    ) {
                        Text(text = stringResource(R.string.Delete), fontSize = 11.sp, color = Color.White)
                    }
                }
            }
            if (view.duplicateFolder) {
                Text(text = duplicateHint, fontSize = 12.sp, color = Color(highlightOnGlassCard(palette)))
            }
        }
        Spacer(Modifier.height(12.dp))

        // ③ 扫描状态（状态文字 → 满宽扫描按钮）
        SectionCard(stringResource(R.string.local_scan_status)) {
            Text(
                text = view.lastResult?.let {
                    stringResource(R.string.scan_result, it.scanned, it.inserted, it.removed)
                } ?: stringResource(R.string.local_scan_ready),
                fontSize = 13.sp,
                color = secondary,
            )
            PrimaryButton(
                text = stringResource(
                    if (view.scanning) R.string.library_scanning else R.string.local_scan_now,
                ),
                accent = accent,
                enabled = canScan,
                onClick = viewModel::scan,
            )
        }
        Spacer(Modifier.height(40.dp))
    }
}

@Composable
private fun SectionCard(title: String, content: @Composable ColumnScope.() -> Unit) {
    val palette = LocalCatClawPalette.current
    Column(
        modifier = Modifier
            .fillMaxWidth()
            .glassCard(palette = palette, corner = 26)
            .padding(16.dp),
        verticalArrangement = Arrangement.spacedBy(10.dp),
    ) {
        Text(text = title, fontSize = 16.sp, fontWeight = FontWeight.Bold, color = Color(palette.textPrimary))
        content()
    }
}

/** MAUI 那两行：方形勾选框 + 标题/描述（原生此前是 Switch，版式不同） */
@Composable
private fun CheckRow(
    checked: Boolean,
    label: String,
    subtitle: String,
    onChange: (Boolean) -> Unit,
) {
    val palette = LocalCatClawPalette.current
    Row(verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(6.dp)) {
        Checkbox(
            checked = checked,
            onCheckedChange = onChange,
            colors = CheckboxDefaults.colors(
                checkedColor = Color(palette.primary),
                uncheckedColor = Color(palette.textSecondary).copy(alpha = 0.7f),
                checkmarkColor = Color.White,
            ),
        )
        Column(Modifier.weight(1f), verticalArrangement = Arrangement.spacedBy(2.dp)) {
            Text(text = label, fontSize = 14.sp, color = Color(palette.textPrimary))
            Text(text = subtitle, fontSize = 11.sp, color = Color(palette.textSecondary), lineHeight = 15.sp)
        }
    }
}

/** 满宽主色按钮（MAUI：Primary 底 / 白字 / 高 40 / 圆角 16） */
@Composable
private fun PrimaryButton(
    text: String,
    accent: Color,
    enabled: Boolean = true,
    onClick: () -> Unit,
) {
    Box(
        modifier = Modifier
            .fillMaxWidth()
            .height(42.dp)
            .clip(RoundedCornerShape(14.dp))
            .background(if (enabled) accent else accent.copy(alpha = 0.35f))
            .clickable(enabled = enabled, onClick = onClick),
        contentAlignment = Alignment.Center,
    ) {
        Text(text = text, fontSize = 13.sp, fontWeight = FontWeight.SemiBold, color = Color.White)
    }
}

/**
 * SAF tree URI → 可读路径（对应 MAUI 的 `Path` 字段）。
 * 例：`content://…/tree/primary%3AMusic` → `/storage/emulated/0/Music`；
 * 非主存储（SD 卡）保留 `XXXX-XXXX:Folder` 形式。
 */
private fun folderPath(uri: String): String {
    val decoded = runCatching { Uri.decode(uri) }.getOrDefault(uri)
    val docId = decoded.substringAfter("tree/", decoded).substringBefore("/document/")
    return when {
        docId.startsWith("primary:") ->
            "/storage/emulated/0/" + docId.removePrefix("primary:").trimStart('/')
        docId.contains(':') -> {
            val volume = docId.substringBefore(':')
            val path = docId.substringAfter(':').trimStart('/')
            if (path.isEmpty()) volume else "$volume:$path"
        }
        else -> docId
    }
}
