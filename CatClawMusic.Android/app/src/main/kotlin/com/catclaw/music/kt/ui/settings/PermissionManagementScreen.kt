package com.catclaw.music.kt.ui.settings

import android.app.Activity
import android.content.Context
import androidx.activity.compose.rememberLauncherForActivityResult
import androidx.activity.result.contract.ActivityResultContracts
import androidx.compose.foundation.background
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.verticalScroll
import androidx.compose.material3.Icon
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableIntStateOf
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
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import androidx.lifecycle.ViewModel
import javax.inject.Inject
import androidx.hilt.navigation.compose.hiltViewModel
import com.catclaw.music.kt.R
import com.catclaw.music.kt.data.permission.AndroidPermissionProbe
import com.catclaw.music.kt.data.permission.PermissionInventory
import com.catclaw.music.kt.data.permission.PermissionKind
import com.catclaw.music.kt.data.permission.PermissionRow
import com.catclaw.music.kt.ui.theme.BackTile
import com.catclaw.music.kt.ui.theme.LocalCatClawPalette
import com.catclaw.music.kt.ui.theme.PillLabel
import com.catclaw.music.kt.ui.theme.TouchMin
import com.catclaw.music.kt.ui.theme.glassCard
import com.catclaw.music.kt.ui.theme.highlightOnGlassCard
import dagger.hilt.android.lifecycle.HiltViewModel

/**
 * 权限管理子页 —— MAUI `PermissionManagementPage` 的对应物。
 *
 * 状态是**每次进页 / 每次点刷新现读**的：授权发生在系统设置页里，回到本页时不重读就会一直显示旧状态，
 * 那是这类页面最典型的"看着像没生效"。所以用一个自增的 tick 触发重算，而不是缓存进 ViewModel。
 */
@Composable
fun PermissionManagementScreen(
    onBack: () -> Unit,
    viewModel: PermissionViewModel = hiltViewModel(),
) {
    val probe = viewModel.probe
    val palette = LocalCatClawPalette.current
    val context = LocalContext.current
    var tick by mutableIntStateOf(0)
    var snapshot by remember(tick) { mutableStateOf(probe.snapshot()) }
    val title = Color(highlightOnGlassCard(palette))

    val requestAudio = rememberLauncherForActivityResult(ActivityResultContracts.RequestMultiplePermissions()) {
        tick++ // 弹窗回来立刻重读，别等用户手动刷新
    }

    Column(
        modifier = Modifier
            .fillMaxWidth()
            .verticalScroll(rememberScrollState())
            .padding(horizontal = 18.dp),
    ) {
        Row(verticalAlignment = Alignment.CenterVertically, modifier = Modifier.fillMaxWidth().padding(top = 10.dp)) {
            BackTile(onBack = onBack, description = stringResource(R.string.GoBack))
            Spacer(Modifier.width(12.dp))
            Text(stringResource(R.string.Permissions), fontSize = 22.sp, fontWeight = FontWeight.Bold, color = Color(palette.textPrimary))
            Spacer(Modifier.weight(1f))
            Box(
                modifier = Modifier
                    .size(TouchMin)
                    .clip(RoundedCornerShape(14.dp))
                    .background(Color(palette.textPrimary).copy(alpha = 0.10f))
                    .clickable(onClickLabel = stringResource(R.string.perm_refresh)) { tick++ },
                contentAlignment = Alignment.Center,
            ) {
                Icon(
                    painter = painterResource(R.drawable.ic_refresh),
                    contentDescription = stringResource(R.string.perm_refresh),
                    tint = Color(palette.textPrimary),
                    modifier = Modifier.size(20.dp),
                )
            }
        }
        Text(
            stringResource(R.string.PermissionsSubtitle),
            fontSize = 13.sp,
            color = Color(palette.textSecondary),
            modifier = Modifier.padding(top = 8.dp, bottom = 4.dp),
        )

        PermissionInventory.rows(snapshot).forEach { row ->
            PermissionCard(
                row = row,
                titleColor = title,
                onAction = {
                    val intent = probe.requestIntent(row.kind)
                    if (intent != null) runCatching { context.startActivity(intent) }
                    else requestAudio.launch(probe.runtimePermissions(row.kind))
                },
            )
        }

        Box(modifier = Modifier.padding(top = 6.dp, bottom = 24.dp)) {
            PillLabel(text = stringResource(R.string.OpenAppSettings), onClick = { runCatching { context.startActivity(probe.appSettingsIntent()) } }, filled = false)
        }
    }
}

@Composable
private fun PermissionCard(row: PermissionRow, titleColor: Color, onAction: () -> Unit) {
    val palette = LocalCatClawPalette.current
    Row(
        modifier = Modifier
            .fillMaxWidth()
            .padding(vertical = 8.dp)
            .glassCard(palette = palette, corner = 22)
            .padding(16.dp),
        verticalAlignment = Alignment.CenterVertically,
        horizontalArrangement = Arrangement.spacedBy(14.dp),
    ) {
        Box(
            modifier = Modifier
                .size(40.dp)
                .clip(RoundedCornerShape(12.dp))
                .background(Color(palette.textPrimary).copy(alpha = 0.10f)),
            contentAlignment = Alignment.Center,
        ) {
            Icon(
                painter = painterResource(iconOf(row)),
                contentDescription = null,
                tint = Color(palette.textPrimary),
                modifier = Modifier.size(20.dp),
            )
        }
        Column(modifier = Modifier.weight(1f)) {
            Text(stringResource(titleOf(row)), fontSize = 15.sp, fontWeight = FontWeight.Medium, color = Color(palette.textPrimary))
            Text(
                stringResource(descOf(row)),
                fontSize = 12.sp,
                color = Color(palette.textSecondary),
                modifier = Modifier.padding(top = 2.dp),
            )
            Text(
                stringResource(if (row.granted) R.string.perm_granted else R.string.perm_denied),
                fontSize = 12.sp,
                fontWeight = FontWeight.SemiBold,
                color = titleColor,
                modifier = Modifier.padding(top = 4.dp),
            )
        }
        PillLabel(
            text = stringResource(if (PermissionInventory.actionIsRequest(row)) R.string.perm_go_grant else R.string.perm_view),
            onClick = onAction,
            filled = PermissionInventory.actionIsRequest(row),
        )
    }
}

/** kind → 文案/图标只在这一处映射，行序与状态判据留在 `:data` 的纯函数里（那边可测） */
@Composable
private fun titleOf(row: PermissionRow) = when (row.kind) {
    PermissionKind.Audio -> R.string.perm_audio_title
    PermissionKind.ManageStorage -> R.string.perm_all_files_title
    PermissionKind.Overlay -> R.string.perm_overlay_title
    PermissionKind.Notification -> R.string.perm_notification_title
}

@Composable
private fun descOf(row: PermissionRow) = when (row.kind) {
    PermissionKind.Audio -> R.string.perm_audio_desc
    PermissionKind.ManageStorage -> R.string.perm_all_files_desc
    PermissionKind.Overlay -> R.string.perm_overlay_desc
    PermissionKind.Notification -> R.string.perm_notification_desc
}

private fun iconOf(row: PermissionRow) = when (row.kind) {
    PermissionKind.Audio -> R.drawable.ic_library
    PermissionKind.ManageStorage -> R.drawable.ic_settings
    PermissionKind.Overlay -> R.drawable.ic_equalizer
    PermissionKind.Notification -> R.drawable.ic_play
}

/** 页面只要一个能拿到 probe 的入口，状态本身留在 Compose 里（每次进页/刷新现读） */
@HiltViewModel
class PermissionViewModel @Inject constructor(val probe: AndroidPermissionProbe) : ViewModel()
