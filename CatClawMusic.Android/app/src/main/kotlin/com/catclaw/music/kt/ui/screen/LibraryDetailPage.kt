package com.catclaw.music.kt.ui.screen

import android.content.res.Configuration
import androidx.activity.compose.BackHandler
import androidx.annotation.StringRes
import androidx.compose.foundation.Canvas
import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.clickable
import androidx.compose.foundation.combinedClickable
import androidx.compose.foundation.horizontalScroll
import androidx.compose.foundation.layout.aspectRatio
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.PaddingValues
import androidx.compose.foundation.layout.RowScope
import com.catclaw.music.kt.core.theme.CatClawPalette
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxHeight
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.lazy.grid.GridCells
import androidx.compose.foundation.lazy.grid.LazyVerticalGrid
import androidx.compose.foundation.lazy.grid.itemsIndexed as gridItemsIndexed
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.LazyRow
import androidx.compose.foundation.lazy.itemsIndexed
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.verticalScroll
import androidx.compose.material3.AlertDialog
import androidx.compose.material3.Icon
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.produceState
import com.catclaw.music.kt.core.library.QualityBucket
import com.catclaw.music.kt.core.library.SongSortKey
import com.catclaw.music.kt.core.library.SongSourceFilter
import com.catclaw.music.kt.core.library.SongStat
import com.catclaw.music.kt.core.library.artistInsight
import com.catclaw.music.kt.core.library.qualityInsight
import com.catclaw.music.kt.core.library.sizeBuckets
import com.catclaw.music.kt.core.model.SongDetailFormat
import com.catclaw.music.kt.ui.stats.ListeningStatsScreen
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.geometry.Offset
import androidx.compose.ui.geometry.Size
import androidx.compose.ui.graphics.StrokeCap
import androidx.compose.ui.graphics.drawscope.Stroke
import androidx.compose.ui.graphics.Brush
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.platform.LocalConfiguration
import androidx.compose.ui.res.painterResource
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.Dp
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import androidx.hilt.navigation.compose.hiltViewModel
import androidx.lifecycle.compose.collectAsStateWithLifecycle
import com.catclaw.music.kt.R
import com.catclaw.music.kt.data.db.AlbumHeader
import com.catclaw.music.kt.data.db.AlbumListItem
import com.catclaw.music.kt.data.db.SongListItem
import com.catclaw.music.kt.ui.song.AddToPlaylistDialog
import com.catclaw.music.kt.ui.song.SongBelongs
import com.catclaw.music.kt.ui.song.SongBelongsKind
import com.catclaw.music.kt.ui.player.CoverTile
import com.catclaw.music.kt.ui.song.SongDetailScreen
import com.catclaw.music.kt.ui.song.CollectionAction
import com.catclaw.music.kt.ui.song.CollectionChip
import com.catclaw.music.kt.ui.song.CollectionCover
import com.catclaw.music.kt.ui.song.CollectionSearch
import com.catclaw.music.kt.ui.song.SongCollectionScreen
import com.catclaw.music.kt.ui.song.SongList
import com.catclaw.music.kt.ui.song.rememberSongMenu
import com.catclaw.music.kt.ui.library.LibraryViewModel
import com.catclaw.music.kt.ui.theme.BackTile
import com.catclaw.music.kt.ui.theme.EmptyState
import com.catclaw.music.kt.ui.theme.GlassChip
import com.catclaw.music.kt.ui.theme.GlassSearchField
import com.catclaw.music.kt.ui.theme.LocalCatClawPalette
import com.catclaw.music.kt.ui.theme.PillLabel
import com.catclaw.music.kt.ui.theme.isLightPalette
import com.catclaw.music.kt.ui.theme.inkOn
import com.catclaw.music.kt.ui.theme.badgeTintOn
import com.catclaw.music.kt.ui.theme.glassCard

/** 艺人/专辑二级详情页（从 LibraryScreen.kt 拆出） */
@Composable
internal fun LibraryDetailScreen(
    detail: LibraryDetail,
    onBack: () -> Unit,
    onOpenSong: (Int) -> Unit,
    /** 从下钻页里的歌再跳到它的另一个归属（专辑页 → 歌手页） */
    onOpenDetail: (LibraryDetail) -> Unit,
    viewModel: LibraryViewModel,
) {
    val palette = LocalCatClawPalette.current
    val byArtist = detail.kind == LibraryKind.Artists
    // 艺人页的第二个 Tab：MAUI `ArtistDetailPage` 是「专辑 / 歌曲」两栏，本端此前只有歌曲列表 ——
    // 想知道"这个人出过哪几张专辑"只能退回艺人列表一张张翻。专辑 Tab 的成员判据与歌曲列表同源
    // （见 `SQL_ALBUMS_OF_ARTIST`），两个 Tab 不会对"这是不是他的专辑"给出两个答案
    var albumTab by remember(detail) { mutableStateOf(false) }
    val rows by produceState<List<SongListItem>?>(initialValue = null, detail) {
        value = if (byArtist) viewModel.songsOfArtist(detail.id) else viewModel.songsOfAlbum(detail.id)
    }
    val loaded = rows.orEmpty()
    val artistAlbums by produceState<List<AlbumListItem>?>(initialValue = null, detail, albumTab) {
        value = if (byArtist && albumTab) viewModel.albumsOfArtist(detail.id) else null
    }
    // 专辑头卡（封面 / 歌手 / 年份）。艺人页不取：艺人没有专辑封面这回事，那头像列一直是空的
    val albumHeader by produceState<AlbumHeader?>(initialValue = null, detail) {
        value = if (byArtist) null else viewModel.albumHeader(detail.id)
    }
    val playlists by viewModel.playlists.collectAsStateWithLifecycle()
    // 「＋ 添加到」的选择弹层。跟着 detail 重置：换了一张专辑不该还开着上一张的框
    var addToPlaylistOpen by remember(detail) { mutableStateOf(false) }

    val head = albumHeader
    val subtitle = listOfNotNull(
        head?.artist?.takeIf { it.isNotBlank() },
        head?.year?.takeIf { it > 0 }?.toString(),
    ).joinToString(" · ").takeIf { it.isNotBlank() }

    SongCollectionScreen(
        title = detail.name,
        countText = stringResource(R.string.playlist_count, loaded.size),
        subtitle = subtitle,
        onBack = onBack,
        cover = head?.let { CollectionCover(it.coverArtPath, it.sampleFilePath, detail.name) },
        actions = if (loaded.isNotEmpty() && !(byArtist && albumTab)) {
            listOfNotNull(
                CollectionAction(
                    label = stringResource(R.string.playlist_play_all),
                    iconRes = R.drawable.ic_play,
                    primary = true,
                    onClick = { viewModel.playRows(loaded, 0) },
                ),
                CollectionAction(
                    label = stringResource(R.string.shuffle_play),
                    iconRes = R.drawable.ic_shuffle,
                    onClick = { viewModel.playShuffled(loaded) },
                ),
                // 艺人页那份列表跨专辑，没有「整张加进歌单」的语义
                if (!byArtist) {
                    CollectionAction(
                        label = stringResource(R.string.song_menu_add_to_playlist),
                        iconRes = R.drawable.ic_add,
                        onClick = { addToPlaylistOpen = true },
                    )
                } else null,
            )
        } else emptyList(),
        chipGroups = if (byArtist) {
            listOf(
                listOf(
                    CollectionChip(
                        label = stringResource(R.string.disc_cat_album),
                        selected = albumTab,
                        onClick = { albumTab = true },
                    ),
                    CollectionChip(
                        label = stringResource(R.string.TabSongs),
                        selected = !albumTab,
                        onClick = { albumTab = false },
                    ),
                ),
            )
        } else emptyList(),
    ) {
        if (byArtist && albumTab) {
            val list = artistAlbums.orEmpty()
            InfoList(
                rows = list.map {
                    InfoRow(
                        title = it.title,
                        // 这里是艺人页里的专辑 Tab：每张的歌手都是同一个人，写歌手等于没写
                        subtitle = stringResource(R.string.playlist_count, it.songCount),
                        coverArtPath = it.coverArtPath,
                        sampleFilePath = it.sampleFilePath,
                    )
                },
                emptyHint = stringResource(R.string.library_none),
                // 点一行就是进这张专辑的二级页，与专辑 Tab 里的卡片同一条路径
                onOpen = { index ->
                    onOpenDetail(LibraryDetail(LibraryKind.Albums, list[index].id, list[index].title))
                },
            )
        } else {
            SongList(
                rows = loaded,
                subtitleOf = { if (byArtist) it.album else it.artist },
                menu = rememberSongMenu(viewModel, onOpenBelongs = { onOpenDetail(it.toLibraryDetail()) }) { onOpenSong(it) },
                emptyHint = stringResource(R.string.song_list_empty_hint),
                onPlay = { viewModel.playRows(loaded, it) },
            )
        }
    }

    if (addToPlaylistOpen) {
        AddToPlaylistDialog(
            title = detail.name,
            playlists = playlists.map { it.id to it.name },
            onPick = { playlistId ->
                // 加的是**屏幕上这份列表**（当前 Tab 的 loaded），不是库里那张专辑的全部歌
                viewModel.addToPlaylist(playlistId, loaded.map { it.id })
                addToPlaylistOpen = false
            },
            onDismiss = { addToPlaylistOpen = false },
        )
    }
}

/**
 * 「＋ 添加到」的歌单选择弹层曾在这里；现搬到 `ui/song/SongActionMenu.kt` 共用
 * （歌曲长按菜单也要用它，两份迟早分叉）。
 *
 * C# 那边 `AlbumDetailPage.xaml:55` 也有一颗同名按钮，但它绑的 `AddToPlaylistCommand`
 * 在整个 C# 工程里都不存在 —— MAUI 的绑定失败是静默的，于是那颗按钮点了没反应。
 * 本端做实它（不画点了不动的项，在这条上是把按钮修好）。
 */

/**
 * 艺人 / 专辑列表的一行。
 *
 * [coverArtPath] 是本端库里存的那一列（专辑/艺人级封面一直是空的：本端没有 MAUI 那台抽图引擎），
 * [sampleFilePath] 是代表曲 —— 出图实际靠它读内嵌封面（与发现页的卡片同一条路子）。
 */
internal data class InfoRow(
    val title: String,
    val subtitle: String,
    val coverArtPath: String? = null,
    val sampleFilePath: String? = null,
)

/**
 * 宫格 / 列表切换 —— MAUI 列表页顶部那对图标按钮（选中项用主色底 + 对比墨色）。
 * 64dp 的两颗按钮挤不下，所以做成一个胶囊里并排两颗 34dp 的图标。
 */
