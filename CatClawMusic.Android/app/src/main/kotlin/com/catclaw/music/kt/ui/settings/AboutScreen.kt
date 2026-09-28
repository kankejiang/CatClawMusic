package com.catclaw.music.kt.ui.settings

import android.content.Context
import android.content.Intent
import android.net.Uri
import androidx.compose.foundation.Image
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
import androidx.compose.foundation.verticalScroll
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.lifecycle.compose.collectAsStateWithLifecycle
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.res.painterResource
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import androidx.hilt.navigation.compose.hiltViewModel
import androidx.lifecycle.ViewModel
import androidx.lifecycle.viewModelScope
import com.catclaw.music.kt.R
import com.catclaw.music.kt.data.update.UpdateOutcome
import com.catclaw.music.kt.data.update.UpdateService
import com.catclaw.music.kt.ui.theme.BackTile
import com.catclaw.music.kt.ui.theme.LocalCatClawPalette
import com.catclaw.music.kt.ui.theme.PillLabel
import com.catclaw.music.kt.ui.theme.glassCard
import dagger.hilt.android.lifecycle.HiltViewModel
import dagger.hilt.android.qualifiers.ApplicationContext
import java.util.Calendar
import javax.inject.Inject
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.launch

/** 检查更新的三种结果；null = 还没查过 */
sealed interface UpdateUi {
    data object Idle : UpdateUi
    data object Checking : UpdateUi
    data class UpToDate(val current: String) : UpdateUi
    data class Available(val latest: String) : UpdateUi
    /** 网络/解析失败：与 C# 一样回"没有新版本"，不弹红字 */
    data object Unreachable : UpdateUi
}

@HiltViewModel
class AboutViewModel @Inject constructor(
    @ApplicationContext private val context: Context,
    private val updateService: UpdateService,
) : ViewModel() {

    val version = "v" + updateService.currentVersion()

    private val _update = MutableStateFlow<UpdateUi>(UpdateUi.Idle)
    val update = _update.asStateFlow()

    fun checkUpdate() {
        if (_update.value == UpdateUi.Checking) return
        _update.value = UpdateUi.Checking
        viewModelScope.launch {
            _update.value = when (val outcome = updateService.check()) {
                is UpdateOutcome.Available -> UpdateUi.Available(outcome.latest)
                UpdateOutcome.UpToDate -> UpdateUi.UpToDate(version)
                UpdateOutcome.Unreachable -> UpdateUi.Unreachable
            }
        }
    }

    fun open(url: String) {
        runCatching {
            context.startActivity(Intent(Intent.ACTION_VIEW, Uri.parse(url)).addFlags(Intent.FLAG_ACTIVITY_NEW_TASK))
        }
    }

    companion object {
        const val LICENSE_URL = "https://github.com/kankejiang/CatClawMusic/blob/main/LICENSE.txt"
        const val GROUP_URL = "https://qm.qq.com/q/Fhu3IEzqa4"
        const val REPO_URL = "https://github.com/kankejiang/CatClawMusic"

        /** 版权年份跟着系统时间走，与 C# `AboutViewModel` 的 `$"© 2024-{DateTime.Now.Year}"` 同做法 */
        fun copyright(): String = "© 2024-%d CatClawMusic. All rights reserved.".format(Calendar.getInstance().get(Calendar.YEAR))
    }
}

/**
 * 关于页 —— MAUI `AboutPage` 的对应物。
 * 三处刻意与 MAUI 不同，且都是"说真话"方向：版本取本机 PackageManager、
 * 版本行写 Kotlin 原生而不是 ".NET MAUI Edition"、更新失败只说"暂时连不上"不弹报错。
 */
@Composable
fun AboutScreen(
    onBack: () -> Unit,
    viewModel: AboutViewModel = hiltViewModel(),
) {
    val palette = LocalCatClawPalette.current
    val context = LocalContext.current
    val update by viewModel.update.collectAsStateWithLifecycle()
    var pendingDialog by remember { mutableStateOf<String?>(null) }

    Column(
        modifier = Modifier
            .fillMaxWidth()
            .verticalScroll(rememberScrollState())
            .padding(horizontal = 18.dp),
        horizontalAlignment = Alignment.CenterHorizontally,
    ) {
        Row(verticalAlignment = Alignment.CenterVertically, modifier = Modifier.fillMaxWidth().padding(top = 10.dp)) {
            BackTile(onBack = onBack, description = stringResource(R.string.GoBack))
            Spacer(Modifier.width(12.dp))
            Text(stringResource(R.string.About), fontSize = 22.sp, fontWeight = FontWeight.Bold, color = Color(palette.textPrimary))
        }

        Column(
            modifier = Modifier
                .fillMaxWidth()
                .padding(vertical = 12.dp)
                .glassCard(palette = palette, corner = 26)
                .padding(20.dp),
            horizontalAlignment = Alignment.CenterHorizontally,
        ) {
            Image(
                painter = painterResource(R.drawable.avatar_yuki),
                contentDescription = null,
                modifier = Modifier.size(72.dp),
            )
            Text(stringResource(R.string.AppName), fontSize = 20.sp, fontWeight = FontWeight.Bold, color = Color(palette.textPrimary), modifier = Modifier.padding(top = 12.dp))
            Text(viewModel.version, fontSize = 13.sp, color = Color(palette.textSecondary), modifier = Modifier.padding(top = 2.dp))
            Text(
                stringResource(R.string.about_tagline),
                fontSize = 13.sp,
                color = Color(palette.textSecondary),
                textAlign = TextAlign.Center,
                modifier = Modifier.padding(top = 8.dp),
            )
            Text(stringResource(R.string.about_edition), fontSize = 11.sp, color = Color(palette.textSecondary), modifier = Modifier.padding(top = 10.dp))

            Row(
                modifier = Modifier.fillMaxWidth().padding(top = 16.dp),
                horizontalArrangement = Arrangement.spacedBy(10.dp),
            ) {
                PillLabel(text = stringResource(R.string.about_license), onClick = { viewModel.open(AboutViewModel.LICENSE_URL) }, filled = false)
                PillLabel(text = stringResource(R.string.about_group), onClick = { viewModel.open(AboutViewModel.GROUP_URL) }, filled = false)
                PillLabel(text = "GitHub", onClick = { viewModel.open(AboutViewModel.REPO_URL) }, filled = false)
            }

            Box(modifier = Modifier.padding(top = 14.dp)) {
                PillLabel(
                    text = stringResource(R.string.about_check_update),
                    onClick = viewModel::checkUpdate,
                    enabled = update != UpdateUi.Checking,
                )
            }
            when (val state = update) {
                UpdateUi.Idle -> Unit
                UpdateUi.Checking -> Text(stringResource(R.string.about_checking), fontSize = 12.sp, color = Color(palette.textSecondary), modifier = Modifier.padding(top = 10.dp))
                is UpdateUi.UpToDate -> Text(stringResource(R.string.about_up_to_date, state.current), fontSize = 12.sp, color = Color(palette.textSecondary), modifier = Modifier.padding(top = 10.dp))
                is UpdateUi.Unreachable -> Text(stringResource(R.string.about_unreachable), fontSize = 12.sp, color = Color(palette.textSecondary), modifier = Modifier.padding(top = 10.dp))
                is UpdateUi.Available -> {
                    Text(stringResource(R.string.about_new_version, state.latest), fontSize = 12.sp, color = Color(palette.textPrimary), modifier = Modifier.padding(top = 10.dp))
                    pendingDialog = state.latest
                }
            }
        }

        Text(AboutViewModel.copyright(), fontSize = 11.sp, color = Color(palette.textSecondary), modifier = Modifier.padding(bottom = 24.dp))
    }

    pendingDialog?.let { latest ->
        androidx.compose.material3.AlertDialog(
            onDismissRequest = { pendingDialog = null },
            containerColor = Color(palette.backgroundStops.last().argb),
            title = { Text(stringResource(R.string.about_new_version_title), color = Color(palette.textPrimary)) },
            text = { Text(stringResource(R.string.about_new_version_body, latest), color = Color(palette.textPrimary)) },
            confirmButton = {
                PillLabel(text = stringResource(R.string.about_go_repo), onClick = {
                    pendingDialog = null
                    viewModel.open(AboutViewModel.REPO_URL)
                })
            },
            dismissButton = {
                PillLabel(text = stringResource(R.string.Cancel), onClick = { pendingDialog = null }, filled = false)
            },
        )
    }
}
