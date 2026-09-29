package com.catclaw.music.kt.ui

import androidx.compose.foundation.background
import androidx.compose.foundation.clickable
import androidx.compose.foundation.isSystemInDarkTheme
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.activity.compose.BackHandler
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.navigationBarsPadding
import com.catclaw.music.kt.ui.theme.BarGlass
import com.catclaw.music.kt.ui.theme.backdropSource
import com.catclaw.music.kt.ui.theme.rememberBackdropState
import androidx.compose.foundation.layout.statusBarsPadding
import androidx.compose.foundation.pager.HorizontalPager
import androidx.compose.foundation.pager.rememberPagerState
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material3.MaterialTheme
import androidx.compose.foundation.layout.size
import androidx.compose.material3.Icon
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.CompositionLocalProvider
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.rememberCoroutineScope
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.draw.scale
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.res.painterResource
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import androidx.hilt.navigation.compose.hiltViewModel
import androidx.lifecycle.compose.collectAsStateWithLifecycle
import androidx.navigation.compose.NavHost
import androidx.navigation.compose.composable
import androidx.navigation.compose.rememberNavController
import com.catclaw.music.kt.R
import com.catclaw.music.kt.core.lyrics.LyricsState
import com.catclaw.music.kt.core.theme.CatClawTheme
import com.catclaw.music.kt.ui.lyrics.LyricsViewModel
import com.catclaw.music.kt.ui.screen.FullLyricsScreen
import com.catclaw.music.kt.ui.screen.LibraryScreen
import com.catclaw.music.kt.ui.screen.PlayingScreen
import com.catclaw.music.kt.ui.screen.PlaylistScreen
import com.catclaw.music.kt.ui.screen.RemoteSourcesScreen
import com.catclaw.music.kt.ui.playback.PlayingViewModel
import com.catclaw.music.kt.ui.player.MiniPlayer
import com.catclaw.music.kt.ui.equalizer.SoundEffectsScreen
import com.catclaw.music.kt.ui.screen.QueueScreen
import com.catclaw.music.kt.ui.screen.SearchScreen
import com.catclaw.music.kt.ui.screen.SettingsScreen
import com.catclaw.music.kt.data.settings.AppearanceSettings
import com.catclaw.music.kt.ui.theme.AppearancePalette
import com.catclaw.music.kt.ui.theme.LocalCatClawPalette
import com.catclaw.music.kt.ui.theme.LocalCustomBackground
import com.catclaw.music.kt.ui.theme.ThemeViewModel
import com.catclaw.music.kt.ui.theme.catClawBackground
import kotlinx.coroutines.launch

/**
 * 顶层结构照搬 MAUI 的 MainPage：五页横滑容器 + 底部四个 tab，设置与远程音乐是全屏子路由。
 *
 * ViewPager 布局：[全屏歌词(0), 正在播放(1), 发现=搜索(2), 歌单(3), 音乐库(4)] ——
 * 底部栏四个 tab 映射到 1..4，第 0 页只能从播放页右滑或点歌词行进入；
 * 歌词页与播放页固定深色预设（MAUI 同规则），其余页跟随深浅色主题。
 */
private const val PAGE_FULL_LYRICS = 0
private const val PAGE_NOW_PLAYING = 1
private const val PAGE_COUNT = 5

/** 图标与 MAUI `MainPage.xaml` 的 TabBar 一致：24×24 的 ic_play / ic_home / ic_playlist / ic_library */
private enum class TopTab(val pageIndex: Int, val labelRes: Int, val iconRes: Int) {
    Playing(PAGE_NOW_PLAYING, R.string.TabPlaying, R.drawable.ic_play),
    Discover(2, R.string.TabDiscover, R.drawable.ic_home),
    Playlist(3, R.string.TabPlaylists, R.drawable.ic_playlist),
    Library(4, R.string.TabLibrary, R.drawable.ic_library),
}

private const val ROUTE_TABS = "tabs"
private const val ROUTE_SETTINGS = "settings"
private const val ROUTE_REMOTE = "remote"

/**
 * TabBar 的一格 —— 对应 MAUI 的 `TabItem{i}`：选中态是 accent 色 α .28 的圆角 12 胶囊（`TabBg{i}` margin 4/3），
 * 图标 24、标签 10sp，选中时图标 1.15 / 标签 1.1 缩放，未选中降到 .6 亮度（MAUI 用 *_gray.svg）。
 */
@Composable
private fun TabCell(
    tab: TopTab,
    selected: Boolean,
    modifier: Modifier = Modifier,
    onClick: () -> Unit,
) {
    val palette = LocalCatClawPalette.current
    val active = Color(palette.textPrimary)
    val content = if (selected) active else active.copy(alpha = 0.6f)
    Box(modifier = modifier.clickable(onClick = onClick), contentAlignment = Alignment.Center) {
        if (selected) {
            Spacer(
                modifier = Modifier
                    .matchParentSize()
                    .padding(horizontal = 4.dp, vertical = 3.dp)
                    .clip(RoundedCornerShape(12.dp))
                    .background(active.copy(alpha = 0.28f)),
            )
        }
        Column(
            modifier = Modifier.padding(vertical = 8.dp),
            horizontalAlignment = Alignment.CenterHorizontally,
            verticalArrangement = Arrangement.spacedBy(4.dp),
        ) {
            Icon(
                painter = painterResource(tab.iconRes),
                contentDescription = null,
                tint = content,
                modifier = Modifier
                    .size(24.dp)
                    .scale(if (selected) 1.15f else 1f),
            )
            Text(
                text = stringResource(tab.labelRes),
                fontSize = 10.sp,
                color = content,
                maxLines = 1,
                overflow = TextOverflow.Ellipsis,
                modifier = Modifier.scale(if (selected) 1.1f else 1f),
            )
        }
    }
}

@Composable
fun CatClawRoot() {
    val rootNavController = rememberNavController()
    NavHost(
        navController = rootNavController,
        startDestination = ROUTE_TABS,
        modifier = Modifier.fillMaxSize(),
    ) {
        composable(ROUTE_TABS) {
            TabsHost(onOpenSettings = { rootNavController.navigate(ROUTE_SETTINGS) })
        }
        composable(ROUTE_SETTINGS) {
            ThemedSurface {
                SettingsScreen(
                    onBack = { rootNavController.popBackStack() },
                    onOpenRemote = { rootNavController.navigate(ROUTE_REMOTE) },
                )
            }
        }
        composable(ROUTE_REMOTE) {
            ThemedSurface {
                RemoteSourcesScreen(onBack = { rootNavController.popBackStack() })
            }
        }
    }
}

/** 全屏子页：按当前外观模式取调色板，背景自己画 */
@Composable
private fun ThemedSurface(content: @Composable () -> Unit) {
    val themeViewModel: ThemeViewModel = hiltViewModel()
    val mode by themeViewModel.mode.collectAsStateWithLifecycle()
    val appearance = themeViewModel.appearance
    val systemDark = isSystemInDarkTheme()
    val palette = remember(mode, systemDark) { AppearancePalette.resolve(mode, systemDark, appearance) }
    val customBg by themeViewModel.customBackground.collectAsStateWithLifecycle()
    CompositionLocalProvider(
        LocalCatClawPalette provides palette,
        LocalCustomBackground provides customBg,
    ) {
        Box(modifier = Modifier.fillMaxSize().catClawBackground().statusBarsPadding(), content = { content() })
    }
}

@Composable
private fun TabsHost(onOpenSettings: () -> Unit) {
    val scope = rememberCoroutineScope()
    val themeViewModel: ThemeViewModel = hiltViewModel()
    val mode by themeViewModel.mode.collectAsStateWithLifecycle()
    val systemDark = isSystemInDarkTheme()
    val monetTick by themeViewModel.monetTick.collectAsStateWithLifecycle()
    val themedPalette = remember(mode, systemDark, monetTick) {
        AppearancePalette.resolve(mode, systemDark, themeViewModel.appearance)
    }
    val customBg by themeViewModel.customBackground.collectAsStateWithLifecycle()
    // 启动页只在进入时读一次：中途改设置不该把用户从当前页弹走
    val pagerState = rememberPagerState(
        initialPage = AppearanceSettings.startupToPagerPage(themeViewModel.startupIndex.value),
    ) { PAGE_COUNT }
    // 按「已停稳」的页决定调色板，避免滑动中途整屏配色乱跳
    val fixedDarkPages = setOf(PAGE_FULL_LYRICS, PAGE_NOW_PLAYING)
    val palette = if (pagerState.settledPage in fixedDarkPages) CatClawTheme.dark else themedPalette
    val backdrop = rememberBackdropState()
    // 队列页要读同一条队列，所以 VM 在这里取一次往下传（hiltViewModel 按路由作用域，两边共享同一实例）
    val playingViewModel: PlayingViewModel = hiltViewModel()
    val queueSongs by playingViewModel.queueSongs.collectAsStateWithLifecycle()
    val playState by playingViewModel.state.collectAsStateWithLifecycle()
    val positionMs by playingViewModel.positionMs.collectAsStateWithLifecycle()
    var showQueue by remember { mutableStateOf(false) }
    // 音效页和队列页一样是整页覆盖层：它是"播放的附属设置"，不该占一个 pager 槽位
    var showSoundFx by remember { mutableStateOf(false) }
    // 聊天覆盖层打开时悬浮两栏要让位（否则输入行被 TabBar 盖住）
    var chatOpen by remember { mutableStateOf(false) }
    // MAUI 的显隐规则：`_currentIndex <= 1`（播放页 / 全屏歌词页）收起 TabBar 与 MiniPlayer；
    // 其余页且队列非空时才显示 MiniPlayer
    val immersivePage = pagerState.settledPage <= PAGE_NOW_PLAYING
    val showMiniPlayer = !immersivePage && queueSongs.isNotEmpty()

    CompositionLocalProvider(
        LocalCatClawPalette provides palette,
        LocalCustomBackground provides customBg,
    ) {
        Box(modifier = Modifier.fillMaxSize()) {
            // 宿主层：被玻璃透出的内容**必须是它的子节点**。
            // `layerBackdrop` 只录 `drawContent()`，所以写在 backdropSource 之前的 background/padding 不进录制层
            // —— 之前「有玻璃面、内容不糊」就是这个原因（真机：绿块作子节点时底栏立刻读到 (18,187,21)）
            Box(
                modifier = Modifier
                    .fillMaxSize()
                    .backdropSource(backdrop),
            ) {
                Box(
                    modifier = Modifier
                        .fillMaxSize()
                        .catClawBackground()
                        .statusBarsPadding(),
                ) {
                    HorizontalPager(
                        state = pagerState,
                        modifier = Modifier.fillMaxSize(),
                    ) { page ->
                        when (page) {
                            PAGE_FULL_LYRICS -> FullLyricsScreen(
                                onClose = { scope.launch { pagerState.animateScrollToPage(PAGE_NOW_PLAYING) } },
                            )
                            PAGE_NOW_PLAYING -> PlayingScreen(
                                onOpenLyrics = { scope.launch { pagerState.animateScrollToPage(PAGE_FULL_LYRICS) } },
                                onOpenSettings = onOpenSettings,
                                onOpenQueue = { showQueue = true },
                                onOpenSoundFx = { showSoundFx = true },
                                viewModel = playingViewModel,
                            )
                            2 -> SearchScreen(
                                active = pagerState.currentPage == 2,
                                onOpenSettings = onOpenSettings,
                                onChatOpen = { chatOpen = it },
                            )
                            3 -> PlaylistScreen()
                            else -> LibraryScreen()
                        }
                    }
                }
            }
            if (!immersivePage && !chatOpen) {
                // MAUI 的 MobileStage 是 RowDefinitions="*,Auto,Auto"：MiniPlayer（Row1）在 TabBar（Row2）**上方**，
                // 两栏都是左右留白 18 的悬浮胶囊（MiniPlayer margin 下 2、TabBar margin 下 4），不是贴边的整宽条
                Column(
                    modifier = Modifier
                        .align(Alignment.BottomCenter)
                        .navigationBarsPadding()
                        .padding(start = 18.dp, end = 18.dp, bottom = 4.dp),
                    verticalArrangement = Arrangement.spacedBy(2.dp),
                ) {
                    playState.currentSong?.let { song ->
                        if (showMiniPlayer) {
                            MiniPlayer(
                                song = song,
                                isPlaying = playState.isPlaying,
                                backdrop = backdrop,
                                progress = if (playState.durationMs > 0) {
                                    positionMs.toFloat() / playState.durationMs
                                } else {
                                    0f
                                },
                                modifier = Modifier.fillMaxWidth(),
                                onOpen = { scope.launch { pagerState.animateScrollToPage(PAGE_NOW_PLAYING) } },
                                onToggle = { playingViewModel.toggle() },
                                onPrevious = { playingViewModel.previous() },
                                onNext = { playingViewModel.next() },
                            )
                        }
                    }
                    BarGlass(state = backdrop, modifier = Modifier.fillMaxWidth()) {
                        Row(modifier = Modifier.fillMaxWidth().padding(horizontal = 4.dp)) {
                            TopTab.entries.forEach { tab ->
                                TabCell(
                                    tab = tab,
                                    selected = pagerState.settledPage == tab.pageIndex,
                                    modifier = Modifier.weight(1f),
                                    onClick = {
                                        scope.launch { pagerState.animateScrollToPage(tab.pageIndex) }
                                    },
                                )
                            }
                        }
                    }
                }
            }
            // 系统返回键先退掉最上层的覆盖层。这两层是压在发现页（聊天/统计）之上的整屏层，
            // 而 Compose 的 BackHandler 是**后组合者优先**，所以放在这里正好盖过发现页那两条
            BackHandler(enabled = showQueue) { showQueue = false }
            BackHandler(enabled = showSoundFx) { showSoundFx = false }
            // 队列页：MAUI 是 push 出来的独立页，这里同样盖满整屏并带自己的返回
            if (showQueue) {                Box(
                    modifier = Modifier
                        .fillMaxSize()
                        .catClawBackground()
                        .statusBarsPadding(),
                ) {
                    QueueScreen(viewModel = playingViewModel, onBack = { showQueue = false })
                }
            }
            // 音效页：MAUI 从播放页 modal push 出去，这里同样盖满整屏（不占 pager 槽位）
            if (showSoundFx) {
                Box(
                    modifier = Modifier
                        .fillMaxSize()
                        .catClawBackground()
                        .statusBarsPadding(),
                ) {
                    SoundEffectsScreen(onBack = { showSoundFx = false })
                }
            }
        }
    }
}

/** 播放页上的当前歌词行：点击横滑到全屏歌词页 */
@Composable
fun CurrentLyricLine(onOpenLyrics: () -> Unit, modifier: Modifier = Modifier) {
    val palette = LocalCatClawPalette.current
    val lyricsViewModel: LyricsViewModel = hiltViewModel()
    val lyrics by lyricsViewModel.lyrics.collectAsStateWithLifecycle()
    val positionMs by lyricsViewModel.positionMs.collectAsStateWithLifecycle()
    val lineMode by lyricsViewModel.lineMode.collectAsStateWithLifecycle()

    val presentation = LyricsState.present(
        lyrics = lyrics,
        positionMs = positionMs,
        lineMode = lineMode,
    )

    Text(
        text = presentation?.line?.text?.takeIf { it.isNotBlank() } ?: stringResource(R.string.lyric_none),
        style = MaterialTheme.typography.titleMedium,
        color = Color(palette.textPrimary),
        maxLines = 1,
        overflow = TextOverflow.Ellipsis,
        textAlign = TextAlign.Center,
        modifier = modifier
            .fillMaxWidth()
            .padding(horizontal = 18.dp, vertical = 6.dp)
            .clickable(onClick = onOpenLyrics),
    )
}
