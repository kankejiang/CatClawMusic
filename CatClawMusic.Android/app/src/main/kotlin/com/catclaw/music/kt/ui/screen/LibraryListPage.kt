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

/** 音乐库统计格点进来的独立列表页（歌曲/收藏/最近/艺人/专辑），从 LibraryScreen.kt 拆出 */
@Composable
fun LibraryListScreen(
    kind: LibraryKind,
    onBack: () -> Unit,
    onOpen: (LibraryDetail) -> Unit,
    onOpenSong: (Int) -> Unit,
    viewModel: LibraryViewModel = hiltViewModel(),
) {
    val palette = LocalCatClawPalette.current
    val songs by viewModel.songs.collectAsStateWithLifecycle()
    val favorites by viewModel.favorites.collectAsStateWithLifecycle()
    val recent by viewModel.recent.collectAsStateWithLifecycle()
    val displaySongs by viewModel.displaySongs.collectAsStateWithLifecycle()
    val shownArtists by viewModel.displayArtists.collectAsStateWithLifecycle()
    val shownAlbums by viewModel.displayAlbums.collectAsStateWithLifecycle()
    val infoQuery by viewModel.infoQuery.collectAsStateWithLifecycle()
    val sort by viewModel.songSort.collectAsStateWithLifecycle()
    val sortDescending by viewModel.songSortDescending.collectAsStateWithLifecycle()
    val query by viewModel.songQuery.collectAsStateWithLifecycle()
    // 全部歌曲页的来源筛选与它真正显示的那份列表（含网络曲）
    val songSource by viewModel.songSource.collectAsStateWithLifecycle()
    val displayAll by viewModel.displayAllSongs.collectAsStateWithLifecycle()
    // 艺人页与专辑页共用一个搜索词，换页要清掉：留着上一张页的关键词，
    // 新页看起来像是「什么都没有」
    LaunchedEffect(kind) { viewModel.setInfoQuery("") }
    // 宫格 / 列表：两张页各记一份、默认值各按 MAUI 的初值（艺人默认宫格、专辑默认列表，
    // 见 `ArtistsViewModel.IsGridView = true` / `AlbumsViewModel.IsGridView = false`）。
    // 与 MAUI 的 ViewModel 字段一样只活在内存里，不落盘
    var artistsGrid by remember { mutableStateOf(true) }
    var albumsGrid by remember { mutableStateOf(false) }
    val menu = rememberSongMenu(viewModel, onOpenBelongs = { onOpen(it.toLibraryDetail()) }) { onOpenSong(it) }
    // MAUI 的 AllSongsPage 顶部就是「播放全部 / 随机播放」两个按钮，本端此前只能一行一行点着播。
    // 艺人/专辑页没有"整张表"这张列表，所以那两个按钮只在歌曲类页面上画。
    // 这里绑的是**屏幕上那份列表**：搜索框里有字、或排的是时长时，「播放全部」要播的是看到的那些，
    // 而不是数据库里的全部——否则按钮和列表就成了两件事
    val songRows: List<SongListItem>? = when (kind) {
        LibraryKind.Songs -> displayAll
        LibraryKind.Favorites -> favorites
        LibraryKind.Recent -> recent
        else -> null
    }

    val title = stringResource(
        when (kind) {
            LibraryKind.Songs -> R.string.lib_stat_songs
            LibraryKind.Artists -> R.string.lib_stat_artists
            LibraryKind.Albums -> R.string.lib_stat_albums
            LibraryKind.Favorites -> R.string.lib_favorites_name
            LibraryKind.Recent -> R.string.playlist_entry_recent
        },
    )
    val count = when (kind) {
        // 头部那个数字跟着列表走：搜出 3 首却还写着「128 首」，读起来像是搜索没生效
        LibraryKind.Songs -> displayAll.size
        LibraryKind.Favorites -> favorites.size
        LibraryKind.Recent -> recent.size
        LibraryKind.Artists -> shownArtists.size
        LibraryKind.Albums -> shownAlbums.size
    }
    val countText = when (kind) {
        // 量词要跟着内容走：艺人页顶着一句「32 首」是把人当歌唱
        LibraryKind.Artists -> stringResource(R.string.lib_count_artists, count)
        LibraryKind.Albums -> stringResource(R.string.lib_count_albums, count)
        else -> stringResource(R.string.playlist_count, count)
    }

    // 筛选 / 排序 / 搜索按 kind 配好交给通用二级页；横屏会合并成一条横滚轨道，
    // 竖屏仍按组各占一行，行为与原来逐页堆的版本一致。
    val sourceChips = if (kind == LibraryKind.Songs) {
        SongSourceFilter.entries.map { mode ->
            CollectionChip(
                label = stringResource(sourceFilterLabelRes(mode)),
                selected = songSource == mode,
                onClick = { viewModel.setSongSource(mode) },
            )
        }
    } else emptyList()
    val sortChips = if (kind == LibraryKind.Songs) {
        SongSortKey.entries.map { mode ->
            CollectionChip(
                label = stringResource(sortLabelRes(mode)) +
                    if (sort == mode) (if (sortDescending) " ↓" else " ↑") else "",
                selected = sort == mode,
                onClick = { viewModel.setSongSort(mode) },
            )
        }
    } else emptyList()
    val collectionSearch = when (kind) {
        LibraryKind.Songs -> CollectionSearch(
            query, stringResource(R.string.library_search_hint), viewModel::setSongQuery,
        )
        LibraryKind.Artists -> CollectionSearch(
            infoQuery, stringResource(R.string.library_search_artist_hint), viewModel::setInfoQuery,
        )
        LibraryKind.Albums -> CollectionSearch(
            infoQuery, stringResource(R.string.library_search_album_hint), viewModel::setInfoQuery,
        )
        else -> null
    }

    // 无歌单封面时用列表第一首歌的封面当头图（网易云同款行为）。
    // 注意不能拿 coverArtPath 判空：本地歌封面内嵌在音频文件里，该字段本来就是 null，
    // 只有 filePath 有值（CoverTile 会从内嵌标签抽图）。
    val firstSong = when (kind) {
        LibraryKind.Songs -> displayAll.firstOrNull()
        LibraryKind.Favorites -> favorites.firstOrNull()
        LibraryKind.Recent -> recent.firstOrNull()
        else -> null
    }
    val heroCover = firstSong?.let {
        CollectionCover(coverArtPath = it.coverArtPath, audioPath = it.filePath, fallback = title)
    }
    SongCollectionScreen(
        title = title,
        countText = countText,
        cover = heroCover,
        onBack = onBack,
        actions = songRows?.let { rows ->
            listOf(
                CollectionAction(
                    label = stringResource(R.string.playlist_play_all),
                    iconRes = R.drawable.ic_play,
                    primary = true,
                    onClick = {
                        when (kind) {
                            LibraryKind.Favorites -> viewModel.playFavoriteAt(0)
                            else -> viewModel.playRows(rows, 0)
                        }
                    },
                ),
                CollectionAction(
                    label = stringResource(R.string.shuffle_play),
                    iconRes = R.drawable.ic_shuffle,
                    onClick = { viewModel.playShuffled(rows) },
                ),
            )
        } ?: emptyList(),
        chipGroups = listOfNotNull(
            sourceChips.takeIf { it.isNotEmpty() },
            sortChips.takeIf { it.isNotEmpty() },
        ),
        search = collectionSearch,
        trailing = {
            when (kind) {
                LibraryKind.Artists -> ViewModeToggle(grid = artistsGrid) { artistsGrid = it }
                LibraryKind.Albums -> ViewModeToggle(grid = albumsGrid) { albumsGrid = it }
                else -> Unit
            }
        },
    ) {
        when (kind) {
            LibraryKind.Songs -> SongList(
                displayAll, subtitleOf = { it.artist }, menu = menu,
                emptyHint = when {
                    // 搜出来是空的时候不能说"曲库为空"——那是两件事，用户会以为歌掉了
                    query.isNotBlank() -> stringResource(R.string.search_none, query.trim())
                    // 选了「网络」而一首都没有时同理：曲库不是空的，是这一档没有内容。
                    // 这时候劝人去"点扫描曲库"是答非所问（网络曲本来就扫不出来）
                    songSource != SongSourceFilter.All -> stringResource(R.string.library_none)
                    else -> stringResource(R.string.library_empty, songs.size)
                },
            ) { viewModel.playRows(displayAll, it) }
            LibraryKind.Favorites -> SongList(
                favorites, subtitleOf = { it.artist }, menu = menu,
                // 这句以前是「曲库为空，点扫描曲库」—— 收藏为空和曲库为空是两件事，
                // 而且这里正是该教用户「长按」的地方（真机截图时发现的）
                emptyHint = stringResource(R.string.favorite_empty_hint),
            ) { viewModel.playFavoriteAt(it) }
            // 最近播放：数据源与歌单页顶部那张入口卡同一条流（LibraryViewModel.recent），
            // 所以两处的顺序与内容永远一致
            LibraryKind.Recent -> SongList(
                recent, subtitleOf = { it.artist }, menu = menu,
                emptyHint = stringResource(R.string.recent_empty_hint),
            ) { viewModel.playRecentAt(it) }
            LibraryKind.Artists -> InfoList(
                rows = shownArtists.map {
                    InfoRow(
                        title = it.name,
                        subtitle = stringResource(R.string.playlist_count, it.songCount),
                        coverArtPath = it.cover,
                        sampleFilePath = it.sampleFilePath,
                    )
                },
                emptyHint = if (infoQuery.isBlank()) {
                    stringResource(R.string.library_none)
                } else {
                    stringResource(R.string.library_search_none, infoQuery.trim())
                },
                onOpen = { index -> onOpen(LibraryDetail(LibraryKind.Artists, shownArtists[index].id, shownArtists[index].name)) },
                grid = artistsGrid,
            )
            LibraryKind.Albums -> InfoList(
                rows = shownAlbums.map {
                    InfoRow(
                        title = it.title,
                        // MAUI 的专辑行是「歌手 · N 首 · 年份」：年份在本端没有这一列，
                        // 先把「歌手 · N 首」补齐 —— 只写歌手名，一眼看不出这张专辑有几首
                        subtitle = listOf(it.artist, stringResource(R.string.playlist_count, it.songCount))
                            .filter { part -> part.isNotBlank() }
                            .joinToString(" · "),
                        coverArtPath = it.coverArtPath,
                        sampleFilePath = it.sampleFilePath,
                    )
                },
                emptyHint = if (infoQuery.isBlank()) {
                    stringResource(R.string.library_none)
                } else {
                    stringResource(R.string.library_search_none, infoQuery.trim())
                },
                onOpen = { index -> onOpen(LibraryDetail(LibraryKind.Albums, shownAlbums[index].id, shownAlbums[index].title)) },
                grid = albumsGrid,
            )
        }
    }

}

/**
 * 艺人 / 专辑二级页：进页现读一次歌单（刚扫完库要能看到新结果），点一行就是"从这个位置起播这张列表"。
 *
 * 副标题两种页故意不同：艺人页每首的歌手都一样，展示歌手等于没展示，所以给专辑名；
 * 专辑页反过来给歌手名。
 */
