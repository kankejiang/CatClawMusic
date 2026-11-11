package com.catclaw.music.kt.ui.library

import android.content.res.Configuration
import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.clickable
import androidx.compose.foundation.interaction.MutableInteractionSource
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material3.LinearProgressIndicator
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
import androidx.compose.ui.platform.LocalConfiguration
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import com.catclaw.music.kt.R
import com.catclaw.music.kt.data.netease.NeteasePlaylistImporter
import com.catclaw.music.kt.ui.theme.GlassSearchField
import com.catclaw.music.kt.ui.theme.LocalCatClawPalette
import com.catclaw.music.kt.ui.theme.PillLabel
import com.catclaw.music.kt.ui.theme.highlightOnGlassCard
import com.catclaw.music.kt.ui.theme.isLightPalette

/**
 * 「导入网易云歌单」面板。
 *
 * 由壳层渲染（`CatClawRoot` 的录制层之外），原因与睡眠定时弹窗同一条：
 * 面板用的是 `liquidGlass`，如果留在 pager 页面里，采样会命中自己那一层 —— 点开就闪退。
 *
 * 内容按状态机走，不做成"一个输入框 + 一个按钮"的哑面板：
 * 导入 500 首是几秒到几十秒的事，进度、成功统计（本地/在线各多少）、失败原因都得当场看见，
 * 否则用户只能靠"歌单突然出现了"猜它到底做了什么。
 */
@Composable
fun NeteaseImportPanel(
    state: NeteaseImportViewModel.State,
    onStart: (String) -> Unit,
    onReset: () -> Unit,
    onDismiss: () -> Unit,
) {
    val palette = LocalCatClawPalette.current
    val ink = Color(palette.textPrimary)
    val hint = Color(palette.textSecondary)
    val landscape = LocalConfiguration.current.orientation == Configuration.ORIENTATION_LANDSCAPE
    var input by remember { mutableStateOf("") }
    val fade = remember { MutableInteractionSource() }
    // 实心面板：进了网易云音乐页之后，这个弹层不再挂在壳层录制层外面，
    // 液态玻璃在这里没有可采样的宿主（会退化成纯色/自采样），实心反而稳定可读。
    val panelBg = if (isLightPalette(palette)) {
        Color.White.copy(alpha = 0.96f)
    } else {
        Color(0xFF13161D).copy(alpha = 0.96f)
    }
    val shape = RoundedCornerShape(28.dp)

    Box(
        modifier = Modifier
            .fillMaxSize()
            .background(Color.Black.copy(alpha = 0.55f))
            .clickable(interactionSource = fade, indication = null, onClick = onDismiss),
        contentAlignment = Alignment.Center,
    ) {
        Column(
            modifier = Modifier
                .width(if (landscape) 520.dp else 340.dp)
                .clip(shape)
                .background(panelBg)
                .border(1.dp, Color(palette.textPrimary).copy(alpha = 0.14f), shape)
                // 吃掉面板上的点击，别穿透到遮罩
                .clickable(interactionSource = fade, indication = null) { }
                .padding(horizontal = 20.dp, vertical = 18.dp),
        ) {
            Text(
                text = stringResource(R.string.netease_import_title),
                color = ink,
                fontSize = 20.sp,
                fontWeight = FontWeight.Bold,
            )
            Spacer(Modifier.height(8.dp))
            Text(
                text = stringResource(R.string.netease_import_desc),
                color = hint,
                fontSize = 12.sp,
            )
            Spacer(Modifier.height(14.dp))

            when (val current = state) {
                is NeteaseImportViewModel.State.Idle -> {
                    GlassSearchField(
                        query = input,
                        onChange = { input = it },
                        placeholder = stringResource(R.string.netease_import_placeholder),
                        modifier = Modifier.fillMaxWidth(),
                    )
                    Spacer(Modifier.height(6.dp))
                    Text(
                        text = stringResource(R.string.netease_import_example),
                        color = hint,
                        fontSize = 11.sp,
                        maxLines = 1,
                        overflow = TextOverflow.Ellipsis,
                    )
                    Spacer(Modifier.height(16.dp))
                    Row(
                        modifier = Modifier.fillMaxWidth(),
                        horizontalArrangement = Arrangement.End,
                    ) {
                        PillLabel(
                            text = stringResource(R.string.netease_import_start),
                            onClick = { onStart(input) },
                            enabled = input.isNotBlank(),
                        )
                    }
                }

                is NeteaseImportViewModel.State.Running -> {
                    Text(
                        text = if (current.total <= 0) {
                            stringResource(R.string.netease_import_running)
                        } else {
                            stringResource(R.string.netease_import_progress, current.done, current.total)
                        },
                        color = ink,
                        fontSize = 14.sp,
                    )
                    Spacer(Modifier.height(10.dp))
                    LinearProgressIndicator(
                        // 总数为 0（还在读歌单）时给不确定进度条，别画一条永远 0% 的假进度
                        progress = {
                            if (current.total <= 0) 0f else current.done.toFloat() / current.total
                        },
                        modifier = Modifier.fillMaxWidth().height(6.dp),
                        // 进度条在玻璃面板上，主色直接当填充色会太淡（可读性闸门也拦这种写法）
                        color = Color(highlightOnGlassCard(palette)),
                        trackColor = Color(palette.textPrimary).copy(alpha = 0.12f),
                    )
                    Spacer(Modifier.height(16.dp))
                    Row(
                        modifier = Modifier.fillMaxWidth(),
                        horizontalArrangement = Arrangement.End,
                    ) {
                        // 导入本身不会被取消：已经写进去的歌曲收不回来，给一个假的"取消"按钮只会骗人
                        PillLabel(
                            text = stringResource(R.string.netease_import_background),
                            onClick = onDismiss,
                            filled = false,
                        )
                    }
                }

                is NeteaseImportViewModel.State.Done -> {
                    val result = current.result
                    Text(
                        text = stringResource(R.string.netease_import_done, result.playlistName),
                        color = ink,
                        fontSize = 15.sp,
                        fontWeight = FontWeight.SemiBold,
                    )
                    Spacer(Modifier.height(6.dp))
                    Text(
                        text = stringResource(
                            R.string.netease_import_done_detail,
                            result.imported,
                            result.localMatched,
                            result.online,
                        ),
                        color = Color(highlightOnGlassCard(palette)),
                        fontSize = 13.sp,
                    )
                    if (result.skipped > 0) {
                        Spacer(Modifier.height(4.dp))
                        Text(
                            text = stringResource(R.string.netease_import_skipped, result.skipped),
                            color = hint,
                            fontSize = 11.sp,
                        )
                    }
                    Spacer(Modifier.height(16.dp))
                    Row(
                        modifier = Modifier.fillMaxWidth(),
                        horizontalArrangement = Arrangement.End,
                    ) {
                        PillLabel(
                            text = stringResource(R.string.netease_import_again),
                            onClick = {
                                input = ""
                                onReset()
                            },
                            filled = false,
                        )
                        Spacer(Modifier.width(10.dp))
                        PillLabel(
                            text = stringResource(R.string.netease_import_close),
                            onClick = onDismiss,
                        )
                    }
                }

                is NeteaseImportViewModel.State.Failed -> {
                    Text(
                        text = when (current.reason) {
                            NeteasePlaylistImporter.Reason.INVALID_LINK ->
                                stringResource(R.string.netease_import_invalid)
                            NeteasePlaylistImporter.Reason.EMPTY_PLAYLIST ->
                                stringResource(R.string.netease_import_empty)
                            NeteasePlaylistImporter.Reason.FETCH_FAILED ->
                                stringResource(R.string.netease_import_failed)
                        },
                        color = ink,
                        fontSize = 14.sp,
                    )
                    Spacer(Modifier.height(16.dp))
                    Row(
                        modifier = Modifier.fillMaxWidth(),
                        horizontalArrangement = Arrangement.End,
                    ) {
                        PillLabel(
                            text = stringResource(R.string.netease_import_retry),
                            onClick = onReset,
                            filled = false,
                        )
                    }
                }
            }
        }
    }
}
