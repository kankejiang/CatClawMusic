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
                onQueryChange = viewModel::setQuery,
                onClose = { searchPage = false },
                onPlay = { viewModel.play(results, it) },
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
@Composable
private fun StaggerIn(index: Int, content: @Composable () -> Unit) {
    AnimatedVisibility(
        visibleState = remember { MutableTransitionState(false).apply { targetState = true } },
        enter = slideInHorizontally(
            tween(320, delayMillis = index * 50, easing = LinearOutSlowInEasing),
        ) { it } + fadeIn(tween(140, delayMillis = index * 50)),
    ) { content() }
}

/** 推荐面板的各个板块，作为 LazyColumn 的 item 逐个发射（板块间 12dp 间距） */
private fun LazyListScope.recommendItems(
    viewModel: SearchViewModel,
    sections: SearchViewModel.Sections,
    artists: List<com.catclaw.music.kt.data.db.ArtistListItem>,
    albums: List<com.catclaw.music.kt.data.db.AlbumListItem>,
    primary: Color,
    secondary: Color,
    onAskYuki: () -> Unit,
    // 横屏时头部只剩标题，「搜索 / 听歌统计」挪到入口上方，回调从这里传进来
    onOpenDetail: (LibraryDetail) -> Unit,
    onOpenSongs: () -> Unit,
    onOpenAlbums: () -> Unit,
    onOpenArtists: () -> Unit,
    neteaseLoggedIn: Boolean,
) {
    val hero = sections.daily.firstOrNull()

    if (hero == null && albums.isEmpty() && artists.isEmpty()) {
        item("rec_empty") {
            EmptyState(
                hint = stringResource(R.string.disc_empty),
                iconRes = R.drawable.ic_library,
                modifier = Modifier.padding(horizontal = 18.dp, vertical = 8.dp),
            )
        }
    }

    // ① 每日推荐英雄卡（横屏时卡片内部自动变成「封面 + 文字」横排）
    if (hero != null) item("rec_hero") {
        StaggerIn(index = 0) {
            DailyHero(
                song = hero,
                onPlay = { viewModel.play(sections.daily, 0) },
                onRefresh = { viewModel.reload() },
                modifier = Modifier
                    .fillMaxWidth()
                    .padding(horizontal = 18.dp, vertical = 6.dp),
            )
        }
    }

    // ② 四个入口：横屏竖屏都是一行四个（横屏宽度够，不需要折成 2×2）
    item("rec_quick") {
        StaggerIn(index = 1) {
            Row(
                modifier = Modifier.fillMaxWidth().padding(horizontal = 18.dp, vertical = 8.dp),
                horizontalArrangement = Arrangement.spacedBy(10.dp),
            ) {
                QuickTile(R.drawable.ic_spark, stringResource(R.string.disc_tag_ai), Modifier.weight(1f), onAskYuki)
                QuickTile(R.drawable.ic_shuffle, stringResource(R.string.disc_tag_shuffle), Modifier.weight(1f), viewModel::shuffleAll)
                if (neteaseLoggedIn) {
                    QuickTile(
                        R.drawable.ic_private_fm,
                        stringResource(R.string.netease_private_fm),
                        Modifier.weight(1f),
                        viewModel::startNeteaseFm,
                    )
                }
                QuickTile(R.drawable.ic_rank, stringResource(R.string.disc_tag_top), Modifier.weight(1f)) {
                    if (sections.frequent.isNotEmpty()) viewModel.play(sections.frequent, 0)
                }
                QuickTile(R.drawable.ic_favorite, stringResource(R.string.disc_favorite), Modifier.weight(1f), viewModel::playFavorites)
            }
        }
    }

    // ③ 推荐歌曲 / ④ 推荐专辑：横屏并排两列（一屏放得下两组货架，竖屏仍是一上一下）
    val recSongs = (sections.daily + sections.fresh + sections.frequent).distinctBy { it.id }.take(10)
    val songsShelf: @Composable () -> Unit = {
        Column {
            RailHead(stringResource(R.string.disc_songs_rec), onSeeAll = onOpenSongs, primary = primary, secondary = secondary)
            LazyRow(
                contentPadding = PaddingValues(horizontal = 18.dp),
                horizontalArrangement = Arrangement.spacedBy(12.dp),
            ) {
                itemsIndexed(recSongs, key = { _, song -> "s${song.id}" }) { index, song ->
                    SongRailCard(song) { viewModel.play(recSongs, index) }
                }
            }
        }
    }
    val albumsShelf: @Composable () -> Unit = {
        Column {
            RailHead(stringResource(R.string.disc_albums_rec), onSeeAll = onOpenAlbums, primary = primary, secondary = secondary)
            LazyRow(
                contentPadding = PaddingValues(horizontal = 18.dp),
                horizontalArrangement = Arrangement.spacedBy(12.dp),
            ) {
                items(albums.take(8), key = { "a${it.id}" }) { album ->
                    RailAlbumCard(album) { onOpenDetail(LibraryDetail(LibraryKind.Albums, album.id, album.title)) }
                }
            }
        }
    }
    if (recSongs.isNotEmpty()) item("rec_songs") { StaggerIn(index = 2) { songsShelf() } }
    if (albums.isNotEmpty()) item("rec_albums") { StaggerIn(index = 2) { albumsShelf() } }

    // ⑤ 推荐艺人：10 位，横滑
    if (artists.isNotEmpty()) item("rec_artists") {
        StaggerIn(index = 3) {
            Column {
                RailHead(stringResource(R.string.disc_artists_rec), onSeeAll = onOpenArtists, primary = primary, secondary = secondary)
                LazyRow(
                    contentPadding = PaddingValues(horizontal = 18.dp),
                    horizontalArrangement = Arrangement.spacedBy(12.dp),
                ) {
                    items(artists.take(10), key = { "r${it.id}" }) { artist ->
                        ArtistBubble(artist) { onOpenDetail(LibraryDetail(LibraryKind.Artists, artist.id, artist.name)) }
                    }
                }
            }
        }
    }
}



/** 每日推荐英雄卡：封面铺满，底部渐变遮罩上压标题/歌手/两个胶囊 */
@Composable
private fun DailyHero(
    song: SongListItem,
    onPlay: () -> Unit,
    onRefresh: () -> Unit,
    modifier: Modifier = Modifier,
) {
    // 竖屏专用。横屏在 411dp 高的画布上根本放不下这张 268dp 的卡，
    // 已整体换成双栏舞台（见 LandscapeDiscover / LandscapeHeroCard）。
    Box(
        modifier = modifier
            .fillMaxWidth()
            .height(268.dp)
            .clip(RoundedCornerShape(22.dp)),
    ) {
        CoverTile(
            coverArtPath = song.coverArtPath,
            audioPath = song.filePath,
            maxPx = 720,
            modifier = Modifier.fillMaxSize(),
        ) {}
        Box(
            modifier = Modifier.fillMaxSize().background(
                Brush.verticalGradient(
                    listOf(Color.Transparent, Color.Black.copy(alpha = 0.30f), Color.Black.copy(alpha = 0.88f)),
                ),
            ),
        )
        // 来源徽标（浮在封面上的小玻璃片：液态玻璃唯一该出现在内容里的位置）
        Row(
            modifier = Modifier
                .align(Alignment.TopStart)
                .padding(14.dp)
                .clip(RoundedCornerShape(50))
                .background(Color(LocalCatClawPalette.current.textPrimary).copy(alpha = 0.16f))
                .padding(horizontal = 12.dp, vertical = 7.dp),
            verticalAlignment = Alignment.CenterVertically,
        ) {
            Text(
                text = stringResource(R.string.disc_tag_daily) + " · " + stringResource(R.string.disc_hero_by_taste),
                fontSize = 11.sp,
                color = Color(inkOnGradient(listOf(0xFF0A0A0F, 0xFF000000))),
                maxLines = 1,
            )
        }
        Column(modifier = Modifier.align(Alignment.BottomStart).fillMaxWidth().padding(16.dp)) {
            Text(
                text = song.title,
                fontSize = 22.sp,
                fontWeight = FontWeight.Bold,
                color = Color(inkOnGradient(listOf(0xFF0A0A0F, 0xFF000000))),
                maxLines = 1,
                overflow = TextOverflow.Ellipsis,
            )
            Text(
                text = song.artist,
                fontSize = 12.sp,
                color = Color(inkOnGradient(listOf(0xFF0A0A0F, 0xFF000000))).copy(alpha = 0.72f),
                maxLines = 1,
                overflow = TextOverflow.Ellipsis,
                modifier = Modifier.padding(top = 5.dp),
            )
            Row(
                modifier = Modifier.padding(top = 12.dp),
                horizontalArrangement = Arrangement.spacedBy(9.dp),
                verticalAlignment = Alignment.CenterVertically,
            ) {
                PillLabel(text = stringResource(R.string.playlist_play_all), onClick = onPlay)
                PillLabel(text = stringResource(R.string.disc_hero_refresh), onClick = onRefresh, filled = false)
            }
        }
    }
}

/** 四个入口的一格：等宽单色玻璃，图标在上、文字在下 */
@Composable
private fun QuickTile(
    iconRes: Int,
    label: String,
    modifier: Modifier = Modifier,
    onClick: () -> Unit,
) {
    val palette = LocalCatClawPalette.current
    Column(
        modifier = modifier
            .height(64.dp)
            .glassCard(palette = palette, corner = 18)
            .clickable(onClick = onClick),
        horizontalAlignment = Alignment.CenterHorizontally,
        verticalArrangement = Arrangement.Center,
    ) {
        Icon(
            painter = painterResource(iconRes),
            contentDescription = null,
            tint = Color(palette.textSecondary),
            modifier = Modifier.size(18.dp),
        )
        Text(
            text = label,
            fontSize = 10.5.sp,
            color = Color(palette.textPrimary),
            maxLines = 1,
            modifier = Modifier.padding(top = 6.dp),
        )
    }
}

/** 货架标题行：左标题、右「查看全部」 */
@Composable
private fun RailHead(title: String, onSeeAll: () -> Unit, primary: Color, secondary: Color) {
    Row(
        modifier = Modifier.fillMaxWidth().padding(horizontal = 18.dp, vertical = 6.dp),
        verticalAlignment = Alignment.CenterVertically,
    ) {
        Text(title, fontSize = 17.sp, fontWeight = FontWeight.Bold, color = primary, modifier = Modifier.weight(1f))
        Text(
            text = stringResource(R.string.disc_see_all) + " ›",
            fontSize = 12.sp,
            color = secondary,
            modifier = Modifier.clip(RoundedCornerShape(8.dp)).clickable(onClick = onSeeAll).padding(horizontal = 4.dp, vertical = 6.dp),
        )
    }
}

/** 货架上的歌曲卡：封面 + 标题 + 歌手，右下角一颗小播放点 */
@Composable
private fun SongRailCard(
    song: SongListItem,
    /** 横屏面板高度按屏高自适应，封面边长由调用方算好传进来 */
    cover: androidx.compose.ui.unit.Dp = 104.dp,
    /** 横屏面板只有 ~120dp，放不下歌手行时只留标题（2026-11-09 真机 dump 量到歌手被裁剩 9dp） */
    showArtist: Boolean = true,
    onClick: () -> Unit,
) {
    val palette = LocalCatClawPalette.current
    Column(modifier = Modifier.width(cover).clip(RoundedCornerShape(12.dp)).clickable(onClick = onClick)) {
        Box(modifier = Modifier.size(cover)) {
            CoverTile(
                coverArtPath = song.coverArtPath,
                audioPath = song.filePath,
                maxPx = 320,
                modifier = Modifier.fillMaxSize().clip(RoundedCornerShape(12.dp)),
            ) {}
            Box(
                modifier = Modifier
                    .align(Alignment.BottomEnd)
                    .padding(6.dp)
                    .size(22.dp)
                    .clip(CircleShape)
                    .background(Color(palette.primaryDark).copy(alpha = 0.72f)),
                contentAlignment = Alignment.Center,
            ) {
                Icon(
                    painter = painterResource(R.drawable.ic_play),
                    contentDescription = null,
                    tint = Color(inkOnGradient(listOf(0xFF0A0A0F, 0xFF000000))),
                    modifier = Modifier.size(11.dp),
                )
            }
        }
        Text(
            text = song.title,
            fontSize = 12.sp,
            fontWeight = FontWeight.SemiBold,
            color = Color(palette.textPrimary),
            maxLines = 1,
            overflow = TextOverflow.Ellipsis,
            modifier = Modifier.padding(top = 7.dp),
        )
        if (showArtist) {
            Text(
                text = song.artist,
                fontSize = 10.5.sp,
                color = Color(palette.textSecondary),
                maxLines = 1,
                overflow = TextOverflow.Ellipsis,
                modifier = Modifier.padding(top = 2.dp),
            )
        }
    }
}
/** 货架上的专辑卡：比网格卡小一号，单行能露 3.5 张 */
@Composable
private fun RailAlbumCard(
    album: com.catclaw.music.kt.data.db.AlbumListItem,
    cover: androidx.compose.ui.unit.Dp = 104.dp,
    showArtist: Boolean = true,
    onClick: () -> Unit,
) {
    val palette = LocalCatClawPalette.current
    Column(modifier = Modifier.width(cover).clip(RoundedCornerShape(12.dp)).clickable(onClick = onClick)) {
        CoverTile(
            coverArtPath = album.coverArtPath,
            audioPath = album.sampleFilePath,
            maxPx = 320,
            modifier = Modifier.size(cover).clip(RoundedCornerShape(12.dp)),
        ) {}
        Text(
            text = album.title,
            fontSize = 12.sp,
            fontWeight = FontWeight.SemiBold,
            color = Color(palette.textPrimary),
            maxLines = 1,
            overflow = TextOverflow.Ellipsis,
            modifier = Modifier.padding(top = 7.dp),
        )
        if (showArtist) {
            Text(
                text = album.artist,
                fontSize = 10.5.sp,
                color = Color(palette.textSecondary),
                maxLines = 1,
                overflow = TextOverflow.Ellipsis,
                modifier = Modifier.padding(top = 2.dp),
            )
        }
    }
}
@Composable
private fun TodayFocusCard(song: SongListItem, onPlay: () -> Unit, onRefresh: () -> Unit) {
    val palette = LocalCatClawPalette.current
    Column(modifier = Modifier.fillMaxWidth().glassCard(palette = palette, corner = 30).padding(16.dp)) {
        Row(verticalAlignment = Alignment.CenterVertically) {
            Text(
                text = stringResource(R.string.disc_tag_daily) + " · " + stringResource(R.string.disc_hero_by_taste),
                fontSize = 11.sp,
                color = Color(badgeTintOn(palette, palette.primaryLight)),
                modifier = Modifier.weight(1f),
            )
            Text(
                text = stringResource(R.string.disc_hero_refresh),
                fontSize = 11.sp,
                color = Color(palette.textSecondary),
                modifier = Modifier
                    .clip(RoundedCornerShape(999.dp))
                    .border(1.dp, Color(palette.textPrimary).copy(alpha = 0.18f), RoundedCornerShape(999.dp))
                    .clickable(onClick = onRefresh)
                    .padding(horizontal = 10.dp, vertical = 4.dp),
            )
        }
        Row(modifier = Modifier.padding(top = 14.dp), verticalAlignment = Alignment.CenterVertically) {
            CoverTile(
                coverArtPath = song.coverArtPath,
                audioPath = song.filePath,
                maxPx = 512,
                modifier = Modifier
                    .size(92.dp)
                    .clip(RoundedCornerShape(24.dp)),
            ) {}
            Spacer(Modifier.width(15.dp))
            Column(modifier = Modifier.weight(1f)) {
                Text(
                    text = song.title,
                    fontSize = 24.sp,
                    fontWeight = FontWeight.ExtraBold,
                    color = Color(palette.textPrimary),
                    maxLines = 1,
                    overflow = TextOverflow.Ellipsis,
                )
                Text(
                    text = listOf(song.artist, song.album).filter { it.isNotBlank() }.joinToString(" · "),
                    fontSize = 13.sp,
                    color = Color(palette.textSecondary),
                    maxLines = 1,
                    overflow = TextOverflow.Ellipsis,
                    modifier = Modifier.padding(top = 6.dp),
                )
            }
            Spacer(Modifier.width(10.dp))
            Box(
                modifier = Modifier
                    .size(56.dp)
                    .clip(CircleShape)
                    .background(Brush.linearGradient(listOf(Color(palette.primaryLight), Color(palette.primaryDark))))
                    .clickable(onClick = onPlay),
                contentAlignment = Alignment.Center,
            ) {
                Icon(
                    painter = painterResource(R.drawable.ic_play),
                    contentDescription = stringResource(R.string.play_or_pause),
                    tint = Color(inkOnGradient(listOf(palette.primaryLight, palette.primaryDark))),
                    modifier = Modifier.size(24.dp),
                )
            }
        }
    }
}

/** 多色玻璃卡：玻璃自身带色（半透明渐变 + 亮描边），环境色斑从里面透出来 */


@Composable
private fun AlbumCard(album: com.catclaw.music.kt.data.db.AlbumListItem, onClick: () -> Unit) {
    val palette = LocalCatClawPalette.current
    Box(
        modifier = Modifier
            .width(150.dp)
            .clip(RoundedCornerShape(24.dp))
            .clickable(onClick = onClick),
    ) {
        // 专辑级 CoverArtPath 本地曲一直是空，所以借代表曲的内嵌图；取不到才留渐变块
        CoverTile(
            coverArtPath = album.coverArtPath,
            audioPath = album.sampleFilePath,
            maxPx = 320,
            modifier = Modifier
                .fillMaxWidth()
                .aspectRatio(1f)
                .clip(RoundedCornerShape(24.dp))
                .background(Brush.linearGradient(listOf(Color(0xFF8FA3CC), Color(0xFF7ED8C3))))
                .border(1.dp, Color(palette.textPrimary).copy(alpha = 0.16f), RoundedCornerShape(24.dp)),
        ) {}
        // 玻璃信息条压在封面下缘：歌名/歌手浮在图上
        Column(
            modifier = Modifier
                .align(Alignment.BottomStart)
                .padding(8.dp)
                .fillMaxWidth()
                .clip(RoundedCornerShape(16.dp))
                .background(Color(palette.textPrimary).copy(alpha = 0.42f))
                .border(1.dp, Color(palette.textPrimary).copy(alpha = 0.25f), RoundedCornerShape(16.dp))
                .padding(horizontal = 10.dp, vertical = 7.dp),
        ) {
            Text(
                text = album.title,
                fontSize = 12.5.sp,
                fontWeight = FontWeight.SemiBold,
                color = Color(highlightOnGlassCard(palette)),
                maxLines = 1,
                overflow = TextOverflow.Ellipsis,
            )
            Text(
                text = album.artist,
                fontSize = 10.5.sp,
                color = Color(palette.textSecondary),
                maxLines = 1,
                overflow = TextOverflow.Ellipsis,
            )
        }
    }
}

@Composable
private fun ArtistBubble(artist: com.catclaw.music.kt.data.db.ArtistListItem, onClick: () -> Unit) {
    val palette = LocalCatClawPalette.current
    Column(
        modifier = Modifier
            .width(74.dp)
            .clip(RoundedCornerShape(18.dp))
            .clickable(onClick = onClick),
        horizontalAlignment = Alignment.CenterHorizontally,
    ) {
        CoverTile(
            coverArtPath = artist.cover,
            audioPath = artist.sampleFilePath,
            maxPx = 320,
            modifier = Modifier
                .size(66.dp)
                .clip(CircleShape),
        ) {
            Text(
                artist.name.take(1),
                fontSize = 22.sp,
                fontWeight = FontWeight.Bold,
                color = Color(inkOnGradient(listOf(palette.primaryLight, palette.primary))),
            )
        }
        Text(artist.name, fontSize = 11.5.sp, color = Color(palette.textPrimary), maxLines = 1, overflow = TextOverflow.Ellipsis, modifier = Modifier.padding(top = 6.dp))
    }
}

/** 歌手网格按行虚拟化：一行 3 个 item，滚出视口的照片不解码 */
private fun LazyListScope.artistItems(
    rows: List<com.catclaw.music.kt.data.db.ArtistListItem>,
    onOpen: (LibraryDetail) -> Unit,
) {
    rows.chunked(3).forEachIndexed { i, line ->
        item("artist$i") {
        val palette = LocalCatClawPalette.current
            Row(modifier = Modifier.fillMaxWidth().padding(horizontal = 16.dp, vertical = 7.dp), horizontalArrangement = Arrangement.spacedBy(12.dp)) {
                line.forEach { artist ->
                    Box(modifier = Modifier.weight(1f), contentAlignment = Alignment.Center) {
                        Column(
                            horizontalAlignment = Alignment.CenterHorizontally,
                            // 点可点节点在外层：先 clickable 再让内部各件自己排布
                            modifier = Modifier
                                .clip(RoundedCornerShape(18.dp))
                                .clickable { onOpen(LibraryDetail(LibraryKind.Artists, artist.id, artist.name)) },
                        ) {
                            CoverTile(
                                coverArtPath = artist.cover,
                                audioPath = artist.sampleFilePath,
                                maxPx = 320,
                                modifier = Modifier
                                    .size(84.dp)
                                    .clip(CircleShape),
                            ) {
                                Text(
                                    artist.name.take(1),
                                    fontSize = 26.sp,
                                    fontWeight = FontWeight.Bold,
                                    color = Color(inkOnGradient(listOf(palette.primary, palette.primaryDark))),
                                )
                            }
                            Text(artist.name, fontSize = 13.sp, color = Color(palette.textPrimary), maxLines = 1, overflow = TextOverflow.Ellipsis)
                            Text(stringResource(R.string.playlist_count, artist.songCount), fontSize = 11.sp, color = Color(palette.textSecondary))
                        }
                    }
                }
                repeat(3 - line.size) { Spacer(Modifier.weight(1f)) }
            }
        }
    }
}

/** 专辑网格按行虚拟化：一行 2 个 item */
private fun LazyListScope.albumItems(
    rows: List<com.catclaw.music.kt.data.db.AlbumListItem>,
    onOpen: (LibraryDetail) -> Unit,
) {
    rows.chunked(2).forEachIndexed { i, line ->
        item("album$i") {
            Row(
                modifier = Modifier.fillMaxWidth().padding(horizontal = 16.dp, vertical = 6.dp),
                horizontalArrangement = Arrangement.spacedBy(10.dp),
            ) {
                line.forEach { album ->
                    Box(modifier = Modifier.weight(1f)) {
                        AlbumCard(album) { onOpen(LibraryDetail(LibraryKind.Albums, album.id, album.title)) }
                    }
                }
                repeat(2 - line.size) { Spacer(Modifier.weight(1f)) }
            }
        }
    }
}

@Composable
private fun StatsPanel(cells: List<Pair<String, String>>, onOpenFull: () -> Unit) {
    val palette = LocalCatClawPalette.current
    Column(modifier = Modifier.padding(horizontal = 18.dp)) {
        cells.chunked(2).forEach { line ->
            Row(modifier = Modifier.fillMaxWidth().padding(bottom = 10.dp), horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                line.forEach { (value, label) ->
                    val primary = Color(palette.textPrimary)
                    Column(
                        modifier = Modifier
                            .weight(1f)
                            .glassCard(palette = palette, corner = 20)
                            .padding(14.dp),
                    ) {
                        Text(value, fontSize = 20.sp, fontWeight = FontWeight.Bold, color = primary)
                        Text(label, fontSize = 11.sp, color = Color(palette.textSecondary), modifier = Modifier.padding(top = 2.dp))
                    }
                }
            }
        }
        // 四格只说"听过多少"，趋势/时段/连续/环比在统计页 —— 没有这个入口，那页就没人看得到
        PillLabel(text = stringResource(R.string.StatsOpenFull), onClick = onOpenFull)
    }
}

@Composable
private fun SectionHead(title: String) {
    val palette = LocalCatClawPalette.current
    Text(
        text = title,
        fontSize = 18.sp,
        fontWeight = FontWeight.Bold,
        color = Color(palette.textPrimary),
        modifier = Modifier.padding(start = 18.dp, top = 16.dp, bottom = 8.dp),
    )
}

@Composable
private fun SongRows(
    title: String,
    rows: List<SongListItem>,
    primary: Color,
    secondary: Color,
    onClick: (Int) -> Unit,
) {
    if (rows.isEmpty()) return
    SectionHead(title)
    Column(
        modifier = Modifier
            .padding(horizontal = 14.dp)
            .glassCard(palette = LocalCatClawPalette.current, corner = 26)
            .padding(6.dp),
    ) {
        rows.forEachIndexed { index, song -> SongRowItem(song, index, primary, secondary, onClick) }
    }
}

@Composable
private fun SongRowItem(
    song: SongListItem,
    index: Int,
    primary: Color,
    secondary: Color,
    onClick: (Int) -> Unit,
) {
    Row(
        modifier = Modifier
            .fillMaxWidth()
            .clip(RoundedCornerShape(20.dp))
            .clickable { onClick(index) }
            .padding(horizontal = 8.dp, vertical = 7.dp),
        verticalAlignment = Alignment.CenterVertically,
    ) {
        CoverTile(
            coverArtPath = song.coverArtPath,
            audioPath = song.filePath,
            modifier = Modifier
                .size(36.dp)
                .clip(RoundedCornerShape(12.dp))
                .background(primary.copy(alpha = 0.08f)),
        ) {
            Text(
                text = (index + 1).toString(),
                fontSize = 12.sp,
                fontWeight = FontWeight.SemiBold,
                color = secondary,
            )
        }
        Spacer(Modifier.width(11.dp))
        Column(modifier = Modifier.weight(1f)) {
            Text(
                text = song.title,
                style = MaterialTheme.typography.bodyLarge,
                color = primary,
                maxLines = 1,
                overflow = TextOverflow.Ellipsis,
            )
            Text(
                text = listOf(song.artist, song.album).filter { it.isNotBlank() }.joinToString(" · "),
                style = MaterialTheme.typography.bodySmall,
                color = secondary,
                maxLines = 1,
                overflow = TextOverflow.Ellipsis,
            )
        }
        Text(
            text = "%d:%02d".format(song.durationSeconds / 60, song.durationSeconds % 60),
            style = MaterialTheme.typography.labelSmall,
            color = secondary,
            modifier = Modifier.padding(start = 8.dp),
        )
    }
}

@Composable
private fun CatChip(label: String, selected: Boolean, modifier: Modifier = Modifier, onClick: () -> Unit) {
    // 视觉定义在 GlassChip；这里只负责在等分槽位里居中
    Box(modifier = modifier, contentAlignment = Alignment.Center) {
        GlassChip(label = label, selected = selected, onClick = onClick)
    }
}

@Composable
private fun GlassIcon(
    iconRes: Int,
    description: String,
    /** 横屏头部只有一条 44dp 的窄栏，44dp 的方块会顶满，收到 38dp */
    size: androidx.compose.ui.unit.Dp = 44.dp,
    onClick: () -> Unit,
) {
    val palette = LocalCatClawPalette.current
    Box(
        modifier = Modifier
            .size(size)
            .glassCard(palette = palette, corner = 16)
            .clickable(onClick = onClick),
        contentAlignment = Alignment.Center,
    ) {
        Icon(
            painter = painterResource(iconRes),
            contentDescription = description,
            tint = Color(palette.textPrimary),
            modifier = Modifier.size(size * 0.5f),
        )
    }
}

/** MAUI 的四段问候：5–11 晨、11–18 午、18–24 晚、0–5 凌晨 */
@Composable
private fun greeting(): String {
    val hour = remember { Calendar.getInstance().get(Calendar.HOUR_OF_DAY) }
    return stringResource(
        when (hour) {
            in 5..10 -> R.string.disc_greeting_morning
            in 11..17 -> R.string.disc_greeting_afternoon
            in 18..23 -> R.string.disc_greeting_evening
            else -> R.string.disc_greeting_night
        },
    )
}

// ─────────────────────────── 横屏发现页（双栏舞台） ───────────────────────────

/**
 * 横屏四入口的色相：冷蓝 / 青 / 紫 / 琥珀。
 * 全部低饱和 —— 用户 2026-11-07 明确要求删掉粉色，这里也不引入高饱和色块。
 */
private const val QUICK_BLUE = 0xFF7C9BE8L
private const val QUICK_TEAL = 0xFF5FBEAEL
private const val QUICK_VIOLET = 0xFF9A8DD6L
private const val QUICK_AMBER = 0xFFD8AA6AL

/** 迷你条浮在底部（高 60 + 下边距 12 + 4dp 呼吸），舞台留 80dp 给它，内容就不会被压住 */
private val LandscapeChromeReserve = 80.dp

/**
 * 横屏发现页 = 一整块「双栏舞台」，高度由调用方给（`fillParentMaxHeight()`，恰好一屏）。
 *
 * 换掉竖屏纵向列表的原因：411dp 高的画布只够「标题 + 英雄卡 + 一排入口」，
 * 竖着堆到第四块就只能被迷你条遮住 —— 2026-11-09 真机截图里四个入口只剩图标头顶。
 * 这里把宽度用起来：左栏约 38% 是每日推荐的实图大卡，右栏上排四入口、下排推荐货架。
 */

/** 发现页独立搜索页：返回 + 自动聚焦输入框 + 搜索结果（与二级页搜索同一套交互） */
@Composable
private fun DiscoverSearchPage(
    query: String,
    results: List<SongListItem>,
    onQueryChange: (String) -> Unit,
    onClose: () -> Unit,
    onPlay: (Int) -> Unit,
    primary: Color,
    secondary: Color,
) {
    val focus = remember { androidx.compose.ui.focus.FocusRequester() }
    LaunchedEffect(Unit) { focus.requestFocus() }
    Column(Modifier.fillMaxSize()) {
        Row(
            modifier = Modifier
                .fillMaxWidth()
                .padding(start = 16.dp, end = 16.dp, top = 12.dp, bottom = 8.dp),
            verticalAlignment = Alignment.CenterVertically,
            horizontalArrangement = Arrangement.spacedBy(10.dp),
        ) {
            BackTile(onBack = onClose, description = stringResource(R.string.queue_back))
            GlassSearchField(
                query = query,
                onChange = onQueryChange,
                placeholder = stringResource(R.string.disc_search_hint),
                modifier = Modifier.weight(1f),
                compact = true,
                focusRequester = focus,
            )
        }
        when {
            query.isBlank() -> EmptyState(
                hint = stringResource(R.string.disc_search_hint),
                iconRes = R.drawable.ic_search,
                modifier = Modifier.padding(horizontal = 18.dp, vertical = 24.dp),
            )
            results.isEmpty() -> EmptyState(
                hint = stringResource(R.string.search_none, query.trim()),
                iconRes = R.drawable.ic_search,
                modifier = Modifier.padding(horizontal = 18.dp, vertical = 10.dp),
            )
            else -> LazyColumn(Modifier.fillMaxSize()) {
                item {
                    SongRows(stringResource(R.string.TabSongs), results, primary, secondary) { onPlay(it) }
                }
            }
        }
    }
}

@Composable
private fun LandscapeDiscover(
    modifier: Modifier = Modifier,
    query: String,
    results: List<SongListItem>,
    sections: SearchViewModel.Sections,
    albums: List<com.catclaw.music.kt.data.db.AlbumListItem>,
    artists: List<com.catclaw.music.kt.data.db.ArtistListItem>,
    primary: Color,
    secondary: Color,
    searchOpen: Boolean,
    onToggleSearch: () -> Unit,
    onQueryChange: (String) -> Unit,
    onOpenStats: () -> Unit,
    onAskYuki: () -> Unit,
    onOpenDetail: (LibraryDetail) -> Unit,
    onOpenSongs: () -> Unit,
    onOpenAlbums: () -> Unit,
    onOpenArtists: () -> Unit,
    neteaseLoggedIn: Boolean,
    viewModel: SearchViewModel,
) {
    val palette = LocalCatClawPalette.current
    Column(modifier.fillMaxSize()) {
        // 头部压到一条 44dp 的窄栏：横屏没有纵向余量留给 30sp 的大标题
        Row(
            modifier = Modifier
                .fillMaxWidth()
                .padding(start = 18.dp, end = 18.dp, top = 6.dp, bottom = 6.dp),
            verticalAlignment = Alignment.CenterVertically,
        ) {
            Text(
                text = stringResource(R.string.TabDiscover),
                fontSize = 21.sp,
                fontWeight = FontWeight.Bold,
                color = primary,
            )
            Box(
                modifier = Modifier
                    .padding(horizontal = 12.dp)
                    .size(width = 1.dp, height = 15.dp)
                    .background(Color(palette.textPrimary).copy(alpha = 0.22f)),
            )
            Text(
                text = greeting(),
                fontSize = 12.sp,
                color = secondary,
                maxLines = 1,
                overflow = TextOverflow.Ellipsis,
                modifier = Modifier.weight(1f),
            )
            GlassIcon(
                R.drawable.ic_search,
                stringResource(R.string.TabSearch),
                size = 38.dp,
                onClick = onToggleSearch,
            )
            Spacer(Modifier.width(8.dp))
            GlassIcon(
                R.drawable.ic_stats,
                stringResource(R.string.lib_open_stats),
                size = 38.dp,
                onClick = onOpenStats,
            )
        }

        if (searchOpen || query.isNotBlank()) {
            GlassSearchField(
                query = query,
                onChange = onQueryChange,
                placeholder = stringResource(R.string.disc_search_hint),
                modifier = Modifier.padding(horizontal = 18.dp, vertical = 4.dp),
            )
            // 刚点开、还没输入时不要摆「没找到与「」相关」的结论 —— 列表干脆不出（输入框已占住焦点）
            if (query.isNotBlank()) {
                LazyColumn(
                    modifier = Modifier.fillMaxWidth().weight(1f),
                    contentPadding = PaddingValues(bottom = LandscapeChromeReserve),
                ) {
                    if (results.isEmpty()) {
                        item("none") {
                            EmptyState(
                                hint = stringResource(R.string.search_none, query.trim()),
                                iconRes = R.drawable.ic_search,
                                modifier = Modifier.padding(horizontal = 18.dp, vertical = 10.dp),
                            )
                        }
                    } else {
                        item("results") {
                            SongRows(stringResource(R.string.TabSongs), results, primary, secondary) {
                                viewModel.play(results, it)
                            }
                        }
                    }
                }
            }
        } else {
            Row(
                modifier = Modifier
                    .fillMaxWidth()
                    .weight(1f)
                    .padding(start = 18.dp, end = 18.dp, top = 2.dp, bottom = LandscapeChromeReserve),
                horizontalArrangement = Arrangement.spacedBy(14.dp),
            ) {
                val hero = sections.daily.firstOrNull()
                if (hero != null) {
                    LandscapeHeroCard(
                        song = hero,
                        onPlay = { viewModel.play(sections.daily, 0) },
                        onRefresh = { viewModel.reload() },
                        // 0.66 : 1 → 主卡约占内容宽度的 38%，与设计稿一致
                        modifier = Modifier.weight(0.66f).fillMaxHeight(),
                    )
                } else {
                    Box(
                        modifier = Modifier.weight(0.66f).fillMaxHeight(),
                        contentAlignment = Alignment.Center,
                    ) {
                        Text(stringResource(R.string.disc_empty), fontSize = 12.sp, color = secondary)
                    }
                }

                Column(
                    modifier = Modifier.weight(1f).fillMaxHeight(),
                    verticalArrangement = Arrangement.spacedBy(10.dp),
                ) {
                    Row(
                        modifier = Modifier.fillMaxWidth().height(64.dp),
                        horizontalArrangement = Arrangement.spacedBy(10.dp),
                    ) {
                        LandscapeQuickTile(
                            R.drawable.ic_spark,
                            stringResource(R.string.disc_tag_ai),
                            QUICK_BLUE,
                            Modifier.weight(1f),
                            onAskYuki,
                        )
                        LandscapeQuickTile(
                            R.drawable.ic_shuffle,
                            stringResource(R.string.disc_tag_shuffle),
                            QUICK_TEAL,
                            Modifier.weight(1f),
                            { viewModel.shuffleAll() },
                        )
                        if (neteaseLoggedIn) {
                            LandscapeQuickTile(
                                R.drawable.ic_private_fm,
                                stringResource(R.string.netease_private_fm),
                                QUICK_TEAL,
                                Modifier.weight(1f),
                                viewModel::startNeteaseFm,
                            )
                        }
                        LandscapeQuickTile(
                            R.drawable.ic_rank,
                            stringResource(R.string.disc_tag_top),
                            QUICK_VIOLET,
                            Modifier.weight(1f),
                            { if (sections.frequent.isNotEmpty()) viewModel.play(sections.frequent, 0) },
                        )
                        LandscapeQuickTile(
                            R.drawable.ic_favorite,
                            stringResource(R.string.disc_favorite),
                            QUICK_AMBER,
                            Modifier.weight(1f),
                            { viewModel.playFavorites() },
                        )
                    }
                    val recSongs = (sections.daily + sections.fresh + sections.frequent)
                        .distinctBy { it.id }
                        .take(10)
                    LandscapeRailPanel(
                        recSongs = recSongs,
                        albums = albums,
                        artists = artists,
                        onOpenSongs = onOpenSongs,
                        onOpenAlbums = onOpenAlbums,
                        onOpenArtists = onOpenArtists,
                        onOpenDetail = onOpenDetail,
                        viewModel = viewModel,
                        modifier = Modifier.weight(1f),
                    )
                }
            }
        }
    }
}

/**
 * 横屏主卡：封面铺满整张卡，底部重渐变压住文字。
 * 与竖屏那张 268dp 的横幅不同 —— 这里高度由舞台决定（约 259dp），宽度约 300dp，
 * 所以文字块要更紧凑：标题最多两行、胶囊保持 44dp 触达。
 */
@Composable
private fun LandscapeHeroCard(
    song: SongListItem,
    onPlay: () -> Unit,
    onRefresh: () -> Unit,
    modifier: Modifier = Modifier,
) {
    val palette = LocalCatClawPalette.current
    val ink = Color(palette.textPrimary)
    Box(modifier = modifier.clip(RoundedCornerShape(24.dp))) {
        CoverTile(
            coverArtPath = song.coverArtPath,
            audioPath = song.filePath,
            maxPx = 640,
            modifier = Modifier.fillMaxSize(),
        ) {}
        Box(
            modifier = Modifier.fillMaxSize().background(
                Brush.verticalGradient(
                    listOf(
                        Color.Black.copy(alpha = 0.18f),
                        Color.Transparent,
                        Color.Black.copy(alpha = 0.35f),
                        Color.Black.copy(alpha = 0.90f),
                    ),
                ),
            ),
        )
        // 顶部徽标：玻璃片浮在封面上，和竖屏那张卡同一处语言
        Text(
            text = stringResource(R.string.disc_hero_by_taste),
            fontSize = 10.sp,
            color = ink,
            maxLines = 1,
            modifier = Modifier
                .padding(14.dp)
                .clip(RoundedCornerShape(50))
                .background(ink.copy(alpha = 0.16f))
                .border(1.dp, ink.copy(alpha = 0.26f), RoundedCornerShape(50))
                .padding(horizontal = 10.dp, vertical = 5.dp),
        )
        Column(
            modifier = Modifier.align(Alignment.BottomStart).fillMaxWidth().padding(16.dp),
        ) {
            Text(
                text = stringResource(R.string.disc_tag_daily),
                fontSize = 9.5.sp,
                color = ink.copy(alpha = 0.66f),
                maxLines = 1,
            )
            Text(
                text = song.title,
                fontSize = 19.sp,
                fontWeight = FontWeight.Bold,
                color = ink,
                maxLines = 2,
                overflow = TextOverflow.Ellipsis,
                modifier = Modifier.padding(top = 5.dp),
            )
            Text(
                text = song.artist,
                fontSize = 11.5.sp,
                color = ink.copy(alpha = 0.72f),
                maxLines = 1,
                overflow = TextOverflow.Ellipsis,
                modifier = Modifier.padding(top = 4.dp),
            )
            Row(
                modifier = Modifier.padding(top = 12.dp),
                horizontalArrangement = Arrangement.spacedBy(8.dp),
                verticalAlignment = Alignment.CenterVertically,
            ) {
                PillLabel(text = stringResource(R.string.playlist_play_all), onClick = onPlay)
                PillLabel(text = stringResource(R.string.disc_hero_refresh), onClick = onRefresh, filled = false)
            }
        }
    }
}

/**
 * 横屏四入口的一格：图标在左上、文字在左下，底子是这一格自己的色相渐变。
 * 竖屏那版是等宽单色玻璃（[QuickTile]），横屏宽高比更扁，横向排布比上下堆好看。
 */
@Composable
private fun LandscapeQuickTile(
    iconRes: Int,
    label: String,
    accentArgb: Long,
    modifier: Modifier = Modifier,
    onClick: () -> Unit,
) {
    val palette = LocalCatClawPalette.current
    val ink = Color(palette.textPrimary)
    val accent = Color(accentArgb)
    val shape = RoundedCornerShape(18.dp)
    Box(
        modifier = modifier
            .fillMaxHeight()
            .clip(shape)
            .background(
                Brush.linearGradient(listOf(accent.copy(alpha = 0.34f), accent.copy(alpha = 0.05f))),
            )
            .border(1.dp, ink.copy(alpha = 0.16f), shape)
            .clickable(onClick = onClick)
            .padding(horizontal = 10.dp, vertical = 7.dp),
    ) {
        Box(
            modifier = Modifier
                .size(24.dp)
                .clip(RoundedCornerShape(8.dp))
                .background(ink.copy(alpha = 0.16f)),
            contentAlignment = Alignment.Center,
        ) {
            Icon(
                painter = painterResource(iconRes),
                contentDescription = null,
                tint = ink,
                modifier = Modifier.size(14.dp),
            )
        }
        Text(
            text = label,
            fontSize = 12.sp,
            fontWeight = FontWeight.SemiBold,
            color = ink,
            maxLines = 2,
            overflow = TextOverflow.Ellipsis,
            modifier = Modifier.align(Alignment.BottomStart),
        )
    }
}

/**
 * 横屏推荐货架：一块玻璃面板，头部是「歌曲 / 专辑 / 艺人」三个分段 + 查看全部。
 * 高度只有 ~185dp，所以卡片封面收到 96dp（竖屏货架是 104dp），保证标题与歌手不被切掉。
 */
@Composable
private fun LandscapeRailPanel(
    recSongs: List<SongListItem>,
    albums: List<com.catclaw.music.kt.data.db.AlbumListItem>,
    artists: List<com.catclaw.music.kt.data.db.ArtistListItem>,
    onOpenSongs: () -> Unit,
    onOpenAlbums: () -> Unit,
    onOpenArtists: () -> Unit,
    onOpenDetail: (LibraryDetail) -> Unit,
    viewModel: SearchViewModel,
    modifier: Modifier = Modifier,
) {
    val palette = LocalCatClawPalette.current
    val ink = Color(palette.textPrimary)
    var tab by rememberSaveable { mutableIntStateOf(0) }
    val tabs = listOf(
        stringResource(R.string.TabSongs),
        stringResource(R.string.TabAlbums),
        stringResource(R.string.TabArtists),
    )
    Column(
        modifier = modifier
            .glassCard(palette = palette, corner = 24)
            .padding(horizontal = 12.dp, vertical = 4.dp),
    ) {
        Row(modifier = Modifier.fillMaxWidth(), verticalAlignment = Alignment.CenterVertically) {
            tabs.forEachIndexed { index, label ->
                val selected = index == tab
                Text(
                    text = label,
                    fontSize = 11.5.sp,
                    fontWeight = if (selected) FontWeight.Bold else FontWeight.Medium,
                    color = if (selected) ink else Color(palette.textSecondary),
                    maxLines = 1,
                    modifier = Modifier
                        .clip(RoundedCornerShape(9.dp))
                        .background(if (selected) ink.copy(alpha = 0.14f) else Color.Transparent)
                        .clickable { tab = index }
                        .padding(horizontal = 9.dp, vertical = 4.dp),
                )
                Spacer(Modifier.width(3.dp))
            }
            Spacer(Modifier.weight(1f))
            Text(
                text = stringResource(R.string.disc_see_all) + " ›",
                fontSize = 11.sp,
                color = Color(palette.textSecondary),
                maxLines = 1,
                modifier = Modifier
                    .clip(RoundedCornerShape(8.dp))
                    .clickable {
                        when (tab) {
                            0 -> onOpenSongs()
                            1 -> onOpenAlbums()
                            else -> onOpenArtists()
                        }
                    }
                    .padding(horizontal = 4.dp, vertical = 4.dp),
            )
        }
        val empty = when (tab) {
            0 -> recSongs.isEmpty()
            1 -> albums.isEmpty()
            else -> artists.isEmpty()
        }
        if (empty) {
            Box(
                modifier = Modifier.fillMaxWidth().weight(1f),
                contentAlignment = Alignment.Center,
            ) {
                Text(stringResource(R.string.disc_empty), fontSize = 11.5.sp, color = Color(palette.textSecondary))
            }
        } else {
            // 面板只剩 ~160dp：封面按剩余高度自适应（标题 17 + 歌手 15 + 间距 10 ≈ 42dp）。
            // 写死 104dp 会把歌手那行挤出可视区 —— 2026-11-09 真机 dump 量到卡片被裁掉 21dp
            BoxWithConstraints(modifier = Modifier.fillMaxWidth().weight(1f).padding(top = 4.dp)) {
                // 卡片 = 封面 + 7dp 间距 + 一行标题（~24dp），余量全给封面；
                // 高度够时才把歌手行加回来（平板/更高的画布）
                val showArtist = maxHeight >= 145.dp
                val textBlock = if (showArtist) 48.dp else 33.dp
                val cover = (maxHeight - textBlock).coerceIn(64.dp, 132.dp)
                LazyRow(
                    modifier = Modifier.fillMaxSize(),
                    horizontalArrangement = Arrangement.spacedBy(10.dp),
                ) {
                    when (tab) {
                        0 -> itemsIndexed(recSongs, key = { _, song -> "ls${song.id}" }) { index, song ->
                            SongRailCard(song, cover = cover, showArtist = showArtist) {
                                viewModel.play(recSongs, index)
                            }
                        }
                        1 -> items(albums, key = { "la${it.id}" }) { album ->
                            RailAlbumCard(album, cover = cover, showArtist = showArtist) {
                                onOpenDetail(LibraryDetail(LibraryKind.Albums, album.id, album.title))
                            }
                        }
                        else -> items(artists, key = { "lr${it.id}" }) { artist ->
                            ArtistBubble(artist) {
                                onOpenDetail(LibraryDetail(LibraryKind.Artists, artist.id, artist.name))
                            }
                        }
                    }
                }
            }
        }
    }
}
