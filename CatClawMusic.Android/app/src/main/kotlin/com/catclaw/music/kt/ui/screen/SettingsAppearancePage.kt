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

/** 设置·外观子页（从 SettingsScreen.kt 拆出） */
@Composable
internal fun AppearanceSettingsScreen(onBack: () -> Unit, viewModel: SettingsViewModel) {
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
            val discoverBg by viewModel.discoverBackground.collectAsStateWithLifecycle()
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

            // 发现页背景：三选一（2026-11-07 用户要求：三者互斥、只作用于发现页）。
            // 以前是三个独立开关，可以同时打开 → 「自定义图上再叠流光、再套莫奈配色」，背景到底是哪一层说不清。
            Text(
                text = stringResource(R.string.discover_bg_title),
                style = MaterialTheme.typography.bodyMedium,
                color = primary,
                modifier = Modifier.padding(top = 10.dp),
            )
            Text(
                text = stringResource(R.string.discover_bg_hint),
                style = MaterialTheme.typography.labelSmall,
                color = secondary,
                modifier = Modifier.padding(bottom = 6.dp),
            )
            Column(verticalArrangement = Arrangement.spacedBy(8.dp)) {
                listOf(
                    Triple(
                        DiscoverBackground.CUSTOM,
                        R.string.discover_bg_custom,
                        R.string.discover_bg_custom_desc,
                    ),
                    Triple(
                        DiscoverBackground.FROSTED,
                        R.string.discover_bg_frosted,
                        R.string.discover_bg_frosted_desc,
                    ),
                    Triple(
                        DiscoverBackground.MONET,
                        R.string.discover_bg_monet,
                        R.string.discover_bg_monet_desc,
                    ),
                ).forEach { (mode, title, desc) ->
                    BackgroundOptionRow(
                        title = stringResource(title),
                        desc = stringResource(desc),
                        selected = discoverBg == mode,
                        onClick = { viewModel.setDiscoverBackground(mode) },
                    )
                }
            }
            // 自定义图只在选到「自定义图片」时才出现图片操作，避免它看起来像常驻开关
            if (discoverBg == DiscoverBackground.CUSTOM) {
                Row(
                    verticalAlignment = Alignment.CenterVertically,
                    horizontalArrangement = Arrangement.spacedBy(8.dp),
                    modifier = Modifier.fillMaxWidth().padding(top = 8.dp),
                ) {
                    Text(
                        text = if (hasCustomBg) "" else stringResource(R.string.custom_bg_none),
                        style = MaterialTheme.typography.labelSmall,
                        color = secondary,
                        modifier = Modifier.weight(1f),
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
            }
        }

    }
}
