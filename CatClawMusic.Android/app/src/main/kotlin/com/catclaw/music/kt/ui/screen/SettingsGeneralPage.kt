package com.catclaw.music.kt.ui.screen

import com.catclaw.music.kt.data.settings.DiscoverBackground
import androidx.activity.compose.BackHandler
import android.app.Activity
import android.content.res.Configuration
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
import androidx.compose.foundation.layout.fillMaxHeight
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
import androidx.compose.ui.platform.LocalConfiguration
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
import com.catclaw.music.kt.ui.settings.LocalMusicSettingsScreen
import com.catclaw.music.kt.ui.download.DownloadsScreen
import com.catclaw.music.kt.ui.plugin.PluginRow
import com.catclaw.music.kt.ui.plugin.PluginSettingsViewModel
import com.catclaw.music.kt.ui.settings.SettingsViewModel
import com.catclaw.music.kt.data.lyrics.DesktopLyricSettings
import com.catclaw.music.kt.data.settings.CacheSettings
import com.catclaw.music.kt.ui.theme.LocalCatClawPalette
import com.catclaw.music.kt.ui.theme.highlightOnGlassCard
import com.catclaw.music.kt.ui.theme.BackTile
import com.catclaw.music.kt.ui.theme.GlassChip
import com.catclaw.music.kt.ui.theme.GlassSwitch
import com.catclaw.music.kt.ui.theme.PillLabel
import com.catclaw.music.kt.ui.theme.TouchMin
import com.catclaw.music.kt.ui.theme.EmptyState
import com.catclaw.music.kt.ui.theme.glassCard

/** 设置·通用子页（从 SettingsScreen.kt 拆出） */
@Composable
internal fun GeneralSettingsScreen(onBack: () -> Unit, viewModel: SettingsViewModel) {
    val palette = LocalCatClawPalette.current
    val context = LocalContext.current
    val primary = Color(palette.textPrimary)
    val secondary = Color(palette.textSecondary)
    var languageDialog by remember { mutableStateOf(false) }
    var resetDialog by remember { mutableStateOf(false) }
    var resetNotice by remember { mutableStateOf<String?>(null) }
    val resetDone = stringResource(R.string.reset_all_settings_done)
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
                onClick = { viewModel.clearCaches() },
            )
        }

        Section(title = stringResource(R.string.DangerZone), color = secondary) {
            ClickableRow(
                title = stringResource(R.string.reset_all_settings),
                value = "",
                hint = stringResource(R.string.reset_all_settings_hint),
                onClick = { resetDialog = true },
            )
            resetNotice?.let {
                Text(it, fontSize = 12.sp, color = secondary, modifier = Modifier.padding(top = 6.dp))
            }
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

    if (resetDialog) {
        // MAUI 那颗按钮是**没有确认**直接 Preferences.Clear() 的；不可撤销的批量清空
        // 至少该问一次。按钮走 PillLabel：TextButton 是 stock 控件，闸门会拦
        AlertDialog(
            onDismissRequest = { resetDialog = false },
            containerColor = Color(palette.backgroundStops.last().argb),
            title = { Text(stringResource(R.string.reset_all_settings), color = primary) },
            text = { Text(stringResource(R.string.reset_all_settings_confirm), color = secondary) },
            confirmButton = {
                PillLabel(
                    text = stringResource(R.string.reset_all_settings_ok),
                    onClick = {
                        resetDialog = false
                        viewModel.resetAllSettings { n -> resetNotice = resetDone.format(n) }
                    },
                )
            },
            dismissButton = {
                PillLabel(
                    text = stringResource(R.string.playlist_rename_cancel),
                    onClick = { resetDialog = false },
                    filled = false,
                )
            },
        )
    }
}
