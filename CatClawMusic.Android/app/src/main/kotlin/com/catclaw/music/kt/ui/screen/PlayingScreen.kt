package com.catclaw.music.kt.ui.screen

import android.Manifest
import android.content.res.Configuration
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
import androidx.compose.foundation.layout.BoxWithConstraints
import androidx.compose.foundation.layout.fillMaxHeight
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
import androidx.compose.ui.draw.shadow
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.asImageBitmap
import androidx.compose.ui.layout.ContentScale
import androidx.compose.ui.platform.LocalConfiguration
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.res.painterResource
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.Dp
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import androidx.hilt.navigation.compose.hiltViewModel
import androidx.lifecycle.compose.collectAsStateWithLifecycle
import com.catclaw.music.kt.R
import com.catclaw.music.kt.core.lyrics.LyricsState
import com.catclaw.music.kt.core.playback.PlayMode
import com.catclaw.music.kt.ui.library.LibraryViewModel
import com.catclaw.music.kt.ui.lyrics.LyricsViewModel
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
import com.catclaw.music.kt.ui.song.songMenuSubtitle
import com.catclaw.music.kt.ui.theme.LocalCatClawPalette
import com.catclaw.music.kt.ui.theme.artworkWash

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

    // 横屏（含平板 / 折叠屏展开）走另一套形状：见 [LandscapeBody]。这一句要在页面顶层拿到，
    // 因为要减的不只是中栏——抓栏在横屏是多余的一行，去掉它才给歌词行腾出高度。
    val landscape = LocalConfiguration.current.orientation == Configuration.ORIENTATION_LANDSCAPE

    // 页面底色由当前那张封面染出来（[artworkWash]：平均色三段渐变，盖在外壳的主题底之上），
    // 于是封面、歌名、控制区落在同一个色温里。
    // 播放页在浅色主题下也被钉成深色（CatClawRoot.fixedDarkPages），所以这里的白色系前景不会翻成黑字。
    val song = state.currentSong
    val cover = rememberCover(song?.coverArtPath, song?.filePath)

    Column(
        modifier = Modifier
            .fillMaxSize()
            .artworkWash(cover)
            .padding(horizontal = 26.dp),
        horizontalAlignment = Alignment.CenterHorizontally,
    ) {
        Spacer(Modifier.height(14.dp))

        Row(
            modifier = Modifier.fillMaxWidth(),
            verticalAlignment = Alignment.CenterVertically,
        ) {
            // 顶栏收成"左右各一个图标 + 居中标题"：Apple 那一行只有下拉箭头和来源，
            // 我们得留住设置与扫描两个真功能，所以降级成图标而不是两颗胶囊
            Icon(
                painter = painterResource(R.drawable.ic_settings),
                contentDescription = stringResource(R.string.SettingsEntry),
                tint = Color(palette.textSecondary),
                modifier = Modifier
                    .size(44.dp)
                    .clip(CircleShape)
                    .clickable(onClick = onOpenSettings)
                    .padding(11.dp),
            )
            Spacer(Modifier.weight(1f))
            Text(
                text = stringResource(R.string.PageTitleNowPlaying),
                fontSize = 11.5.sp,
                fontWeight = FontWeight.SemiBold,
                letterSpacing = 1.6.sp,
                color = Color(palette.textSecondary),
                maxLines = 1,
            )
            Spacer(Modifier.weight(1f))
            Icon(
                painter = painterResource(R.drawable.ic_refresh),
                contentDescription = stringResource(if (scanning) R.string.library_scanning else R.string.m1_scan_play),
                tint = if (scanning) Color(palette.textSecondary) else Color(palette.textPrimary).copy(alpha = 0.72f),
                modifier = Modifier
                    .size(44.dp)
                    .clip(CircleShape)
                    .clickable(enabled = !scanning) { viewModel.scanAndPlayAll() }
                    .padding(11.dp),
            )
        }

        Spacer(Modifier.height(10.dp))

        // MAUI 的 RootGrid 是 `Auto,*,Auto`：中间（封面+歌词）吃满剩余高度，控制区沉底。
        // 队列移出本页后如果不做这一步，播放页下方会空出一大片
        Box(
            modifier = Modifier
                .fillMaxWidth()
                .weight(1f),
            contentAlignment = Alignment.Center,
        ) {
            BoxWithConstraints(modifier = Modifier.fillMaxSize()) {
                // 封面边长必须同时受「宽度」与「这一栏剩给文字的高度」约束：Column 是顺序分配高度的，
                // 封面先吃满之后下面的文字块会拿到 0 高——**0 高的 Text 一个像素都不画，可它的 clickable
                // 仍然撑出 48dp 触达框**，于是节点树里那行还在、屏幕上却是一条空白（真机量出来的坑）。
                val coverSide = minOf(
                    maxWidth * if (landscape) 0.94f else 0.76f,
                    maxHeight - if (landscape) 0.dp else 190.dp,
                ).coerceAtLeast(150.dp)
                if (landscape) {
                    // 横屏走 MAUI `LandscapeRoot` 的同一形状：左封面 + 右「标题·歌手 / 当前这一句词」
                    LandscapeBody(
                        side = coverSide,
                        cover = cover,
                        placeholder = song?.title?.take(1) ?: "♪",
                        title = state.currentSong?.title,
                        artist = state.currentSong?.artist,
                        error = state.error,
                        positionMs = positionMs,
                    )
                } else {
                    Column(horizontalAlignment = Alignment.CenterHorizontally) {
                        CoverSquare(side = coverSide, cover = cover, placeholder = song?.title?.take(1) ?: "♪")

                        Spacer(Modifier.height(26.dp))
                        // 标题/歌手左对齐到页面边距：Apple 那两行是顶格的，居中会让长歌名断成两行还看不出主次
                        TitleBlock(title = state.currentSong?.title, artist = state.currentSong?.artist, error = state.error)

                        Spacer(Modifier.height(14.dp))
                        // 歌词预览与标题/歌手共用一条左边距（Apple 那三行是同一列），点它横滑进全屏歌词页
                        com.catclaw.music.kt.ui.CurrentLyricLine(onOpenLyrics = onOpenLyrics)
                    }
                }
            }
        }

        ProgressRow(
            positionMs = positionMs,
            durationMs = state.durationMs,
            onSeek = { viewModel.seekTo(it) },
        )

        // 主控制行改成 Apple 那一档：**纯图标、无底色**，中间那颗只比两侧大一号。
        // 原来是「primaryLight 圆底 + 白图标」——那是全 app 最重的一块色块，
        // 和刚铺上的封面染色打架，看上去像两套设计拼在一起
        Row(
            modifier = Modifier.fillMaxWidth().height(78.dp),
            verticalAlignment = Alignment.CenterVertically,
        ) {
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
            Box(
                modifier = Modifier
                    .size(72.dp)
                    .clip(CircleShape)
                    .clickable { viewModel.toggle() },
                contentAlignment = Alignment.Center,
            ) {
                Icon(
                    imageVector = if (state.isPlaying) PlayerIcons.Pause else PlayerIcons.Play,
                    contentDescription = stringResource(R.string.play_or_pause),
                    tint = Color(palette.textPrimary),
                    modifier = Modifier.size(46.dp),
                )
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

        // Apple 的"上滑看更多"提示就是一根小灰棒。这里它不是装饰：点它就是进歌词页，
        // 与下面那行歌词预览同一个动作，两者都真跳得过去。
        // 横屏不画这一行：中栏只有 ~330px 高，这根棒要占掉 68px，而它的功能次级图标行的「词」已经给了
        if (!landscape) {
            Box(
                modifier = Modifier
                    .fillMaxWidth()
                    .height(26.dp)
                    .clickable(onClick = onOpenLyrics),
                contentAlignment = Alignment.Center,
            ) {
                Box(
                    modifier = Modifier
                        .size(width = 40.dp, height = 4.dp)
                        .clip(CircleShape)
                        .background(Color(palette.textPrimary).copy(alpha = 0.26f)),
                )
            }
        }

        // 次要图标行：MAUI 有 6 个（歌词/定时/详情/音效/横屏/更多），
        // 这里放**已实现**的五个——「横屏」在 MAUI 不是一颗按钮，而是窗口转横时换的那套形状
        // （本端就是上面 LandscapeBody 那一支）；安卓的旋转归系统管，摆一颗「切横屏」的图标反而是假控件
        // （本项目的既有判例：宁可不放，也不放不工作的入口）；
        // 「详情」不单独占一格，它收在「更多」里（歌曲信息是歌曲的属性，不是播放页的属性）
        Row(
            modifier = Modifier.fillMaxWidth().padding(top = 2.dp),
            // 五颗图标等距分布：原来是"居中定距 34dp"，第五颗（更多）会被推到屏幕边上贴住，
            // 视觉上像被裁掉（真机 bounds 到 1432/1440）
            horizontalArrangement = Arrangement.SpaceEvenly,
        ) {
            val desktopOn by viewModel.desktopLyricEnabled.collectAsStateWithLifecycle()
            Icon(
                imageVector = PlayerIcons.Lyrics,
                contentDescription = stringResource(R.string.open_lyrics),
                tint = Color(palette.textSecondary),
                modifier = Modifier
                    .size(44.dp)
                    .clickable(onClick = onOpenLyrics)
                    .padding(10.dp),
            )
            Icon(
                imageVector = PlayerIcons.DesktopLyrics,
                contentDescription = stringResource(
                    R.string.lyric_desktop,
                    stringResource(if (desktopOn) R.string.lyric_on else R.string.lyric_off),
                ),
                tint = if (desktopOn) Color(palette.textPrimary) else Color(palette.textSecondary),
                modifier = Modifier
                    .size(44.dp)
                    .clickable {
                        if (!viewModel.toggleDesktopLyric()) {
                            context.startActivity(viewModel.overlayPermissionIntent())
                        }
                    }
                    .padding(10.dp),
            )
            Icon(
                imageVector = PlayerIcons.SoundFx,
                contentDescription = stringResource(R.string.soundfx_entry),
                tint = Color(palette.textSecondary),
                modifier = Modifier
                    .size(44.dp)
                    .clickable(onClick = onOpenSoundFx)
                    .padding(10.dp),
            )
            Icon(
                imageVector = PlayerIcons.Timer,
                contentDescription = if (sleepState.running) {
                    stringResource(R.string.sleep_timer_running_desc, formatCountdown(sleepState.remainingSec))
                } else {
                    stringResource(R.string.sleep_timer_entry)
                },
                // 定时开着要用最高对比点出来：图标是唯一能看见它的地方（弹窗得点开才有）
                tint = if (sleepState.running) Color(palette.textPrimary) else Color(palette.textSecondary),
                modifier = Modifier
                    .size(44.dp)
                    .clickable { showSleepDialog = true }
                    .padding(10.dp),
            )
            // 「更多」只在真有一首歌可动的时候画：没有当前曲目时菜单里每一项都点不动
            state.currentSong?.takeIf { it.id > 0 }?.let {
                Icon(
                    imageVector = PlayerIcons.More,
                    contentDescription = stringResource(R.string.playing_more),
                    tint = Color(palette.textSecondary),
                    modifier = Modifier
                        .size(44.dp)
                        .clickable { songMenuOpen = true }
                        .padding(10.dp),
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
            target = SongMenuTarget(song.id, song.title, songMenuSubtitle(song.artist, song.album), song),
            menu = menu,
            onDismiss = { songMenuOpen = false },
        )
    }
}

/** 播放页的封面方块：竖屏与横屏同一形状（小圆角 + 重投影），只是边长不同 */
@Composable
private fun CoverSquare(side: Dp, cover: android.graphics.Bitmap?, placeholder: String) {
    val palette = LocalCatClawPalette.current
    Box(
        modifier = Modifier
            .size(side)
            // Apple 的封面是"小圆角 + 重投影"，不是描边；投影把封面从染色的环境里抬出来
            .shadow(26.dp, RoundedCornerShape(14.dp))
            .clip(RoundedCornerShape(14.dp))
            .background(Color(palette.textPrimary).copy(alpha = 0.10f)),
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
                text = placeholder,
                fontSize = 64.sp,
                fontWeight = FontWeight.Bold,
                color = Color(palette.textSecondary),
            )
        }
    }
}

/** 标题 / 歌手 / 错误行：两栏布局共用，左对齐到所在栏的边距。[compact] 给横屏——那窗口只有 1440px 高 */
@Composable
private fun TitleBlock(title: String?, artist: String?, error: String?, compact: Boolean = false) {
    val palette = LocalCatClawPalette.current
    Column(modifier = Modifier.fillMaxWidth()) {
        Text(
            text = title ?: stringResource(R.string.m1_nothing),
            fontSize = if (compact) 18.sp else 22.sp,
            fontWeight = FontWeight.Bold,
            color = Color(palette.textPrimary),
            maxLines = 1,
            overflow = TextOverflow.Ellipsis,
        )
        Text(
            text = artist ?: "",
            fontSize = if (compact) 13.sp else 17.sp,
            color = Color(palette.textSecondary),
            maxLines = 1,
            overflow = TextOverflow.Ellipsis,
            modifier = Modifier.padding(top = 3.dp),
        )
        error?.let {
            Text(
                text = it,
                style = MaterialTheme.typography.bodySmall,
                color = Color(palette.textPrimary),
                maxLines = 2,
                overflow = TextOverflow.Ellipsis,
            )
        }
    }
}

/**
 * 横屏（窗口宽 > 高，含平板与折叠屏展开）的播放页中栏：左封面 + 右「标题·歌手 / 当前这一句词」。
 * 形状照 MAUI `NowPlayingPage` 的 `LandscapeRoot`（左封面、右信息），但右栏放的是**一句词而不是整首列表**：
 * 真机量过，1440px 高的横屏窗口里中间那一栏只剩 ~300px，标题块自己就占掉 216px，
 * 塞滚动列表（或带译文/罗马音副行的 [LyricLineText]）会被压成一条看不见的窄缝——
 * 整首词在横滑到的全屏歌词页里本来就有，那一页在横屏是通栏的，比在这里挤一个残破的列表更合适。
 * 歌词数据走与全屏歌词页同一个 [LyricsViewModel]（同一批 Room / 控制器流），点这一句 = seek 回这一句起点。
 */
@Composable
private fun LandscapeBody(
    side: Dp,
    cover: android.graphics.Bitmap?,
    placeholder: String,
    title: String?,
    artist: String?,
    error: String?,
    positionMs: Long,
    lyricsViewModel: LyricsViewModel = hiltViewModel(),
) {
    val palette = LocalCatClawPalette.current
    val lyrics by lyricsViewModel.lyrics.collectAsStateWithLifecycle()
    val lineMode by lyricsViewModel.lineMode.collectAsStateWithLifecycle()
    val presentation = LyricsState.present(
        lyrics = lyricsViewModel.visibleLyrics(),
        positionMs = positionMs,
        lineMode = lineMode,
    )
    val line = presentation?.line?.takeIf { it.text.isNotBlank() }

    Row(
        modifier = Modifier.fillMaxSize(),
        verticalAlignment = Alignment.CenterVertically,
        horizontalArrangement = Arrangement.spacedBy(26.dp),
    ) {
        CoverSquare(side = side, cover = cover, placeholder = placeholder)
        Column(modifier = Modifier.weight(1f)) {
            TitleBlock(title = title, artist = artist, error = error, compact = true)
            Spacer(Modifier.height(6.dp))
            Text(
                text = line?.text ?: stringResource(R.string.lyric_none),
                fontSize = if (line == null) 13.sp else 17.sp,
                fontWeight = if (line == null) FontWeight.Normal else FontWeight.Medium,
                color = if (line == null) Color(palette.textSecondary) else Color(palette.textPrimary),
                maxLines = 1,
                overflow = TextOverflow.Ellipsis,
                modifier = Modifier
                    .fillMaxWidth()
                    .clip(RoundedCornerShape(8.dp))
                    .clickable(enabled = line != null) { line?.let { lyricsViewModel.seekTo(it.startMs) } }
                    .padding(vertical = 4.dp),
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
            .size(44.dp)
            .clickable(onClick = onClick),
        contentAlignment = Alignment.Center,
    ) {
        Icon(imageVector = icon, contentDescription = description, tint = tint, modifier = Modifier.size(22.dp))
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
            .size(56.dp)
            .clickable(onClick = onClick),
        contentAlignment = Alignment.Center,
    ) {
        Icon(imageVector = icon, contentDescription = description, tint = tint, modifier = Modifier.size(32.dp))
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
@OptIn(androidx.compose.material3.ExperimentalMaterial3Api::class)
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
            fontSize = 11.5.sp,
            color = Color(palette.textSecondary),
        )
        // Apple 的进度条是 3dp 细线 + 白色已播 + 小圆点，滑块只在拖动时才明显。
        // 这里保留 Slider 的语义（拖动期间只显本地值、松手才 seek），只把视觉换成那一档：
        // 换掉控件会丢回写规则，换 track/thumb 插槽不会
        Slider(
            value = fraction,
            onValueChange = { dragFraction = it },
            onValueChangeFinished = {
                dragFraction.takeIf { it >= 0 }?.let { onSeek((it * durationMs).toLong()) }
                dragFraction = -1f
            },
            modifier = Modifier.weight(1f).padding(horizontal = 10.dp),
            colors = SliderDefaults.colors(
                thumbColor = Color(palette.textPrimary),
                activeTrackColor = Color(palette.textPrimary),
                inactiveTrackColor = Color(palette.textPrimary).copy(alpha = 0.22f),
            ),
            track = {
                Box(
                    modifier = Modifier
                        .fillMaxWidth()
                        .height(3.dp)
                        .clip(CircleShape)
                        .background(Color(palette.textPrimary).copy(alpha = 0.22f)),
                ) {
                    Box(
                        modifier = Modifier
                            .fillMaxWidth(fraction.coerceIn(0f, 1f))
                            .fillMaxHeight()
                            .background(Color(palette.textPrimary)),
                    )
                }
            },
            thumb = {
                Box(
                    modifier = Modifier
                        .size(11.dp)
                        .clip(CircleShape)
                        .background(Color(palette.textPrimary)),
                )
            },
        )
        Text(
            text = formatTime(durationMs),
            fontSize = 11.5.sp,
            color = Color(palette.textSecondary),
        )
    }
}

private fun formatTime(ms: Long): String {
    val total = (ms / 1000).coerceAtLeast(0)
    return "%02d:%02d".format(total / 60, total % 60)
}
