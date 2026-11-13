package com.catclaw.music.kt.ui.settings

import android.content.Intent
import android.net.Uri
import android.os.Environment
import android.provider.Settings
import androidx.compose.foundation.background
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.heightIn
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.items
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material3.Icon
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
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.res.painterResource
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import androidx.compose.ui.window.Dialog
import com.catclaw.music.kt.R
import com.catclaw.music.kt.ui.theme.LocalCatClawPalette
import com.catclaw.music.kt.ui.theme.glassCard
import java.io.File

/**
 * 自研文件夹管理器（替代 SAF 树选择器）。
 *
 * 为什么不用 SAF：SAF 只给文档 URI，改歌曲元数据（写 ID3）要整文件重写、拿不到真实路径；
 * 这里在 `MANAGE_EXTERNAL_STORAGE` 授权下直接浏览真实目录，选出来的路径可直接 `File` 读写。
 *
 * 根目录锁定在外部存储根（`/storage/emulated/0`），不允许往上走到 `/`，避免浏览系统目录。
 */
@Composable
fun FolderPickerDialog(
    onDismiss: () -> Unit,
    onPick: (String) -> Unit,
) {
    val palette = LocalCatClawPalette.current
    val primary = Color(palette.textPrimary)
    val secondary = Color(palette.textSecondary)
    val accent = Color(palette.primary)
    val context = LocalContext.current
    val root = remember { Environment.getExternalStorageDirectory().absolutePath }
    var currentPath by remember { mutableStateOf(root) }
    val granted = remember { Environment.isExternalStorageManager() }

    val dirs = remember(currentPath, granted) {
        if (!granted) {
            emptyList()
        } else {
            runCatching {
                File(currentPath).listFiles()
                    ?.filter { it.isDirectory && !it.name.startsWith(".") }
                    ?.sortedBy { it.name.lowercase() }
                    .orEmpty()
            }.getOrDefault(emptyList())
        }
    }

    Dialog(onDismissRequest = onDismiss) {
        Column(
            modifier = Modifier
                .fillMaxWidth()
                .heightIn(max = 560.dp)
                // Dialog 是独立窗口，玻璃的 backdrop 采不到下层 → 用 96% 主题底色打底
                .clip(RoundedCornerShape(24.dp))
                .background(Color(palette.backgroundStops.last().argb).copy(alpha = 0.96f))
                .glassCard(palette = palette, corner = 24)
                .padding(16.dp),
            verticalArrangement = Arrangement.spacedBy(10.dp),
        ) {
            Text(stringResource(R.string.folder_picker_title), fontSize = 18.sp, fontWeight = FontWeight.Bold, color = primary)
            Text(
                text = currentPath,
                fontSize = 11.sp,
                color = secondary,
                maxLines = 2,
                overflow = TextOverflow.Ellipsis,
            )

            if (!granted) {
                // 没授权时整个列表是空的，先给一条可点的出路
                Text(
                    text = stringResource(R.string.folder_picker_need_permission),
                    fontSize = 12.sp,
                    color = Color(com.catclaw.music.kt.ui.theme.highlightOnGlassCard(palette)),
                )
                PrimaryButton(
                    text = stringResource(R.string.folder_picker_grant),
                    accent = accent,
                ) {
                    val intent = Intent(Settings.ACTION_MANAGE_APP_ALL_FILES_ACCESS_PERMISSION).apply {
                        data = Uri.parse("package:${context.packageName}")
                    }
                    runCatching { context.startActivity(intent) }
                }
            } else {
                Row(
                    modifier = Modifier
                        .fillMaxWidth()
                        .clip(RoundedCornerShape(12.dp))
                        .clickable(enabled = currentPath != root) {
                            File(currentPath).parentFile?.let { currentPath = it.absolutePath }
                        }
                        .padding(horizontal = 10.dp, vertical = 8.dp),
                    verticalAlignment = Alignment.CenterVertically,
                    horizontalArrangement = Arrangement.spacedBy(8.dp),
                ) {
                    Text("↑", fontSize = 16.sp, color = if (currentPath != root) primary else secondary.copy(alpha = 0.4f))
                    Text(stringResource(R.string.folder_picker_up), fontSize = 13.sp, color = if (currentPath != root) primary else secondary.copy(alpha = 0.4f))
                }

                LazyColumn(modifier = Modifier.fillMaxWidth().heightIn(max = 320.dp)) {
                    items(dirs, key = { it.absolutePath }) { dir ->
                        Row(
                            modifier = Modifier
                                .fillMaxWidth()
                                .clip(RoundedCornerShape(12.dp))
                                .clickable { currentPath = dir.absolutePath }
                                .padding(horizontal = 10.dp, vertical = 10.dp),
                            verticalAlignment = Alignment.CenterVertically,
                            horizontalArrangement = Arrangement.spacedBy(10.dp),
                        ) {
                            Icon(
                                painter = painterResource(R.drawable.ic_nav_folder),
                                contentDescription = null,
                                tint = secondary,
                                modifier = Modifier.size(18.dp),
                            )
                            Text(
                                text = dir.name,
                                fontSize = 14.sp,
                                color = primary,
                                maxLines = 1,
                                overflow = TextOverflow.Ellipsis,
                                modifier = Modifier.weight(1f),
                            )
                            Text("›", fontSize = 16.sp, color = secondary.copy(alpha = 0.6f))
                        }
                    }
                }
            }

            Row(horizontalArrangement = Arrangement.spacedBy(10.dp)) {
                Box(
                    modifier = Modifier
                        .weight(1f)
                        .height(42.dp)
                        .clip(RoundedCornerShape(14.dp))
                        .background(Color(palette.textPrimary).copy(alpha = 0.10f))
                        .clickable(onClick = onDismiss),
                    contentAlignment = Alignment.Center,
                ) {
                    Text(stringResource(R.string.folder_picker_cancel), fontSize = 13.sp, color = primary)
                }
                Box(
                    modifier = Modifier
                        .weight(1f)
                        .height(42.dp)
                        .clip(RoundedCornerShape(14.dp))
                        .background(if (granted) accent else accent.copy(alpha = 0.35f))
                        .clickable(enabled = granted) { onPick(currentPath) },
                    contentAlignment = Alignment.Center,
                ) {
                    Text(
                        text = stringResource(R.string.folder_picker_pick),
                        fontSize = 13.sp,
                        fontWeight = FontWeight.SemiBold,
                        color = Color.White,
                    )
                }
            }
            Spacer(Modifier.height(2.dp))
        }
    }
}
