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
import com.catclaw.music.kt.ui.settings.AiSettingsScreen
import com.catclaw.music.kt.ui.settings.SettingsViewModel
import com.catclaw.music.kt.data.lyrics.DesktopLyricSettings
import com.catclaw.music.kt.data.settings.CacheSettings
import com.catclaw.music.kt.ui.theme.LocalCatClawPalette

/** M4 壳层：设置页。语言切换以自己的 SharedPreferences 为事实来源（那台 MIUI 没有 locale_manager 服务），51 个语言全在包里。 */
@Composable
fun SettingsScreen(
    onBack: () -> Unit,
    onOpenRemote: () -> Unit = {},
    viewModel: SettingsViewModel = hiltViewModel(),
) {
    val palette = LocalCatClawPalette.current
    val context = LocalContext.current
    val mode by viewModel.mode.collectAsStateWithLifecycle()
    val desktopOn by viewModel.desktopLyricEnabled.collectAsStateWithLifecycle()
    val locked by viewModel.desktopLyricLocked.collectAsStateWithLifecycle()
    val fontSp by viewModel.desktopLyricFontSize.collectAsStateWithLifecycle()
    val bgOpacity by viewModel.desktopLyricBgOpacity.collectAsStateWithLifecycle()
    var languageDialog by remember { mutableStateOf(false) }
    var aiPage by remember { mutableStateOf(false) }

    val primary = Color(palette.textPrimary)
    val secondary = Color(palette.textSecondary)

    // 语言生效 = 存 tag + 重建 Activity（attachBaseContext 会按 tag 重贴 Configuration）
    val activity = context.findActivity()
    val chooseLanguage: (String) -> Unit = { tag ->
        languageDialog = false
        AppLocale.apply(context, tag)
        activity?.recreate()
    }

    // AI 配置项多（多模型 + 运行参数），整页换过去而不是在同一列里往下滚
    if (aiPage) {
        AiSettingsScreen(onBack = { aiPage = false })
        return
    }

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
                .clip(RoundedCornerShape(28.dp))
                .background(
                    Brush.linearGradient(
                        listOf(Color(0x7A55D6FF), Color(0x4D7ED8C3)),
                    ),
                )
                .padding(start = 10.dp, end = 12.dp, top = 12.dp, bottom = 12.dp),
            verticalAlignment = Alignment.CenterVertically,
        ) {
            Box(
                modifier = Modifier
                    .size(40.dp)
                    .clip(RoundedCornerShape(14.dp))
                    .clickable(onClick = onBack),
                contentAlignment = Alignment.Center,
            ) {
                Icon(
                    painter = painterResource(R.drawable.ic_arrow_back),
                    contentDescription = stringResource(R.string.GoBack),
                    tint = Color.White,
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
                    color = Color(0xF2F4FFFF).copy(alpha = 0.86f),
                    maxLines = 2,
                    overflow = TextOverflow.Ellipsis,
                )
            }
            Box(
                modifier = Modifier
                    .size(48.dp)
                    .clip(RoundedCornerShape(14.dp))
                    .background(Color.White.copy(alpha = 0.13f))
                    .clickable {
                        viewModel.setMode(if (mode == AppearanceMode.Dark) AppearanceMode.Light else AppearanceMode.Dark)
                    },
                contentAlignment = Alignment.Center,
            ) {
                Icon(
                    painter = painterResource(R.drawable.ic_dark_mode),
                    contentDescription = stringResource(R.string.Appearance),
                    tint = Color.White,
                    modifier = Modifier.size(28.dp),
                )
            }
        }
        Spacer(Modifier.height(10.dp))

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

        Section(title = stringResource(R.string.Language), color = secondary) {
            ClickableRow(
                title = stringResource(R.string.language_dialog_title),
                value = currentLanguageLabel(context),
                onClick = { languageDialog = true },
                hint = stringResource(R.string.LanguageHint),
            )
        }

        Section(title = stringResource(R.string.RemoteMusic), color = secondary) {
            ClickableRow(
                title = stringResource(R.string.RemoteMusicSection),
                value = stringResource(R.string.RemoteMusic),
                hint = stringResource(R.string.RemoteMusicSubtitle),
                onClick = onOpenRemote,
            )
        }

        Section(title = stringResource(R.string.ai_settings), color = secondary) {
            ClickableRow(
                title = stringResource(R.string.ai_settings),
                value = stringResource(R.string.ai_configs),
                hint = stringResource(R.string.ai_no_config),
                onClick = { aiPage = true },
            )
        }

        Section(title = stringResource(R.string.appearance_bg_startup), color = secondary) {
            val startupIndex by viewModel.startupIndex.collectAsStateWithLifecycle()
            val hasCustomBg by viewModel.hasCustomBackground.collectAsStateWithLifecycle()
            val monet by viewModel.monetEnabled.collectAsStateWithLifecycle()
            val frosted by viewModel.frostedEnabled.collectAsStateWithLifecycle()
            val cover by viewModel.coverEnabled.collectAsStateWithLifecycle()
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
                    val on = value == startupIndex
                    Text(
                        text = label,
                        style = MaterialTheme.typography.labelLarge,
                        color = if (on) Color(palette.primary) else secondary,
                        modifier = Modifier
                            .clip(RoundedCornerShape(14.dp))
                            .background(if (on) Color(palette.primary).copy(alpha = 0.18f) else Color.Transparent)
                            .clickable { viewModel.setStartupIndex(value) }
                            .padding(horizontal = 12.dp, vertical = 6.dp),
                    )
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
                Text(
                    text = stringResource(R.string.custom_bg_change),
                    style = MaterialTheme.typography.labelLarge,
                    color = Color(palette.primary),
                    modifier = Modifier
                        .clip(RoundedCornerShape(12.dp))
                        .clickable { picker.launch(arrayOf("image/*")) }
                        .padding(horizontal = 10.dp, vertical = 6.dp),
                )
                if (hasCustomBg) {
                    Text(
                        text = stringResource(R.string.custom_bg_clear),
                        style = MaterialTheme.typography.labelLarge,
                        color = secondary,
                        modifier = Modifier
                            .clip(RoundedCornerShape(12.dp))
                            .clickable { viewModel.clearCustomBackground() }
                            .padding(horizontal = 10.dp, vertical = 6.dp),
                    )
                }
            }

            SwitchRow(
                title = stringResource(R.string.frosted_bg),
                subtitle = "",
                checked = frosted,
                onChecked = { viewModel.setFrostedEnabled(it) },
            )
            SwitchRow(
                title = stringResource(R.string.monet_bg),
                subtitle = "",
                checked = monet,
                onChecked = { viewModel.setMonetEnabled(it) },
            )
            SwitchRow(
                title = stringResource(R.string.cover_bg),
                subtitle = "",
                checked = cover,
                onChecked = { viewModel.setCoverEnabled(it) },
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

        Section(title = stringResource(R.string.DesktopLyricEnable), color = secondary) {
            SwitchRow(
                title = stringResource(R.string.DesktopLyricEnable),
                subtitle = stringResource(R.string.DesktopLyricDesc),
                checked = desktopOn,
                onChecked = { want ->
                    if (!viewModel.setDesktopLyric(want)) {
                        context.startActivity(viewModel.overlayPermissionIntent())
                    }
                },
            )
            if (desktopOn) {
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
            }
        }
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
                color = Color(palette.primary),
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
            // 照 MAUI Styles.xaml 的 GlassCardStyle：CardBackgroundColor #1AFFFFFF、圆角 26、Padding 16、
            // 描边 GlassStrokeColor #28FFFFFF。之前用 primaryLight 35% 会在深色底上糊成一片浅紫
            .clip(RoundedCornerShape(26.dp))
            .background(Color(palette.textPrimary).copy(alpha = 0.10f))
            .border(1.dp, Color(palette.textPrimary).copy(alpha = 0.16f), RoundedCornerShape(26.dp))
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
    val palette = LocalCatClawPalette.current
    Box(
        modifier = Modifier
            .background(
                if (selected) Color(palette.primary) else Color(palette.textSecondary).copy(alpha = 0.18f),
                RoundedCornerShape(50),
            )
            .clickable(onClick = onClick)
            .padding(horizontal = 16.dp, vertical = 8.dp),
    ) {
        Text(
            text = label,
            style = MaterialTheme.typography.labelLarge,
            color = if (selected) Color(palette.primaryLight) else Color(palette.textSecondary),
        )
    }
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
                color = Color(palette.primary),
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
private fun SwitchRow(title: String, subtitle: String, checked: Boolean, onChecked: (Boolean) -> Unit) {
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
            color = if (selected) Color(palette.primary) else Color(palette.textPrimary),
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
