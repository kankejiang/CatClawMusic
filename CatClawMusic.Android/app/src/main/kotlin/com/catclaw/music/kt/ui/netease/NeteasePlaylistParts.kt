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

/** 网易云歌单详情与行组件（从 NeteaseMusicScreen.kt 拆出） */
@Composable
internal fun NeteasePlaylistDetail(
    detail: NeteaseMusicViewModel.Detail,
    likedIds: Set<String>,
    onBack: () -> Unit,
    onPlayAll: () -> Unit,
    onPlayAllShuffled: () -> Unit,
    songs: List<OnlineSong>,
    query: String,
    sort: SongSortKey?,
    descending: Boolean,
    onQuery: (String) -> Unit,
    onSort: (SongSortKey) -> Unit,
    onPlayAt: (Int) -> Unit,
    onLike: (OnlineSong, Boolean) -> Unit,
) {
    val secondary = Color(LocalCatClawPalette.current.textSecondary)
    val landscape = LocalConfiguration.current.orientation == Configuration.ORIENTATION_LANDSCAPE

    // 与本地歌单/音乐库二级页完全同一套骨架：大封面头、上滑收起、返回键、统一行组件
    SongCollectionScreen(
        title = detail.playlist?.name.orEmpty(),
        countText = stringResource(R.string.playlist_count, detail.songs.size),
        onBack = onBack,
        cover = com.catclaw.music.kt.ui.song.CollectionCover(
            coverArtPath = detail.playlist?.coverUrl,
            audioPath = null,
            fallback = detail.playlist?.name.orEmpty(),
        ),
        actions = listOf(
            com.catclaw.music.kt.ui.song.CollectionAction(
                label = stringResource(R.string.netease_play_all),
                iconRes = R.drawable.ic_play,
                primary = true,
                onClick = onPlayAll,
            ),
            com.catclaw.music.kt.ui.song.CollectionAction(
                label = stringResource(R.string.shuffle_play),
                iconRes = R.drawable.ic_shuffle,
                onClick = onPlayAllShuffled,
            ),
        ),
        chipGroups = listOf(
            SongSortKey.entries.map { key ->
                com.catclaw.music.kt.ui.song.CollectionChip(
                    label = stringResource(sortLabelRes(key)) +
                        if (sort == key) (if (descending) " ↓" else " ↑") else "",
                    selected = sort == key,
                    onClick = { onSort(key) },
                )
            },
        ),
        search = com.catclaw.music.kt.ui.song.CollectionSearch(
            query,
            stringResource(R.string.playlist_search_hint),
            onQuery,
        ),
    ) {
        if (detail.loading) {
            HintText(stringResource(R.string.netease_loading), secondary)
        }
        LazyColumn(
            modifier = Modifier.fillMaxSize(),
            contentPadding = PaddingValues(bottom = 96.dp),
        ) {
            itemsIndexed(songs, key = { index, song -> "d-$index-${song.id}" }) { index, song ->
                PlaylistSongRowUi(
                    song = PlaylistSongUi(
                        key = song.id,
                        title = song.title,
                        artist = song.artist,
                        album = song.album,
                        cover = song.coverUrl,
                        durationSec = song.durationSeconds,
                        vip = song.vip,
                        blocked = song.blocked,
                        favorite = song.id in likedIds,
                    ),
                    index = index,
                    isLandscape = landscape,
                    onClick = { onPlayAt(detail.songs.indexOf(song).coerceAtLeast(0)) },
                    onLongClick = { onLike(song, song.id !in likedIds) },
                )
            }
        }
    }
}
@Composable
internal fun SongRow(
    song: OnlineSong,
    index: Int,
    liked: Boolean,
    onClick: () -> Unit,
    onLike: () -> Unit,
) {
    val palette = LocalCatClawPalette.current
    val primary = Color(palette.textPrimary)
    val secondary = Color(palette.textSecondary)
    Row(
        modifier = Modifier
            .fillMaxWidth()
            // 下架/无版权整行降透明度：还能点（可能外链可播），但不该看起来和正常曲一样
            .alpha(if (song.blocked) 0.45f else 1f)
            .glassCard(palette = palette, corner = 16)
            // 点一下播放；长按切换红心（在线曲没有"移出歌单"的概念，长按就一件事）
            .combinedClickable(onClick = onClick, onLongClick = onLike)
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
        if (liked) {
            Text(text = "❤", fontSize = 12.sp, color = Color(highlightOnGlassCard(palette)))
        }
        if (song.blocked) {
            Text(
                text = stringResource(R.string.netease_blocked),
                fontSize = 9.5.sp,
                color = secondary,
                modifier = Modifier
                    .clip(RoundedCornerShape(999.dp))
                    .background(Color(palette.textPrimary).copy(alpha = 0.08f))
                    .padding(horizontal = 6.dp, vertical = 2.dp),
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
internal fun PlaylistCard(
    playlist: OnlinePlaylist,
    landscape: Boolean,
    modifier: Modifier = Modifier,
    subtitle: String? = null,
    onClick: () -> Unit,
) {
    val palette = LocalCatClawPalette.current
    val primary = Color(palette.textPrimary)
    val secondary = Color(palette.textSecondary)
    // 横屏两列时卡片本身不高（首屏要塞两行），封面固定 112dp；竖屏每列约 200dp 宽，
    // 同样的 112dp 会把封面裁成横条，改成 1:1 与歌单卡的传统观感一致
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
                text = subtitle ?: stringResource(R.string.netease_songs_unit, playlist.songCount),
                fontSize = 10.sp,
                color = secondary,
            )
        }
    }
}

@Composable
internal fun PlaylistRow(playlist: OnlinePlaylist, onClick: () -> Unit) {
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

// ── 登录面板 ───────────────────────────────────────────────────
