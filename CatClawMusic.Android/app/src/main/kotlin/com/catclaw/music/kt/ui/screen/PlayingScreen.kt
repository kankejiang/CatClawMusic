package com.catclaw.music.kt.ui.screen

import android.Manifest
import android.content.pm.PackageManager
import android.os.Build
import androidx.activity.compose.BackHandler
import androidx.activity.compose.rememberLauncherForActivityResult
import androidx.activity.result.contract.ActivityResultContracts
import androidx.core.content.ContextCompat
import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.Image
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
import androidx.compose.material3.Icon
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Slider
import androidx.compose.material3.SliderDefaults
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableFloatStateOf
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.asImageBitmap
import androidx.compose.ui.layout.ContentScale
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import androidx.hilt.navigation.compose.hiltViewModel
import androidx.lifecycle.compose.collectAsStateWithLifecycle
import com.catclaw.music.kt.R
import com.catclaw.music.kt.core.model.Song
import com.catclaw.music.kt.core.playback.PlayMode
import com.catclaw.music.kt.ui.library.LibraryViewModel
import com.catclaw.music.kt.ui.playback.PlayingViewModel
import com.catclaw.music.kt.ui.player.Icon
import com.catclaw.music.kt.ui.player.PlayerIcon
import com.catclaw.music.kt.ui.player.PlayerIcons
import com.catclaw.music.kt.ui.player.SleepTimerDialog
import com.catclaw.music.kt.ui.player.formatCountdown
import com.catclaw.music.kt.ui.player.rememberCover
import com.catclaw.music.kt.ui.song.SongDetailScreen
import com.catclaw.music.kt.ui.song.SongMenuFor
import com.catclaw.music.kt.ui.song.SongMenuTarget
import com.catclaw.music.kt.ui.song.rememberSongMenu
import com.catclaw.music.kt.ui.theme.LocalCatClawPalette
import com.catclaw.music.kt.ui.theme.PillLabel
import com.catclaw.music.kt.ui.theme.inkOn

@Composable
fun PlayingScreen(
    onOpenLyrics: () -> Unit = {},
    onOpenSettings: () -> Unit = {},
    onOpenQueue: () -> Unit = {},
    onOpenSoundFx: () -> Unit = {},
    viewModel: PlayingViewModel = hiltViewModel(),
) {
    val state by viewModel.state.collectAsStateWithLifecycle()
    val positionMs by viewModel.positionMs.collectAsStateWithLifecycle()
    val scanning by viewModel.busy.collectAsStateWithLifecycle()
    val lastScan by viewModel.lastScan.collectAsStateWithLifecycle()
    // 定时跑在播放器那一侧，这里只是显示与开关；关掉这页它也该继续
    val sleepState by viewModel.sleep.collectAsStateWithLifecycle()
    var showSleepDialog by remember { mutableStateOf(false) }
    val palette = LocalCatClawPalette.current
    val context = LocalContext.current
    // 「更多」菜单要的能力（收藏/加入歌单/下一首/详情/歌手·专辑）与列表页是同一份，
    // 都来自 LibraryViewModel；两个 VM 读的是同一批 Room 单例流，不会因为多拿一个 VM 而出现两份状态
    val libraryViewModel: LibraryViewModel = hiltViewModel()
    var songMenuOpen by remember { mutableStateOf(false) }
    var songDetailId by remember { mutableStateOf<Int?>(null) }
    // 歌手页/专辑页由播放页自己托管（与歌单页同一个做法）：MAUI 的「更多」有这两项，
    // 本端要么真跳得过去，要么不画，不留一个点了不动的入口
    var belongsDetail by remember { mutableStateOf<LibraryDetail?>(null) }
    val menu = rememberSongMenu(
        viewModel = libraryViewModel,
        onOpenBelongs = { belongsDetail = it.toLibraryDetail() },
        onOpenDetail = { songDetailId = it },
    )
    // 两层都画了自己的返回方块，系统返回键也必须退它们。组合在后的那条优先，
    // 所以按「先下层、后上层」排：两层同时开着时先退最上面那层
    BackHandler(enabled = belongsDetail != null) { belongsDetail = null }
    BackHandler(enabled = songDetailId != null) { songDetailId = null }

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

    // 整屏层沿用音乐库/歌单页的做法：直接顶掉本页内容而不是再叠一层 Box——
    // 外壳已经把渐变底与状态栏内边距铺好了，页面里再画一遍就是两层底
    songDetailId?.let { id ->
        SongDetailScreen(songId = id, load = { libraryViewModel.songDetail(it) }, onBack = { songDetailId = null })
        return
    }
    belongsDetail?.let { detail ->
        LibraryDetailScreen(
            detail = detail,
            onBack = { belongsDetail = null },
            onOpenSong = { songDetailId = it },
            onOpenDetail = { belongsDetail = it },
            viewModel = libraryViewModel,
        )
        return
    }

    Column(
        modifier = Modifier
            .fillMaxSize()
            .padding(horizontal = 20.dp),
        horizontalAlignment = Alignment.CenterHorizontally,
    ) {
        Spacer(Modifier.height(16.dp))

        Row(
            modifier = Modifier.fillMaxWidth(),
            verticalAlignment = Alignment.CenterVertically,
            horizontalArrangement = Arrangement.spacedBy(10.dp),
        ) {
            Text(
                text = stringResource(R.string.PageTitleNowPlaying),
                fontSize = 22.sp,
                fontWeight = FontWeight.Bold,
                color = Color(palette.textPrimary),
                modifier = Modifier.weight(1f),
                maxLines = 1,
                overflow = TextOverflow.Ellipsis,
            )
            PillLabel(
                text = stringResource(R.string.SettingsEntry),
                onClick = onOpenSettings,
                filled = false,
            )
            PillLabel(
                text = stringResource(if (scanning) R.string.library_scanning else R.string.m1_scan_play),
                onClick = { viewModel.scanAndPlayAll() },
                enabled = !scanning,
            )
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
                val song = state.currentSong
                val cover = rememberCover(song?.coverArtPath, song?.filePath)
                Box(
                    modifier = Modifier
                        .fillMaxWidth(0.62f)
                        .aspectRatio(1f)
                        .clip(RoundedCornerShape(26.dp))
                        .background(Color(palette.primaryLight))
                        .border(1.dp, Color(palette.textPrimary).copy(alpha = 0.16f), RoundedCornerShape(26.dp)),
                    contentAlignment = Alignment.Center,
                ) {
                    if (cover != null) {
                        Image(
                            bitmap = cover.asImageBitmap(),
                            contentDescription = null,
                            contentScale = ContentScale.Crop,
                            modifier = Modifier.matchParentSize(),
                        )
                    } else {
                        Text(
                            text = song?.title?.take(1) ?: "♪",
                            fontSize = 64.sp,
                            fontWeight = FontWeight.Bold,
                            color = Color(inkOn(palette.primaryLight)),
                        )
                    }
                }

                Spacer(Modifier.height(20.dp))
                Text(
                    text = state.currentSong?.title ?: stringResource(R.string.m1_nothing),
                    fontSize = 19.sp,
                    fontWeight = FontWeight.SemiBold,
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
                    // 这行落在页面渐变底上，不在封面那块 primaryLight 面里，取墨色才会翻成人眼看得见的
                    Text(
                        text = it,
                        style = MaterialTheme.typography.bodySmall,
                        color = Color(palette.textPrimary),
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
                    description = stringResource(R.string.mode_change) + "：" + stringResource(modeLabelRes(state.mode)),
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
                        tint = Color(inkOn(palette.primaryLight)),
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
        // 这里放**已实现**的五个——横屏在安卓侧还没有功能，
        // 摆上去就是假控件（本项目的既有判例：宁可不放，也不放不工作的入口）；
        // 「详情」不单独占一格，它收在「更多」里（歌曲信息是歌曲的属性，不是播放页的属性）
        Row(
            modifier = Modifier.fillMaxWidth().padding(top = 6.dp),
            // MAUI 是 6 等分槽位；图标不满，用 weight 会推到两端显得很空，改成居中定距
            horizontalArrangement = Arrangement.spacedBy(34.dp, Alignment.CenterHorizontally),
        ) {
            val desktopOn by viewModel.desktopLyricEnabled.collectAsStateWithLifecycle()
            Icon(
                imageVector = PlayerIcons.Lyrics,
                contentDescription = stringResource(R.string.open_lyrics),
                tint = Color(palette.primary),
                modifier = Modifier
                    .size(49.dp)
                    .clickable(onClick = onOpenLyrics)
                    .padding(8.dp),
            )
            Icon(
                imageVector = PlayerIcons.DesktopLyrics,
                contentDescription = stringResource(
                    R.string.lyric_desktop,
                    stringResource(if (desktopOn) R.string.lyric_on else R.string.lyric_off),
                ),
                tint = if (desktopOn) Color(palette.primary) else Color(palette.textSecondary),
                modifier = Modifier
                    .size(49.dp)
                    .clickable {
                        if (!viewModel.toggleDesktopLyric()) {
                            context.startActivity(viewModel.overlayPermissionIntent())
                        }
                    },
            )
            Icon(
                imageVector = PlayerIcons.SoundFx,
                contentDescription = stringResource(R.string.soundfx_entry),
                tint = Color(palette.textSecondary),
                modifier = Modifier
                    .size(49.dp)
                    .clickable(onClick = onOpenSoundFx)
                    .padding(8.dp),
            )
            Icon(
                imageVector = PlayerIcons.Timer,
                contentDescription = if (sleepState.running) {
                    stringResource(R.string.sleep_timer_running_desc, formatCountdown(sleepState.remainingSec))
                } else {
                    stringResource(R.string.sleep_timer_entry)
                },
                // 定时开着要用主色点出来：图标是唯一能看见它的地方（弹窗得点开才有）
                tint = if (sleepState.running) Color(palette.primary) else Color(palette.textSecondary),
                modifier = Modifier
                    .size(49.dp)
                    .clickable { showSleepDialog = true }
                    .padding(8.dp),
            )
            // 「更多」只在真有一首歌可动的时候画：没有当前曲目时菜单里每一项都点不动
            state.currentSong?.takeIf { it.id > 0 }?.let {
                Icon(
                    imageVector = PlayerIcons.More,
                    contentDescription = stringResource(R.string.playing_more),
                    tint = Color(palette.textSecondary),
                    modifier = Modifier
                        .size(49.dp)
                        .clickable { songMenuOpen = true }
                        .padding(8.dp),
                )
            }
        }
        if (showSleepDialog) {
            SleepTimerDialog(
                sleep = sleepState,
                onStart = { minutes, stopAfterSong, fadeOut ->
                    viewModel.startSleepTimer(minutes, stopAfterSong, fadeOut)
                },
                onCancel = { viewModel.cancelSleepTimer() },
                onDismiss = { showSleepDialog = false },
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

    // 菜单指向当前曲目。用 takeIf 而不是 `if (songMenuOpen)`：菜单项点完即 dismiss，
    // 目标歌在这一刻取，滑到别的歌上时不会把菜单留给上一首
    state.currentSong?.takeIf { songMenuOpen && it.id > 0 }?.let { song ->
        SongMenuFor(
            target = SongMenuTarget(song.id, song.title, menuSubtitle(song), song),
            menu = menu,
            onDismiss = { songMenuOpen = false },
        )
    }
}

/** 菜单第二行：歌手 · 专辑，缺一段就少一段，不留「· 」这种断口 */
private fun menuSubtitle(song: Song): String =
    listOf(song.artist, song.album).filter { it.isNotBlank() }.joinToString(" · ")

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

/** 播报当前档位而不是只说「切换播放模式」，否则读屏听到的永远是同一句 */
private fun modeLabelRes(mode: PlayMode): Int = when (mode) {
    PlayMode.Sequential -> R.string.mode_sequential
    PlayMode.Shuffle -> R.string.mode_shuffle
    PlayMode.SingleRepeat -> R.string.mode_single
    PlayMode.ListRepeat -> R.string.mode_list
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
    // 播放页在浅色主题下也被钉成深色（fixedDarkPages），所以这里不能沿用环境色 ——
    // 沿用会让时间文字拿到浅色方案的黑色，落在深底上直接看不见
    val palette = LocalCatClawPalette.current

    Row(
        modifier = Modifier.fillMaxWidth(),
        verticalAlignment = Alignment.CenterVertically,
    ) {
        Text(
            text = formatTime(shownMs.toLong()),
            style = MaterialTheme.typography.labelSmall,
            color = Color(palette.textSecondary),
        )
        Slider(
            value = fraction,
            onValueChange = { dragFraction = it },
            onValueChangeFinished = {
                dragFraction.takeIf { it >= 0 }?.let { onSeek((it * durationMs).toLong()) }
                dragFraction = -1f
            },
            modifier = Modifier.weight(1f),
            colors = SliderDefaults.colors(
                thumbColor = Color(palette.primary),
                activeTrackColor = Color(palette.primary),
                inactiveTrackColor = Color(palette.textPrimary).copy(alpha = 0.18f),
            ),
        )
        Text(
            text = formatTime(durationMs),
            style = MaterialTheme.typography.labelSmall,
            color = Color(palette.textSecondary),
        )
    }
}

private fun formatTime(ms: Long): String {
    val total = (ms / 1000).coerceAtLeast(0)
    return "%02d:%02d".format(total / 60, total % 60)
}
