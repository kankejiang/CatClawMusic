package com.catclaw.music.kt.ui.playlist

import androidx.compose.foundation.layout.PaddingValues
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.ui.platform.LocalConfiguration
import androidx.lifecycle.compose.collectAsStateWithLifecycle
import androidx.hilt.navigation.compose.hiltViewModel
import com.catclaw.music.kt.core.library.SongSortKey
import com.catclaw.music.kt.ui.screen.sortLabelRes
import android.content.res.Configuration
import androidx.compose.runtime.Composable
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.unit.dp
import com.catclaw.music.kt.R
import com.catclaw.music.kt.data.db.SongListItem
import com.catclaw.music.kt.data.db.toSong
import com.catclaw.music.kt.ui.song.CollectionAction
import com.catclaw.music.kt.ui.song.CollectionChip
import com.catclaw.music.kt.ui.song.CollectionCover
import com.catclaw.music.kt.ui.song.CollectionSearch
import com.catclaw.music.kt.ui.screen.PlaylistSongRowUi
import com.catclaw.music.kt.ui.screen.PlaylistSongUi
import com.catclaw.music.kt.ui.song.SongCollectionScreen
import com.catclaw.music.kt.ui.song.SongMenuTarget
import com.catclaw.music.kt.ui.song.songMenuSubtitle
import com.catclaw.music.kt.ui.theme.EmptyState
import com.catclaw.music.kt.ui.theme.LocalCatClawPalette

/**
 * 可复用的歌单详情视图（MVVM 渲染层）。
 *
 * 职责边界：
 * - 只吃**状态**（title/count/cover/rows/query）与**回调**（播放/排序/搜索/菜单），
 *   不持有 ViewModel、DAO、网络客户端 —— 因此任何页面都能直接复用；
 * - 布局与音乐库二级页共用 [SongCollectionScreen]：右上角搜索进独立页、排序收进「更多」菜单、
 *   上滑收起大封面头、返回键常驻。
 */
@Composable
fun PlaylistDetailContent(
    title: String,
    countText: String,
    cover: CollectionCover?,
    actions: List<CollectionAction>,
    sortChips: List<CollectionChip>,
    search: CollectionSearch?,
    rows: List<SongListItem>,
    query: String,
    landscape: Boolean,
    onBack: () -> Unit,
    onPlay: (Int) -> Unit,
    onOpenMenu: (SongMenuTarget) -> Unit,
) {
    SongCollectionScreen(
        title = title,
        countText = countText,
        onBack = onBack,
        cover = cover,
        actions = actions,
        chipGroups = listOf(sortChips),
        search = search,
    ) {
        PlaylistSongRows(
            rows = rows,
            query = query,
            compact = landscape,
            onPlay = onPlay,
            onOpenMenu = onOpenMenu,
        )
    }
}

/** 歌单内的歌曲列表：统一行组件（封面/标题/标签/歌手-专辑/时长/长按菜单） */
@Composable
internal fun PlaylistSongRows(
    rows: List<SongListItem>,
    query: String,
    compact: Boolean,
    onPlay: (Int) -> Unit,
    onOpenMenu: (SongMenuTarget) -> Unit,
) {
    val palette = LocalCatClawPalette.current
    val primary = Color(palette.textPrimary)
    val secondary = Color(palette.textSecondary)
    if (rows.isEmpty()) {
        EmptyState(
            hint = if (query.isBlank()) {
                stringResource(R.string.playlist_empty)
            } else {
                stringResource(R.string.search_none, query.trim())
            },
            iconRes = R.drawable.ic_add,
        )
        return
    }
    LazyColumn(
        modifier = Modifier.fillMaxSize(),
        contentPadding = PaddingValues(bottom = if (compact) 80.dp else 96.dp),
    ) {
        items(rows.size, key = { "row-$it" }) { index ->
            val song = rows[index]
            PlaylistSongRowUi(
                song = PlaylistSongUi(
                    key = song.id,
                    title = song.title,
                    artist = song.artist.orEmpty(),
                    album = song.album.orEmpty(),
                    cover = song.coverArtPath,
                    coverAudioPath = song.filePath,
                    durationSec = song.durationSeconds,
                ),
                index = index,
                isLandscape = compact,
                onClick = { onPlay(index) },
                onLongClick = {
                    onOpenMenu(
                        SongMenuTarget(
                            id = song.id,
                            title = song.title,
                            subtitle = songMenuSubtitle(song.artist, song.album),
                            song = song.toSong(),
                        ),
                    )
                },
            )
        }
    }
}

/**
 * 独立歌单详情页（开箱即用）：任何页面一行 `PlaylistDetailScreen(playlistId, onBack, onOpenMenu)` 即可。
 *
 * 内部自己 hiltViewModel + 收集状态，调用方不需要传歌单实体、封面、actions、排序 chips、搜索。
 * 返回时调用方负责 `viewModel.close()`（页内嵌场景由外层 BackHandler 处理）。
 */
@Composable
fun PlaylistDetailScreen(
    playlistId: Int,
    onBack: () -> Unit,
    onOpenMenu: (SongMenuTarget) -> Unit,
    viewModel: PlaylistDetailViewModel = hiltViewModel(),
) {
    LaunchedEffect(playlistId) { viewModel.open(playlistId) }
    val playlist by viewModel.playlist.collectAsStateWithLifecycle()
    val cover by viewModel.cover.collectAsStateWithLifecycle()
    val rows by viewModel.displayItems.collectAsStateWithLifecycle()
    val sort by viewModel.itemSort.collectAsStateWithLifecycle()
    val descending by viewModel.itemSortDescending.collectAsStateWithLifecycle()
    val query by viewModel.itemQuery.collectAsStateWithLifecycle()
    val landscape = LocalConfiguration.current.orientation == Configuration.ORIENTATION_LANDSCAPE
    val entity = playlist ?: return

    PlaylistDetailContent(
        title = entity.name,
        countText = stringResource(R.string.playlist_count, rows.size),
        cover = CollectionCover(
            coverArtPath = cover?.coverArtPath,
            audioPath = cover?.filePath ?: rows.firstOrNull()?.filePath,
            fallback = entity.name,
        ),
        actions = listOf(
            CollectionAction(
                label = stringResource(R.string.playlist_play_all),
                iconRes = R.drawable.ic_play,
                primary = true,
                onClick = { viewModel.playAll() },
            ),
            CollectionAction(
                label = stringResource(R.string.shuffle_play),
                iconRes = R.drawable.ic_shuffle,
                onClick = { viewModel.playAllShuffled() },
            ),
            CollectionAction(
                label = stringResource(R.string.playlist_add_current),
                iconRes = R.drawable.ic_add,
                onClick = { viewModel.addCurrentSong() },
            ),
        ),
        sortChips = SongSortKey.entries.map { key ->
            CollectionChip(
                label = stringResource(sortLabelRes(key)) +
                    if (sort == key) (if (descending) " ↓" else " ↑") else "",
                selected = sort == key,
                onClick = { viewModel.setItemSort(key) },
            )
        },
        search = CollectionSearch(
            query,
            stringResource(R.string.playlist_search_hint),
            viewModel::setItemQuery,
        ),
        rows = rows,
        query = query,
        landscape = landscape,
        onBack = onBack,
        onPlay = viewModel::playAt,
        onOpenMenu = onOpenMenu,
    )
}
