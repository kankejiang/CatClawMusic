package com.catclaw.music.kt.ui.settings

import android.content.Context
import android.content.Intent
import android.net.Uri
import androidx.compose.foundation.Image
import androidx.compose.foundation.background
import androidx.compose.ui.draw.clip
import androidx.compose.foundation.clickable
import androidx.compose.ui.text.style.TextOverflow
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
    ) {
        Row(verticalAlignment = Alignment.CenterVertically, modifier = Modifier.fillMaxWidth().padding(top = 10.dp)) {
            BackTile(onBack = onBack, description = stringResource(R.string.GoBack))
            Spacer(Modifier.width(12.dp))
            Text(stringResource(R.string.About), fontSize = 22.sp, fontWeight = FontWeight.Bold, color = Color(palette.textPrimary))
        }

        // ── 主体：**纯排版**，不放头像（2026-11-04 用户要求移除猫爪娘头像）——
        // 大字号应用名 + 版本号 + 一句标语，靠字重/字号拉层次，不做图形装饰。
        Column(
            modifier = Modifier.fillMaxWidth().padding(top = 26.dp, bottom = 4.dp),
            horizontalAlignment = Alignment.CenterHorizontally,
        ) {
            Text(
                text = stringResource(R.string.AppName),
                fontSize = 30.sp,
                fontWeight = FontWeight.Bold,
                letterSpacing = (-0.8).sp,
                color = Color(palette.textPrimary),
            )
            Text(
                text = viewModel.version,
                fontSize = 13.sp,
                letterSpacing = 0.3.sp,
                color = Color(palette.textSecondary),
                modifier = Modifier.padding(top = 6.dp),
            )
            Text(
                text = stringResource(R.string.about_tagline),
                fontSize = 12.5.sp,
                lineHeight = 19.sp,
                textAlign = TextAlign.Center,
                color = Color(palette.textSecondary),
                modifier = Modifier.padding(top = 16.dp, start = 12.dp, end = 12.dp),
            )
        }

        // ── 信息卡：iOS 分组列表那种「左标签 / 右值 / 发丝分隔线」
        Column(
            modifier = Modifier
                .fillMaxWidth()
                .padding(top = 22.dp)
                .glassCard(palette = palette, corner = 22),
        ) {
            AboutInfoRow(
                label = stringResource(R.string.about_version),
                value = viewModel.version,
                palette = palette,
                first = true,
            )
            AboutInfoRow(
                label = stringResource(R.string.about_runtime),
                // 顺便把「Kotlin 原生版」这句带回来（对应 MAUI 的 ".NET MAUI Edition"），
                // 也避免这条文案变成死资源（LocaleParityTest 会盯着）
                value = stringResource(R.string.about_edition) + " · Android " +
                    android.os.Build.VERSION.RELEASE + " (SDK " + android.os.Build.VERSION.SDK_INT + ")",
                palette = palette,
            )
            AboutInfoRow(
                label = stringResource(R.string.about_package),
                value = context.packageName,
                palette = palette,
            )
            AboutLinkRow(stringResource(R.string.about_license), palette) { viewModel.open(AboutViewModel.LICENSE_URL) }
            AboutLinkRow(stringResource(R.string.about_group), palette) { viewModel.open(AboutViewModel.GROUP_URL) }
            AboutLinkRow("GitHub", palette) { viewModel.open(AboutViewModel.REPO_URL) }
        }

        // ── 更新卡：一行入口 + 一行状态，比原来那颗大粉按钮克制
        Column(
            modifier = Modifier
                .fillMaxWidth()
                .padding(top = 12.dp)
                .glassCard(palette = palette, corner = 22),
        ) {
            Row(
                modifier = Modifier
                    .fillMaxWidth()
                    .clickable(enabled = update != UpdateUi.Checking) { viewModel.checkUpdate() }
                    .padding(horizontal = 16.dp, vertical = 15.dp),
                verticalAlignment = Alignment.CenterVertically,
            ) {
                Text(
                    text = stringResource(R.string.about_check_update),
                    fontSize = 14.sp,
                    fontWeight = FontWeight.Medium,
                    color = Color(palette.textPrimary),
                )
                Spacer(Modifier.weight(1f))
                when (val state = update) {
                    UpdateUi.Idle -> Text("›", fontSize = 18.sp, color = Color(palette.textSecondary))
                    UpdateUi.Checking -> Text(stringResource(R.string.about_checking), fontSize = 12.sp, color = Color(palette.textSecondary))
                    is UpdateUi.UpToDate -> Text(stringResource(R.string.about_up_to_date, state.current), fontSize = 12.sp, color = Color(palette.textSecondary))
                    is UpdateUi.Unreachable -> Text(stringResource(R.string.about_unreachable), fontSize = 12.sp, color = Color(palette.textSecondary))
                    is UpdateUi.Available -> Text(
                        text = stringResource(R.string.about_new_version, state.latest),
                        fontSize = 12.sp,
                        // 主色当字色必须先过可读性判据（VisualLanguageTest）
                        color = Color(com.catclaw.music.kt.ui.theme.highlightOnGlassCard(palette)),
                    )
                }
            }
        }

        // ── 打赏卡：微信 / 支付宝收款码并排（2026-11-10 用户提供的收款页截图）
        Column(
            modifier = Modifier
                .fillMaxWidth()
                .padding(top = 12.dp)
                .glassCard(palette = palette, corner = 22)
                .padding(horizontal = 16.dp, vertical = 14.dp),
        ) {
            Text(
                text = stringResource(R.string.about_reward_title),
                fontSize = 14.sp,
                fontWeight = FontWeight.Medium,
                color = Color(palette.textPrimary),
            )
            Text(
                text = stringResource(R.string.about_reward_hint),
                fontSize = 11.5.sp,
                color = Color(palette.textSecondary),
                modifier = Modifier.padding(top = 4.dp),
            )
            Spacer(Modifier.height(12.dp))
            Row(
                modifier = Modifier.fillMaxWidth(),
                horizontalArrangement = Arrangement.spacedBy(14.dp),
            ) {
                RewardQr(R.drawable.reward_wechat, Modifier.weight(1f))
                RewardQr(R.drawable.reward_alipay, Modifier.weight(1f))
            }
        }

        Text(
            text = AboutViewModel.copyright(),
            fontSize = 11.sp,
            color = Color(palette.textSecondary).copy(alpha = 0.7f),
            textAlign = TextAlign.Center,
            modifier = Modifier.fillMaxWidth().padding(top = 24.dp, bottom = 28.dp),
        )
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

/** 信息卡里的一行「左标签 / 右值」，带顶部发丝线（首行不画） */
@Composable
private fun AboutInfoRow(
    label: String,
    value: String,
    palette: com.catclaw.music.kt.core.theme.CatClawPalette,
    first: Boolean = false,
) {
    AboutHairline(palette = palette, visible = !first)
    Row(
        modifier = Modifier.fillMaxWidth().padding(horizontal = 16.dp, vertical = 14.dp),
        verticalAlignment = Alignment.CenterVertically,
    ) {
        Text(text = label, fontSize = 13.sp, color = Color(palette.textSecondary))
        Spacer(Modifier.weight(1f))
        Text(
            text = value,
            fontSize = 13.sp,
            color = Color(palette.textPrimary),
            maxLines = 1,
            overflow = TextOverflow.Ellipsis,
            modifier = Modifier.padding(start = 12.dp),
        )
    }
}

/** 信息卡里的一行可点项，右侧一个排版箭头 */
@Composable
private fun AboutLinkRow(
    label: String,
    palette: com.catclaw.music.kt.core.theme.CatClawPalette,
    onClick: () -> Unit,
) {
    AboutHairline(palette = palette, visible = true)
    Row(
        modifier = Modifier
            .fillMaxWidth()
            .clickable(onClick = onClick)
            .padding(horizontal = 16.dp, vertical = 14.dp),
        verticalAlignment = Alignment.CenterVertically,
    ) {
        Text(text = label, fontSize = 13.sp, color = Color(palette.textPrimary))
        Spacer(Modifier.weight(1f))
        Text("›", fontSize = 17.sp, color = Color(palette.textSecondary))
    }
}

/** 0.5dp 发丝线：iOS 那套「用线不用阴影」的分隔方式 */
@Composable
private fun AboutHairline(palette: com.catclaw.music.kt.core.theme.CatClawPalette, visible: Boolean) {
    if (!visible) return
    Box(
        modifier = Modifier
            .fillMaxWidth()
            .padding(start = 16.dp)
            .height(0.5.dp)
            .background(Color(palette.textSecondary).copy(alpha = 0.14f)),
    )
}

/**
 * 打赏收款码：白底衬一层再圆角裁切 —— 收款页截图本身是白底，压在玻璃卡上需要这层白，
 * 否则二维码的静区被玻璃染暗，扫码识别率会掉。两张统一 196dp 高，并排时底边齐。
 */
@Composable
private fun RewardQr(iconRes: Int, modifier: Modifier = Modifier) {
    Image(
        painter = painterResource(iconRes),
        contentDescription = null,
        contentScale = androidx.compose.ui.layout.ContentScale.Fit,
        modifier = modifier
            .height(196.dp)
            .clip(androidx.compose.foundation.shape.RoundedCornerShape(14.dp))
            .background(Color.White)
            .padding(6.dp),
    )
}
