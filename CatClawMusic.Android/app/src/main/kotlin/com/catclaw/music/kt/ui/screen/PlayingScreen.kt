package com.catclaw.music.kt.ui.screen

import android.Manifest
import android.content.pm.PackageManager
import android.os.Build
import androidx.activity.compose.rememberLauncherForActivityResult
import androidx.activity.result.contract.ActivityResultContracts
import androidx.core.content.ContextCompat
import androidx.compose.foundation.background
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.aspectRatio
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material3.Button
import androidx.compose.material3.CircularProgressIndicator
import androidx.compose.material3.Icon
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Slider
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableFloatStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.dp
import androidx.hilt.navigation.compose.hiltViewModel
import androidx.lifecycle.compose.collectAsStateWithLifecycle
import com.catclaw.music.kt.R
import com.catclaw.music.kt.core.playback.PlayMode
import com.catclaw.music.kt.ui.playback.PlayingViewModel
import com.catclaw.music.kt.ui.player.Icon
import com.catclaw.music.kt.ui.player.PlayerIcon
import com.catclaw.music.kt.ui.player.PlayerIcons
import com.catclaw.music.kt.ui.theme.LocalCatClawPalette

@Composable
fun PlayingScreen(
    onOpenLyrics: () -> Unit = {},
    onOpenSettings: () -> Unit = {},
    onOpenQueue: () -> Unit = {},
    viewModel: PlayingViewModel = hiltViewModel(),
) {
    val state by viewModel.state.collectAsStateWithLifecycle()
    val positionMs by viewModel.positionMs.collectAsStateWithLifecycle()
    val scanning by viewModel.busy.collectAsStateWithLifecycle()
    val lastScan by viewModel.lastScan.collectAsStateWithLifecycle()
    val palette = LocalCatClawPalette.current
    val context = LocalContext.current

    // Android 13+ 无 POST_NOTIFICATIONS 时 StartForeground 会抛 SecurityException；
    // 读本地曲库需要 READ_MEDIA_AUDIO（33+）或 READ_EXTERNAL_STORAGE（<=32）
    val permissionLauncher = rememberLauncherForActivityResult(
        ActivityResultContracts.RequestMultiplePermissions(),
    ) { }
    LaunchedEffect(Unit) {
        val needed = if (Build.VERSION.SDK_INT >= 33) {
            arrayOf(
                Manifest.permission.POST_NOTIFICATIONS,
                Manifest.permission.READ_MEDIA_AUDIO,
            )
        } else {
            arrayOf(Manifest.permission.READ_EXTERNAL_STORAGE)
        }
        val missing = needed.filter {
            ContextCompat.checkSelfPermission(context, it) != PackageManager.PERMISSION_GRANTED
        }
        if (missing.isNotEmpty()) permissionLauncher.launch(missing.toTypedArray())
    }

    Column(
        modifier = Modifier
            .fillMaxSize()
            .padding(horizontal = 20.dp),
        horizontalAlignment = Alignment.CenterHorizontally,
    ) {
        Spacer(Modifier.height(16.dp))

        Row(modifier = Modifier.fillMaxWidth(), verticalAlignment = Alignment.CenterVertically) {
            Text(
                text = stringResource(R.string.PageTitleNowPlaying),
                style = MaterialTheme.typography.titleLarge,
                color = Color(palette.textPrimary),
                modifier = Modifier.weight(1f),
            )
            Text(
                text = stringResource(R.string.SettingsEntry),
                style = MaterialTheme.typography.bodyMedium,
                color = Color(palette.textSecondary),
                modifier = Modifier
                    .clickable(onClick = onOpenSettings)
                    .padding(horizontal = 10.dp, vertical = 6.dp),
            )
            Button(onClick = { viewModel.scanAndPlayAll() }, enabled = !scanning) {
                if (scanning) {
                    CircularProgressIndicator(
                        modifier = Modifier.size(16.dp),
                        strokeWidth = 2.dp,
                        color = Color(palette.textPrimary),
                    )
                } else {
                    Text(stringResource(R.string.ScanMusic))
                }
            }
        }

        Spacer(Modifier.height(16.dp))

        // MAUI 的 RootGrid 是 `Auto,*,Auto`：中间（封面+歌词）吃满剩余高度，控制区沉底。
        // 队列移出本页后如果不做这一步，播放页下方会空出一大片
        Box(
            modifier = Modifier
                .fillMaxWidth()
                .weight(1f),
            contentAlignment = Alignment.Center,
        ) {
            Column(horizontalAlignment = Alignment.CenterHorizontally) {
                Box(
                    modifier = Modifier
                        .fillMaxWidth(0.62f)
                        .aspectRatio(1f)
                        .background(Color(palette.primaryLight), RoundedCornerShape(16.dp)),
                    contentAlignment = Alignment.Center,
                ) {
                    Text(
                        text = state.currentSong?.title?.take(1) ?: "♪",
                        style = MaterialTheme.typography.displayLarge,
                        color = Color(palette.textPrimary),
                    )
                }

                Spacer(Modifier.height(20.dp))
                Text(
                    text = state.currentSong?.title ?: stringResource(R.string.m1_nothing),
                    style = MaterialTheme.typography.titleMedium,
                    color = Color(palette.textPrimary),
                    maxLines = 1,
                    overflow = TextOverflow.Ellipsis,
                )
                Text(
                    text = state.currentSong?.artist ?: "",
                    style = MaterialTheme.typography.bodyMedium,
                    color = Color(palette.textSecondary),
                    maxLines = 1,
                    overflow = TextOverflow.Ellipsis,
                )
                state.error?.let {
                    Text(
                        text = it,
                        style = MaterialTheme.typography.bodySmall,
                        color = Color(palette.primaryDark),
                        maxLines = 2,
                        overflow = TextOverflow.Ellipsis,
                    )
                }

                com.catclaw.music.kt.ui.CurrentLyricLine(onOpenLyrics = onOpenLyrics)
            }
        }

        ProgressRow(
            positionMs = positionMs,
            durationMs = state.durationMs,
            onSeek = { viewModel.seekTo(it) },
        )

        // 主控制行：照 MAUI `PhoneControls` 的 `*,*,*,*,*` 五等分与尺寸 40/52/73/52/40，
        // 全部是图标按钮（不是 ▶/❚❚ 文字），最右是「播放列表」入口——队列不在本页内嵌
        Row(modifier = Modifier.fillMaxWidth().height(80.dp)) {
            Box(modifier = Modifier.weight(1f), contentAlignment = Alignment.Center) {
                IconButton40(
                    icon = modeIcon(state.mode),
                    description = stringResource(R.string.mode_change),
                    tint = Color(palette.textSecondary),
                    onClick = { viewModel.cycleMode() },
                )
            }
            Box(modifier = Modifier.weight(1f), contentAlignment = Alignment.Center) {
                IconButton52(
                    icon = PlayerIcons.Previous,
                    description = stringResource(R.string.prev_song),
                    tint = Color(palette.textPrimary),
                    onClick = { viewModel.previous() },
                )
            }
            Box(modifier = Modifier.weight(1f), contentAlignment = Alignment.Center) {
                Box(
                    modifier = Modifier
                        .size(73.dp)
                        .background(Color(palette.primaryLight), CircleShape)
                        .clickable { viewModel.toggle() },
                    contentAlignment = Alignment.Center,
                ) {
                    Icon(
                        imageVector = if (state.isPlaying) PlayerIcons.Pause else PlayerIcons.Play,
                        contentDescription = stringResource(R.string.play_or_pause),
                        tint = Color(palette.primaryDark),
                        modifier = Modifier.size(30.dp),
                    )
                }
            }
            Box(modifier = Modifier.weight(1f), contentAlignment = Alignment.Center) {
                IconButton52(
                    icon = PlayerIcons.Next,
                    description = stringResource(R.string.next_song),
                    tint = Color(palette.textPrimary),
                    onClick = { viewModel.next() },
                )
            }
            Box(modifier = Modifier.weight(1f), contentAlignment = Alignment.Center) {
                IconButton40(
                    icon = PlayerIcons.QueueMusic,
                    description = stringResource(R.string.queue_open),
                    tint = Color(palette.textSecondary),
                    onClick = onOpenQueue,
                )
            }
        }

        // 次要图标行：MAUI 有 6 个（歌词/定时/详情/音效/横屏/更多），
        // 这里只放**已实现**的两个——定时、音效、详情、横屏在安卓侧还没有功能，
        // 摆上去就是假控件（本项目的既有判例：宁可不放，也不放不工作的入口）
        Row(
            modifier = Modifier.fillMaxWidth().padding(top = 6.dp),
            // MAUI 是 6 等分槽位；这里只有 2 个已实现的图标，用 weight 会把它们推到两端显得很空，
            // 改成居中定距
            horizontalArrangement = Arrangement.spacedBy(34.dp, Alignment.CenterHorizontally),
        ) {
            val desktopOn by viewModel.desktopLyricEnabled.collectAsStateWithLifecycle()
            Icon(
                imageVector = PlayerIcons.Lyrics,
                contentDescription = stringResource(R.string.open_lyrics),
                tint = Color(palette.primary),
                modifier = Modifier
                    .size(33.dp)
                    .padding(8.dp)
                    .clickable(onClick = onOpenLyrics),
            )
            Icon(
                imageVector = PlayerIcons.DesktopLyrics,
                contentDescription = stringResource(
                    R.string.lyric_desktop,
                    stringResource(if (desktopOn) R.string.lyric_on else R.string.lyric_off),
                ),
                tint = if (desktopOn) Color(palette.primary) else Color(palette.textSecondary),
                modifier = Modifier
                    .size(33.dp)
                    .padding(8.dp)
                    .clickable {
                        if (!viewModel.toggleDesktopLyric()) {
                            context.startActivity(viewModel.overlayPermissionIntent())
                        }
                    },
            )
        }

        Spacer(Modifier.height(8.dp))
        lastScan?.let {
            Text(
                text = stringResource(R.string.scan_result, it.scanned, it.inserted, it.removed),
                style = MaterialTheme.typography.labelSmall,
                color = Color(palette.textSecondary),
            )
        }
    }
}

@Composable
private fun IconButton40(
    icon: PlayerIcon,
    description: String,
    tint: Color,
    onClick: () -> Unit,
) {
    Box(
        modifier = Modifier
            .size(40.dp)
            .clickable(onClick = onClick),
        contentAlignment = Alignment.Center,
    ) {
        Icon(imageVector = icon, contentDescription = description, tint = tint, modifier = Modifier.size(20.dp))
    }
}

@Composable
private fun IconButton52(
    icon: PlayerIcon,
    description: String,
    tint: Color,
    onClick: () -> Unit,
) {
    Box(
        modifier = Modifier
            .size(52.dp)
            .clickable(onClick = onClick),
        contentAlignment = Alignment.Center,
    ) {
        Icon(imageVector = icon, contentDescription = description, tint = tint, modifier = Modifier.size(26.dp))
    }
}

@Composable
private fun modeIcon(mode: PlayMode): PlayerIcon = when (mode) {
    PlayMode.Sequential -> PlayerIcons.Sequential
    PlayMode.Shuffle -> PlayerIcons.Shuffle
    PlayMode.SingleRepeat -> PlayerIcons.RepeatOne
    PlayMode.ListRepeat -> PlayerIcons.Repeat
}

/**
 * 进度条：拖动期间只显示本地值，松手才 seek——
 * 与 MAUI 侧「Slider 拖动 OneWay、DragCompleted 后 seek 再恢复双向」同一条规则，
 * 否则拖动会被播放器回写的位置抢回去。
 */
@Composable
private fun ProgressRow(positionMs: Long, durationMs: Long, onSeek: (Long) -> Unit) {
    var dragFraction by remember { mutableFloatStateOf(-1f) }
    val shownMs = if (dragFraction >= 0) dragFraction * durationMs else positionMs.toFloat()
    val fraction = if (durationMs > 0) (shownMs / durationMs).coerceIn(0f, 1f) else 0f

    Row(
        modifier = Modifier.fillMaxWidth(),
        verticalAlignment = Alignment.CenterVertically,
    ) {
        Text(formatTime(shownMs.toLong()), style = MaterialTheme.typography.labelSmall)
        Slider(
            value = fraction,
            onValueChange = { dragFraction = it },
            onValueChangeFinished = {
                dragFraction.takeIf { it >= 0 }?.let { onSeek((it * durationMs).toLong()) }
                dragFraction = -1f
            },
            modifier = Modifier.weight(1f),
        )
        Text(formatTime(durationMs), style = MaterialTheme.typography.labelSmall)
    }
}

private fun formatTime(ms: Long): String {
    val total = (ms / 1000).coerceAtLeast(0)
    return "%02d:%02d".format(total / 60, total % 60)
}
