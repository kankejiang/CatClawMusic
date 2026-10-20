package com.catclaw.music.kt.ui

import android.app.Activity
import android.content.pm.ActivityInfo
import androidx.compose.animation.AnimatedVisibility
import androidx.compose.animation.core.LinearOutSlowInEasing
import androidx.compose.animation.core.tween
import androidx.compose.animation.fadeIn
import androidx.compose.animation.fadeOut
import androidx.compose.animation.slideInHorizontally
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
import com.catclaw.music.kt.ui.theme.LocalBackdropState
import com.catclaw.music.kt.ui.theme.backdropSource
import com.catclaw.music.kt.ui.theme.rememberBackdropState
import androidx.compose.foundation.layout.statusBarsPadding
import androidx.compose.foundation.pager.HorizontalPager
import androidx.compose.foundation.pager.rememberPagerState
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.ui.platform.LocalContext
import androidx.compose.foundation.layout.size
import androidx.compose.material3.Icon
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.CompositionLocalProvider
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.rememberCoroutineScope
import androidx.compose.runtime.snapshotFlow
import androidx.compose.runtime.setValue
import kotlinx.coroutines.delay
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.draw.scale
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.res.painterResource
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.semantics.hideFromAccessibility
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import androidx.hilt.navigation.compose.hiltViewModel
import androidx.lifecycle.compose.collectAsStateWithLifecycle
import androidx.navigation.compose.NavHost
import androidx.navigation.compose.composable
import androidx.navigation.compose.rememberNavController
import com.catclaw.music.kt.R
import com.catclaw.music.kt.core.theme.CatClawTheme
import com.catclaw.music.kt.ui.screen.FullLyricsScreen
import com.catclaw.music.kt.ui.screen.LibraryScreen
import com.catclaw.music.kt.ui.screen.PlayingScreen
import com.catclaw.music.kt.ui.screen.PlaylistScreen
import com.catclaw.music.kt.ui.screen.RemoteSourcesScreen
import com.catclaw.music.kt.ui.playback.PlayingViewModel
import com.catclaw.music.kt.ui.player.MiniPlayer
import com.catclaw.music.kt.ui.theme.GlassTokens
import com.catclaw.music.kt.ui.equalizer.SoundEffectsScreen
import com.catclaw.music.kt.ui.screen.QueueScreen
import com.catclaw.music.kt.ui.screen.SearchScreen
import com.catclaw.music.kt.ui.screen.SettingsDest
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
    // 设置路由一进来落在哪一页。用提升的状态而不是导航参数：只有「从别处直接跳某个子页」才需要它，
    // 而那条路径目前只有一个调用点（音乐库的资料库卡 → 下载管理）
    var settingsDest by remember { mutableStateOf(SettingsDest.Hub) }
    NavHost(
        navController = rootNavController,
        startDestination = ROUTE_TABS,
        modifier = Modifier.fillMaxSize(),
    ) {
        composable(ROUTE_TABS) {
            TabsHost(
                onOpenSettings = {
                    settingsDest = SettingsDest.Hub
                    rootNavController.navigate(ROUTE_SETTINGS)
                },
                onOpenRemote = { rootNavController.navigate(ROUTE_REMOTE) },
                onOpenDownloads = {
                    settingsDest = SettingsDest.Downloads
                    rootNavController.navigate(ROUTE_SETTINGS)
                },
            )
        }
        composable(ROUTE_SETTINGS) {
            ThemedSurface {
                SettingsScreen(
                    onBack = { rootNavController.popBackStack() },
                    onOpenRemote = { rootNavController.navigate(ROUTE_REMOTE) },
                    initialDest = settingsDest,
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
        // 这些页在录制层之外、又自己铺了背景：玻璃无从采样，显式退回实心（不是「忘了提供」）
        LocalBackdropState provides null,
    ) {
        Box(modifier = Modifier.fillMaxSize().catClawBackground().statusBarsPadding(), content = { content() })
    }
}

@Composable
private fun TabsHost(
    onOpenSettings: () -> Unit,
    // 音乐库的资料库卡要用：远程音乐与下载管理都不是本页内容，得从壳层跳整页
    onOpenRemote: () -> Unit,
    onOpenDownloads: () -> Unit,
) {
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
    // 在组合期算好，再喂给下面 pager 宿主的 semantics（见那处的注释）
    val coverLayersOpen = showQueue || showSoundFx
    // 聊天覆盖层打开时悬浮两栏要让位（否则输入行被 TabBar 盖住）
    var chatOpen by remember { mutableStateOf(false) }
    // MAUI 的显隐规则：`_currentIndex <= 1`（播放页 / 全屏歌词页）收起 TabBar 与 MiniPlayer；
    // 其余页且队列非空时才显示 MiniPlayer
    val immersivePage = pagerState.settledPage <= PAGE_NOW_PLAYING
    val showMiniPlayer = !immersivePage && queueSongs.isNotEmpty()

    // 手动横屏（MAUI `NowPlayingPage` 那颗旋转按钮）：锁 SensorLandscape，让「手机竖着拿、
    // 但想躺着看播放页」不必先去打开系统的自动旋转。状态放在外壳而不是播放页：
    // 离开播放页就要解锁，而播放页在 pager 预组装下并不会被销毁，它自己察觉不到「已经走了」
    var manualLandscape by remember { mutableStateOf(false) }
    val activity = LocalContext.current as? Activity
    LaunchedEffect(manualLandscape, activity) {
        // 解除时回 UNSPECIFIED（跟随系统），**不是** MAUI 的 SensorPortrait：
        // MAUI 那个应用本身就是竖屏应用，它的「还原」是回竖屏；本端默认跟随传感器，
        // 平板横着放时若被锁成竖屏，就成了另一个 bug
        activity?.requestedOrientation = if (manualLandscape) {
            ActivityInfo.SCREEN_ORIENTATION_SENSOR_LANDSCAPE
        } else {
            ActivityInfo.SCREEN_ORIENTATION_UNSPECIFIED
        }
    }
    // 滑走（或按播放页的 ⌄ 收起）就自动解锁：留一个「退出播放页后仍被锁在横屏」的状态，
    // 用户只能靠系统设置救回来。MAUI 是在返回键里解锁，本端滑动切换更常用，所以在页面落定时解
    LaunchedEffect(pagerState.settledPage) {
        if (pagerState.settledPage != PAGE_NOW_PLAYING) manualLandscape = false
    }

    CompositionLocalProvider(
        LocalCatClawPalette provides palette,
        LocalCustomBackground provides customBg,
        // 液玻材质从这里取宿主层：全 app 的卡片/面板都走它，不必层层传参
        LocalBackdropState provides backdrop,
    ) {
        Box(modifier = Modifier.fillMaxSize()) {
            // 2026-10-18 实测（本次卡顿排查）：这一层录的是**整页内容**，而歌词逐字动画每帧都在变，
            // 于是每帧重录一整屏 —— A/B 摘掉它：470fps→589 帧/20s、jank 48%→27%。
            // 但摘掉玻璃就没了，所以还留着；要既保玻璃又去掉这笔开销，得把录制层改成「只录背景」
            // （背景是静的，内容层当兄弟），见下面 [LocalBackdropState] 那段注释。
            // 宿主层：被玻璃透出的内容**必须是它的子节点**。
            // `layerBackdrop` 只录 `drawContent()`，所以写在 backdropSource 之前的 background/padding 不进录制层
            // —— 之前「有玻璃面、内容不糊」就是这个原因（真机：绿块作子节点时底栏立刻读到 (18,187,21)）。
            // 2026-10-03：排查滑动帧率时这行被 A/B 临时摘掉过，忘恢复导致底栏/迷你条玻璃失效——别再摘
            Box(modifier = Modifier.fillMaxSize()) {
                // 背景层：**只画背景**（2026-10-18 改）。
                // 原来这一层录的是整页内容，而滑动 / 歌词逐字动画每帧都在变 → 每帧重录一整屏：
                // A/B 摘掉录制层实测 470→589 帧/20s、jank 48%→27%。背景是静的（渐变 + 光晕 + 自定义图），
                // 录一次就够；内容层改当它的**兄弟**（下面那个 Box）画在上面，图层不再随动画重录。
                Box(modifier = Modifier.fillMaxSize().catClawBackground())
            }
            // 内容层 = **录制层**。这正是 Halcyon 的挂法：`EllaAppScreen.kt:1012` 把
            // `Modifier.layerMiuixBackdrop(backdrop)` 挂在**包住内容**的 Box 上，
            // 而它们的底栏/侧栏是这层的**兄弟** —— 消费者不能落在录制层内部。
            // 2026-10-20 实测：把录制层提到最外层（连底栏一起包住）→ 玻璃自采样 →
            // RenderThread SIGSEGV（stack overflow），进程启动即崩。
            Box(
                modifier = Modifier
                    .fillMaxSize()
                    .backdropSource(backdrop)
                    // 状态栏留白不再统一加在这里：播放页 / 全屏歌词页要沉浸到状态栏后面
                    // （封面染色铺满整个状态栏区域），留白由各页自己处理（2026-10-01）
                    // 整屏覆盖层（队列 / 音效）压住五页时，身后那页仍留在同一棵无障碍树里——
                    // 真机 dump 量到队列页上「设置」与「返回」两个节点的 bounds 100% 重叠，
                    // 读屏用户能滑进看不见、也点不到的那一层。覆盖层显示时把身后整棵子树摘掉。
                    // 条件在组合期算好再进 semantics：写在 lambda 里的话状态读取发生在语义阶段，
                    // 不会触发重组，覆盖层关掉后那棵子树就再也摘不回来了。
                    .semantics { if (coverLayersOpen) hideFromAccessibility() },
            ) {
                    HorizontalPager(
                        state = pagerState,
                        modifier = Modifier.fillMaxSize(),
                        // 预组装左右各一页：发现页几十张封面的首次组装/解码挪到启动空闲期，
                        // 不再压在滑入手势里（positionMs 已收敛到进度条组件，播放页不会再高频重组）
                        beyondViewportPageCount = 1,
                    ) { page ->
                        when (page) {
                            PAGE_FULL_LYRICS -> FullLyricsScreen(
                                // 按「已停稳」的页判可见（对齐 MAUI OnAppearing/OnDisappearing）：
                                // 驱动位置流 60fps↔120ms 降频与进页立即钉行
                                visible = pagerState.settledPage == PAGE_FULL_LYRICS,
                                onClose = { scope.launch { pagerState.animateScrollToPage(PAGE_NOW_PLAYING) } },
                            )
                            PAGE_NOW_PLAYING -> PlayingScreen(
                                onOpenLyrics = { scope.launch { pagerState.animateScrollToPage(PAGE_FULL_LYRICS) } },
                                onOpenSettings = onOpenSettings,
                                onOpenQueue = { showQueue = true },
                                onOpenSoundFx = { showSoundFx = true },
                                // ⌄ 收起：横滑回发现页（网易云的 ⌄ 是关掉播放器回主页面）
                                onCollapse = { scope.launch { pagerState.animateScrollToPage(2) } },
                                manualLandscape = manualLandscape,
                                onToggleLandscape = { manualLandscape = !manualLandscape },
                                viewModel = playingViewModel,
                            )
                            // 普通三页不画状态栏后面的区域，头部从状态栏下方开始
                            2 -> Box(Modifier.fillMaxSize().statusBarsPadding()) {
                                SearchScreen(
                                    active = pagerState.currentPage == 2,
                                    onOpenSettings = onOpenSettings,
                                    onChatOpen = { chatOpen = it },
                                    // 聊天模式下这条迷你条浮在输入行上面：高度让出去，
                                    // 否则输入框正好压在条子底下（MAUI 是 `SetBottomReservedHeight(extra)`）
                                    chatBottomReserve = if (showMiniPlayer) GlassTokens.BarHeight + 6.dp else 0.dp,
                                )
                            }
                            3 -> Box(Modifier.fillMaxSize().statusBarsPadding()) { PlaylistScreen() }
                            else -> Box(Modifier.fillMaxSize().statusBarsPadding()) {
                                LibraryScreen(onOpenRemote = onOpenRemote, onOpenDownloads = onOpenDownloads)
                            }
                        }
                    }
            }
            // 底栏/迷你条只在从播放页/歌词页切回页签时「延迟弹出」：减速滑入（LinearOutSlowIn，无回弹），
            // 迷你条先进、底栏延后 90ms 跟进；页签之间来回滑它们原地不动（反复弹会晃眼）。
            // 手势结束落定后弹出手势期间玻璃是空的，模糊不参与滑动帧（gfxinfo A/B：卡顿帧 35%→11%）
            // 「动画进行中 → 玻璃切实心」这个开关**暂时关着**（2026-10-18）。
            //
            // 试过的做法是「最近 120ms 出了几帧」的全局帧率探针，看着很巧，其实是错的：
            // `withFrameNanos` 循环**本身每帧都在请求帧回调** → 应用永远"忙碌" →
            // 玻璃永远切实心（截图里那两条栏已经不发糊了），而且一直按 120Hz 唤醒、白烧电。
            // 这是典型的观察者效应：探针把被测对象改掉了。
            //
            // 正解是让**可滚的那几个列表自己上报**：`snapshotFlow { listState.isScrollInProgress }`
            // （它同时覆盖拖动与惯性滑行）写进一个共享的活动计数，两条栏读这个计数。
            // 要改 6–8 个列表页，所以单独一步做，别在这里糊一个全局启发式。
            // 位置流只在「播放页 / 全屏歌词页真的在台上」时提到 ~60fps（见 LyricsViewModel 的注释）。
            // 由外壳按 settledPage 统一置位，而不是让页面自己 onDispose：
            // 页是**预组装**的（beyondViewportPageCount = 1），旁边那页一直 composed ——
            // 各自置位会让它也跟着 60fps 轮询，白耗电（这条流是主线程轮询）。
            val lyricsViewModel: com.catclaw.music.kt.ui.lyrics.LyricsViewModel = hiltViewModel()
            LaunchedEffect(pagerState) {
                snapshotFlow { pagerState.settledPage }.collect { page ->
                    val onPlayer = page == PAGE_NOW_PLAYING
                    val onLyrics = page == PAGE_FULL_LYRICS
                    playingViewModel.setPlayingSurfaceVisible(onPlayer)
                    lyricsViewModel.setPlayingLyricVisible(onPlayer || onLyrics)
                }
            }
            val glassOn = true
            val chromeVisible = !immersivePage && !chatOpen
            // 聊天模式下底栏让位（否则输入行被 TabBar 盖住），但**迷你条要留下** ——
            // MAUI 那条规则是「聊天模式下迷你播放器显示在聊天界面输入框上方」
            // （`MainPage.xaml.cs:1310-1316`：主壳层那条隐藏、聊天页自己再放一条），本端只有一条，
            // 所以做法就是别把它跟着一起收起来 —— 否则聊天时看不见在放什么，也回不去播放页
            var miniIn by remember { mutableStateOf(false) }
            var barIn by remember { mutableStateOf(false) }
            LaunchedEffect(chromeVisible, showMiniPlayer) {
                miniIn = showMiniPlayer
                if (chromeVisible) {
                    delay(50)
                    barIn = true
                } else {
                    barIn = false
                }
            }
            AnimatedVisibility(
                visible = chromeVisible || showMiniPlayer,
                enter = fadeIn(tween(1)),
                exit = fadeOut(tween(130)),
                modifier = Modifier.align(Alignment.BottomCenter),
            ) {
                Column(
                    modifier = Modifier
                        .navigationBarsPadding()
                        .padding(start = 18.dp, end = 18.dp, bottom = 4.dp),
                    verticalArrangement = Arrangement.spacedBy(2.dp),
                ) {
                    playState.currentSong?.let { song ->
                        if (showMiniPlayer) {
                            AnimatedVisibility(
                                visible = miniIn,
                                enter = slideInHorizontally(tween(320, easing = LinearOutSlowInEasing)) { it } +
                                    fadeIn(tween(140)),
                                exit = fadeOut(tween(100)),
                            ) {
                                MiniPlayer(
                                    song = song,
                                    isPlaying = playState.isPlaying,
                                    backdrop = backdrop,
                                    glassOn = glassOn,
                                    // 传 lambda：让这条线在绘制期读位置（否则整个外壳每帧重组，见 MiniPlayer 的注释）
                                    progress = {
                                        if (playState.durationMs > 0) {
                                            positionMs.toFloat() / playState.durationMs
                                        } else {
                                            0f
                                        }
                                    },
                                    modifier = Modifier.fillMaxWidth(),
                                    onOpen = { scope.launch { pagerState.animateScrollToPage(PAGE_NOW_PLAYING) } },
                                    onToggle = { playingViewModel.toggle() },
                                    onPrevious = { playingViewModel.previous() },
                                    onNext = { playingViewModel.next() },
                                )
                            }
                        }
                    }
                    AnimatedVisibility(
                        visible = barIn,
                        enter = slideInHorizontally(
                            tween(320, delayMillis = 50, easing = LinearOutSlowInEasing),
                        ) { it } + fadeIn(tween(140, delayMillis = 50)),
                        exit = fadeOut(tween(100)),
                    ) {
                        BarGlass(state = backdrop, modifier = Modifier.fillMaxWidth(), glassOn = glassOn) {
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
            }
            // 全屏歌词页是 pager 的第 0 页而不是 NavHost 路由，没人接系统返回键就是**直接退掉整个应用**
            // （真机：歌词页按返回掉回桌面）。它是从播放页滑出来的，返回就该滑回去。
            // 排在下面两条之前：覆盖层压着歌词页时，返回要先退覆盖层（后组合者优先）。
            BackHandler(enabled = pagerState.currentPage == PAGE_FULL_LYRICS) {
                scope.launch { pagerState.animateScrollToPage(PAGE_NOW_PLAYING) }
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

