package com.catclaw.music.kt.ui.netease

import androidx.activity.compose.BackHandler
import androidx.compose.foundation.background
import androidx.compose.foundation.clickable
import android.content.res.Configuration
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.aspectRatio
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxSize
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
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.Brush
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
import com.catclaw.music.kt.data.netease.OnlinePlaylist
import com.catclaw.music.kt.data.netease.OnlineSong
import com.catclaw.music.kt.ui.player.CoverTile
import com.catclaw.music.kt.ui.theme.BackTile
import com.catclaw.music.kt.ui.theme.GlassSearchField
import com.catclaw.music.kt.ui.theme.LocalCatClawPalette
import com.catclaw.music.kt.ui.theme.PillLabel
import com.catclaw.music.kt.ui.theme.glassCard
import com.catclaw.music.kt.ui.theme.highlightOnGlassCard

/**
 * 网易云在线音乐页 —— 对应插件 `NeteaseOnlineMusicPage`。
 *
 * 三个层：首页（推荐歌单 + 榜单）→ 歌单详情（曲目 + 播放）→ 搜索（歌曲 + 歌单）。
 * 各自的数据与请求都在 [NeteaseMusicViewModel] 里，页面只负责按状态画。
 *
 * 播放复用主播放器：在线曲会被转成 core 的 `Song` 塞进同一个队列，
 * 播完自动接下一首，迷你条/播放页/歌词页都能正常显示。
 */
@Composable
fun NeteaseMusicScreen(
    onBack: () -> Unit,
    viewModel: NeteaseMusicViewModel = hiltViewModel(),
) {
    val palette = LocalCatClawPalette.current
    val primary = Color(palette.textPrimary)
    val secondary = Color(palette.textSecondary)
    val home by viewModel.home.collectAsStateWithLifecycle()
    val search by viewModel.search.collectAsStateWithLifecycle()
    val detail by viewModel.detail.collectAsStateWithLifecycle()
    var query by remember { mutableStateOf("") }

    LaunchedEffect(Unit) { viewModel.loadHome() }

    // 详情页在页内切换而不是导航路由：返回键应当先退回列表，再退整页
    BackHandler(enabled = detail != null) { viewModel.closeDetail() }

    detail?.let { current ->
        NeteasePlaylistDetail(
            detail = current,
            onBack = viewModel::closeDetail,
            onPlayAll = { viewModel.playAll(current.songs) },
            onPlayAt = { viewModel.playFrom(current.songs, it) },
        )
        return
    }

    Column(modifier = Modifier.fillMaxSize()) {
        val landscape = LocalConfiguration.current.orientation ==
            Configuration.ORIENTATION_LANDSCAPE
        // 竖屏 1440px 宽塞不下「标题 + 搜索框 + 未登录提示」一行：搜索框会被压到 ~80dp，
        // 只能显示两个字（真机实测）。竖屏改成两行：第一行标题与登录态，第二行整条搜索框。
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
                    onChange = {
                        query = it
                        viewModel.search(it)
                    },
                    placeholder = stringResource(R.string.netease_search_hint),
                    modifier = Modifier.weight(1f),
                    compact = true,
                )
                if (!viewModel.loggedIn) {
                    Text(
                        text = stringResource(R.string.netease_login_hint),
                        fontSize = 10.5.sp,
                        color = secondary,
                        maxLines = 1,
                    )
                }
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
                    if (!viewModel.loggedIn) {
                        Text(
                            text = stringResource(R.string.netease_login_hint),
                            fontSize = 10.5.sp,
                            color = secondary,
                            maxLines = 1,
                        )
                    }
                }
                GlassSearchField(
                    query = query,
                    onChange = {
                        query = it
                        viewModel.search(it)
                    },
                    placeholder = stringResource(R.string.netease_search_hint),
                    modifier = Modifier.fillMaxWidth(),
                    compact = true,
                )
            }
        }

        if (search.query.isNotBlank()) {
            SearchResults(
                search = search,
                onPlay = { song, index -> viewModel.playFrom(search.songs, index) },
                onOpenPlaylist = viewModel::openPlaylist,
            )
            return@Column
        }

        LazyColumn(
            modifier = Modifier.fillMaxSize().padding(horizontal = 18.dp),
            verticalArrangement = Arrangement.spacedBy(10.dp),
        ) {
            item {
                SectionTitle(stringResource(R.string.netease_recommend), primary)
            }
            items(home.recommend.chunked(2).size) { rowIndex ->
                val row = home.recommend.chunked(2)[rowIndex]
                Row(horizontalArrangement = Arrangement.spacedBy(12.dp)) {
                    row.forEach { playlist ->
                        PlaylistCard(
                            playlist = playlist,
                            modifier = Modifier.weight(1f),
                            onClick = { viewModel.openPlaylist(playlist) },
                        )
                    }
                    if (row.size == 1) Spacer(Modifier.weight(1f))
                }
            }
            item { SectionTitle(stringResource(R.string.netease_toplists), primary) }
            items(home.toplists.size) { index ->
                val playlist = home.toplists[index]
                PlaylistRow(playlist = playlist, onClick = { viewModel.openPlaylist(playlist) })
            }
            if (home.loading) {
                item { HintText(stringResource(R.string.netease_loading), secondary) }
            } else if (home.error) {
                item {
                    HintText(
                        text = stringResource(R.string.netease_failed),
                        color = secondary,
                        modifier = Modifier.clickable { viewModel.loadHome() },
                    )
                }
            }
            item { Spacer(Modifier.height(96.dp)) }
        }
    }
}

@Composable
private fun SearchResults(
    search: NeteaseMusicViewModel.Search,
    onPlay: (OnlineSong, Int) -> Unit,
    onOpenPlaylist: (OnlinePlaylist) -> Unit,
) {
    val palette = LocalCatClawPalette.current
    val primary = Color(palette.textPrimary)
    val secondary = Color(palette.textSecondary)
    LazyColumn(
        modifier = Modifier.fillMaxSize().padding(horizontal = 18.dp),
        verticalArrangement = Arrangement.spacedBy(8.dp),
    ) {
        if (search.loading) {
            item { HintText(stringResource(R.string.netease_loading), secondary) }
        }
        if (search.songs.isNotEmpty()) {
            item { SectionTitle(stringResource(R.string.netease_songs_section), primary) }
            itemsIndexed(search.songs) { index, song ->
                SongRow(song = song, index = index, onClick = { onPlay(song, index) })
            }
        }
        if (search.playlists.isNotEmpty()) {
            item { SectionTitle(stringResource(R.string.netease_playlists_section), primary) }
            items(search.playlists.chunked(2).size) { rowIndex ->
                val row = search.playlists.chunked(2)[rowIndex]
                Row(horizontalArrangement = Arrangement.spacedBy(12.dp)) {
                    row.forEach { playlist ->
                        PlaylistCard(
                            playlist = playlist,
                            modifier = Modifier.weight(1f),
                            onClick = { onOpenPlaylist(playlist) },
                        )
                    }
                    if (row.size == 1) Spacer(Modifier.weight(1f))
                }
            }
        }
        if (!search.loading && search.songs.isEmpty() && search.playlists.isEmpty()) {
            item { HintText(stringResource(R.string.netease_empty), secondary) }
        }
        item { Spacer(Modifier.height(96.dp)) }
    }
}

@Composable
private fun NeteasePlaylistDetail(
    detail: NeteaseMusicViewModel.Detail,
    onBack: () -> Unit,
    onPlayAll: () -> Unit,
    onPlayAt: (Int) -> Unit,
) {
    val palette = LocalCatClawPalette.current
    val primary = Color(palette.textPrimary)
    val secondary = Color(palette.textSecondary)
    Column(Modifier.fillMaxSize()) {
        Row(
            modifier = Modifier
                .fillMaxWidth()
                .padding(start = 18.dp, end = 18.dp, top = 8.dp, bottom = 6.dp),
            verticalAlignment = Alignment.CenterVertically,
            horizontalArrangement = Arrangement.spacedBy(12.dp),
        ) {
            BackTile(onBack = onBack, description = stringResource(R.string.GoBack))
            CoverTile(
                coverArtPath = detail.playlist?.coverUrl,
                audioPath = null,
                maxPx = 160,
                modifier = Modifier
                    .size(52.dp)
                    .clip(RoundedCornerShape(14.dp))
                    .background(Color(palette.primary).copy(alpha = 0.18f)),
            ) {
                Text(
                    text = detail.playlist?.name?.take(1).orEmpty(),
                    color = primary,
                    fontWeight = FontWeight.Bold,
                    fontSize = 18.sp,
                )
            }
            Column(Modifier.weight(1f)) {
                Text(
                    text = detail.playlist?.name.orEmpty(),
                    fontSize = 19.sp,
                    fontWeight = FontWeight.Bold,
                    color = primary,
                    maxLines = 1,
                    overflow = TextOverflow.Ellipsis,
                )
                Text(
                    text = stringResource(R.string.netease_songs_unit, detail.songs.size),
                    fontSize = 11.sp,
                    color = secondary,
                )
            }
            PillLabel(
                text = stringResource(R.string.netease_play_all),
                onClick = onPlayAll,
                enabled = detail.songs.isNotEmpty(),
            )
        }

        if (detail.loading) {
            HintText(stringResource(R.string.netease_loading), secondary)
        }
        LazyColumn(
            modifier = Modifier.fillMaxSize().padding(horizontal = 18.dp),
            verticalArrangement = Arrangement.spacedBy(8.dp),
        ) {
            itemsIndexed(detail.songs) { index, song ->
                SongRow(song = song, index = index, onClick = { onPlayAt(index) })
            }
            item { Spacer(Modifier.height(96.dp)) }
        }
    }
}

@Composable
private fun SongRow(song: OnlineSong, index: Int, onClick: () -> Unit) {
    val palette = LocalCatClawPalette.current
    val primary = Color(palette.textPrimary)
    val secondary = Color(palette.textSecondary)
    Row(
        modifier = Modifier
            .fillMaxWidth()
            .glassCard(palette = palette, corner = 16)
            .clickable(onClick = onClick)
            .padding(horizontal = 10.dp, vertical = 7.dp),
        verticalAlignment = Alignment.CenterVertically,
        horizontalArrangement = Arrangement.spacedBy(10.dp),
    ) {
        CoverTile(
            coverArtPath = song.coverUrl,
            audioPath = null,
            maxPx = 128,
            modifier = Modifier
                .size(40.dp)
                .clip(RoundedCornerShape(12.dp))
                .background(Color(palette.primary).copy(alpha = 0.16f)),
        ) {
            Text(
                text = (index + 1).toString(),
                fontSize = 13.sp,
                fontWeight = FontWeight.Bold,
                color = primary,
            )
        }
        Column(Modifier.weight(1f)) {
            Text(
                text = song.title,
                fontSize = 14.sp,
                fontWeight = FontWeight.Medium,
                color = primary,
                maxLines = 1,
                overflow = TextOverflow.Ellipsis,
            )
            Text(
                text = listOf(song.artist, song.album).filter { it.isNotBlank() }.joinToString(" · "),
                fontSize = 11.sp,
                color = secondary,
                maxLines = 1,
                overflow = TextOverflow.Ellipsis,
            )
        }
        if (song.vip) {
            Text(
                text = "VIP",
                fontSize = 9.5.sp,
                color = Color(highlightOnGlassCard(palette)),
                modifier = Modifier
                    .clip(RoundedCornerShape(999.dp))
                    .background(Color(palette.textPrimary).copy(alpha = 0.10f))
                    .padding(horizontal = 6.dp, vertical = 2.dp),
            )
        }
    }
}

@Composable
private fun PlaylistCard(
    playlist: OnlinePlaylist,
    modifier: Modifier = Modifier,
    onClick: () -> Unit,
) {
    val palette = LocalCatClawPalette.current
    val primary = Color(palette.textPrimary)
    val secondary = Color(palette.textSecondary)
    // 横屏两列时卡片本身不高（首屏要塞两行），封面固定 112dp；竖屏每列约 200dp 宽，
    // 同样的 112dp 会把封面裁成横条，改成 1:1 与歌单卡的传统观感一致
    val landscape = LocalConfiguration.current.orientation == Configuration.ORIENTATION_LANDSCAPE
    Column(
        modifier = modifier
            .glassCard(palette = palette, corner = 18)
            .clickable(onClick = onClick)
            .padding(8.dp),
    ) {
        CoverTile(
            coverArtPath = playlist.coverUrl,
            audioPath = null,
            maxPx = 320,
            modifier = Modifier
                .fillMaxWidth()
                .then(if (landscape) Modifier.height(112.dp) else Modifier.aspectRatio(1f))
                .clip(RoundedCornerShape(13.dp))
                .background(Color(palette.primary).copy(alpha = 0.18f)),
        ) {
            Text(
                text = playlist.name.take(1),
                fontSize = 26.sp,
                fontWeight = FontWeight.Bold,
                color = primary,
            )
        }
        Spacer(Modifier.height(6.dp))
        Text(
            text = playlist.name,
            fontSize = 12.5.sp,
            color = primary,
            maxLines = 1,
            overflow = TextOverflow.Ellipsis,
        )
        if (playlist.songCount > 0) {
            Text(
                text = stringResource(R.string.netease_songs_unit, playlist.songCount),
                fontSize = 10.sp,
                color = secondary,
            )
        }
    }
}

@Composable
private fun PlaylistRow(playlist: OnlinePlaylist, onClick: () -> Unit) {
    val palette = LocalCatClawPalette.current
    val primary = Color(palette.textPrimary)
    val secondary = Color(palette.textSecondary)
    Row(
        modifier = Modifier
            .fillMaxWidth()
            .glassCard(palette = palette, corner = 16)
            .clickable(onClick = onClick)
            .padding(horizontal = 10.dp, vertical = 7.dp),
        verticalAlignment = Alignment.CenterVertically,
        horizontalArrangement = Arrangement.spacedBy(10.dp),
    ) {
        CoverTile(
            coverArtPath = playlist.coverUrl,
            audioPath = null,
            maxPx = 128,
            modifier = Modifier
                .size(44.dp)
                .clip(RoundedCornerShape(12.dp))
                .background(Color(palette.primary).copy(alpha = 0.16f)),
        ) {
            Text(playlist.name.take(1), color = primary, fontWeight = FontWeight.Bold)
        }
        Column(Modifier.weight(1f)) {
            Text(playlist.name, fontSize = 14.sp, color = primary, maxLines = 1, overflow = TextOverflow.Ellipsis)
            Text(
                text = stringResource(R.string.netease_songs_unit, playlist.songCount),
                fontSize = 11.sp,
                color = secondary,
            )
        }
    }
}

@Composable
private fun SectionTitle(text: String, color: Color) {
    Text(
        text = text,
        fontSize = 15.sp,
        fontWeight = FontWeight.Bold,
        color = color,
        modifier = Modifier.padding(top = 8.dp, bottom = 2.dp),
    )
}

@Composable
private fun HintText(text: String, color: Color, modifier: Modifier = Modifier) {
    Box(modifier = modifier.fillMaxWidth().padding(vertical = 18.dp), contentAlignment = Alignment.Center) {
        Text(text = text, fontSize = 12.5.sp, color = color)
    }
}
