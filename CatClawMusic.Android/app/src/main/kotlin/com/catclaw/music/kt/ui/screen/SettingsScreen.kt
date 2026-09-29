package com.catclaw.music.kt.ui.screen

import android.app.Activity
import android.content.Context
import android.content.ContextWrapper
import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.size
import androidx.compose.ui.unit.sp
import androidx.compose.material3.Icon
import androidx.compose.ui.graphics.Brush
import androidx.compose.ui.res.painterResource
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.items
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.verticalScroll
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material3.AlertDialog
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Slider
import androidx.compose.material3.SliderDefaults
import androidx.compose.material3.Switch
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
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.dp
import androidx.hilt.navigation.compose.hiltViewModel
import androidx.lifecycle.compose.collectAsStateWithLifecycle
import com.catclaw.music.kt.R
import com.catclaw.music.kt.core.theme.AppearanceMode
import com.catclaw.music.kt.ui.settings.AppLocale
import com.catclaw.music.kt.ui.settings.BackupRestoreScreen
import com.catclaw.music.kt.ui.settings.DiagnosticLogScreen
import com.catclaw.music.kt.ui.settings.AboutScreen
import com.catclaw.music.kt.ui.settings.PermissionManagementScreen
import com.catclaw.music.kt.ui.settings.AiSettingsScreen
import com.catclaw.music.kt.ui.plugin.PluginRow
import com.catclaw.music.kt.ui.plugin.PluginSettingsViewModel
import com.catclaw.music.kt.ui.settings.SettingsViewModel
import com.catclaw.music.kt.data.lyrics.DesktopLyricSettings
import com.catclaw.music.kt.data.settings.CacheSettings
import com.catclaw.music.kt.ui.theme.LocalCatClawPalette
import com.catclaw.music.kt.ui.theme.highlightOnGlassCard
import com.catclaw.music.kt.ui.theme.BackTile
import com.catclaw.music.kt.ui.theme.GlassChip
import com.catclaw.music.kt.ui.theme.PillLabel
import com.catclaw.music.kt.ui.theme.TouchMin
import com.catclaw.music.kt.ui.theme.EmptyState
import com.catclaw.music.kt.ui.theme.glassCard

/** MAUI `SettingsPage` 是枢纽 + push 子页；这里用同一份状态在整屏之间切换 */
private enum class SettingsDest { Hub, Appearance, DesktopLyric, Plugin, General, Ai, Backup, Permission, DiagnosticLog, About }

/** M4 壳层：设置枢纽页。语言切换以自己的 SharedPreferences 为事实来源（那台 MIUI 没有 locale_manager 服务），51 个语言全在包里。 */
@Composable
fun SettingsScreen(
    onBack: () -> Unit,
    onOpenRemote: () -> Unit = {},
    viewModel: SettingsViewModel = hiltViewModel(),
) {
    val palette = LocalCatClawPalette.current
    var dest by remember { mutableStateOf(SettingsDest.Hub) }

    when (dest) {
        SettingsDest.Appearance -> AppearanceSettingsScreen({ dest = SettingsDest.Hub }, viewModel)
        SettingsDest.DesktopLyric -> DesktopLyricSettingsScreen({ dest = SettingsDest.Hub }, viewModel)
        SettingsDest.Plugin -> PluginSettingsScreen(onBack = { dest = SettingsDest.Hub })
        SettingsDest.General -> GeneralSettingsScreen({ dest = SettingsDest.Hub }, viewModel)
        SettingsDest.Ai -> AiSettingsScreen(onBack = { dest = SettingsDest.Hub })
        SettingsDest.Backup -> BackupRestoreScreen(onBack = { dest = SettingsDest.Hub })
        SettingsDest.Permission -> PermissionManagementScreen(onBack = { dest = SettingsDest.Hub })
        SettingsDest.About -> AboutScreen(onBack = { dest = SettingsDest.Hub })
        SettingsDest.DiagnosticLog -> DiagnosticLogScreen(onBack = { dest = SettingsDest.Hub })
        SettingsDest.Hub -> Unit
    }
    if (dest != SettingsDest.Hub) return

    val mode by viewModel.mode.collectAsStateWithLifecycle()
    val aiStatus by viewModel.currentConfigName.collectAsStateWithLifecycle()

    val primary = Color(palette.textPrimary)
    val secondary = Color(palette.textSecondary)

    Column(
        modifier = Modifier
            .fillMaxSize()
            .verticalScroll(rememberScrollState())
            .padding(horizontal = 20.dp),
    ) {
        // HeroCard：照 MAUI `SettingsPage.xaml` —— 渐变卡 + 左上返回 + 标题/副标题 + 右上深浅色切换
        Row(
            modifier = Modifier
                .fillMaxWidth()
                .padding(top = 10.dp, bottom = 6.dp)
                .clip(RoundedCornerShape(26.dp))
                .background(
                    Brush.linearGradient(
                        listOf(Color(0x7A55D6FF), Color(0x4D7ED8C3)),
                    ),
                )
                .border(1.dp, primary.copy(alpha = 0.16f), RoundedCornerShape(26.dp))
                .padding(start = 10.dp, end = 12.dp, top = 12.dp, bottom = 12.dp),
            verticalAlignment = Alignment.CenterVertically,
        ) {
            Box(
                modifier = Modifier
                    .size(TouchMin)
                    .clip(RoundedCornerShape(14.dp))
                    .clickable(onClickLabel = stringResource(R.string.GoBack)) { onBack() },
                contentAlignment = Alignment.Center,
            ) {
                Icon(
                    painter = painterResource(R.drawable.ic_arrow_back),
                    contentDescription = stringResource(R.string.GoBack),
                    tint = primary,
                    modifier = Modifier.size(19.dp),
                )
            }
            Spacer(Modifier.width(12.dp))
            Column(modifier = Modifier.weight(1f)) {
                Text(
                    text = stringResource(R.string.Settings),
                    fontSize = 28.sp,
                    fontWeight = FontWeight.Bold,
                    color = primary,
                )
                Text(
                    text = stringResource(R.string.SettingsSubtitle),
                    fontSize = 14.sp,
                    // 渐变上的次要文字用正文墨色（层级靠字号），次要色压在半透明渐变上只有 3.56/3.93:1
                    color = primary,
                    maxLines = 2,
                    overflow = TextOverflow.Ellipsis,
                )
            }
            Box(
                modifier = Modifier
                    .size(48.dp)
                    .clip(RoundedCornerShape(14.dp))
                    .background(primary.copy(alpha = 0.12f))
                    .border(1.dp, primary.copy(alpha = 0.16f), RoundedCornerShape(14.dp))
                    .clickable {
                        viewModel.setMode(if (mode == AppearanceMode.Dark) AppearanceMode.Light else AppearanceMode.Dark)
                    },
                contentAlignment = Alignment.Center,
            ) {
                Icon(
                    painter = painterResource(R.drawable.ic_dark_mode),
                    contentDescription = stringResource(R.string.Appearance),
                    tint = primary,
                    modifier = Modifier.size(28.dp),
                )
            }
        }
        Spacer(Modifier.height(10.dp))

        // 枢纽：MAUI 的每一行自己是一张 GlassCard，上面挂一个 ✦ 分组标题
        SectionHeader(stringResource(R.string.ContentAndServices))
        HubRow(
            iconRes = R.drawable.ic_edit,
            tileArgb = 0x20C4B5FDL,
            title = stringResource(R.string.Appearance),
            desc = stringResource(R.string.AppearanceDesc),
            onClick = { dest = SettingsDest.Appearance },
        )
        HubRow(
            iconRes = R.drawable.ic_lyrics,
            tileArgb = 0x2042A5F5L,
            title = stringResource(R.string.PageTitleDesktopLyric),
            desc = stringResource(R.string.DesktopLyricDesc),
            onClick = { dest = SettingsDest.DesktopLyric },
        )
        HubRow(
            iconRes = R.drawable.ic_search,
            tileArgb = 0x2042A5F5L,
            title = stringResource(R.string.RemoteMusic),
            desc = stringResource(R.string.RemoteMusicSubtitle),
            onClick = onOpenRemote,
        )
        HubRow(
            iconRes = R.drawable.ic_equalizer,
            tileArgb = 0x20FF7043L,
            title = stringResource(R.string.Plugins),
            desc = stringResource(R.string.plugin_dir_title),
            onClick = { dest = SettingsDest.Plugin },
        )
        HubRow(
            iconRes = R.drawable.ic_play,
            tileArgb = 0x20E84393L,
            title = stringResource(R.string.ExploreSettings),
            desc = aiStatus.ifBlank { stringResource(R.string.ai_no_config) },
            onClick = { dest = SettingsDest.Ai },
        )

        SectionHeader(stringResource(R.string.SystemAndMaintenance))
        HubRow(
            iconRes = R.drawable.ic_settings,
            tileArgb = 0x1AFFFFFFL,
            title = stringResource(R.string.GeneralSettings),
            desc = stringResource(R.string.GeneralSettingsDesc),
            onClick = { dest = SettingsDest.General },
        )
        HubRow(
            iconRes = R.drawable.ic_add,
            tileArgb = 0x207ED8C3L,
            title = stringResource(R.string.BackupRestore),
            desc = stringResource(R.string.BackupRestoreDesc),
            onClick = { dest = SettingsDest.Backup },
        )
        HubRow(
            iconRes = R.drawable.ic_info,
            tileArgb = 0x20FFB74DL,
            title = stringResource(R.string.Permissions),
            desc = stringResource(R.string.PermissionsSubtitle),
            onClick = { dest = SettingsDest.Permission },
        )
        HubRow(
            iconRes = R.drawable.ic_info,
            tileArgb = 0x1A7ED8C3L,
            title = stringResource(R.string.diagnostic_title),
            desc = stringResource(R.string.diagnostic_subtitle),
            onClick = { dest = SettingsDest.DiagnosticLog },
        )
        HubRow(
            iconRes = R.drawable.ic_info,
            tileArgb = 0x2042A5F5L,
            title = stringResource(R.string.About),
            desc = stringResource(R.string.AboutDesc),
            onClick = { dest = SettingsDest.About },
        )
    }
}

/** 外观子页 —— MAUI `AppearanceSettingsPage`：主题、启动页、自定义背景、三种动态背景 */
@Composable
private fun AppearanceSettingsScreen(onBack: () -> Unit, viewModel: SettingsViewModel) {
    val palette = LocalCatClawPalette.current
    val primary = Color(palette.textPrimary)
    val secondary = Color(palette.textSecondary)
    val mode by viewModel.mode.collectAsStateWithLifecycle()

    Column(
        modifier = Modifier
            .fillMaxSize()
            .verticalScroll(rememberScrollState())
            .padding(horizontal = 20.dp),
    ) {
        SubHeader(title = stringResource(R.string.Appearance), onBack = onBack)

        Section(title = stringResource(R.string.Appearance), color = secondary) {
            Row(horizontalArrangement = Arrangement.spacedBy(10.dp)) {
                AppearanceMode.entries.forEach { option ->
                    ChoiceChip(
                        label = appearanceLabel(option),
                        selected = option == mode,
                        onClick = { viewModel.setMode(option) },
                    )
                }
            }
        }

        Section(title = stringResource(R.string.appearance_bg_startup), color = secondary) {
            val startupIndex by viewModel.startupIndex.collectAsStateWithLifecycle()
            val hasCustomBg by viewModel.hasCustomBackground.collectAsStateWithLifecycle()
            val monet by viewModel.monetEnabled.collectAsStateWithLifecycle()
            val picker = androidx.activity.compose.rememberLauncherForActivityResult(
                androidx.activity.result.contract.ActivityResultContracts.OpenDocument(),
            ) { uri -> uri?.let { viewModel.importCustomBackground(it) } }

            Text(stringResource(R.string.startup_page), style = MaterialTheme.typography.bodyMedium, color = primary)
            Row(horizontalArrangement = Arrangement.spacedBy(10.dp), modifier = Modifier.padding(vertical = 6.dp)) {
                listOf(
                    stringResource(R.string.startup_library) to 0,
                    stringResource(R.string.startup_explore) to 1,
                    stringResource(R.string.startup_playing) to 2,
                ).forEach { (label, value) ->
                    GlassChip(label = label, selected = value == startupIndex, onClick = { viewModel.setStartupIndex(value) })
                }
            }

            Row(verticalAlignment = Alignment.CenterVertically, modifier = Modifier.fillMaxWidth().padding(vertical = 6.dp)) {
                Text(
                    text = stringResource(R.string.custom_bg_image),
                    style = MaterialTheme.typography.bodyLarge,
                    color = primary,
                    modifier = Modifier.weight(1f),
                )
                Text(
                    text = stringResource(R.string.custom_bg_none).let { if (hasCustomBg) "" else it },
                    style = MaterialTheme.typography.labelSmall,
                    color = secondary,
                )
                PillLabel(
                    text = stringResource(R.string.custom_bg_change),
                    onClick = { picker.launch(arrayOf("image/*")) },
                    filled = false,
                )
                if (hasCustomBg) {
                    PillLabel(
                        text = stringResource(R.string.custom_bg_clear),
                        onClick = { viewModel.clearCustomBackground() },
                        filled = false,
                    )
                }
            }

            // 只有莫奈取色在本端有真实消费者（AppearancePalette.resolve 会读它）。
            // MAUI 的「雾面动态背景 / 动态封面背景」两档靠 FrostedBackground 引擎与封面取色驱动，
            // 本端还没移植那套渲染 —— 开关留着只会「拨了没反应还记住你的选择」，所以不放上来。
            SwitchRow(
                title = stringResource(R.string.monet_bg),
                subtitle = "",
                checked = monet,
                onChecked = { viewModel.setMonetEnabled(it) },
            )
        }

    }
}

/** 通用子页 —— MAUI `GeneralSettingsPage` 里本端已实现的那部分：语言 + 音频缓存 */
@Composable
private fun GeneralSettingsScreen(onBack: () -> Unit, viewModel: SettingsViewModel) {
    val palette = LocalCatClawPalette.current
    val context = LocalContext.current
    val primary = Color(palette.textPrimary)
    val secondary = Color(palette.textSecondary)
    var languageDialog by remember { mutableStateOf(false) }
    // 语言生效 = 存 tag + 重建 Activity（attachBaseContext 会按 tag 重贴 Configuration）
    val activity = context.findActivity()

    Column(
        modifier = Modifier
            .fillMaxSize()
            .verticalScroll(rememberScrollState())
            .padding(horizontal = 20.dp),
    ) {
        SubHeader(title = stringResource(R.string.GeneralSettings), onBack = onBack)

        Section(title = stringResource(R.string.Language), color = secondary) {
            ClickableRow(
                title = stringResource(R.string.language_dialog_title),
                value = currentLanguageLabel(context),
                onClick = { languageDialog = true },
                hint = stringResource(R.string.LanguageHint),
            )
        }

        Section(title = stringResource(R.string.CacheSize), color = secondary) {
            LaunchedEffect(Unit) { viewModel.refreshCacheUsage() }
            val usage by viewModel.cacheUsageBytes.collectAsStateWithLifecycle()
            val limitMb by viewModel.cacheSizeMb.collectAsStateWithLifecycle()
            Text(
                text = stringResource(
                    R.string.cache_used,
                    String.format(java.util.Locale.getDefault(), "%.1f", usage / 1048576.0),
                    limitMb,
                ),
                style = MaterialTheme.typography.bodyMedium,
                color = secondary,
            )
            SliderRow(
                title = stringResource(R.string.CacheSize),
                value = limitMb.toFloat(),
                valueRange = CacheSettings.MIN_MB.toFloat()..CacheSettings.MAX_MB.toFloat(),
                steps = (CacheSettings.MAX_MB - CacheSettings.MIN_MB) / 100 - 1,
                label = "$limitMb MB",
                onValue = { viewModel.setCacheSizeMb(it.toInt()) },
            )
            ClickableRow(
                title = stringResource(R.string.ClearCache),
                value = "",
                hint = stringResource(R.string.cache_dir, viewModel.downloadDirectory()),
                onClick = { viewModel.clearAudioCache() },
            )
        }
    }

    val chooseLanguage: (String) -> Unit = { tag ->
        languageDialog = false
        AppLocale.apply(context, tag)
        activity?.recreate()
    }
    if (languageDialog) {
        AlertDialog(
            onDismissRequest = { languageDialog = false },
            containerColor = Color(palette.backgroundStops.last().argb),
            title = { Text(stringResource(R.string.language_dialog_title), color = primary) },
            text = {
                LazyColumn(modifier = Modifier.fillMaxWidth().height(360.dp)) {
                    item {
                        LanguageOption(
                            label = stringResource(R.string.language_system),
                            selected = AppLocale.current(context).isEmpty(),
                            onClick = { chooseLanguage(AppLocale.SYSTEM) },
                        )
                    }
                    items(AppLocale.SUPPORTED) { (tag, selfName) ->
                        LanguageOption(
                            label = selfName,
                            selected = AppLocale.current(context) == tag,
                            onClick = { chooseLanguage(tag) },
                        )
                    }
                }
            },
            confirmButton = {},
        )
    }
}

/** 桌面歌词子页 —— MAUI `DesktopLyricPage` 里本端已实现的那几项：开关、锁定、字号、背景不透明度 */
@Composable
private fun DesktopLyricSettingsScreen(onBack: () -> Unit, viewModel: SettingsViewModel) {
    val palette = LocalCatClawPalette.current
    val context = LocalContext.current
    val secondary = Color(palette.textSecondary)
    val desktopWanted by viewModel.desktopLyricWanted.collectAsStateWithLifecycle()
    val desktopAttached by viewModel.desktopLyricAttached.collectAsStateWithLifecycle()
    val locked by viewModel.desktopLyricLocked.collectAsStateWithLifecycle()
    val fontSp by viewModel.desktopLyricFontSize.collectAsStateWithLifecycle()
    val bgOpacity by viewModel.desktopLyricBgOpacity.collectAsStateWithLifecycle()
    val textColor by viewModel.desktopLyricTextColor.collectAsStateWithLifecycle()
    val highlightColor by viewModel.desktopLyricHighlightColor.collectAsStateWithLifecycle()

    Column(
        modifier = Modifier
            .fillMaxSize()
            .verticalScroll(rememberScrollState())
            .padding(horizontal = 20.dp),
    ) {
        SubHeader(title = stringResource(R.string.PageTitleDesktopLyric), onBack = onBack)

        Section(title = stringResource(R.string.DesktopLyricEnable), color = secondary) {
            SwitchRow(
                title = stringResource(R.string.DesktopLyricEnable),
                subtitle = stringResource(R.string.DesktopLyricDesc),
                // 这里读的是**意图**：窗口没起来时若也显示"关"，用户点一下写成开、界面却仍是关，
                // 开关就成了一个怎么点都不动的控件，而下面那些样式行还被它挡住进不去
                checked = desktopWanted,
                onChecked = { want ->
                    if (!viewModel.setDesktopLyric(want)) {
                        context.startActivity(viewModel.overlayPermissionIntent())
                    }
                },
            )
            if (desktopWanted && !desktopAttached) {
                Row(
                    modifier = Modifier.fillMaxWidth().padding(top = 6.dp),
                    verticalAlignment = Alignment.CenterVertically,
                ) {
                    Text(
                        text = stringResource(R.string.desktop_lyric_not_attached),
                        fontSize = 12.sp,
                        color = secondary,
                        modifier = Modifier.weight(1f),
                    )
                    PillLabel(
                        text = stringResource(R.string.perm_go_grant),
                        onClick = { context.startActivity(viewModel.overlayPermissionIntent()) },
                        filled = false,
                    )
                }
            }
            if (desktopWanted) {
                SwitchRow(
                    title = stringResource(R.string.DesktopLyricLockPosition),
                    subtitle = stringResource(R.string.DesktopLyricLockPositionDesc),
                    checked = locked,
                    onChecked = { viewModel.setDesktopLyricLocked(it) },
                )
                SliderRow(
                    title = stringResource(R.string.DesktopLyricFontSize),
                    value = fontSp,
                    valueRange = DesktopLyricSettings.MIN_FONT..DesktopLyricSettings.MAX_FONT,
                    steps = (DesktopLyricSettings.MAX_FONT - DesktopLyricSettings.MIN_FONT).toInt() - 1,
                    label = fontSp.toInt().toString(),
                    onValue = { viewModel.setDesktopLyricFontSize(it) },
                )
                SliderRow(
                    title = stringResource(R.string.DesktopLyricBgOpacity),
                    value = bgOpacity,
                    valueRange = 0f..1f,
                    steps = 19,
                    label = "${(bgOpacity * 100).toInt()}%",
                    onValue = { viewModel.setDesktopLyricBgOpacity(it) },
                )
                // 这两个颜色悬浮窗一直在读（DesktopLyricController 的 textBase / textFill），
                // 之前设置页没有入口，等于"引擎可配、用户不可配"
                ColorRow(
                    title = stringResource(R.string.DesktopLyricTextColor),
                    current = textColor,
                    onPick = { viewModel.setDesktopLyricTextColor(it) },
                )
                ColorRow(
                    title = stringResource(R.string.DesktopLyricHighlightColor),
                    current = highlightColor,
                    onPick = { viewModel.setDesktopLyricHighlightColor(it) },
                )
            }
        }
    }
}

/**
 * 插件管理子页 —— 插件协议 v2 的第一个消费者。
 *
 * 坏掉的安装不会被静默吞掉：每条都带自己的拒绝原因（协议版本 / 平台 / 宿主版本 / 清单不合法），
 * 这正是 v2 把「以清单为唯一事实」写进 [com.catclaw.music.kt.core.plugin.PluginRegistry] 的目的。
 */
@Composable
private fun PluginSettingsScreen(onBack: () -> Unit, viewModel: PluginSettingsViewModel = hiltViewModel()) {
    val palette = LocalCatClawPalette.current
    val secondary = Color(palette.textSecondary)
    val rows by viewModel.rows.collectAsStateWithLifecycle()

    Column(
        modifier = Modifier
            .fillMaxSize()
            .verticalScroll(rememberScrollState())
            .padding(horizontal = 20.dp),
    ) {
        SubHeader(title = stringResource(R.string.Plugins), onBack = onBack)

        Section(title = stringResource(R.string.plugin_dir_title), color = secondary) {
            Row(verticalAlignment = Alignment.CenterVertically, modifier = Modifier.fillMaxWidth()) {
                Text(
                    text = stringResource(R.string.plugin_dir_path, viewModel.directoryPath()),
                    fontSize = 12.sp,
                    color = secondary,
                    modifier = Modifier.weight(1f),
                )
                PillLabel(
                    text = stringResource(R.string.plugin_rescan),
                    onClick = { viewModel.rescan() },
                    filled = false,
                )
            }
            if (rows.isNotEmpty()) {
                // 「几个可用 / 一共几个」——扫描完先看总量，再逐条看状态
                Text(
                    text = stringResource(R.string.plugin_summary, rows.count { it.usable }, rows.size),
                    fontSize = 12.sp,
                    color = secondary,
                    modifier = Modifier.padding(top = 6.dp),
                )
            }
            if (rows.isEmpty()) {
                EmptyState(
                    hint = stringResource(R.string.plugin_empty),
                    iconRes = R.drawable.ic_settings,
                    modifier = Modifier.padding(top = 8.dp),
                )
            }
            rows.forEach { row -> PluginEntryRow(row) }
        }
    }
}

@Composable
private fun PluginEntryRow(row: PluginRow) {
    val palette = LocalCatClawPalette.current
    val status = if (row.usable) Color(highlightOnGlassCard(palette)) else Color(palette.textSecondary)
    Column(
        modifier = Modifier
            .fillMaxWidth()
            .padding(top = 12.dp),
        verticalArrangement = Arrangement.spacedBy(2.dp),
    ) {
        Text(
            text = row.name + " " + row.version,
            fontSize = 15.sp,
            fontWeight = FontWeight.SemiBold,
            color = Color(palette.textPrimary),
        )
        Text(
            text = if (row.capabilities.isBlank()) {
                stringResource(R.string.plugin_no_caps)
            } else {
                stringResource(R.string.plugin_caps, row.capabilities)
            },
            fontSize = 12.sp,
            color = Color(palette.textSecondary),
        )
        Text(
            text = when (row.statusArgs.size) {
                2 -> stringResource(row.statusRes, row.statusArgs[0], row.statusArgs[1])
                1 -> stringResource(row.statusRes, row.statusArgs[0])
                else -> stringResource(row.statusRes)
            },
            fontSize = 12.sp,
            color = status,
        )
    }
}

/** 枢纽分组标题：MAUI 是「✦」小标 + 12sp Bold、CharacterSpacing 2 */
@Composable
private fun SectionHeader(title: String) {
    val palette = LocalCatClawPalette.current
    Row(
        modifier = Modifier
            .fillMaxWidth()
            .padding(start = 4.dp, top = 14.dp, bottom = 8.dp),
        verticalAlignment = Alignment.CenterVertically,
        horizontalArrangement = Arrangement.spacedBy(8.dp),
    ) {
        Text(text = "✦", fontSize = 12.sp, color = Color(palette.primary))
        Text(
            text = title,
            fontSize = 12.sp,
            fontWeight = FontWeight.Bold,
            letterSpacing = 2.sp,
            color = Color(palette.textSecondary),
        )
    }
}

/** 枢纽的一行 —— MAUI 每行自己是一张 GlassCard：40×40 圆角 12 图标块（各行自己的主题色 12.5% 底）+ 标题/描述 + 16dp 箭头 */
@Composable
private fun HubRow(
    iconRes: Int,
    tileArgb: Long,
    title: String,
    desc: String,
    onClick: () -> Unit,
) {
    val palette = LocalCatClawPalette.current
    Row(
        modifier = Modifier
            .fillMaxWidth()
            .padding(bottom = 10.dp)
            .glassCard(palette = palette, corner = 26)
            .clickable(onClick = onClick)
            .padding(horizontal = 14.dp, vertical = 12.dp),
        verticalAlignment = Alignment.CenterVertically,
        horizontalArrangement = Arrangement.spacedBy(14.dp),
    ) {
        Box(
            modifier = Modifier
                .size(40.dp)
                .clip(RoundedCornerShape(12.dp))
                .background(Color(tileArgb)),
            contentAlignment = Alignment.Center,
        ) {
            Icon(
                painter = painterResource(iconRes),
                contentDescription = null,
                tint = Color(palette.textPrimary),
                modifier = Modifier.size(22.dp),
            )
        }
        Column(modifier = Modifier.weight(1f), verticalArrangement = Arrangement.spacedBy(2.dp)) {
            Text(text = title, fontSize = 15.sp, fontWeight = FontWeight.SemiBold, color = Color(palette.textPrimary))
            Text(
                text = desc,
                fontSize = 12.sp,
                color = Color(palette.textSecondary),
                maxLines = 2,
                overflow = TextOverflow.Ellipsis,
            )
        }
        Icon(
            painter = painterResource(R.drawable.ic_arrow_forward),
            contentDescription = null,
            tint = Color(palette.textPrimary).copy(alpha = 0.4f),
            modifier = Modifier.size(16.dp),
        )
    }
}

/** 子页页头：返回块 + 标题（MAUI 子页是各自 push 出去的整屏） */
@Composable
private fun SubHeader(title: String, onBack: () -> Unit) {
    val palette = LocalCatClawPalette.current
    Row(
        modifier = Modifier
            .fillMaxWidth()
            .padding(top = 12.dp, bottom = 4.dp),
        verticalAlignment = Alignment.CenterVertically,
        horizontalArrangement = Arrangement.spacedBy(12.dp),
    ) {
        BackTile(onBack = onBack, description = stringResource(R.string.GoBack))
        Text(text = title, fontSize = 22.sp, fontWeight = FontWeight.Bold, color = Color(palette.textPrimary))
    }
}

/** 带当前值标签的滑杆：桌面歌词字号与背景不透明度用 */
@Composable
private fun SliderRow(
    title: String,
    value: Float,
    valueRange: ClosedFloatingPointRange<Float>,
    steps: Int,
    label: String,
    onValue: (Float) -> Unit,
) {
    val palette = LocalCatClawPalette.current
    Column(modifier = Modifier.fillMaxWidth().padding(vertical = 6.dp)) {
        Row(verticalAlignment = Alignment.CenterVertically, modifier = Modifier.fillMaxWidth()) {
            Text(
                text = title,
                style = MaterialTheme.typography.bodyLarge,
                color = Color(palette.textPrimary),
                modifier = Modifier.weight(1f),
            )
            Text(
                text = label,
                style = MaterialTheme.typography.bodyMedium,
                // 值标签这块面是玻璃卡：浅色主题下 primary 只有 2.28:1，helper 会退回正文色
                color = Color(highlightOnGlassCard(palette)),
            )
        }
        Slider(
            value = value.coerceIn(valueRange.start, valueRange.endInclusive),
            onValueChange = onValue,
            valueRange = valueRange,
            steps = steps,
            colors = SliderDefaults.colors(
                thumbColor = Color(palette.primary),
                activeTrackColor = Color(palette.primary),
            ),
        )
    }
}

/**
 * 色板行：悬浮窗正文 / 高亮颜色的选择。
 * 选中态用一圈主色描边说清楚，不靠"点下去看起来有反应"来暗示；
 * 44dp 的触达壳套 26dp 的色块，色块本身小到能一眼排成一排。
 */
@Composable
private fun ColorRow(title: String, current: Int, onPick: (Int) -> Unit) {
    val palette = LocalCatClawPalette.current
    Column(modifier = Modifier.fillMaxWidth().padding(vertical = 6.dp)) {
        Text(
            text = title,
            style = MaterialTheme.typography.bodyLarge,
            color = Color(palette.textPrimary),
        )
        Row(
            modifier = Modifier.fillMaxWidth().padding(top = 4.dp),
            horizontalArrangement = Arrangement.spacedBy(4.dp),
        ) {
            DesktopLyricSettings.COLOR_SWATCHES.forEach { argb ->
                val selected = argb == current
                Box(
                    modifier = Modifier
                        .size(TouchMin)
                        .clickable(onClickLabel = title) { onPick(argb) },
                    contentAlignment = Alignment.Center,
                ) {
                    Box(
                        modifier = Modifier
                            .size(26.dp)
                            .clip(CircleShape)
                            .background(Color(argb))
                            .border(
                                width = if (selected) 3.dp else 1.dp,
                                color = if (selected) Color(palette.primary) else Color(palette.textPrimary).copy(alpha = 0.28f),
                                shape = CircleShape,
                            ),
                    )
                }
            }
        }
    }
}

/** LocalContext 未必就是 Activity（可能是 ContextThemeWrapper），逐层剥到底 */
private tailrec fun Context.findActivity(): Activity? = when (this) {
    is Activity -> this
    is ContextWrapper -> baseContext.findActivity()
    else -> null
}

@Composable
private fun Section(title: String, color: Color, content: @Composable () -> Unit) {
    val palette = LocalCatClawPalette.current
    Column(
        modifier = Modifier
            .fillMaxWidth()
            .padding(bottom = 14.dp)
            // 卡面配方（MAUI GlassCardStyle：α.10 填充 + 圆角 26 + α.16 描边）收在 Modifier.glassCard 里
            .glassCard(palette = palette, corner = 26)
            .padding(16.dp),
    ) {
        Text(
            text = title,
            fontSize = 12.sp,
            fontWeight = FontWeight.Bold,
            letterSpacing = 2.sp,
            color = Color(palette.textSecondary),
            modifier = Modifier.padding(bottom = 10.dp),
        )
        content()
    }
}

@Composable
private fun ChoiceChip(label: String, selected: Boolean, onClick: () -> Unit) {
    GlassChip(label = label, selected = selected, onClick = onClick)
}

@Composable
private fun ClickableRow(title: String, value: String, hint: String, onClick: () -> Unit) {
    val palette = LocalCatClawPalette.current
    Column(modifier = Modifier.fillMaxWidth().clickable(onClick = onClick).padding(vertical = 6.dp)) {
        Row(verticalAlignment = Alignment.CenterVertically, modifier = Modifier.fillMaxWidth()) {
            Text(
                text = title,
                style = MaterialTheme.typography.bodyLarge,
                color = Color(palette.textPrimary),
                modifier = Modifier.weight(1f),
            )
            Text(
                text = value,
                style = MaterialTheme.typography.bodyMedium,
                color = Color(highlightOnGlassCard(palette)),
                maxLines = 1,
                overflow = TextOverflow.Ellipsis,
            )
        }
        Text(
            text = hint,
            style = MaterialTheme.typography.labelSmall,
            color = Color(palette.textSecondary),
        )
    }
}

@Composable
fun SwitchRow(title: String, subtitle: String, checked: Boolean, onChecked: (Boolean) -> Unit) {
    val palette = LocalCatClawPalette.current
    Row(
        modifier = Modifier.fillMaxWidth().padding(vertical = 6.dp),
        verticalAlignment = Alignment.CenterVertically,
    ) {
        Column(modifier = Modifier.weight(1f)) {
            Text(title, style = MaterialTheme.typography.bodyLarge, color = Color(palette.textPrimary))
            Text(subtitle, style = MaterialTheme.typography.labelSmall, color = Color(palette.textSecondary))
        }
        Switch(checked = checked, onCheckedChange = onChecked)
    }
}

@Composable
private fun LanguageOption(label: String, selected: Boolean, onClick: () -> Unit) {
    val palette = LocalCatClawPalette.current
    Box(
        modifier = Modifier
            .fillMaxWidth()
            .clickable(onClick = onClick)
            .padding(vertical = 12.dp, horizontal = 4.dp),
    ) {
        Text(
            text = label,
            style = MaterialTheme.typography.bodyLarge,
            color = if (selected) Color(highlightOnGlassCard(palette)) else Color(palette.textPrimary),
        )
    }
}

@Composable
private fun appearanceLabel(mode: AppearanceMode): String = stringResource(
    when (mode) {
        AppearanceMode.System -> R.string.appearance_system
        AppearanceMode.Light -> R.string.appearance_light
        AppearanceMode.Dark -> R.string.appearance_dark
    },
)

@Composable
private fun currentLanguageLabel(context: Context): String {
    val tag = AppLocale.current(context)
    val system = stringResource(R.string.language_system)
    return if (tag.isEmpty()) system else AppLocale.displayName(tag)
}
