package com.catclaw.music.kt.ui.screen

import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.clickable
import androidx.activity.compose.BackHandler
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.PaddingValues
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.aspectRatio
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
private enum class DiscCat { Rec, Rank, Artist, Album, Stats }

@Composable
fun SearchScreen(
    active: Boolean,
    onOpenSettings: () -> Unit = {},
    onChatOpen: (Boolean) -> Unit = {},
    /** 聊天模式下外壳那条迷你播放条的高度（见 `ChatScreen.bottomReserve`） */
    chatBottomReserve: androidx.compose.ui.unit.Dp = androidx.compose.ui.unit.Dp(0f),
    viewModel: SearchViewModel = hiltViewModel(),
) {
    val palette = LocalCatClawPalette.current
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
    var cat by rememberSaveable { mutableIntStateOf(0) }
    var searchOpen by rememberSaveable { mutableStateOf(false) }

    LaunchedEffect(active) { if (active) viewModel.activate() }

    Box(modifier = Modifier.fillMaxSize()) {
        // 环境色斑层：玻璃卡片要透出颜色才像玻璃。径向渐变圆自带软边，不用 RenderEffect 模糊，
        // 滑动时每帧零额外 GPU 成本（刚把滑动卡顿治好，别再请回来一个）
        // 这层在之前排查卡顿时被误删过一次——液态玻璃没有它就不成立，恢复后别再动
        Box(modifier = Modifier.matchParentSize()) {
            Blob(Color(0xFFFF8FB8), 300.dp, x = (-70).dp, y = (-40).dp)
            Blob(Color(0xFF55D6FF), 280.dp, x = 250.dp, y = 30.dp)
            Blob(Color(0xFFB7AEFF), 260.dp, x = 30.dp, y = 420.dp)
            Blob(Color(0xFF7ED8C3), 300.dp, x = 220.dp, y = 640.dp)
        }
        // 整页 LazyColumn：歌手/专辑网格按行虚拟化，滚出视口的照片不解码不绘制——
        // 之前的 verticalScroll Column 全量组装，几十个 84dp 照片一次解码，滚动必卡
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
            contentPadding = PaddingValues(bottom = 132.dp),
        ) {
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
                    GlassIcon(R.drawable.ic_search, "搜索") { searchOpen = !searchOpen }
                    Spacer(Modifier.width(10.dp))
                    GlassIcon(R.drawable.ic_refresh, "刷新") { viewModel.reload() }
                    Spacer(Modifier.width(10.dp))
                    GlassIcon(R.drawable.ic_settings, "设置", onClick = onOpenSettings)
                }
            }

            if (searchOpen || query.isNotBlank()) {
                item("search") {
                    GlassSearchField(
                        query = query,
                        onChange = viewModel::setQuery,
                        placeholder = stringResource(R.string.disc_search_hint),
                        modifier = Modifier.padding(horizontal = 18.dp, vertical = 6.dp),
                    )
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
                item("tabs") {
                    Row(
                        modifier = Modifier.fillMaxWidth().padding(horizontal = 18.dp, vertical = 8.dp),
                        horizontalArrangement = Arrangement.spacedBy(8.dp),
                    ) {
                        val labels = listOf(
                            stringResource(R.string.disc_cat_rec), stringResource(R.string.disc_cat_rank),
                            stringResource(R.string.disc_cat_artist), stringResource(R.string.disc_cat_album),
                            stringResource(R.string.disc_cat_stats),
                        )
                        labels.forEachIndexed { i, label ->
                            CatChip(label, selected = cat == i, modifier = Modifier.weight(1f)) { cat = i }
                        }
                    }
                }

                when (DiscCat.entries[cat]) {
                    DiscCat.Rec -> recommendItems(
                        viewModel = viewModel,
                        sections = sections,
                        artists = artists,
                        albums = albums,
                        primary = primary,
                        secondary = secondary,
                        onAskYuki = { chatMode = true },
                        onOpenDetail = { detail = it },
                    )
                    DiscCat.Rank -> {
                        item("rank_top") {
                            SongRows(stringResource(R.string.disc_tag_top), sections.frequent, primary, secondary) {
                                viewModel.play(sections.frequent, it)
                            }
                        }
                        item("rank_fresh") {
                            SongRows(stringResource(R.string.disc_shelf_recent_added), sections.fresh, primary, secondary) {
                                viewModel.play(sections.fresh, it)
                            }
                        }
                    }
                    DiscCat.Artist -> artistItems(artists) { detail = it }
                    DiscCat.Album -> albumItems(albums) { detail = it }
                    DiscCat.Stats -> item("stats") {
                        StatsPanel(
                            listOf(
                                artists.size.toString() to stringResource(R.string.lib_stat_artists),
                                albums.size.toString() to stringResource(R.string.lib_stat_albums),
                                stats.totalPlays.toString() to stringResource(R.string.disc_stat_total_plays),
                                stats.songsPlayed.toString() to stringResource(R.string.disc_stat_songs_played),
                            ),
                            onOpenFull = { statsOpen = true },
                        )
                    }
                }
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
    onOpenDetail: (LibraryDetail) -> Unit,
) {
    // ① 今日焦点：玻璃大卡透出环境色斑
    val hero = sections.daily.firstOrNull()
    item("rec_hero") {
        StaggerIn(index = 0) {
            Column(modifier = Modifier.padding(start = 16.dp, end = 16.dp, bottom = 12.dp)) {
                if (hero != null) {
                    TodayFocusCard(
                        song = hero,
                        onPlay = { viewModel.play(sections.daily, 0) },
                        onRefresh = { viewModel.reload() },
                    )
                }
            }
        }
    }

    // 曲库为空时其余板块全空，得把「先去扫描」这句显式说出来（AI 卡不依赖曲库仍可用）
    if (hero == null && albums.isEmpty() && artists.isEmpty() && sections.daily.isEmpty()) {
        item("rec_empty") {
            EmptyState(
                hint = stringResource(R.string.disc_empty),
                iconRes = R.drawable.ic_library,
                modifier = Modifier.padding(horizontal = 18.dp, vertical = 8.dp),
            )
        }
    }

    // ② 四张多色玻璃卡：入口功能与推荐内容分开
    item("rec_quick") {
        StaggerIn(index = 1) {
            Row(
                modifier = Modifier.fillMaxWidth().padding(horizontal = 16.dp),
                horizontalArrangement = Arrangement.spacedBy(10.dp),
            ) {
                MultiGlassCard(
                    label = stringResource(R.string.disc_tag_ai),
                    stops = listOf(0xFF7ED8C3L, 0xFF667EEAL),
                    modifier = Modifier.weight(1f),
                    onClick = onAskYuki,
                )
                MultiGlassCard(
                    label = stringResource(R.string.disc_tag_daily),
                    stops = listOf(0xFFFB8B24L, 0xFFF56FA0L),
                    modifier = Modifier.weight(1f),
                    onClick = { if (sections.daily.isNotEmpty()) viewModel.play(sections.daily, 0) },
                )
                MultiGlassCard(
                    label = stringResource(R.string.disc_tag_shuffle),
                    stops = listOf(0xFF55D6FFL, 0xFF6250F6L),
                    modifier = Modifier.weight(1f),
                    onClick = { if (sections.daily.isNotEmpty()) viewModel.play(sections.daily, Random.nextInt(sections.daily.size)) },
                )
                MultiGlassCard(
                    label = stringResource(R.string.disc_tag_top),
                    stops = listOf(0xFFFF7AAEL, 0xFFC06C84L),
                    modifier = Modifier.weight(1f),
                    onClick = { if (sections.frequent.isNotEmpty()) viewModel.play(sections.frequent, 0) },
                )
            }
        }
    }

    // ③ 推荐专辑：玻璃容器 + 封面下缘玻璃信息条
    if (albums.isNotEmpty()) item("rec_albums") {
        StaggerIn(index = 2) {
            Column {
                SectionHead(stringResource(R.string.disc_albums_rec))
                Box(modifier = Modifier.padding(horizontal = 16.dp)) {
                    Column(
                        modifier = Modifier
                            .glassCard(palette = LocalCatClawPalette.current, corner = 30)
                            .padding(vertical = 13.dp),
                    ) {
                        LazyRow(
                            contentPadding = PaddingValues(horizontal = 12.dp),
                            horizontalArrangement = Arrangement.spacedBy(12.dp),
                        ) {
                            items(albums.take(12), key = { "a${it.id}" }) { album ->
                                AlbumCard(album) {
                                    onOpenDetail(LibraryDetail(LibraryKind.Albums, album.id, album.title))
                                }
                            }
                        }
                    }
                }
            }
        }
    }

    // ④ 每日推荐：标题在包裹上方（与推荐专辑同一约定）；卡内只留口味说明和随机播放
    if (sections.daily.isNotEmpty()) item("rec_daily") {
        StaggerIn(index = 3) {
            Column {
                SectionHead(stringResource(R.string.disc_tag_daily))
                Column(
                    modifier = Modifier
                        .padding(horizontal = 16.dp)
                        .glassCard(palette = LocalCatClawPalette.current, corner = 30)
                        .padding(14.dp),
                ) {
                    Row(
                        modifier = Modifier.fillMaxWidth().padding(start = 8.dp, bottom = 8.dp),
                        verticalAlignment = Alignment.CenterVertically,
                    ) {
                        Text(
                            text = stringResource(R.string.disc_daily_by_taste),
                            fontSize = 11.5.sp,
                            color = secondary,
                            modifier = Modifier.weight(1f),
                        )
                        PillLabel(
                            text = stringResource(R.string.disc_tag_shuffle),
                            onClick = { if (sections.daily.isNotEmpty()) viewModel.play(sections.daily, Random.nextInt(sections.daily.size)) },
                        )
                    }
                    sections.daily.take(8).forEachIndexed { index, song ->
                        SongRowItem(song, index, primary, secondary) { viewModel.play(sections.daily, it) }
                    }
                }
            }
        }
    }

    // ⑤ 推荐歌手横滑
    if (artists.isNotEmpty()) item("rec_artists") {
        StaggerIn(index = 4) {
            Column {
                SectionHead(stringResource(R.string.disc_artists_rec))
                LazyRow(
                    contentPadding = PaddingValues(horizontal = 16.dp),
                    horizontalArrangement = Arrangement.spacedBy(12.dp),
                    modifier = Modifier.fillMaxWidth().height(120.dp),
                ) {
                    items(artists.take(20), key = { "r${it.id}" }) { artist ->
                        ArtistBubble(artist) {
                            onOpenDetail(LibraryDetail(LibraryKind.Artists, artist.id, artist.name))
                        }
                    }
                }
            }
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
private fun MultiGlassCard(label: String, stops: List<Long>, modifier: Modifier = Modifier, onClick: () -> Unit) {
    val palette = LocalCatClawPalette.current
    val shape = RoundedCornerShape(20.dp)
    // 墨色要对「α0.5 色标压在页底上的实际成色」算，而不是对纯色标算——
    // 中间调纯色上黑白都到不了 4.5，实际成色暗一档就过了（对齐 PaletteContrastTest 的量法）
    val pageBg = if (isLightPalette(palette)) 0xFFF2F3FAL else 0xFF15132EL
    val surface = stops.map { blend((it and 0x00FFFFFFL) or 0x80000000L, pageBg) }
    Box(
        modifier = modifier
            .clip(shape)
            .background(Brush.linearGradient(stops.map { Color(it).copy(alpha = 0.5f) }))
            .border(1.dp, Color(inkOnGradient(surface)).copy(alpha = 0.30f), shape)
            .clickable(onClick = onClick)
            .padding(vertical = 19.dp),
        contentAlignment = Alignment.Center,
    ) {
        Text(
            text = label,
            fontSize = 12.sp,
            fontWeight = FontWeight.SemiBold,
            color = Color(inkOnGradient(surface)),
        )
    }
}

/** 环境色斑：径向渐变圆自带软边，不用 RenderEffect 模糊 */
@Composable
private fun Blob(color: Color, sizeDp: androidx.compose.ui.unit.Dp, x: androidx.compose.ui.unit.Dp, y: androidx.compose.ui.unit.Dp) {
    Box(
        modifier = Modifier
            .offset(x = x, y = y)
            .size(sizeDp)
            .background(Brush.radialGradient(listOf(color.copy(alpha = 0.5f), Color.Transparent))),
    )
}

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
                .background(Brush.linearGradient(listOf(Color(0xFFFF8FB8), Color(0xFF7ED8C3))))
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
private fun GlassIcon(iconRes: Int, description: String, onClick: () -> Unit) {
    val palette = LocalCatClawPalette.current
    Box(
        modifier = Modifier
            .size(44.dp)
            .glassCard(palette = palette, corner = 16)
            .clickable(onClick = onClick),
        contentAlignment = Alignment.Center,
    ) {
        Icon(
            painter = painterResource(iconRes),
            contentDescription = description,
            tint = Color(palette.textPrimary),
            modifier = Modifier.size(22.dp),
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
