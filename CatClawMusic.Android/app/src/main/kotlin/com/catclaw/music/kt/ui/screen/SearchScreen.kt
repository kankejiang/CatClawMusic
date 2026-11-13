package com.catclaw.music.kt.ui.screen

import androidx.compose.foundation.lazy.itemsIndexed
import androidx.compose.foundation.Image
import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.clickable
import androidx.activity.compose.BackHandler
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.BoxWithConstraints
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.PaddingValues
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.aspectRatio
import androidx.compose.foundation.layout.fillMaxHeight
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.offset
import androidx.compose.foundation.layout.statusBarsPadding
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.LazyListScope
import androidx.compose.foundation.lazy.LazyRow
import androidx.compose.foundation.lazy.items
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.animation.AnimatedVisibility
import androidx.compose.animation.core.LinearOutSlowInEasing
import androidx.compose.animation.core.MutableTransitionState
import androidx.compose.animation.core.tween
import androidx.compose.animation.fadeIn
import androidx.compose.animation.slideInHorizontally
import androidx.compose.material3.Icon
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableIntStateOf
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.saveable.rememberSaveable
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.Brush
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.res.painterResource
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.semantics.hideFromAccessibility
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import androidx.hilt.navigation.compose.hiltViewModel
import androidx.lifecycle.compose.collectAsStateWithLifecycle
import com.catclaw.music.kt.R
import com.catclaw.music.kt.ui.player.CoverTile
import com.catclaw.music.kt.data.db.SongListItem
import com.catclaw.music.kt.ui.chat.ChatScreen
import com.catclaw.music.kt.ui.stats.ListeningStatsScreen
import com.catclaw.music.kt.ui.library.LibraryViewModel
import com.catclaw.music.kt.ui.search.SearchViewModel
import com.catclaw.music.kt.ui.song.SongDetailScreen
import com.catclaw.music.kt.ui.theme.LocalCatClawPalette
import com.catclaw.music.kt.ui.theme.GlassChip
import com.catclaw.music.kt.ui.theme.EmptyState
import com.catclaw.music.kt.ui.theme.glassCard
import com.catclaw.music.kt.ui.theme.PillLabel
import com.catclaw.music.kt.ui.theme.BackTile
import com.catclaw.music.kt.ui.theme.GlassSearchField
import com.catclaw.music.kt.ui.theme.catClawBackground
import com.catclaw.music.kt.ui.theme.inkOnGradient
import com.catclaw.music.kt.ui.theme.badgeTintOn
import com.catclaw.music.kt.ui.theme.highlightOnGlassCard
import com.catclaw.music.kt.ui.theme.isLightPalette
import com.catclaw.music.kt.ui.theme.blend
import kotlin.random.Random
import java.util.Calendar

/**
 * 发现 tab —— 照 MAUI `SearchPage.xaml` 做成**枢纽**：问候语 + 三个玻璃图标 + 五个分类面板。
 * 本端已重排成「液态玻璃」语言（docs/discover-page-redesign.html）：环境色斑垫底，
 * 所有板块浮在玻璃上；整页 LazyColumn，歌手/专辑网格按行虚拟化。
 */
@Composable
fun SearchScreen(
    active: Boolean,
    onOpenSettings: () -> Unit = {},
    onChatOpen: (Boolean) -> Unit = {},
    /** 货架右上「查看全部」的去处：切到音乐库并直接打开对应列表 */
    onOpenLibrary: (LibraryKind) -> Unit = {},
    /** 聊天模式下外壳那条迷你播放条的高度（见 `ChatScreen.bottomReserve`） */
    chatBottomReserve: androidx.compose.ui.unit.Dp = androidx.compose.ui.unit.Dp(0f),
    viewModel: SearchViewModel = hiltViewModel(),
) {
    val palette = LocalCatClawPalette.current
    val neteaseLoggedIn by viewModel.neteaseLoggedIn.collectAsStateWithLifecycle()
    // 登录发生在网易云页；切回发现页时刷新登录态（pager 页面常驻，不会重建）
    LaunchedEffect(active) { if (active) viewModel.refreshNeteaseLogin() }
    val query by viewModel.query.collectAsStateWithLifecycle()
    val results by viewModel.results.collectAsStateWithLifecycle()
    val onlineResults by viewModel.onlineResults.collectAsStateWithLifecycle()
    val onlineLoading by viewModel.onlineLoading.collectAsStateWithLifecycle()
    val sections by viewModel.sections.collectAsStateWithLifecycle()
    val artists by viewModel.artists.collectAsStateWithLifecycle()
    val albums by viewModel.albums.collectAsStateWithLifecycle()
    val stats by viewModel.stats.collectAsStateWithLifecycle()
    val primary = Color(palette.textPrimary)
    val secondary = Color(palette.textSecondary)
    var chatMode by rememberSaveable { mutableStateOf(false) }
    var statsOpen by rememberSaveable { mutableStateOf(false) }
    // 聊天与统计都是整屏覆盖层，外壳的悬浮两栏必须让位，否则页底的内容会被 TabBar 盖住。
    // 还要看 active：Pager 不会销毁别的页，横滑走开时这两个标志仍是 true，
    // 只按覆盖层标志上报会让别的 tab 也永久没有底栏
    LaunchedEffect(chatMode, statsOpen, active) { onChatOpen((chatMode || statsOpen) && active) }
    // 系统返回键必须先退掉本页的覆盖层。整个 app 之前没有任何 BackHandler：
    // 覆盖层开着按返回会直接退出应用（真机上验到过），而覆盖层里往往正是用户刚看的东西
    BackHandler(enabled = statsOpen && active) { statsOpen = false }
    BackHandler(enabled = chatMode && active) { chatMode = false }
    // 歌手/专辑卡的下钻：复用音乐库那套 `LibraryDetailScreen`（同一个路由作用域，两边 hiltViewModel
    // 拿到同一个实例）。此前这四处的卡片根本没有 clickable —— 推荐艺人 / 推荐专辑 / 歌手 tab /
    // 专辑 tab 全是「点了没反应」，而目标页其实早就在库里了
    var detail by remember { mutableStateOf<LibraryDetail?>(null) }
    var songDetailId by remember { mutableStateOf<Int?>(null) }
    val libraryViewModel: LibraryViewModel = hiltViewModel()
    // 两层下钻也要逐层退，且歌曲详情在最上：组合在后者优先，所以先写 detail 再写 songDetailId
    BackHandler(enabled = detail != null && active) { detail = null }
    BackHandler(enabled = songDetailId != null && active) { songDetailId = null }
    // 点搜索进独立搜索页（与二级页同一套行为），不再原地展开
    var searchPage by rememberSaveable { mutableStateOf(false) }
    // 独立搜索页也是整屏覆盖层：不接返回键会直接退出应用
    BackHandler(enabled = searchPage && active) { searchPage = false }

    LaunchedEffect(active) { if (active) viewModel.activate() }

    Box(modifier = Modifier.fillMaxSize()) {
        if (searchPage) {
            DiscoverSearchPage(
                query = query,
                results = results,
                online = onlineResults,
                onlineLoading = onlineLoading,
                onQueryChange = viewModel::setQuery,
                onClose = { searchPage = false },
                onPlay = { viewModel.play(results, it) },
                onPlayOnline = { viewModel.playOnline(onlineResults, it) },
                primary = primary,
                secondary = secondary,
            )
            return@Box
        }
        // 2026-11-06 用户要求：发现页那四个**固定的大圆点**（环境色斑层）删掉。
        // 它们原本是给玻璃卡透色用的，但现在外壳已经铺了雾面动态背景，这几团固定色斑
        // 反而像背景没洗干净；玻璃卡改由动态背景透色。
        // 整页 LazyColumn：歌手/专辑网格按行虚拟化，滚出视口的照片不解码不绘制——
        // 之前的 verticalScroll Column 全量组装，几十个 84dp 照片一次解码，滚动必卡
        // 横屏：底栏在 480dp 高的画布上占比更高，Hero 要降高、底部留白要加大
        val landscape = androidx.compose.ui.platform.LocalConfiguration.current.orientation ==
            android.content.res.Configuration.ORIENTATION_LANDSCAPE
        LazyColumn(
            modifier = Modifier
                .fillMaxSize()
                // 聊天 / 统计这两层盖住发现页时，把身后这棵子树从无障碍树里摘掉：
                // 同一个 ComposeView 里两层节点会落在同一块 bounds 上，读屏能滑进看不见的那一层
                // （与 CatClawRoot 对队列页 / 音效页的处理同一条）。条件在组合期读，别挪进 lambda。
                .semantics { if (chatMode || statsOpen) hideFromAccessibility() }
                // 下钻层（歌手 / 专辑 / 歌曲详情）压住时同理：它们与这份列表在同一个 ComposeView 里，
                // 不摘的话读屏能滑进那些被盖住的推荐卡。两条条件各自独立，所以分两个 semantics
                .semantics { if (detail != null || songDetailId != null) hideFromAccessibility() },
            // 横屏舞台自身恰好一屏（fillParentMaxHeight），不再留底部滚动余量
            contentPadding = PaddingValues(bottom = if (landscape) 0.dp else 132.dp),
        ) {
            // ── 横屏：不用纵向列表 ──
            // 411dp 高的画布堆不下「标题 + 英雄卡 + 四入口 + 货架」，硬堆的结果就是四个入口
            // 被迷你条切掉半截（2026-11-09 真机截图）。改成整块「双栏舞台」：
            // 左边实图主卡吃满高度、右边四入口 + 推荐货架，首屏自足。
            if (landscape) {
                item("landscape_stage") {
                    LandscapeDiscover(
                        modifier = Modifier.fillParentMaxHeight(),
                        query = query,
                        results = results,
                        sections = sections,
                        albums = albums,
                        artists = artists,
                        primary = primary,
                        secondary = secondary,
                        searchOpen = false,
                        onToggleSearch = { searchPage = true },
                        onQueryChange = { viewModel.setQuery(it) },
                        onOpenStats = { statsOpen = true },
                        onAskYuki = { chatMode = true },
                        onOpenDetail = { detail = it },
                        onOpenSongs = { onOpenLibrary(LibraryKind.Songs) },
                        onOpenAlbums = { onOpenLibrary(LibraryKind.Albums) },
                        onOpenArtists = { onOpenLibrary(LibraryKind.Artists) },
                        neteaseLoggedIn = neteaseLoggedIn,
                        viewModel = viewModel,
                    )
                }
                return@LazyColumn
            }

            // 头部：标题 + 问候语 + 三个 44×44 玻璃图标
            item("header") {
                Row(
                    modifier = Modifier.fillMaxWidth().padding(start = 18.dp, end = 18.dp, top = 14.dp, bottom = 10.dp),
                    verticalAlignment = Alignment.Top,
                ) {
                    Column(modifier = Modifier.weight(1f)) {
                        Text(stringResource(R.string.TabDiscover), fontSize = 30.sp, fontWeight = FontWeight.Bold, color = primary)
                        Text(greeting(), fontSize = 13.sp, color = secondary, modifier = Modifier.padding(top = 4.dp))
                    }
                    // 这里是竖屏头部：横屏在 LandscapeDiscover 里有自己的一条（图标更小、含搜索与统计）
                    GlassIcon(R.drawable.ic_search, stringResource(R.string.TabSearch)) { searchPage = true }
                    Spacer(Modifier.width(10.dp))
                    GlassIcon(R.drawable.ic_stats, stringResource(R.string.lib_open_stats)) { statsOpen = true }
                    Spacer(Modifier.width(10.dp))
                    GlassIcon(R.drawable.ic_settings, stringResource(R.string.Settings), onClick = onOpenSettings)
                }
            }

            if (query.isNotBlank()) {
                if (results.isEmpty()) {
                    // 搜不到东西不能是白屏：空列表会直接 return，所以这里单独给状态
                    item("none") {
                        EmptyState(
                            hint = stringResource(R.string.search_none, query.trim()),
                            iconRes = R.drawable.ic_search,
                            modifier = Modifier.padding(horizontal = 18.dp, vertical = 10.dp),
                        )
                    }
                } else {
                    item("results") {
                        SongRows(stringResource(R.string.TabSongs), results, primary, secondary) { viewModel.play(results, it) }
                    }
                }
            } else {


                // 2026-11-06 用户定的结构：每日推荐英雄卡 → 四个入口 → 推荐专辑 → 推荐艺人。
                // 五颗分类 chip 全部取消：歌手/专辑由货架右上「查看全部」承接，统计挪到头部柱状图标，
                // 「排行」本来就有「最多播放」入口，「每日推荐歌单列表」也不再单列（英雄卡就是它）。
                recommendItems(
                    viewModel = viewModel,
                    sections = sections,
                    artists = artists,
                    albums = albums,
                    primary = primary,
                    secondary = secondary,
                    onAskYuki = { chatMode = true },
                    onOpenDetail = { detail = it },
                    onOpenSongs = { onOpenLibrary(LibraryKind.Songs) },
                    onOpenAlbums = { onOpenLibrary(LibraryKind.Albums) },
                    onOpenArtists = { onOpenLibrary(LibraryKind.Artists) },
                    neteaseLoggedIn = neteaseLoggedIn,
                )
            }
        }

        // 聊天是盖在发现页上的一层（MAUI 同为 overlay 而非独立路由），退出后搜索结果原样保留
        if (chatMode) ChatScreen(onBack = { chatMode = false }, bottomReserve = chatBottomReserve)
        // 听歌统计同样是一层覆盖：它从「统计」这一格进，返回就该回到原来那格，不该新增一次路由栈翻页。
        // 背景必须由这层来铺（和队列页/音效页同一个写法）—— 页面组件本身不画底，
        // 少了这个 Box，真机上整页内容会直接压在发现页上，两层字叠成一片糊
        if (statsOpen) {
            Box(
                modifier = Modifier
                    .fillMaxSize()
                    .catClawBackground()
                    .statusBarsPadding(),
            ) {
                ListeningStatsScreen(onBack = { statsOpen = false })
            }
        }

        // 歌手/专辑下钻与歌曲详情：与统计页同一写法 —— 这一层自己铺背景并接管状态栏留白。
        // 少铺一层底，真机上就是两页文字直接叠在一起（队列页/音效页都踩过同一个坑）。
        if (detail != null || songDetailId != null) {
            Box(
                modifier = Modifier
                    .fillMaxSize()
                    .catClawBackground()
                    .statusBarsPadding(),
            ) {
                val openSongId = songDetailId
                if (openSongId != null) {
                    // 详情页是全页，托管在这一层而不是列表里：否则它会叠在下钻页的头下面，出现两个返回键
                    SongDetailScreen(
                        songId = openSongId,
                        load = { libraryViewModel.songDetail(it) },
                        loadLyrics = { libraryViewModel.lyricsFor(it) },
                        onPlay = { libraryViewModel.playSongById(it) },
                        onBack = { songDetailId = null },
                    )
                } else {
                    detail?.let { target ->
                        LibraryDetailScreen(
                            detail = target,
                            onBack = { detail = null },
                            onOpenSong = { songDetailId = it },
                            // 下钻页里再点归属（专辑页 → 歌手页）时替换本层，不叠新的
                            onOpenDetail = { detail = it },
                            viewModel = libraryViewModel,
                        )
                    }
                }
            }
        }
    }
}

/**
 * 板块入场：减速滑入（LinearOutSlowIn，无回弹），与底栏/迷你条同一套语言。
 * 按 index 自上而下错峰（每档 50ms）；`remember` 住状态，滚出去再滚回来不重播。
 */
