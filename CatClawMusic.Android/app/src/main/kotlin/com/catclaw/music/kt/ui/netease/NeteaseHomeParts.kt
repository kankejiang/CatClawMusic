package com.catclaw.music.kt.ui.netease

import android.content.res.Configuration
import androidx.activity.compose.BackHandler
import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.clickable
import androidx.compose.foundation.combinedClickable
import androidx.compose.foundation.interaction.MutableInteractionSource
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.ExperimentalLayoutApi
import androidx.compose.foundation.layout.FlowRow
import androidx.compose.foundation.layout.PaddingValues
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.aspectRatio
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.lazy.LazyRow
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.itemsIndexed
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.foundation.text.BasicTextField
import androidx.compose.foundation.text.KeyboardOptions
import androidx.compose.ui.graphics.SolidColor
import androidx.compose.ui.text.TextStyle
import androidx.compose.ui.text.input.KeyboardType
import androidx.compose.ui.text.input.PasswordVisualTransformation
import androidx.compose.ui.text.input.VisualTransformation
import androidx.compose.ui.draw.alpha
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.platform.LocalConfiguration
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import androidx.hilt.navigation.compose.hiltViewModel
import androidx.lifecycle.compose.collectAsStateWithLifecycle
import com.catclaw.music.kt.R
import com.catclaw.music.kt.data.netease.NeteaseArtist
import com.catclaw.music.kt.data.netease.NeteasePlaylistClient
import com.catclaw.music.kt.data.netease.OnlinePlaylist
import androidx.compose.runtime.saveable.rememberSaveable
import com.catclaw.music.kt.core.library.SongSortFields
import com.catclaw.music.kt.core.library.SongSortKey
import com.catclaw.music.kt.core.library.defaultDescending
import com.catclaw.music.kt.core.library.filterAndSortSongs
import com.catclaw.music.kt.ui.screen.sortLabelRes
import com.catclaw.music.kt.data.netease.OnlineSong
import com.catclaw.music.kt.ui.song.SongCollectionScreen
import com.catclaw.music.kt.ui.screen.PlaylistDetailHeader
import com.catclaw.music.kt.ui.screen.PlaylistSongRowUi
import com.catclaw.music.kt.ui.screen.PlaylistSongUi
import com.catclaw.music.kt.ui.library.NeteaseImportPanel
import com.catclaw.music.kt.ui.library.NeteaseImportViewModel
import com.catclaw.music.kt.ui.player.CoverTile
import com.catclaw.music.kt.ui.theme.BackTile
import com.catclaw.music.kt.ui.theme.GlassChip
import com.catclaw.music.kt.ui.theme.GlassSearchField
import com.catclaw.music.kt.ui.theme.LocalCatClawPalette
import com.catclaw.music.kt.ui.theme.PillLabel
import com.catclaw.music.kt.ui.theme.glassCard
import com.catclaw.music.kt.ui.theme.isLightPalette
import com.catclaw.music.kt.ui.theme.highlightOnGlassCard
import kotlinx.coroutines.delay

/** 网易云首页/广场/排行榜/歌手/搜索块（从 NeteaseMusicScreen.kt 拆出） */
@Composable
internal fun TopBar(
    query: String,
    onQueryChange: (String) -> Unit,
    accountName: String?,
    quality: NeteasePlaylistClient.Quality,
    onCycleQuality: () -> Unit,
    onOpenLogin: () -> Unit,
    onBack: () -> Unit,
) {
    val palette = LocalCatClawPalette.current
    val primary = Color(palette.textPrimary)
    val secondary = Color(palette.textSecondary)
    val landscape = LocalConfiguration.current.orientation == Configuration.ORIENTATION_LANDSCAPE

    val qualitySlot: @Composable () -> Unit = {
        Text(
            text = when (quality) {
                NeteasePlaylistClient.Quality.Lossless -> stringResource(R.string.netease_quality_lossless)
                NeteasePlaylistClient.Quality.Exhigh -> stringResource(R.string.netease_quality_320)
                NeteasePlaylistClient.Quality.Standard -> stringResource(R.string.netease_quality_128)
            },
            fontSize = 10.5.sp,
            color = Color(highlightOnGlassCard(palette)),
            maxLines = 1,
            modifier = Modifier
                .clip(RoundedCornerShape(999.dp))
                .background(Color(palette.textPrimary).copy(alpha = 0.08f))
                .clickable(onClick = onCycleQuality)
                .padding(horizontal = 9.dp, vertical = 4.dp),
        )
    }

    val accountSlot: @Composable () -> Unit = {
        if (accountName == null) {
            Text(
                text = stringResource(R.string.netease_login),
                fontSize = 11.sp,
                fontWeight = FontWeight.Medium,
                color = Color(highlightOnGlassCard(palette)),
                maxLines = 1,
                modifier = Modifier
                    .clip(RoundedCornerShape(999.dp))
                    .background(Color(palette.textPrimary).copy(alpha = 0.10f))
                    .clickable(onClick = onOpenLogin)
                    .padding(horizontal = 12.dp, vertical = 5.dp),
            )
        } else {
            Text(
                text = accountName,
                fontSize = 11.sp,
                color = secondary,
                maxLines = 1,
                overflow = TextOverflow.Ellipsis,
                modifier = Modifier
                    .clip(RoundedCornerShape(999.dp))
                    .clickable(onClick = onOpenLogin)
                    .padding(horizontal = 8.dp, vertical = 4.dp),
            )
        }
    }

    if (landscape) {
        Row(
            modifier = Modifier
                .fillMaxWidth()
                .padding(start = 18.dp, end = 18.dp, top = 8.dp, bottom = 6.dp),
            verticalAlignment = Alignment.CenterVertically,
            horizontalArrangement = Arrangement.spacedBy(12.dp),
        ) {
            BackTile(onBack = onBack, description = stringResource(R.string.GoBack))
            Text(
                text = stringResource(R.string.netease_music_title),
                fontSize = 22.sp,
                fontWeight = FontWeight.Bold,
                color = primary,
            )
            GlassSearchField(
                query = query,
                onChange = onQueryChange,
                placeholder = stringResource(R.string.netease_search_hint),
                modifier = Modifier.weight(1f),
                compact = true,
            )
            qualitySlot()
            accountSlot()
        }
    } else {
        Column(
            modifier = Modifier
                .fillMaxWidth()
                .padding(start = 18.dp, end = 18.dp, top = 6.dp, bottom = 4.dp),
            verticalArrangement = Arrangement.spacedBy(8.dp),
        ) {
            Row(
                modifier = Modifier.fillMaxWidth(),
                verticalAlignment = Alignment.CenterVertically,
                horizontalArrangement = Arrangement.spacedBy(12.dp),
            ) {
                BackTile(onBack = onBack, description = stringResource(R.string.GoBack))
                Text(
                    text = stringResource(R.string.netease_music_title),
                    fontSize = 22.sp,
                    fontWeight = FontWeight.Bold,
                    color = primary,
                    modifier = Modifier.weight(1f),
                )
                qualitySlot()
                accountSlot()
            }
            GlassSearchField(
                query = query,
                onChange = onQueryChange,
                placeholder = stringResource(R.string.netease_search_hint),
                modifier = Modifier.fillMaxWidth(),
                compact = true,
            )
        }
    }
}

// ── 首页 ───────────────────────────────────────────────────────

/** 顶部四个分页（对齐插件 HomeTabs：精选 / 歌单广场 / 排行榜 / 歌手） */
@Composable
internal fun HomeTabsBar(selected: Int, onSelect: (Int) -> Unit) {
    val palette = LocalCatClawPalette.current
    val primary = Color(palette.textPrimary)
    val secondary = Color(palette.textSecondary)
    val tabs = listOf(
        stringResource(R.string.netease_tab_featured),
        stringResource(R.string.netease_tab_square),
        stringResource(R.string.netease_tab_toplists),
        stringResource(R.string.netease_tab_artists),
    )
    Row(
        modifier = Modifier.fillMaxWidth().padding(horizontal = 18.dp, vertical = 4.dp),
        horizontalArrangement = Arrangement.spacedBy(22.dp),
    ) {
        tabs.forEachIndexed { index, label ->
            val active = index == selected
            Column(
                horizontalAlignment = Alignment.CenterHorizontally,
                modifier = Modifier
                    .clip(RoundedCornerShape(8.dp))
                    .clickable { onSelect(index) }
                    .padding(vertical = 4.dp),
            ) {
                Text(
                    text = label,
                    fontSize = 14.sp,
                    fontWeight = if (active) FontWeight.Bold else FontWeight.Medium,
                    color = if (active) primary else secondary,
                )
                Spacer(Modifier.height(4.dp))
                Box(
                    modifier = Modifier
                        .width(if (active) 18.dp else 0.dp)
                        .height(3.dp)
                        .clip(RoundedCornerShape(2.dp))
                        .background(Color(palette.primary)),
                )
            }
        }
    }
}

/** 歌单广场：官方分类 chips + 2 列歌单网格 + 「加载更多」 */
@Composable
internal fun SquareList(
    square: NeteaseMusicViewModel.Square,
    onSelectCategory: (String) -> Unit,
    onOpenPlaylist: (OnlinePlaylist) -> Unit,
    onLoadMore: () -> Unit,
) {
    val palette = LocalCatClawPalette.current
    val primary = Color(palette.textPrimary)
    val secondary = Color(palette.textSecondary)
    val landscape = LocalConfiguration.current.orientation == Configuration.ORIENTATION_LANDSCAPE
    // 「推荐」+ 官方分类（catalogue 的顺序本身就是官方顺序）
    val categories = listOf("推荐") + square.groups.flatMap { it.names }.distinct()
    LazyColumn(
        modifier = Modifier.fillMaxSize().padding(horizontal = 18.dp),
        verticalArrangement = Arrangement.spacedBy(10.dp),
    ) {
        item("square_chips") {
            LazyRow(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                items(categories.size) { index ->
                    val name = categories[index]
                    val active = name == square.selected
                    Text(
                        text = name,
                        fontSize = 12.sp,
                        color = if (active) Color(palette.primary) else secondary,
                        modifier = Modifier
                            .clip(RoundedCornerShape(999.dp))
                            .background(Color(palette.textPrimary).copy(alpha = if (active) 0.16f else 0.06f))
                            .clickable { onSelectCategory(name) }
                            .padding(horizontal = 12.dp, vertical = 6.dp),
                    )
                }
            }
        }
        val rows = square.playlists.chunked(2)
        items(count = rows.size, key = { "sq-" + it }) { rowIndex ->
            Row(horizontalArrangement = Arrangement.spacedBy(10.dp)) {
                rows[rowIndex].forEach { playlist ->
                    PlaylistCard(
                        playlist = playlist,
                        landscape = landscape,
                        modifier = Modifier.weight(1f),
                        onClick = { onOpenPlaylist(playlist) },
                    )
                }
                if (rows[rowIndex].size == 1) Spacer(Modifier.weight(1f))
            }
        }
        if (square.hasMore || square.loading) {
            item("square_more") {
                Box(Modifier.fillMaxWidth().padding(vertical = 8.dp), contentAlignment = Alignment.Center) {
                    Text(
                        text = if (square.loading) {
                            stringResource(R.string.netease_loading)
                        } else {
                            stringResource(R.string.netease_load_more)
                        },
                        fontSize = 13.sp,
                        color = primary,
                        modifier = Modifier
                            .clip(RoundedCornerShape(999.dp))
                            .background(Color(palette.textPrimary).copy(alpha = 0.08f))
                            .clickable(enabled = !square.loading) { onLoadMore() }
                            .padding(horizontal = 18.dp, vertical = 8.dp),
                    )
                }
            }
        }
    }
}

/** 排行榜：官方全量榜单两列卡片 */
@Composable
internal fun RankGrid(toplists: List<OnlinePlaylist>, onOpenPlaylist: (OnlinePlaylist) -> Unit) {
    val landscape = LocalConfiguration.current.orientation == Configuration.ORIENTATION_LANDSCAPE
    LazyColumn(
        modifier = Modifier.fillMaxSize().padding(horizontal = 18.dp),
        verticalArrangement = Arrangement.spacedBy(10.dp),
    ) {
        val rows = toplists.chunked(2)
        items(count = rows.size, key = { "rank-" + it }) { rowIndex ->
            Row(horizontalArrangement = Arrangement.spacedBy(10.dp)) {
                rows[rowIndex].forEach { playlist ->
                    PlaylistCard(
                        playlist = playlist,
                        landscape = landscape,
                        modifier = Modifier.weight(1f),
                        subtitle = stringResource(R.string.netease_official_chart),
                        onClick = { onOpenPlaylist(playlist) },
                    )
                }
                if (rows[rowIndex].size == 1) Spacer(Modifier.weight(1f))
            }
        }
    }
}

/** 歌手：热门歌手三列圆形头像 */
@Composable
internal fun ArtistGrid(artists: List<NeteaseArtist>) {
    val palette = LocalCatClawPalette.current
    val primary = Color(palette.textPrimary)
    val secondary = Color(palette.textSecondary)
    LazyColumn(
        modifier = Modifier.fillMaxSize().padding(horizontal = 18.dp),
        verticalArrangement = Arrangement.spacedBy(14.dp),
    ) {
        val rows = artists.chunked(3)
        items(count = rows.size, key = { "ar-" + it }) { rowIndex ->
            Row(horizontalArrangement = Arrangement.spacedBy(12.dp)) {
                rows[rowIndex].forEach { artist ->
                    Column(
                        modifier = Modifier.weight(1f),
                        horizontalAlignment = Alignment.CenterHorizontally,
                    ) {
                        CoverTile(
                            coverArtPath = artist.picUrl,
                            audioPath = null,
                            maxPx = 256,
                            modifier = Modifier
                                .fillMaxWidth()
                                .aspectRatio(1f)
                                .clip(RoundedCornerShape(999.dp))
                                .background(Color(palette.primary).copy(alpha = 0.16f)),
                        ) {
                            Text(artist.name.take(1), fontSize = 22.sp, fontWeight = FontWeight.Bold, color = primary)
                        }
                        Spacer(Modifier.height(6.dp))
                        Text(
                            text = artist.name,
                            fontSize = 12.sp,
                            color = primary,
                            maxLines = 1,
                            overflow = TextOverflow.Ellipsis,
                        )
                        if (artist.albumCount > 0) {
                            Text(
                                text = stringResource(R.string.netease_albums_count, artist.albumCount),
                                fontSize = 10.sp,
                                color = secondary,
                                maxLines = 1,
                            )
                        }
                    }
                }
                repeat(3 - rows[rowIndex].size) { Spacer(Modifier.weight(1f)) }
            }
        }
    }
}

@OptIn(ExperimentalLayoutApi::class)
@Composable
internal fun HomeList(
    home: NeteaseMusicViewModel.Home,
    myPlaylists: List<OnlinePlaylist>,
    loggedIn: Boolean,
    onOpenPlaylist: (OnlinePlaylist) -> Unit,
    onDaily: () -> Unit,
    onPrivateFm: () -> Unit,
    onLiked: () -> Unit,
    onImport: () -> Unit,
    onReload: () -> Unit,
) {
    val palette = LocalCatClawPalette.current
    val primary = Color(palette.textPrimary)
    val secondary = Color(palette.textSecondary)
    val landscape = LocalConfiguration.current.orientation == Configuration.ORIENTATION_LANDSCAPE

    LazyColumn(
        modifier = Modifier.fillMaxSize().padding(horizontal = 18.dp),
        verticalArrangement = Arrangement.spacedBy(10.dp),
    ) {
        item {
            FlowRow(
                modifier = Modifier.fillMaxWidth().padding(top = 4.dp),
                horizontalArrangement = Arrangement.spacedBy(8.dp),
                verticalArrangement = Arrangement.spacedBy(8.dp),
            ) {
                // 导入歌单不需要登录（本地匹配 + 在线外链），所以不受 loggedIn 限制
                GlassChip(label = stringResource(R.string.netease_import_chip), selected = false, onClick = onImport)
                if (loggedIn) {
                    GlassChip(label = stringResource(R.string.netease_daily), selected = false, onClick = onDaily)
                    GlassChip(label = stringResource(R.string.netease_private_fm), selected = false, onClick = onPrivateFm)
                    GlassChip(label = stringResource(R.string.netease_liked), selected = false, onClick = onLiked)
                }
            }
        }

        item { SectionTitle(stringResource(R.string.netease_recommend), primary) }
        items(count = home.recommend.chunked(2).size, key = { "rec-$it" }) { rowIndex ->
            val row = home.recommend.chunked(2)[rowIndex]
            Row(horizontalArrangement = Arrangement.spacedBy(12.dp)) {
                row.forEach { playlist ->
                    PlaylistCard(
                        playlist = playlist,
                        landscape = landscape,
                        modifier = Modifier.weight(1f),
                        onClick = { onOpenPlaylist(playlist) },
                    )
                }
                if (row.size == 1) Spacer(Modifier.weight(1f))
            }
        }

        if (myPlaylists.isNotEmpty()) {
            item { SectionTitle(stringResource(R.string.netease_my_playlists), primary) }
            items(count = myPlaylists.chunked(2).size, key = { "mine-$it" }) { rowIndex ->
                val row = myPlaylists.chunked(2)[rowIndex]
                Row(horizontalArrangement = Arrangement.spacedBy(12.dp)) {
                    row.forEach { playlist ->
                        PlaylistCard(
                            playlist = playlist,
                            landscape = landscape,
                            modifier = Modifier.weight(1f),
                            onClick = { onOpenPlaylist(playlist) },
                        )
                    }
                    if (row.size == 1) Spacer(Modifier.weight(1f))
                }
            }
        }

        item { SectionTitle(stringResource(R.string.netease_toplists), primary) }
        items(count = home.toplists.size, key = { "top-$it" }) { index ->
            val playlist = home.toplists[index]
            PlaylistRow(playlist = playlist, onClick = { onOpenPlaylist(playlist) })
        }

        if (home.loading) {
            item { HintText(stringResource(R.string.netease_loading), secondary) }
        } else if (home.error) {
            item {
                HintText(
                    text = stringResource(R.string.netease_failed),
                    color = secondary,
                    modifier = Modifier.clickable(onClick = onReload),
                )
            }
        }
        item { Spacer(Modifier.height(96.dp)) }
    }
}

// ── 搜索 ───────────────────────────────────────────────────────

@Composable
internal fun SearchResults(
    search: NeteaseMusicViewModel.Search,
    likedIds: Set<String>,
    onPlay: (Int) -> Unit,
    onOpenPlaylist: (OnlinePlaylist) -> Unit,
    onLike: (OnlineSong, Boolean) -> Unit,
    onLoadMore: () -> Unit,
) {
    val palette = LocalCatClawPalette.current
    val primary = Color(palette.textPrimary)
    val secondary = Color(palette.textSecondary)
    val landscape = LocalConfiguration.current.orientation == Configuration.ORIENTATION_LANDSCAPE
    LazyColumn(
        modifier = Modifier.fillMaxSize().padding(horizontal = 18.dp),
        verticalArrangement = Arrangement.spacedBy(8.dp),
    ) {
        if (search.loading) {
            item { HintText(stringResource(R.string.netease_loading), secondary) }
        }
        if (search.artists.isNotEmpty()) {
            item { SectionTitle(stringResource(R.string.netease_artists_section), primary) }
            items(count = search.artists.size, key = { "ar-" + search.artists[it].id }) { index ->
                val artist = search.artists[index]
                Row(
                    modifier = Modifier
                        .fillMaxWidth()
                        .glassCard(palette = palette, corner = 16)
                        .padding(horizontal = 10.dp, vertical = 7.dp),
                    verticalAlignment = Alignment.CenterVertically,
                    horizontalArrangement = Arrangement.spacedBy(10.dp),
                ) {
                    CoverTile(
                        coverArtPath = artist.picUrl,
                        audioPath = null,
                        maxPx = 128,
                        modifier = Modifier
                            .size(40.dp)
                            .clip(RoundedCornerShape(999.dp))
                            .background(Color(palette.primary).copy(alpha = 0.16f)),
                    ) {
                        Text(artist.name.take(1), color = primary, fontWeight = FontWeight.Bold)
                    }
                    Column(Modifier.weight(1f)) {
                        Text(artist.name, fontSize = 14.sp, color = primary, maxLines = 1, overflow = TextOverflow.Ellipsis)
                        Text(
                            text = stringResource(R.string.netease_songs_unit, artist.songCount) +
                                " · " + artist.albumCount + " albums",
                            fontSize = 11.sp,
                            color = secondary,
                        )
                    }
                }
            }
        }

        if (search.songs.isNotEmpty()) {
            item { SectionTitle(stringResource(R.string.netease_songs_section), primary) }
            itemsIndexed(search.songs, key = { index, song -> "s-$index-${song.id}" }) { index, song ->
                SongRow(
                    song = song,
                    index = index,
                    liked = song.id in likedIds,
                    onClick = { onPlay(index) },
                    onLike = { onLike(song, song.id !in likedIds) },
                )
            }
        }
        if (search.playlists.isNotEmpty()) {
            item { SectionTitle(stringResource(R.string.netease_playlists_section), primary) }
            items(count = search.playlists.chunked(2).size, key = { "sp-$it" }) { rowIndex ->
                val row = search.playlists.chunked(2)[rowIndex]
                Row(horizontalArrangement = Arrangement.spacedBy(12.dp)) {
                    row.forEach { playlist ->
                        PlaylistCard(
                            playlist = playlist,
                            landscape = landscape,
                            modifier = Modifier.weight(1f),
                            onClick = { onOpenPlaylist(playlist) },
                        )
                    }
                    if (row.size == 1) Spacer(Modifier.weight(1f))
                }
            }
        }
        if (search.hasMore || search.loadingMore) {
            item {
                Box(
                    modifier = Modifier.fillMaxWidth().padding(vertical = 10.dp),
                    contentAlignment = Alignment.Center,
                ) {
                    PillLabel(
                        text = if (search.loadingMore) {
                            stringResource(R.string.netease_loading)
                        } else {
                            stringResource(R.string.netease_load_more)
                        },
                        onClick = onLoadMore,
                        filled = false,
                        enabled = !search.loadingMore,
                    )
                }
            }
        }
        if (!search.loading && search.songs.isEmpty() && search.playlists.isEmpty() && search.artists.isEmpty()) {
            item { HintText(stringResource(R.string.netease_empty), secondary) }
        }
        item { Spacer(Modifier.height(96.dp)) }
    }
}

// ── 详情 ───────────────────────────────────────────────────────
