package com.catclaw.music.kt.ui.player

import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.ExperimentalLayoutApi
import androidx.compose.foundation.layout.FlowRow
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.material3.AlertDialog
import androidx.compose.material3.Slider
import androidx.compose.material3.SliderDefaults
import androidx.compose.material3.Switch
import androidx.compose.material3.SwitchDefaults
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import com.catclaw.music.kt.R
import com.catclaw.music.kt.core.playback.SleepTimerRules
import com.catclaw.music.kt.ui.theme.GlassChip
import com.catclaw.music.kt.ui.theme.LocalCatClawPalette
import com.catclaw.music.kt.ui.theme.PillLabel
import com.catclaw.music.kt.ui.theme.highlightOnGlassCard

/** 预设分钟数，与 C# `NowPlayingPage.Actions.cs:132` 的 chips 同集合（"自定义…"换成了滑杆，见下） */
private val PRESET_MINUTES = listOf(15, 30, 45, 60, 90)

/** mm:ss；超过一小时才带小时位 */
internal fun formatCountdown(totalSec: Int): String {
    val h = totalSec / 3600
    val m = (totalSec % 3600) / 60
    val s = totalSec % 60
    return if (h > 0) "%d:%02d:%02d".format(h, m, s) else "%d:%02d".format(m, s)
}

/**
 * 睡眠定时弹窗 —— 对应 MAUI 的 `SleepTimerPopup`。
 *
 * 与 C# 的一处偏离：「自定义…」那条内联输入框换成 5..180 分钟的滑杆。
 * 本项目的视觉闸门不许页面里出现 stock 输入框，而为这一个数字专门写一套自绘输入
 * 不值当；滑杆给的是同一件事（选一个分钟数），且少一次键盘往返。
 */
@OptIn(ExperimentalLayoutApi::class)
@Composable
fun SleepTimerDialog(
    sleep: SleepTimerRules.State,
    onStart: (minutes: Int, stopAfterSong: Boolean, fadeOut: Boolean) -> Unit,
    onCancel: () -> Unit,
    onDismiss: () -> Unit,
) {
    val palette = LocalCatClawPalette.current
    val ink = Color(palette.textPrimary)
    val hint = Color(palette.textSecondary)
    var minutes by remember { mutableStateOf(if (sleep.totalSec > 0) sleep.totalSec / 60 else 30) }
    var stopAfterSong by remember { mutableStateOf(sleep.stopAfterSong) }
    var fadeOut by remember { mutableStateOf(sleep.fadeOut) }

    AlertDialog(
        onDismissRequest = onDismiss,
        confirmButton = {
            if (sleep.running) {
                PillLabel(text = stringResource(R.string.sleep_timer_close), onClick = onDismiss)
            } else {
                PillLabel(
                    text = stringResource(R.string.sleep_timer_start),
                    onClick = {
                        onStart(minutes, stopAfterSong, fadeOut)
                        onDismiss()
                    },
                )
            }
        },
        dismissButton = {
            if (sleep.running) {
                PillLabel(
                    text = stringResource(R.string.sleep_timer_cancel),
                    onClick = {
                        onCancel()
                        onDismiss()
                    },
                    filled = false,
                )
            }
        },
        title = { Text(text = stringResource(R.string.sleep_timer_title), color = ink, fontSize = 20.sp, fontWeight = FontWeight.Bold) },
        text = {
            Column(modifier = Modifier.fillMaxWidth()) {
                if (sleep.running) {
                    Text(
                        text = if (sleep.waitingForSongEnd) {
                            stringResource(R.string.sleep_timer_waiting)
                        } else {
                            stringResource(R.string.sleep_timer_remaining, formatCountdown(sleep.remainingSec))
                        },
                        fontSize = 16.sp,
                        color = ink,
                    )
                    Spacer(Modifier.height(6.dp))
                    Text(
                        text = stringResource(
                            R.string.sleep_timer_options_echo,
                            stringResource(R.string.sleep_timer_minutes_n, sleep.totalSec / 60),
                            if (sleep.stopAfterSong) stringResource(R.string.sleep_timer_on) else stringResource(R.string.sleep_timer_off),
                            if (sleep.fadeOut) stringResource(R.string.sleep_timer_on) else stringResource(R.string.sleep_timer_off),
                        ),
                        fontSize = 12.sp,
                        color = hint,
                    )
                } else {
                    Text(text = stringResource(R.string.sleep_timer_stop_time), fontSize = 12.sp, color = hint)
                    Spacer(Modifier.height(8.dp))
                    FlowRow(
                        modifier = Modifier.fillMaxWidth(),
                        horizontalArrangement = Arrangement.spacedBy(8.dp),
                        verticalArrangement = Arrangement.spacedBy(8.dp),
                    ) {
                        PRESET_MINUTES.forEach { m ->
                            GlassChip(
                                label = stringResource(R.string.sleep_timer_minutes_n, m),
                                selected = minutes == m,
                                onClick = { minutes = m },
                            )
                        }
                    }
                    Spacer(Modifier.height(10.dp))
                    Row(modifier = Modifier.fillMaxWidth(), verticalAlignment = Alignment.CenterVertically) {
                        Text(
                            text = stringResource(R.string.sleep_timer_custom),
                            fontSize = 13.sp,
                            color = ink,
                            modifier = Modifier.weight(1f),
                        )
                        Text(
                            text = stringResource(R.string.sleep_timer_minutes_n, minutes),
                            fontSize = 13.sp,
                            color = Color(highlightOnGlassCard(palette)),
                        )
                    }
                    Slider(
                        value = minutes.toFloat(),
                        onValueChange = { minutes = (it / 5f).toInt() * 5 },
                        valueRange = 5f..180f,
                        steps = 34,
                        colors = SliderDefaults.colors(
                            thumbColor = Color(palette.primary),
                            activeTrackColor = Color(palette.primary),
                        ),
                    )
                    OptionRow(
                        title = stringResource(R.string.sleep_timer_stop_after_song),
                        subtitle = stringResource(R.string.sleep_timer_stop_after_song_sub),
                        checked = stopAfterSong,
                        onChecked = { stopAfterSong = it },
                    )
                    OptionRow(
                        title = stringResource(R.string.sleep_timer_fade),
                        subtitle = stringResource(R.string.sleep_timer_fade_sub),
                        checked = fadeOut,
                        onChecked = { fadeOut = it },
                    )
                }
            }
        },
        containerColor = Color(palette.backgroundStops.last().argb),
    )
}

@Composable
private fun OptionRow(title: String, subtitle: String, checked: Boolean, onChecked: (Boolean) -> Unit) {
    val palette = LocalCatClawPalette.current
    Row(
        modifier = Modifier.fillMaxWidth().padding(top = 8.dp),
        verticalAlignment = Alignment.CenterVertically,
    ) {
        Column(modifier = Modifier.weight(1f)) {
            Text(text = title, fontSize = 14.sp, color = Color(palette.textPrimary))
            Text(text = subtitle, fontSize = 12.sp, color = Color(palette.textSecondary))
        }
        Switch(
            checked = checked,
            onCheckedChange = onChecked,
            colors = SwitchDefaults.colors(
                checkedThumbColor = Color(palette.primary),
                checkedTrackColor = Color(palette.primary).copy(alpha = 0.32f),
            ),
        )
    }
}
