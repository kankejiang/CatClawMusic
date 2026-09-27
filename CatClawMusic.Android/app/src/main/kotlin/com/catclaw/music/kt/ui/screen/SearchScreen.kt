package com.catclaw.music.kt.ui.screen

import androidx.compose.foundation.background
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
import androidx.compose.material3.OutlinedTextField
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
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import androidx.hilt.navigation.compose.hiltViewModel
import androidx.lifecycle.compose.collectAsStateWithLifecycle
import com.catclaw.music.kt.R
import com.catclaw.music.kt.data.db.SongListItem
import com.catclaw.music.kt.ui.chat.ChatScreen
import com.catclaw.music.kt.ui.search.SearchViewModel
import com.catclaw.music.kt.ui.theme.LocalCatClawPalette
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
    viewModel: SearchViewModel = hiltViewModel(),
) {
    val palette = LocalCatClawPalette.current
    val query by viewModel.query.collectAsStateWithLifecycle()
    val results by viewModel.results.collectAsStateWithLifecycle()
    val sections by viewModel.sections.collectAsStateWithLifecycle()
    val artists by viewModel.artists.collectAsStateWithLifecycle()
    val albums by viewModel.albums.collectAsStateWithLifecycle()
    val primary = Color(palette.textPrimary)
    val secondary = Color(palette.textSecondary)
    var chatMode by rememberSaveable { mutableStateOf(false) }
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
                OutlinedTextField(
                    value = query,
                    onValueChange = viewModel::setQuery,
                    placeholder = { Text(stringResource(R.string.disc_search_hint), color = secondary) },
                    singleLine = true,
                    modifier = Modifier.fillMaxWidth().padding(horizontal = 18.dp, vertical = 6.dp),
                )
            }

            if (query.isNotBlank()) {
                SongRows(stringResource(R.string.TabSongs), results, primary, secondary) { viewModel.play(results, it) }
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
                        SongRows(stringResource(R.string.disc_recent_played), sections.fresh, primary, secondary) {
                            viewModel.play(sections.fresh, it)
                        }
                    }
                    DiscCat.Artist -> ArtistGrid(artists.map { it.name to it.songCount })
                    DiscCat.Album -> AlbumGrid(albums.map { it.title to it.artist })
                    DiscCat.Stats -> StatsPanel(
                        listOf(
                            artists.size.toString() to stringResource(R.string.lib_stat_artists),
                            albums.size.toString() to stringResource(R.string.lib_stat_albums),
                            sections.frequent.size.toString() to stringResource(R.string.disc_tag_top),
                            sections.fresh.size.toString() to stringResource(R.string.disc_recent_played),
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

    if (albums.isNotEmpty()) {
        SectionHead(stringResource(R.string.disc_albums_rec))
        LazyRow(
            contentPadding = androidx.compose.foundation.layout.PaddingValues(horizontal = 16.dp),
            horizontalArrangement = Arrangement.spacedBy(10.dp),
            modifier = Modifier.fillMaxWidth().height(170.dp),
        ) {
            items(albums.take(12), key = { "a${it.id}" }) { AlbumCard(it.title, it.artist) }
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
        Text(
            text = tag,
            fontSize = 8.sp,
            fontWeight = FontWeight.Bold,
            color = Color.White,
            modifier = Modifier
                .align(Alignment.TopStart)
                .clip(RoundedCornerShape(999.dp))
                .background(Color.White.copy(alpha = 0.19f))
                .padding(horizontal = 7.dp, vertical = 2.dp),
        )
        Column(modifier = Modifier.align(Alignment.BottomStart).padding(end = 34.dp)) {
            Text(title, fontSize = 14.sp, fontWeight = FontWeight.SemiBold, color = Color.White, maxLines = 2, overflow = TextOverflow.Ellipsis)
            Text(desc, fontSize = 9.sp, color = Color.White.copy(alpha = 0.8f), maxLines = 1, overflow = TextOverflow.Ellipsis)
        }
    }
}

@Composable
private fun AlbumCard(title: String, artist: String) {
    Column(modifier = Modifier.width(124.dp)) {
        Box(
            modifier = Modifier
                .fillMaxWidth()
                .aspectRatio(1f)
                .clip(RoundedCornerShape(18.dp))
                .background(Brush.linearGradient(listOf(Color(0xFFFF8FB8), Color(0xFF7ED8C3)))),
        )
        Text(title, fontSize = 12.sp, color = Color(0xFFF5F7FF), maxLines = 1, overflow = TextOverflow.Ellipsis, modifier = Modifier.padding(top = 6.dp))
        Text(artist, fontSize = 10.sp, color = Color(0xFF8D93B7), maxLines = 1, overflow = TextOverflow.Ellipsis)
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
            Text(name.take(1), fontSize = 22.sp, fontWeight = FontWeight.Bold, color = Color.White)
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
                                Text(name.take(1), fontSize = 26.sp, fontWeight = FontWeight.Bold, color = Color.White)
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
private fun AlbumGrid(rows: List<Pair<String, String>>) {
    Column(modifier = Modifier.padding(horizontal = 16.dp), verticalArrangement = Arrangement.spacedBy(12.dp)) {
        rows.chunked(2).forEach { line ->
            Row(horizontalArrangement = Arrangement.spacedBy(10.dp), modifier = Modifier.fillMaxWidth()) {
                line.forEach { (title, artist) ->
                    Box(modifier = Modifier.weight(1f)) { AlbumCard(title, artist) }
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
                    Column(
                        modifier = Modifier
                            .weight(1f)
                            .clip(RoundedCornerShape(16.dp))
                            .background(Color(palette.textPrimary).copy(alpha = 0.08f))
                            .padding(14.dp),
                    ) {
                        Text(value, fontSize = 20.sp, fontWeight = FontWeight.Bold, color = Color(palette.textPrimary))
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
    Column(modifier = Modifier.padding(horizontal = 8.dp)) {
        rows.forEachIndexed { index, song ->
            Row(
                modifier = Modifier
                    .fillMaxWidth()
                    .clip(RoundedCornerShape(14.dp))
                    .clickable { onClick(index) }
                    .padding(horizontal = 12.dp, vertical = 9.dp),
                verticalAlignment = Alignment.CenterVertically,
            ) {
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
                )
            }
        }
    }
}

@Composable
private fun CatChip(label: String, selected: Boolean, modifier: Modifier = Modifier, onClick: () -> Unit) {
    val palette = LocalCatClawPalette.current
    Box(
        modifier = modifier
            .clip(RoundedCornerShape(26.dp))
            .background(if (selected) Color(0xFF55D6FF) else Color(palette.textPrimary).copy(alpha = 0.08f))
            .clickable(onClick = onClick)
            .padding(vertical = 10.dp),
        contentAlignment = Alignment.Center,
    ) {
        Text(
            text = label,
            fontSize = 14.sp,
            fontWeight = FontWeight.Bold,
            color = if (selected) Color(0xFF08131A) else Color(palette.textSecondary),
        )
    }
}

@Composable
private fun GlassIcon(iconRes: Int, description: String, onClick: () -> Unit) {
    val palette = LocalCatClawPalette.current
    Box(
        modifier = Modifier
            .size(44.dp)
            .clip(RoundedCornerShape(16.dp))
            .background(Color(palette.textPrimary).copy(alpha = 0.20f))
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
