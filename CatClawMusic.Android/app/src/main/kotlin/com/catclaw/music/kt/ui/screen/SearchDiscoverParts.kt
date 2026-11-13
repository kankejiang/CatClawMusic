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

/** 发现页的卡片/货架/行等小组件（从 SearchScreen.kt 拆出） */
@Composable
internal fun StaggerIn(index: Int, content: @Composable () -> Unit) {
    AnimatedVisibility(
        visibleState = remember { MutableTransitionState(false).apply { targetState = true } },
        enter = slideInHorizontally(
            tween(320, delayMillis = index * 50, easing = LinearOutSlowInEasing),
        ) { it } + fadeIn(tween(140, delayMillis = index * 50)),
    ) { content() }
}

/** 推荐面板的各个板块，作为 LazyColumn 的 item 逐个发射（板块间 12dp 间距） */
internal fun LazyListScope.recommendItems(
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
internal fun DailyHero(
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
internal fun QuickTile(
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
internal fun RailHead(title: String, onSeeAll: () -> Unit, primary: Color, secondary: Color) {
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
internal fun SongRailCard(
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
internal fun RailAlbumCard(
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
internal fun TodayFocusCard(song: SongListItem, onPlay: () -> Unit, onRefresh: () -> Unit) {
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
internal fun AlbumCard(album: com.catclaw.music.kt.data.db.AlbumListItem, onClick: () -> Unit) {
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
internal fun ArtistBubble(artist: com.catclaw.music.kt.data.db.ArtistListItem, onClick: () -> Unit) {
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
internal fun LazyListScope.artistItems(
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
internal fun LazyListScope.albumItems(
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
internal fun StatsPanel(cells: List<Pair<String, String>>, onOpenFull: () -> Unit) {
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
internal fun SectionHead(title: String) {
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
internal fun SongRows(
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
internal fun SongRowItem(
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
internal fun CatChip(label: String, selected: Boolean, modifier: Modifier = Modifier, onClick: () -> Unit) {
    // 视觉定义在 GlassChip；这里只负责在等分槽位里居中
    Box(modifier = modifier, contentAlignment = Alignment.Center) {
        GlassChip(label = label, selected = selected, onClick = onClick)
    }
}

@Composable
internal fun GlassIcon(
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
internal fun greeting(): String {
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
internal const val QUICK_BLUE = 0xFF7C9BE8L
internal const val QUICK_TEAL = 0xFF5FBEAEL
internal const val QUICK_VIOLET = 0xFF9A8DD6L
internal const val QUICK_AMBER = 0xFFD8AA6AL

/** 迷你条浮在底部（高 60 + 下边距 12 + 4dp 呼吸），舞台留 80dp 给它，内容就不会被压住 */
internal val LandscapeChromeReserve = 80.dp

/**
 * 横屏发现页 = 一整块「双栏舞台」，高度由调用方给（`fillParentMaxHeight()`，恰好一屏）。
 *
 * 换掉竖屏纵向列表的原因：411dp 高的画布只够「标题 + 英雄卡 + 一排入口」，
 * 竖着堆到第四块就只能被迷你条遮住 —— 2026-11-09 真机截图里四个入口只剩图标头顶。
 * 这里把宽度用起来：左栏约 38% 是每日推荐的实图大卡，右栏上排四入口、下排推荐货架。
 */


/** 在线搜索结果行：封面 + 标题 + 歌手-专辑 + 时长（点击即播） */
@Composable
internal fun OnlineResultRow(
    song: com.catclaw.music.kt.data.netease.OnlineSong,
    primary: Color,
    secondary: Color,
    onClick: () -> Unit,
) {
    Row(
        modifier = Modifier
            .fillMaxWidth()
            .clickable(onClick = onClick)
            .padding(horizontal = 18.dp, vertical = 6.dp),
        verticalAlignment = Alignment.CenterVertically,
        horizontalArrangement = Arrangement.spacedBy(10.dp),
    ) {
        com.catclaw.music.kt.ui.player.CoverTile(
            coverArtPath = song.coverUrl,
            audioPath = null,
            maxPx = 140,
            modifier = Modifier
                .size(44.dp)
                .clip(RoundedCornerShape(8.dp)),
        ) { }
        Column(Modifier.weight(1f), verticalArrangement = Arrangement.spacedBy(2.dp)) {
            Text(song.title, fontSize = 14.sp, color = primary, maxLines = 1, overflow = TextOverflow.Ellipsis)
            Text(
                text = listOf(song.artist, song.album).filter { it.isNotBlank() }.joinToString(" - "),
                fontSize = 11.sp,
                color = secondary,
                maxLines = 1,
                overflow = TextOverflow.Ellipsis,
            )
        }
        Text(
            text = if (song.durationMs > 0) {
                "%02d:%02d".format(song.durationMs / 60000, (song.durationMs / 1000) % 60)
            } else "--:--",
            fontSize = 11.sp,
            color = secondary,
        )
    }
}
