package com.catclaw.music.kt.ui.settings

import android.net.Uri
import androidx.compose.foundation.background
import androidx.compose.foundation.clickable
import androidx.compose.animation.core.LinearEasing
import androidx.compose.animation.core.animateFloat
import androidx.compose.animation.core.infiniteRepeatable
import androidx.compose.animation.core.rememberInfiniteTransition
import androidx.compose.animation.core.tween
import androidx.compose.foundation.layout.BoxWithConstraints
import androidx.compose.foundation.layout.fillMaxHeight
import androidx.compose.foundation.layout.offset
import androidx.compose.foundation.layout.width
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
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
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
    // 自研文件夹管理器（SAF 拿不到真实路径，改标签要整文件重写）
    var showPicker by remember { mutableStateOf(false) }

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
                onClick = { showPicker = true },
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
                        // 旧版本的 SAF 树 URI 在新扫描器里会被跳过：就地标出来，别让人以为扫描没读到
                        Text(
                            text = if (uri.startsWith("/")) java.io.File(uri).name else "旧 SAF 文件夹（请删除后重新添加）",
                            fontSize = 13.sp,
                            color = primary,
                            maxLines = 1,
                            overflow = TextOverflow.Ellipsis,
                        )
                        Text(
                            text = if (uri.startsWith("/")) uri else folderPath(uri),
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
                text = when {
                    view.scanning -> stringResource(R.string.library_scanning)
                    view.lastResult != null -> stringResource(
                        R.string.scan_result,
                        view.lastResult!!.scanned,
                        view.lastResult!!.inserted,
                        view.lastResult!!.removed,
                    )
                    else -> stringResource(R.string.local_scan_ready)
                },
                fontSize = 13.sp,
                color = secondary,
            )
            if (view.scanning) {
                ScanProgressBar(accent)
            }
            PrimaryButton(
                text = stringResource(
                    if (view.scanning) R.string.library_scanning else R.string.local_scan_now,
                ),
                accent = accent,
                enabled = canScan,
                loading = view.scanning,
                onClick = viewModel::scan,
            )
        }
        Spacer(Modifier.height(40.dp))
    }

    if (showPicker) {
        FolderPickerDialog(
            onDismiss = { showPicker = false },
            onPick = { path ->
                viewModel.addFolder(path)
                showPicker = false
            },
        )
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

/**
 * 扫描进度光带：一条主色渐变从左流到右（indeterminate）。
 * 扫描本身是 suspend 的一次性调用，没有真实进度可报，所以用流动感表达"正在跑"。
 */
@Composable
private fun ScanProgressBar(color: Color) {
    val transition = rememberInfiniteTransition(label = "scan-progress")
    val phase by transition.animateFloat(
        initialValue = 0f,
        targetValue = 1f,
        animationSpec = infiniteRepeatable(tween(durationMillis = 1200, easing = LinearEasing)),
        label = "scan-phase",
    )
    BoxWithConstraints(
        modifier = Modifier
            .fillMaxWidth()
            .height(4.dp)
            .clip(RoundedCornerShape(999.dp))
            .background(color.copy(alpha = 0.16f)),
    ) {
        val totalPx = constraints.maxWidth.toFloat()
        val bandPx = totalPx * 0.38f
        val offsetPx = ((totalPx - bandPx) * phase).toInt()
        Box(
            modifier = Modifier
                .offset { androidx.compose.ui.unit.IntOffset(offsetPx, 0) }
                .width(with(androidx.compose.ui.platform.LocalDensity.current) { bandPx.toDp() })
                .fillMaxHeight()
                .background(
                    androidx.compose.ui.graphics.Brush.horizontalGradient(
                        listOf(Color.Transparent, color, Color.Transparent),
                    ),
                ),
        )
    }
}

/** 满宽主色按钮（MAUI：Primary 底 / 白字 / 高 40 / 圆角 16） */
@Composable
internal fun PrimaryButton(
    text: String,
    accent: Color,
    enabled: Boolean = true,
    loading: Boolean = false,
    onClick: () -> Unit,
) {
    Box(
        modifier = Modifier
            .fillMaxWidth()
            .height(42.dp)
            .clip(RoundedCornerShape(14.dp))
            .background(if (enabled || loading) accent else accent.copy(alpha = 0.35f))
            .clickable(enabled = enabled, onClick = onClick),
        contentAlignment = Alignment.Center,
    ) {
        if (loading) {
            // 按钮内进度：一段浅色填充从左扫到右（indeterminate，扫描没有真实百分比）
            val transition = rememberInfiniteTransition(label = "btn-progress")
            val fraction by transition.animateFloat(
                initialValue = 0f,
                targetValue = 1f,
                animationSpec = infiniteRepeatable(tween(durationMillis = 1400, easing = LinearEasing)),
                label = "btn-progress-fraction",
            )
            Box(
                modifier = Modifier
                    .align(Alignment.CenterStart)
                    .fillMaxHeight()
                    .fillMaxWidth(fraction)
                    .background(Color.White.copy(alpha = 0.20f)),
            )
        }
        Text(text = text, fontSize = 13.sp, fontWeight = FontWeight.SemiBold, color = Color.White)
    }
}

/**
 * SAF tree URI → 可读路径（对应 MAUI 的 `Path` 字段）。
 * 例：`content://…/tree/primary%3AMusic` → `/storage/emulated/0/Music`；
 * 非主存储（SD 卡）保留 `XXXX-XXXX:Folder` 形式。
 */
private fun folderPath(uri: String): String {
    if (uri.startsWith("/")) return uri
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
