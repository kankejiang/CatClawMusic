package com.catclaw.music.kt.ui.settings

import androidx.activity.compose.rememberLauncherForActivityResult
import androidx.activity.result.contract.ActivityResultContracts
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.Arrangement
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
import androidx.compose.foundation.verticalScroll
import androidx.compose.material3.Switch
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import androidx.hilt.navigation.compose.hiltViewModel
import androidx.lifecycle.compose.collectAsStateWithLifecycle
import com.catclaw.music.kt.R
import com.catclaw.music.kt.ui.theme.BackTile
import com.catclaw.music.kt.ui.theme.GlassSwitch
import com.catclaw.music.kt.ui.theme.LocalCatClawPalette
import com.catclaw.music.kt.ui.theme.PillLabel
import com.catclaw.music.kt.ui.theme.glassCard
import com.catclaw.music.kt.ui.theme.highlightOnGlassCard

/**
 * 本地音乐设置 —— 对应 MAUI `LocalMusicSettingsPage` 的三张卡：扫描方式 / 音乐文件夹 / 扫描状态。
 *
 * 只摆**真有消费者**的东西：C# 那页上的 `ScanSubfolders` 与 `AutoScan` 查证过没有任何读取方
 * （递归是写死的、自动扫描从来没被触发过），所以这里不放那两个开关。
 * 文件夹选择走系统 SAF 树选择器（与 C# `FolderPicker` 同一个 Intent），
 * 不在应用内自己写一个文件浏览器 —— 那需要 MANAGE_EXTERNAL_STORAGE，本端没有申请。
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
    val duplicateHint = stringResource(R.string.local_scan_folder_duplicate)
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

        SectionCard(stringResource(R.string.local_scan_method)) {
            GlassSwitch(
                label = stringResource(R.string.local_scan_media_store),
                subtitle = stringResource(R.string.local_scan_media_store_sub),
                checked = view.useMediaStore,
                onChange = viewModel::setMediaStore,
            )
            GlassSwitch(
                label = stringResource(R.string.local_scan_saf),
                subtitle = stringResource(R.string.local_scan_saf_sub),
                checked = view.useSafScan,
                onChange = viewModel::setSafScan,
            )
            if (!view.useMediaStore && !view.useSafScan) {
                // 两种全关时扫描按钮是灰的，这里必须说清为什么灰着，而不是留一个点不动的按钮
                Text(
                    text = stringResource(R.string.local_scan_none),
                    fontSize = 12.sp,
                    color = Color(highlightOnGlassCard(palette)),
                )
            }
        }
        Spacer(Modifier.height(12.dp))

        SectionCard(stringResource(R.string.local_scan_folders)) {
            if (view.folders.isEmpty()) {
                Text(text = stringResource(R.string.local_scan_no_folder), fontSize = 13.sp, color = secondary)
            }
            view.folders.forEach { uri ->
                Row(
                    modifier = Modifier.fillMaxWidth().padding(vertical = 6.dp),
                    verticalAlignment = Alignment.CenterVertically,
                ) {
                    Text(
                        text = folderLabel(uri),
                        fontSize = 14.sp,
                        color = primary,
                        maxLines = 1,
                        overflow = TextOverflow.MiddleEllipsis,
                        modifier = Modifier.weight(1f),
                    )
                    Text(
                        text = stringResource(R.string.Delete),
                        fontSize = 13.sp,
                        color = secondary,
                        textAlign = TextAlign.Center,
                        modifier = Modifier
                            .size(width = 56.dp, height = 44.dp)
                            .clickable(onClickLabel = stringResource(R.string.local_scan_remove_folder)) {
                                viewModel.removeFolder(uri)
                            },
                    )
                }
            }
            if (view.duplicateFolder) {
                Text(
                    text = duplicateHint,
                    fontSize = 12.sp,
                    color = Color(highlightOnGlassCard(palette)),
                )
            }
            Spacer(Modifier.height(8.dp))
            PillLabel(
                text = stringResource(R.string.local_scan_add_folder),
                onClick = { picker.launch(null) },
                filled = false,
            )
        }
        Spacer(Modifier.height(12.dp))

        SectionCard(stringResource(R.string.local_scan_status)) {
            PillLabel(
                text = stringResource(
                    if (view.scanning) R.string.library_scanning else R.string.local_scan_now,
                ),
                onClick = viewModel::scan,
                enabled = !view.scanning && (view.useMediaStore || view.useSafScan),
            )
            view.lastResult?.let {
                Text(
                    text = stringResource(
                        R.string.scan_result,
                        it.scanned,
                        it.inserted,
                        it.removed,
                    ),
                    fontSize = 12.sp,
                    color = secondary,
                )
            }
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
        Text(text = title, fontSize = 15.sp, fontWeight = FontWeight.Bold, color = Color(palette.textPrimary))
        content()
    }
}

