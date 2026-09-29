package com.catclaw.music.kt.ui.screen

import androidx.compose.foundation.background
import androidx.compose.foundation.border
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
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.lazy.LazyRow
import androidx.compose.foundation.lazy.items
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.verticalScroll
import androidx.compose.material3.Icon
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Text
import androidx.compose.material3.TextField
import androidx.compose.material3.TextFieldDefaults
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
import com.catclaw.music.kt.ui.search.SearchViewModel
import com.catclaw.music.kt.ui.theme.LocalCatClawPalette
import com.catclaw.music.kt.ui.theme.GlassChip
import com.catclaw.music.kt.ui.theme.EmptyState
import com.catclaw.music.kt.ui.theme.glassCard
import com.catclaw.music.kt.ui.theme.inkOnGradient
import com.catclaw.music.kt.ui.theme.HeroScrimBandArgb
import com.catclaw.music.kt.ui.theme.HeroScrimDeepArgb
import com.catclaw.music.kt.ui.theme.HeroTagScrimArgb
import java.util.Calendar

/**
 * 发现 tab —— 照 MAUI `SearchPage.xaml` 做成**枢纽**：问候语 + 三个玻璃图标 + 五个分类面板。
 * 之前这版只有一个搜索框加三段列表，不是你的设计。
 *
 * 分类面板只放**取到的到**的数据：`sections.daily/fresh/frequent` 是 DAO 的真实查询，
 * 歌手/专辑来自 `observeArtists/observeAlbums`。MAUI 的「统计」面板要的是曲库总量，
 * 本端 VM 只有这几段的条数，所以标签按实际含义写（不拿局部数据冒充全库）。
 */
private enum class DiscCat { Rec, Rank, Artist, Album, Stats }

@Composable
fun SearchScreen(
    active: Boolean,
    onOpenSettings: () -> Unit = {},
    onChatOpen: (Boolean) -> Unit = {},
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
    // 聊天是整屏覆盖层，外壳的悬浮两栏必须让位，否则输入行会被 TabBar 盖住。
    // 还要看 active：Pager 不会销毁别的页，横滑走开时 chatMode 仍是 true，
    // 只按 chatMode 上报会让别的 tab 也永久没有底栏
    LaunchedEffect(chatMode, active) { onChatOpen(chatMode && active) }
    var cat by rememberSaveable { mutableIntStateOf(0) }
    var searchOpen by rememberSaveable { mutableStateOf(false) }

    LaunchedEffect(active) { if (active) viewModel.activate() }

    Box(modifier = Modifier.fillMaxSize()) {
        Column(
            modifier = Modifier
                .fillMaxSize()
                .verticalScroll(rememberScrollState())
                .padding(bottom = 132.dp),
        ) {
            // 头部：标题 + 问候语 + 三个 44×44 玻璃图标
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

            if (searchOpen || query.isNotBlank()) {
                TextField(
                    value = query,
                    onValueChange = viewModel::setQuery,
                    placeholder = { Text(stringResource(R.string.disc_search_hint), color = secondary, fontSize = 14.sp) },
                    singleLine = true,
                    colors = TextFieldDefaults.colors(
                        focusedContainerColor = primary.copy(alpha = 0.10f),
                        unfocusedContainerColor = primary.copy(alpha = 0.08f),
                        focusedIndicatorColor = Color.Transparent,
                        unfocusedIndicatorColor = Color.Transparent,
                        focusedTextColor = primary,
                        unfocusedTextColor = primary,
                        cursorColor = Color(palette.primary),
                    ),
                    shape = RoundedCornerShape(22.dp),
                    modifier = Modifier.fillMaxWidth().padding(horizontal = 18.dp, vertical = 6.dp),
                )
            }

            if (query.isNotBlank()) {
                if (results.isEmpty()) {
                    // 搜不到东西不能是白屏：SongRows 空列表会直接 return，所以这里单独给状态
                    EmptyState(
                        hint = stringResource(R.string.search_none, query.trim()),
                        iconRes = R.drawable.ic_search,
                        modifier = Modifier.padding(horizontal = 18.dp, vertical = 10.dp),
                    )
                } else {
                    SongRows(stringResource(R.string.TabSongs), results, primary, secondary) { viewModel.play(results, it) }
                }
            } else {
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

                when (DiscCat.entries[cat]) {
                    DiscCat.Rec -> RecommendPanel(viewModel, sections, artists, albums, primary, secondary) { chatMode = true }
                    DiscCat.Rank -> Column {
                        SongRows(stringResource(R.string.disc_tag_top), sections.frequent, primary, secondary) {
                            viewModel.play(sections.frequent, it)
                        }
                        SongRows(stringResource(R.string.disc_shelf_recent_added), sections.fresh, primary, secondary) {
                            viewModel.play(sections.fresh, it)
                        }
                    }
                    DiscCat.Artist -> ArtistGrid(artists.map { it.name to it.songCount })
                    DiscCat.Album -> AlbumGrid(albums)
                    DiscCat.Stats -> StatsPanel(
                        listOf(
                            artists.size.toString() to stringResource(R.string.lib_stat_artists),
                            albums.size.toString() to stringResource(R.string.lib_stat_albums),
                            stats.totalPlays.toString() to stringResource(R.string.disc_stat_total_plays),
                            stats.songsPlayed.toString() to stringResource(R.string.disc_stat_songs_played),
                        ),
                    )
                }
            }
        }

        // 聊天是盖在发现页上的一层（MAUI 同为 overlay 而非独立路由），退出后搜索结果原样保留
        if (chatMode) ChatScreen(onBack = { chatMode = false })
    }
}

@Composable
private fun RecommendPanel(
    viewModel: SearchViewModel,
    sections: SearchViewModel.Sections,
    artists: List<com.catclaw.music.kt.data.db.ArtistListItem>,
    albums: List<com.catclaw.music.kt.data.db.AlbumListItem>,
    primary: Color,
    secondary: Color,
    onAskYuki: () -> Unit,
) {
    val heroTags = listOf(
        stringResource(R.string.disc_tag_daily), stringResource(R.string.disc_tag_top),
        stringResource(R.string.disc_tag_fav), stringResource(R.string.disc_tag_shuffle),
    )
    val heroes = sections.daily.take(3) + sections.frequent.take(1) + sections.fresh.take(1)
    LazyRow(
        contentPadding = androidx.compose.foundation.layout.PaddingValues(horizontal = 14.dp),
        horizontalArrangement = Arrangement.spacedBy(10.dp),
        modifier = Modifier.fillMaxWidth().height(150.dp),
    ) {
        item {
            HeroCard(
                tag = stringResource(R.string.disc_tag_ai),
                title = stringResource(R.string.disc_hero_ai_title),
                desc = stringResource(R.string.disc_hero_ai_desc),
                brush = Brush.linearGradient(listOf(Color(0xFF667EEA), Color(0xFF764BA2))),
                onClick = onAskYuki,
            )
        }
        items(heroes.size) { i ->
            val song = heroes[i]
            HeroCard(
                tag = heroTags.getOrElse(i) { heroTags[0] },
                title = song.title,
                desc = listOf(song.artist, song.album).filter { it.isNotBlank() }.joinToString(" · "),
                brush = Brush.linearGradient(listOf(Color(0xFFFF8FB8), Color(0xFFF56FA0))),
                onClick = { viewModel.play(heroes, i) },
            )
        }
    }

    // AI 卡片始终可用；但曲库为空时其余板块全空，得把「先去扫描」这句显式说出来
    if (heroes.isEmpty() && albums.isEmpty() && artists.isEmpty() && sections.daily.isEmpty()) {
        EmptyState(
            hint = stringResource(R.string.disc_empty),
            iconRes = R.drawable.ic_library,
            modifier = Modifier.padding(horizontal = 18.dp, vertical = 8.dp),
        )
    }

    if (albums.isNotEmpty()) {
        SectionHead(stringResource(R.string.disc_albums_rec))
        LazyRow(
            contentPadding = androidx.compose.foundation.layout.PaddingValues(horizontal = 16.dp),
            horizontalArrangement = Arrangement.spacedBy(10.dp),
            modifier = Modifier.fillMaxWidth().height(170.dp),
        ) {
            items(albums.take(12), key = { "a${it.id}" }) { AlbumCard(it) }
        }
    }

    if (sections.daily.isNotEmpty()) {
        // SongRows 自己会画小标题，这里不能再 SectionHead 一次（真机上出现过「每日推荐」重复两遍）
        SongRows(stringResource(R.string.disc_tag_daily), sections.daily.take(8), primary, secondary) {
            viewModel.play(sections.daily, it)
        }
    }

    if (artists.isNotEmpty()) {
        SectionHead(stringResource(R.string.disc_artists_rec))
        LazyRow(
            contentPadding = androidx.compose.foundation.layout.PaddingValues(horizontal = 16.dp),
            horizontalArrangement = Arrangement.spacedBy(12.dp),
            modifier = Modifier.fillMaxWidth().height(120.dp),
        ) {
            items(artists.take(20), key = { "r${it.id}" }) { ArtistBubble(it.name) }
        }
    }
}

@Composable
private fun HeroCard(tag: String, title: String, desc: String, brush: Brush, onClick: () -> Unit) {
    Box(
        modifier = Modifier
            .size(width = 130.dp, height = 130.dp)
            .clip(RoundedCornerShape(16.dp))
            .background(brush)
            .clickable(onClick = onClick)
            .padding(11.dp),
    ) {
        // 垫层压在渐变之上、文字之下：0.5 处之前保持透明（不糊掉板块身份），
        // 文字带起 α.40、底边 α.72 —— 白字在最亮的粉停靠点上从 2.13 回到 5.37:1
        Box(
            modifier = Modifier
                .matchParentSize()
                .background(
                    Brush.verticalGradient(
                        0f to Color.Transparent,
                        0.5f to Color(HeroScrimBandArgb),
                        1f to Color(HeroScrimDeepArgb),
                    ),
                ),
        )
        Text(
            text = tag,
            fontSize = 8.sp,
            fontWeight = FontWeight.Bold,
            color = Color.White,
            modifier = Modifier
                .align(Alignment.TopStart)
                .clip(RoundedCornerShape(999.dp))
                .background(Color(HeroTagScrimArgb))
                .padding(horizontal = 7.dp, vertical = 2.dp),
        )
        Column(modifier = Modifier.align(Alignment.BottomStart).padding(end = 34.dp)) {
            Text(title, fontSize = 14.sp, fontWeight = FontWeight.SemiBold, color = Color.White, maxLines = 2, overflow = TextOverflow.Ellipsis)
            Text(desc, fontSize = 9.sp, color = Color.White.copy(alpha = 0.8f), maxLines = 1, overflow = TextOverflow.Ellipsis)
        }
    }
}

@Composable
private fun AlbumCard(album: com.catclaw.music.kt.data.db.AlbumListItem) {
    val palette = LocalCatClawPalette.current
    Column(modifier = Modifier.width(124.dp)) {
        // 专辑级 CoverArtPath 本地曲一直是空，所以借代表曲的内嵌图；取不到才留渐变块
        CoverTile(
            coverArtPath = album.coverArtPath,
            audioPath = album.sampleFilePath,
            maxPx = 320,
            modifier = Modifier
                .fillMaxWidth()
                .aspectRatio(1f)
                .clip(RoundedCornerShape(18.dp))
                .background(Brush.linearGradient(listOf(Color(0xFFFF8FB8), Color(0xFF7ED8C3))))
                .border(1.dp, Color(palette.textPrimary).copy(alpha = 0.16f), RoundedCornerShape(18.dp)),
        ) {}
        Text(
            text = album.title,
            fontSize = 12.sp,
            fontWeight = FontWeight.SemiBold,
            color = Color(palette.textPrimary),
            maxLines = 1,
            overflow = TextOverflow.Ellipsis,
            modifier = Modifier.padding(top = 6.dp),
        )
        Text(
            text = album.artist,
            fontSize = 10.sp,
            color = Color(palette.textSecondary),
            maxLines = 1,
            overflow = TextOverflow.Ellipsis,
        )
    }
}

@Composable
private fun ArtistBubble(name: String) {
    val palette = LocalCatClawPalette.current
    Column(modifier = Modifier.width(74.dp), horizontalAlignment = Alignment.CenterHorizontally) {
        Box(
            modifier = Modifier
                .size(66.dp)
                .clip(CircleShape)
                .background(Brush.linearGradient(listOf(Color(palette.primaryLight), Color(palette.primary)))),
            contentAlignment = Alignment.Center,
        ) {
            Text(
                name.take(1),
                fontSize = 22.sp,
                fontWeight = FontWeight.Bold,
                color = Color(inkOnGradient(listOf(palette.primaryLight, palette.primary))),
            )
        }
        Text(name, fontSize = 11.5.sp, color = Color(palette.textPrimary), maxLines = 1, overflow = TextOverflow.Ellipsis, modifier = Modifier.padding(top = 6.dp))
    }
}

@Composable
private fun ArtistGrid(rows: List<Pair<String, Int>>) {
    val palette = LocalCatClawPalette.current
    Column(modifier = Modifier.padding(horizontal = 16.dp)) {
        rows.chunked(3).forEach { line ->
            Row(modifier = Modifier.fillMaxWidth().padding(bottom = 14.dp), horizontalArrangement = Arrangement.spacedBy(12.dp)) {
                line.forEach { (name, count) ->
                    Box(modifier = Modifier.weight(1f), contentAlignment = Alignment.Center) {
                        Column(horizontalAlignment = Alignment.CenterHorizontally) {
                            Box(
                                modifier = Modifier
                                    .size(84.dp)
                                    .clip(CircleShape)
                                    .background(Brush.linearGradient(listOf(Color(palette.primary), Color(palette.primaryDark)))),
                                contentAlignment = Alignment.Center,
                            ) {
                                Text(
                                    name.take(1),
                                    fontSize = 26.sp,
                                    fontWeight = FontWeight.Bold,
                                    color = Color(inkOnGradient(listOf(palette.primary, palette.primaryDark))),
                                )
                            }
                            Text(name, fontSize = 13.sp, color = Color(palette.textPrimary), maxLines = 1, overflow = TextOverflow.Ellipsis)
                            Text(stringResource(R.string.playlist_count, count), fontSize = 11.sp, color = Color(palette.textSecondary))
                        }
                    }
                }
                repeat(3 - line.size) { Spacer(Modifier.weight(1f)) }
            }
        }
    }
}

@Composable
private fun AlbumGrid(rows: List<com.catclaw.music.kt.data.db.AlbumListItem>) {
    Column(modifier = Modifier.padding(horizontal = 16.dp), verticalArrangement = Arrangement.spacedBy(12.dp)) {
        rows.chunked(2).forEach { line ->
            Row(horizontalArrangement = Arrangement.spacedBy(10.dp), modifier = Modifier.fillMaxWidth()) {
                line.forEach { album ->
                    Box(modifier = Modifier.weight(1f)) { AlbumCard(album) }
                }
                repeat(2 - line.size) { Spacer(Modifier.weight(1f)) }
            }
        }
    }
}

@Composable
private fun StatsPanel(cells: List<Pair<String, String>>) {
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
        rows.forEachIndexed { index, song ->
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
