package com.catclaw.music.kt.ui.screen

import android.Manifest
import android.content.res.Configuration
import androidx.annotation.StringRes
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
import androidx.compose.foundation.lazy.itemsIndexed
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
import androidx.compose.foundation.layout.statusBarsPadding
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.gestures.detectHorizontalDragGestures
import androidx.compose.foundation.gestures.detectTapGestures
import androidx.compose.ui.draw.drawBehind
import androidx.compose.ui.geometry.CornerRadius
import androidx.compose.ui.geometry.Offset
import androidx.compose.ui.geometry.Size
import androidx.compose.ui.input.pointer.pointerInput
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material3.Icon
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Slider
import androidx.compose.material3.SliderDefaults
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.DisposableEffect
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableFloatStateOf
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.draw.clipToBounds
import androidx.compose.ui.draw.shadow
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.graphicsLayer
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
import kotlinx.coroutines.flow.StateFlow
import androidx.lifecycle.compose.collectAsStateWithLifecycle
import com.catclaw.music.kt.R
import com.catclaw.music.kt.ui.LocalSleepTimerOpener
import com.catclaw.music.kt.core.lyrics.LyricsState
import com.catclaw.music.kt.core.playback.PlayMode
import com.catclaw.music.kt.ui.library.LibraryViewModel
import com.catclaw.music.kt.ui.lyrics.PlayingLyricLine
import com.catclaw.music.kt.ui.lyrics.LyricsViewModel
import com.catclaw.music.kt.ui.playback.PlayingViewModel
import com.catclaw.music.kt.ui.player.Icon
import com.catclaw.music.kt.ui.player.PlayerIcon
import com.catclaw.music.kt.ui.player.PlayerIcons
import com.catclaw.music.kt.ui.player.SleepTimerDialog
import com.catclaw.music.kt.ui.player.rememberCover
import com.catclaw.music.kt.ui.song.SongDetailScreen
import com.catclaw.music.kt.ui.player.formatCountdown
import com.catclaw.music.kt.ui.song.SongMenuFor
import com.catclaw.music.kt.ui.song.SongMenuItem
import com.catclaw.music.kt.ui.song.SongMenuSection
import com.catclaw.music.kt.ui.song.SongMenuTarget
import com.catclaw.music.kt.ui.song.rememberSongMenu
import com.catclaw.music.kt.ui.song.songMenuSubtitle
import com.catclaw.music.kt.ui.theme.LocalCatClawPalette
import com.catclaw.music.kt.ui.theme.glassCard
import com.catclaw.music.kt.ui.theme.highlightOnGlassCard
import com.catclaw.music.kt.ui.theme.artworkBackdrop
import com.catclaw.music.kt.ui.theme.VinylCover
import com.catclaw.music.kt.ui.theme.artworkWash

@Composable
fun PlayingScreen(
    onOpenLyrics: () -> Unit = {},
    onOpenSettings: () -> Unit = {},
    onOpenQueue: () -> Unit = {},
    onOpenSoundFx: () -> Unit = {},
    onCollapse: () -> Unit = {},
    /** 是否处于「手动横屏」锁定（图标据此点亮）。状态归外壳所有：离开本页要自动解锁 */
    manualLandscape: Boolean = false,
    onToggleLandscape: () -> Unit = {},
    viewModel: PlayingViewModel = hiltViewModel(),
    neteaseViewModel: com.catclaw.music.kt.ui.netease.NeteaseMusicViewModel = hiltViewModel(),
) {
    val state by viewModel.state.collectAsStateWithLifecycle()
    val fmMode by neteaseViewModel.radioMode.collectAsStateWithLifecycle()
    var fmPickerOpen by remember { mutableStateOf(false) }
    val neteaseLikedIds by viewModel.neteaseLikedIds.collectAsStateWithLifecycle()
    val neteaseHint by viewModel.neteaseHint.collectAsStateWithLifecycle()
    val lastScan by viewModel.lastScan.collectAsStateWithLifecycle()
    val palette = LocalCatClawPalette.current
    val context = LocalContext.current
    LaunchedEffect(neteaseHint) {
        neteaseHint?.let {
            android.widget.Toast.makeText(context, it, android.widget.Toast.LENGTH_SHORT).show()
            viewModel.consumeNeteaseHint()
        }
    }
    // 「更多」菜单要的能力（收藏/加入歌单/下一首/详情/歌手·专辑）与列表页是同一份，
    // 都来自 LibraryViewModel；两个 VM 读的是同一批 Room 单例流，不会因为多拿一个 VM 而出现两份状态
    val libraryViewModel: LibraryViewModel = hiltViewModel()
    // 位置流的提频由**外壳**按 settledPage 置位（见 CatClawRoot 里的注释）：
    // 页面是预组装的，自己置位会让旁边那页也跟着 60fps 轮询。
    var songMenuOpen by remember { mutableStateOf(false) }
    var songDetailId by remember { mutableStateOf<Int?>(null) }
    // 歌手页/专辑页由播放页自己托管（与歌单页同一个做法）：MAUI 的「更多」有这两项，
    // 本端要么真跳得过去，要么不画，不留一个点了不动的入口
    var belongsDetail by remember { mutableStateOf<LibraryDetail?>(null) }
    // 睡眠定时弹窗的开关。内核（SleepTimerRules）与弹窗本身早就写好了，
    // 但此前全仓库没有任何一处调用点 —— 定时器在跑，用户却打不开它
    val menu = rememberSongMenu(
        viewModel = libraryViewModel,
        onOpenBelongs = { belongsDetail = it.toLibraryDetail() },
        onOpenDetail = { songDetailId = it },
    )
    // 网易云曲红心只写「我喜欢的音乐」（服务器），本地曲才写本地 Favorites。
    // 两套体系互不串台，播放页 / 列表页读同一个单例镜像。
    fun favoriteOf(remoteId: String?, songId: Int?): Boolean {
        val netease = remoteId?.takeIf { it.startsWith("netease:") }?.removePrefix("netease:")
        return if (netease != null) netease in neteaseLikedIds
        else songId != null && songId in menu.favoriteIds
    }
    fun toggleFavoriteOf(remoteId: String?, songId: Int?) {
        val netease = remoteId?.takeIf { it.startsWith("netease:") }?.removePrefix("netease:")
        if (netease != null) viewModel.toggleNeteaseLike(netease)
        else if (songId != null) menu.onToggleFavorite(songId)
    }
    val sleep by viewModel.sleep.collectAsStateWithLifecycle()
    val desktopLyricOn by viewModel.desktopLyricEnabled.collectAsStateWithLifecycle()
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
        SongDetailScreen(
            songId = id,
            load = { libraryViewModel.songDetail(it) },
            loadLyrics = { libraryViewModel.lyricsFor(it) },
            onPlay = { libraryViewModel.playSongById(it) },
            onBack = { songDetailId = null },
        )
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
    // 定时弹窗由壳层渲染（录制层外），这里只拿到「打开」这一个动作
    val openSleepTimer = LocalSleepTimerOpener.current
    val landscape = LocalConfiguration.current.orientation == Configuration.ORIENTATION_LANDSCAPE

    // 页面底 = 当前这张封面本身（[artworkBackdrop]：全屏 AspectFill + 上下压暗，网易云「一起听」那套），
    // 竖屏不再画居中的封面方块——封面已经占满全屏，再画一个就是重复。
    // 播放页在浅色主题下也被钉成深色（CatClawRoot.fixedDarkPages），所以这里的白色系前景不会翻成黑字。
    val song = state.currentSong
    // 全屏铺满需要比列表行更高的分辨率，别再用 512 的默认值拉成糊图
    val cover = rememberCover(song?.coverArtPath, song?.filePath, maxPx = 1440)

    Column(
        modifier = Modifier
            .fillMaxSize()
            // 有封面：网易云那套「原图占上半屏 + 下缘雾化 + 均色底」；没封面：退回 Apple 的染色渐变。
            // clipToBounds 必须：AspectFill 的图按高度缩放后比页宽宽 880px，不裁剪会画进
            // HorizontalPager 的相邻页（真机：歌词页右缘/发现页左缘各露一条 48px 的白边）
            .clipToBounds()
            // 横屏不铺满封面底：横屏画幅宽，整屏都是专辑图细节会把文字淹没
            // （真机实测：歌词与标题直接糊在专辑图里）。横屏只留外壳的主题渐变 + 左栏方块封面。
            .then(
                when {
                    landscape -> Modifier
                    cover != null -> Modifier.artworkBackdrop(cover)
                    else -> Modifier.artworkWash(cover)
                },
            )
            // 封面铺满整个屏幕（含状态栏区域），内容再从状态栏下方排
            .statusBarsPadding()
            .padding(horizontal = if (landscape) 0.dp else 26.dp),
        horizontalAlignment = Alignment.CenterHorizontally,
    ) {
        // 横屏整页交给 LandscapeNowPlaying（MAUI LandscapeRoot 的 1:1 移植）。
        // 放在这里而不是替换中间那块 Box：横屏的进度条/控制行也要跟着换形状，
        // 只换中间会在竖屏骨架里留下两条竖屏控件。
        if (landscape) {
            LandscapeNowPlaying(
                cover = cover,
                title = state.currentSong?.title,
                artist = state.currentSong?.artist,
                placeholder = song?.title?.take(1) ?: "♪",
                isPlaying = state.isPlaying,
                mode = state.mode,
                isFmMode = state.isFmMode,
                durationMs = state.durationMs,
                positionMsFlow = viewModel.positionMs,
                favorite = favoriteOf(state.currentSong?.remoteId, state.currentSong?.id),
                onToggle = { viewModel.toggle() },
                onNext = { viewModel.next() },
                onPrevious = { viewModel.previous() },
                onCycleMode = { if (state.isFmMode) fmPickerOpen = true else viewModel.cycleMode() },
                onSeek = { viewModel.seekTo(it) },
                onToggleFavorite = { toggleFavoriteOf(state.currentSong?.remoteId, state.currentSong?.id) },
                onOpenQueue = onOpenQueue,
                onSleepTimer = openSleepTimer,
                onMore = { songMenuOpen = true },
                onCollapse = onCollapse,
                palette = palette,
            )
        } else {
        Spacer(Modifier.height(12.dp))
        // 顶部：歌名/歌手在左上角（雾带里），右上角一颗「收起」（⌄ 回发现页）
        Row(
            modifier = Modifier.fillMaxWidth(),
            verticalAlignment = Alignment.CenterVertically,
        ) {
            Column(modifier = Modifier.weight(1f)) {
                // 官方私人漫游：顶部「私人漫游 · 默认模式 ⌄」，点开切模式
                if (state.isFmMode) {
                    Row(
                        modifier = Modifier
                            .clip(RoundedCornerShape(999.dp))
                            .clickable { fmPickerOpen = true }
                            .padding(horizontal = 8.dp, vertical = 3.dp),
                        verticalAlignment = Alignment.CenterVertically,
                        horizontalArrangement = Arrangement.spacedBy(4.dp),
                    ) {
                        Text(
                            text = "私人漫游 · ${fmMode.label}",
                            fontSize = 12.sp,
                            color = Color(highlightOnGlassCard(palette)),
                        )
                        Text("⌄", fontSize = 12.sp, color = Color(highlightOnGlassCard(palette)))
                    }
                    Spacer(Modifier.height(2.dp))
                }
                TitleBlock(title = state.currentSong?.title, artist = state.currentSong?.artist, error = state.error)
            }
            Spacer(Modifier.width(8.dp))
            // 官方右上 ↻：换一批
            if (state.isFmMode) {
                Icon(
                    painter = painterResource(R.drawable.ic_refresh),
                    contentDescription = "换一批",
                    tint = Color(palette.textPrimary),
                    modifier = Modifier
                        .size(44.dp)
                        .clip(CircleShape)
                        .clickable { neteaseViewModel.refreshRadio() }
                        .padding(10.dp),
                )
            }
            Icon(
                painter = painterResource(R.drawable.ic_collapse),
                contentDescription = stringResource(R.string.player_collapse),
                tint = Color(palette.textPrimary),
                modifier = Modifier
                    .size(48.dp)
                    .clip(CircleShape)
                    .clickable(onClick = onCollapse)
                    .padding(10.dp),
            )
        }
        Spacer(Modifier.height(2.dp))

        // MAUI 的 RootGrid 是 `Auto,*,Auto`：中间（歌词预览）吃满剩余高度，控制区沉底。
        // 队列移出本页后如果不做这一步，播放页下方会空出一大片。
        // 竖屏内容沉底：封面占了上半屏，标题要落在雾化结束后的底色带上（网易云同款位置）
        Box(
            modifier = Modifier
                .fillMaxWidth()
                .weight(1f),
            contentAlignment = if (landscape) Alignment.Center else Alignment.BottomCenter,
        ) {
            BoxWithConstraints(modifier = Modifier.fillMaxSize()) {
                // 封面边长同时受「宽度」与「这一栏的高度」约束，只喂给横屏（竖屏封面已是全屏背景）
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
                        positionMsFlow = viewModel.positionMs,
                    )
                } else if (cover == null) {
                    // 没封面：把**唱片碟放大居中**摆在这块区域中央，而不是沉底放一个小方块。
                    // 有封面的歌，这整片是被封面铺满的（`artworkBackdrop` 全屏背景）；
                    // 没封面时原来只在底部摆个小方块，上方一大片只剩渐变 —— 整页「下重上空」。
                    // 2026-10-19 用户问「左右对称了，那上下呢」，问的就是这里。
                    //
                    // 尺寸取 0.62 宽（比原来沉底时的 0.76 小一号）：它现在是页面的**主体**，
                    // 四周要留出呼吸位；正方形 + 居中，唱片在方块内天然两个方向都居中。
                    VinylCover(
                        modifier = Modifier
                            .align(Alignment.Center)
                            .fillMaxWidth(0.62f)
                            .aspectRatio(1f)
                            .shadow(26.dp, RoundedCornerShape(14.dp)),
                        primary = Color(palette.primary),
                    )
                }
            }
        }

        // 歌词 + 收藏/更多同一排，进度条在这排下面：
        // 2026-10-18 真机要求「歌词改小成图标那么大、靠左、放在进度条上方」——
        // 单独占一行时它又大又空，和这排图标并排后高度自然对齐，左字右图标读起来也顺。
        // 收藏态从 menu.favoriteIds 读 —— 与列表页同一个来源；
        // 定时关闭 / 音效 / 桌面歌词 / 横竖屏四颗已收进「更多」菜单（它们只能靠「点亮」表达状态，
        // 菜单里每行都能把状态写成字）
        Row(
            modifier = Modifier.fillMaxWidth(),
            verticalAlignment = Alignment.CenterVertically,
        ) {
            // 左：当前这一句词，点它进全屏歌词页（那一页才有整首词与点词 seek）
            PlayingLyricLine(
                onOpenLyrics = onOpenLyrics,
                modifier = Modifier.weight(1f),
            )
            Spacer(Modifier.width(8.dp))
            val current = state.currentSong
            val favorite = favoriteOf(current?.remoteId, current?.id)
            // 定时关闭 / 音效 / 桌面歌词 / 横竖屏 四颗图标已收进「更多」菜单（见下方 sections）：
            // 挤在角上时它们只能靠「点亮」表达状态，而菜单里每一行都能把状态写成字
            Icon(
                painter = painterResource(
                    if (favorite) R.drawable.ic_favorite else R.drawable.ic_favorite_border,
                ),
                contentDescription = stringResource(
                    if (favorite) R.string.favorite_remove else R.string.favorite_add,
                ),
                tint = if (favorite) Color(palette.textPrimary) else Color(palette.textSecondary),
                modifier = Modifier
                    .size(40.dp)
                    .clip(CircleShape)
                    .clickable(enabled = current != null) { toggleFavoriteOf(current?.remoteId, current?.id) }
                    .padding(9.dp),
            )
            Spacer(Modifier.width(6.dp))
            // 「更多」只在真有一首歌可动的时候画：没有当前曲目时菜单里每一项都点不动
            if (current != null) {
                Icon(
                    imageVector = PlayerIcons.More,
                    contentDescription = stringResource(R.string.playing_more),
                    tint = Color(palette.textSecondary),
                    modifier = Modifier
                        .size(40.dp)
                        .clip(CircleShape)
                        .clickable { songMenuOpen = true }
                        .padding(9.dp),
                )
            }
        }

        ProgressRow(
            positionMsFlow = viewModel.positionMs,
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
                    icon = modeIcon(state.mode, state.isFmMode),
                    description = stringResource(R.string.mode_change) + "：" + stringResource(modeLabelRes(state.mode, state.isFmMode)),
                    tint = Color(palette.textSecondary),
                    onClick = { if (state.isFmMode) fmPickerOpen = true else viewModel.cycleMode() },
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

        // Apple 的"上滑看更多"小灰棒也暂时摘掉（2026-10-01 用户要求，恢复看 git 历史）；
        // 进歌词页的入口还剩歌词预览行一处

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

    // 菜单指向当前曲目。用 takeIf 而不是 `if (songMenuOpen)`：菜单项点完即 dismiss，
    // 目标歌在这一刻取，滑到别的歌上时不会把菜单留给上一首
    state.currentSong?.takeIf { songMenuOpen && it.id > 0 }?.let { song ->
        SongMenuFor(
            // isCurrent = true：这是「正在播放的这首」的菜单，不摆「下一首播放」
            target = SongMenuTarget(song.id, song.title, songMenuSubtitle(song.artist, song.album), song, isCurrent = true),
            menu = menu,
            // 播放页专属的一节。每项都点完即关菜单（与内置那些项一致），
            // 所以开弹窗的那两项自己把 songMenuOpen 收掉
            sections = listOf(
                SongMenuSection(
                    title = stringResource(R.string.song_menu_section_play),
                    items = listOf(
                        SongMenuItem(
                            label = stringResource(R.string.sleep_timer_title),
                            icon = R.drawable.ic_timer,
                            tileColor = palette.primary,
                            value = if (sleep.running) {
                                stringResource(R.string.menu_timer_remaining_short, formatCountdown(sleep.remainingSec))
                            } else {
                                null
                            },
                            onClick = {
                                songMenuOpen = false
                                openSleepTimer()
                            },
                        ),
                        SongMenuItem(
                            label = stringResource(R.string.soundfx_title),
                            icon = R.drawable.ic_equalizer,
                            tileColor = palette.accent,
                            onClick = {
                                songMenuOpen = false
                                onOpenSoundFx()
                            },
                        ),
                        SongMenuItem(
                            label = stringResource(R.string.PageTitleDesktopLyric),
                            icon = R.drawable.ic_lyrics,
                            tileColor = palette.primary,
                            value = stringResource(if (desktopLyricOn) R.string.sleep_timer_on else R.string.sleep_timer_off),
                            onClick = {
                                songMenuOpen = false
                                // 没授悬浮窗权限时跳系统授权页（与设置页同一做法）
                                if (!viewModel.toggleDesktopLyric()) {
                                    context.startActivity(viewModel.overlayPermissionIntent())
                                }
                            },
                        ),
                        SongMenuItem(
                            label = stringResource(R.string.playing_rotate),
                            icon = R.drawable.ic_screen_rotation,
                            tileColor = palette.accent,
                            value = if (manualLandscape) stringResource(R.string.menu_landscape_on) else null,
                            onClick = {
                                songMenuOpen = false
                                onToggleLandscape()
                            },
                        ),
                    ),
                ),
            ),
            onDismiss = { songMenuOpen = false },
        )
    }
    if (fmPickerOpen) {
        FmModePickerDialog(
            current = fmMode,
            onPick = {
                neteaseViewModel.setRadioMode(it)
                fmPickerOpen = false
            },
            onDismiss = { fmPickerOpen = false },
        )
    }
}

/** 播放页的封面方块：竖屏与横屏同一形状（小圆角 + 重投影），只是边长不同 */

/** FM 推荐模式选择（插件 FmModeSelectPage 的 3 档简版） */
@Composable
private fun FmModePickerDialog(
    current: com.catclaw.music.kt.data.repository.netease.FmMode,
    onPick: (com.catclaw.music.kt.data.repository.netease.FmMode) -> Unit,
    onDismiss: () -> Unit,
) {
    val palette = LocalCatClawPalette.current
    val primary = Color(palette.textPrimary)
    val secondary = Color(palette.textSecondary)
    androidx.compose.ui.window.Dialog(onDismissRequest = onDismiss) {
        Column(
            modifier = Modifier
                .fillMaxWidth()
                .clip(RoundedCornerShape(24.dp))
                .background(Color(palette.backgroundStops.last().argb).copy(alpha = 0.96f))
                .glassCard(palette = palette, corner = 24)
                .padding(18.dp),
            verticalArrangement = Arrangement.spacedBy(6.dp),
        ) {
            Text("私人漫游模式", fontSize = 18.sp, fontWeight = FontWeight.Bold, color = primary)
            Text("切换后重新加载电台", fontSize = 11.5.sp, color = secondary)
            com.catclaw.music.kt.data.repository.netease.FmMode.entries.forEach { mode ->
                Row(
                    modifier = Modifier
                        .fillMaxWidth()
                        .clip(RoundedCornerShape(14.dp))
                        .clickable { onPick(mode) }
                        .padding(horizontal = 12.dp, vertical = 12.dp),
                    verticalAlignment = Alignment.CenterVertically,
                ) {
                    Column(Modifier.weight(1f)) {
                        Text(mode.label, fontSize = 15.sp, color = primary)
                        Text(mode.code, fontSize = 11.sp, color = secondary)
                    }
                    if (mode == current) Text("✓", fontSize = 16.sp, color = Color(com.catclaw.music.kt.ui.theme.highlightOnGlassCard(palette)))
                }
            }
        }
    }
}
