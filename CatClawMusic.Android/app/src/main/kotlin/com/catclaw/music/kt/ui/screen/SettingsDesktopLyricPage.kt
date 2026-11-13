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

/** 设置·桌面歌词子页（从 SettingsScreen.kt 拆出） */
@Composable
internal fun DesktopLyricSettingsScreen(onBack: () -> Unit, viewModel: SettingsViewModel) {
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
