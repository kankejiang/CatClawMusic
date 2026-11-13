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

/** 发现页独立搜索页与横屏舞台（从 SearchScreen.kt 拆出） */
/** 发现页独立搜索页：返回 + 自动聚焦输入框 + 搜索结果（与二级页搜索同一套交互） */
@Composable
internal fun DiscoverSearchPage(
    query: String,
    results: List<SongListItem>,
    online: List<com.catclaw.music.kt.data.netease.OnlineSong>,
    onlineLoading: Boolean,
    onQueryChange: (String) -> Unit,
    onClose: () -> Unit,
    onPlay: (Int) -> Unit,
    onPlayOnline: (Int) -> Unit,
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
            results.isEmpty() && online.isEmpty() && !onlineLoading -> EmptyState(
                hint = stringResource(R.string.search_none, query.trim()),
                iconRes = R.drawable.ic_search,
                modifier = Modifier.padding(horizontal = 18.dp, vertical = 10.dp),
            )
            else -> LazyColumn(Modifier.fillMaxSize()) {
                if (results.isNotEmpty()) {
                    item {
                        SongRows(stringResource(R.string.TabSongs), results, primary, secondary) { onPlay(it) }
                    }
                }
                if (onlineLoading && online.isEmpty()) {
                    item {
                        Text(
                            text = stringResource(R.string.netease_loading),
                            fontSize = 12.sp,
                            color = secondary,
                            modifier = Modifier.padding(horizontal = 18.dp, vertical = 10.dp),
                        )
                    }
                }
                if (online.isNotEmpty()) {
                    item {
                        Text(
                            // 按来源命名（当前接入的是网易云）：未来多源时直接跟 platformName 走
                            text = online.firstOrNull()?.platformName?.takeIf { it.isNotBlank() }
                                ?: stringResource(R.string.netease_music_title),
                            fontSize = 15.sp,
                            fontWeight = FontWeight.Bold,
                            color = primary,
                            modifier = Modifier.padding(start = 18.dp, end = 18.dp, top = 10.dp, bottom = 4.dp),
                        )
                    }
                    itemsIndexed(online, key = { i, song -> "online-$i-${song.id}" }) { index, song ->
                        OnlineResultRow(song, primary, secondary) { onPlayOnline(index) }
                    }
                }
            }
        }
    }
}

@Composable
internal fun LandscapeDiscover(
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
internal fun LandscapeHeroCard(
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
internal fun LandscapeQuickTile(
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
internal fun LandscapeRailPanel(
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
