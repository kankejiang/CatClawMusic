package com.catclaw.music.kt.ui.lyrics

import android.content.Context
import android.net.Uri
import androidx.activity.compose.rememberLauncherForActivityResult
import androidx.activity.result.contract.ActivityResultContracts
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
import androidx.compose.material3.Switch
import androidx.compose.material3.SwitchDefaults
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import androidx.hilt.navigation.compose.hiltViewModel
import androidx.lifecycle.compose.collectAsStateWithLifecycle
import com.catclaw.music.kt.R
import com.catclaw.music.kt.lyrico.plugin.PluginEntry
import com.catclaw.music.kt.lyrico.ui.SourcePluginViewModel
import com.catclaw.music.kt.ui.theme.BackTile
import com.catclaw.music.kt.ui.theme.GlassChip
import com.catclaw.music.kt.ui.theme.LocalCatClawPalette
import com.catclaw.music.kt.ui.theme.PillLabel
import com.catclaw.music.kt.ui.theme.glassCard
import com.catclaw.music.kt.ui.theme.highlightOnGlassCard

/**
 * 歌词源插件管理（Lyrico 内置源 + 用户导入源）。
 *
 * 与设置页里那个「插件」（MAUI 插件）是两套体系，所以单独一页、单独入口，
 * 避免用户把两种「插件」混为一谈。
 */
@Composable
fun SourcePluginScreen(
    onBack: () -> Unit,
    viewModel: SourcePluginViewModel = hiltViewModel(),
) {
    val palette = LocalCatClawPalette.current
    val state by viewModel.state.collectAsStateWithLifecycle()
    val ink = Color(palette.textPrimary)
    val dim = Color(palette.textSecondary)
    val accent = Color(highlightOnGlassCard(palette))
    val context = LocalContext.current

    // 用系统文件选择器挑 ZIP：不申请存储权限也能读（SAF 授权单文件）
    val pickZip = rememberLauncherForActivityResult(
        contract = ActivityResultContracts.OpenDocument(),
    ) { uri: Uri? ->
        if (uri != null) {
            val bytes = readAll(context, uri)
            if (bytes != null) {
                viewModel.installZip(bytes)
            } else {
                // 读取失败也要有反馈，不能静默
                viewModel.installZip(ByteArray(0))
            }
        }
    }

    Column(
        modifier = Modifier
            .fillMaxSize()
            .statusBarsPadding()
            // 这页是全屏二级页，底栏在它之下不会出现，只需避让系统导航条
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
                    text = stringResource(R.string.source_plugin_title),
                    fontSize = 20.sp,
                    fontWeight = FontWeight.Bold,
                    color = ink,
                )
                Text(
                    text = stringResource(R.string.source_plugin_subtitle),
                    fontSize = 11.sp,
                    color = dim,
                )
            }
        }
        Spacer(Modifier.height(10.dp))
        Row(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
            PillLabel(
                text = stringResource(R.string.source_plugin_import),
                onClick = { pickZip.launch(arrayOf("application/zip", "application/octet-stream", "*/*")) },
                filled = true,
                enabled = !state.busy,
            )
            PillLabel(
                text = stringResource(R.string.source_plugin_refresh),
                onClick = viewModel::refresh,
                filled = false,
                enabled = !state.busy,
            )
            PillLabel(
                text = if (state.onlineLoading) {
                    stringResource(R.string.source_plugin_checking)
                } else {
                    stringResource(R.string.source_plugin_check_online)
                },
                onClick = { viewModel.checkOnline() },
                filled = false,
                enabled = !state.onlineLoading && !state.busy,
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
                kotlinx.coroutines.delay(3000)
                viewModel.consumeMessage()
            }
        }

        Spacer(Modifier.height(8.dp))
        if (state.loading) {
            Text(text = stringResource(R.string.batch_loading), fontSize = 13.sp, color = dim)
            return@Column
        }
        if (state.plugins.isEmpty()) {
            Text(
                text = stringResource(R.string.source_plugin_empty),
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
            item { SectionLabel(stringResource(R.string.source_plugin_section_local)) }
            items(state.plugins, key = { it.pluginId }) { plugin ->
                PluginRow(
                    plugin = plugin,
                    enabled = plugin.enabled,
                    onToggle = { viewModel.setEnabled(plugin.pluginId, !plugin.enabled) },
                    onUninstall = { viewModel.uninstall(plugin.pluginId, plugin.bundled) },
                )
            }

            if (state.online.isNotEmpty() || state.onlineError != null) {
                item { SectionLabel(stringResource(R.string.source_plugin_section_online)) }
            }
            state.onlineError?.let { err ->
                item {
                    Text(text = err, fontSize = 11.sp, color = dim)
                }
            }
            items(state.online, key = { "online-" + it.plugin.id }) { item ->
                OnlinePluginRow(item = item, onInstall = { viewModel.installFromCatalog(item) })
            }
        }
    }
}

@Composable
private fun PluginRow(
    plugin: PluginEntry,
    enabled: Boolean,
    onToggle: () -> Unit,
    onUninstall: () -> Unit,
) {
    val palette = LocalCatClawPalette.current
    val ink = Color(palette.textPrimary)
    val dim = Color(palette.textSecondary)
    val accent = Color(highlightOnGlassCard(palette))

    Column(
        modifier = Modifier
            .fillMaxWidth()
            .glassCard(palette = palette, corner = 16)
            .padding(horizontal = 12.dp, vertical = 10.dp),
    ) {
        Row(verticalAlignment = Alignment.CenterVertically) {
            Text(
                text = plugin.displayName,
                fontSize = 14.sp,
                fontWeight = FontWeight.SemiBold,
                color = ink,
                modifier = Modifier.weight(1f),
            )
            // 用开关而不是胶囊按钮：位置（左/右）本身就是状态，不需要文案解释。
            // 胶囊方案下「已启用」到底能不能点、「停用」当前是开还是关，都要靠猜；
            // 实测曾把 7 个插件全误关成 enabled=0。
            Switch(
                checked = enabled,
                onCheckedChange = { onToggle() },
                colors = SwitchDefaults.colors(
                    checkedThumbColor = Color(palette.primary),
                    checkedTrackColor = Color(palette.primary).copy(alpha = 0.32f),
                    uncheckedThumbColor = Color(palette.textSecondary),
                    uncheckedTrackColor = ink.copy(alpha = 0.12f),
                    uncheckedBorderColor = ink.copy(alpha = 0.20f),
                ),
            )
        }
        Text(
            text = listOfNotNull(
                plugin.versionName.takeIf { it.isNotBlank() }?.let { "v$it" },
                plugin.author.takeIf { it.isNotBlank() },
                if (plugin.bundled) stringResource(R.string.source_plugin_bundled) else null,
            ).joinToString(" · "),
            fontSize = 10.sp,
            color = dim,
        )
        if (plugin.description.isNotBlank()) {
            Text(
                text = plugin.description,
                fontSize = 11.sp,
                color = dim,
                maxLines = 2,
            )
        }
        Row(verticalAlignment = Alignment.CenterVertically) {
            Text(
                text = plugin.capabilities.joinToString(" / "),
                fontSize = 10.sp,
                color = accent,
                modifier = Modifier.weight(1f),
                maxLines = 1,
            )
            if (!plugin.bundled) {
                Text(
                    text = stringResource(R.string.source_plugin_uninstall),
                    fontSize = 10.sp,
                    color = Color(palette.textSecondary),
                    modifier = Modifier
                        .clip(RoundedCornerShape(8.dp))
                        .clickable(onClick = onUninstall),
                )
            }
        }
    }
}

/** 读整个 content:// 文档；失败返回 null（调用方要据此给反馈）。 */
private fun readAll(context: Context, uri: Uri): ByteArray? = runCatching {
    context.contentResolver.openInputStream(uri)?.use { it.readBytes() }
}.getOrNull()
@Composable
private fun SectionLabel(text: String) {
    val palette = LocalCatClawPalette.current
    Text(
        text = text,
        fontSize = 11.sp,
        fontWeight = FontWeight.SemiBold,
        color = Color(palette.textSecondary),
        modifier = Modifier.padding(top = 8.dp, bottom = 2.dp),
    )
}

/** 在线目录里的一条：显示版本与「安装/已是最新」。 */
@Composable
private fun OnlinePluginRow(
    item: com.catclaw.music.kt.lyrico.ui.OnlinePluginItem,
    onInstall: () -> Unit,
) {
    val palette = LocalCatClawPalette.current
    val ink = Color(palette.textPrimary)
    val dim = Color(palette.textSecondary)
    val accent = Color(highlightOnGlassCard(palette))
    val plugin = item.plugin
    Row(
        modifier = Modifier
            .fillMaxWidth()
            .glassCard(palette = palette, corner = 14)
            .padding(horizontal = 12.dp, vertical = 10.dp),
        verticalAlignment = Alignment.CenterVertically,
    ) {
        Column(modifier = Modifier.weight(1f)) {
            Text(text = plugin.name, fontSize = 14.sp, fontWeight = FontWeight.SemiBold, color = ink)
            Text(
                text = listOfNotNull(
                    plugin.versionName.takeIf { it.isNotBlank() }?.let { "v$it" },
                    "code ${plugin.versionCode}",
                ).joinToString(" · "),
                fontSize = 10.sp,
                color = dim,
            )
            if (plugin.changelog.isNotBlank()) {
                Text(
                    text = plugin.changelog,
                    fontSize = 10.sp,
                    color = dim,
                    maxLines = 2,
                )
            }
        }
        Text(
            text = when (item.status) {
                com.catclaw.music.kt.lyrico.plugin.PluginUpdateStatus.NOT_INSTALLED ->
                    stringResource(R.string.source_plugin_install)
                com.catclaw.music.kt.lyrico.plugin.PluginUpdateStatus.UPDATE_AVAILABLE ->
                    stringResource(R.string.source_plugin_update)
                com.catclaw.music.kt.lyrico.plugin.PluginUpdateStatus.UP_TO_DATE ->
                    stringResource(R.string.source_plugin_uptodate)
            },
            fontSize = 11.sp,
            color = if (item.status == com.catclaw.music.kt.lyrico.plugin.PluginUpdateStatus.UP_TO_DATE) {
                dim
            } else {
                accent
            },
            modifier = Modifier
                .clip(RoundedCornerShape(8.dp))
                .clickable(
                    enabled = item.status != com.catclaw.music.kt.lyrico.plugin.PluginUpdateStatus.UP_TO_DATE,
                    onClick = onInstall,
                ),
        )
    }
}