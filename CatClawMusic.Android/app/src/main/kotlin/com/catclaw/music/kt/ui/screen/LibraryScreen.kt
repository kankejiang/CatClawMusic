package com.catclaw.music.kt.ui.screen

import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.clickable
import androidx.compose.foundation.combinedClickable
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.PaddingValues
import androidx.compose.foundation.layout.RowScope
import com.catclaw.music.kt.core.theme.CatClawPalette
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.itemsIndexed
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.verticalScroll
import androidx.compose.material3.Icon
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.produceState
import androidx.compose.runtime.remember
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
import com.catclaw.music.kt.ui.song.SongDetailScreen
import com.catclaw.music.kt.ui.song.SongList
import com.catclaw.music.kt.ui.song.rememberSongMenu
import com.catclaw.music.kt.ui.library.LibraryViewModel
import com.catclaw.music.kt.ui.theme.BackTile
import com.catclaw.music.kt.ui.theme.EmptyState
import com.catclaw.music.kt.ui.theme.LocalCatClawPalette
import com.catclaw.music.kt.ui.theme.PillLabel
import com.catclaw.music.kt.ui.theme.isLightPalette
import com.catclaw.music.kt.ui.theme.badgeTintOn
import com.catclaw.music.kt.ui.theme.glassCard

/**
 * 音乐库 —— 照 MAUI `LibraryPage.xaml` 做成**枢纽页**：Hero 卡 + 4 个统计格 + 资料库列表。
 * 之前这里的 歌曲/艺人/专辑/收藏 四个 tab 是我自己加的，MAUI 没有：统计格才是入口，
 * 点进去是独立列表页（对应 `AllSongsPage` / `ArtistsPage` / `AlbumsPage`）。
 *
 * 只放**取得到**的数据：小时数由本地曲库时长求和；MAUI 的「存储占用」「数据洞察环形图」
 * 需要文件大小与格式分布，本端没有这两个聚合查询，就不摆空壳（宁缺毋假）。
 */
enum class LibraryKind { Songs, Artists, Albums, Favorites }

/**
 * 艺人 / 专辑的二级页目标 —— 对应 MAUI push 的 `ArtistDetailPage` / `AlbumDetailPage`。
 * 带 id 而不是名字：同名艺人在曲库里是两回事（见 `SQL_SONGS_OF_ARTIST` 的注释）。
 */
data class LibraryDetail(val kind: LibraryKind, val id: Int, val name: String)

@Composable
fun LibraryScreen(viewModel: LibraryViewModel = hiltViewModel()) {
    // 统计格 / 资料库行的下钻。MAUI 是 push 独立页（AllSongsPage 等），这里在页内切换：
    // 少一条路由与一次 VM 作用域切换，返回行为一致
    var opened by remember { mutableStateOf<LibraryKind?>(null) }
    var detail by remember { mutableStateOf<LibraryDetail?>(null) }
    // 歌曲详情是整页，托管在页面这一层而不是列表里：否则它会叠在列表页头下面，出现两个返回键
    var songDetailId by remember { mutableStateOf<Int?>(null) }
    songDetailId?.let { id ->
        SongDetailScreen(songId = id, load = { viewModel.songDetail(it) }, onBack = { songDetailId = null })
        return
    }
    detail?.let {
        LibraryDetailScreen(
            detail = it,
            onBack = { detail = null },
            onOpenSong = { songDetailId = it },
            viewModel = viewModel,
        )
        return
    }
    opened?.let { kind ->
        LibraryListScreen(
            kind = kind,
            onBack = { opened = null },
            onOpen = { detail = it },
            onOpenSong = { songDetailId = it },
            viewModel = viewModel,
        )
        return
    }
    val onOpenKind: (LibraryKind) -> Unit = { opened = it }
    val palette = LocalCatClawPalette.current
    val songs by viewModel.songs.collectAsStateWithLifecycle()
    val artists by viewModel.artists.collectAsStateWithLifecycle()
    val albums by viewModel.albums.collectAsStateWithLifecycle()
    val favorites by viewModel.favorites.collectAsStateWithLifecycle()
    val scanning by viewModel.scanning.collectAsStateWithLifecycle()
    val hours = songs.sumOf { it.durationSeconds.toLong() } / 3600.0

    Column(
        modifier = Modifier
            .fillMaxSize()
            .verticalScroll(rememberScrollState())
            .padding(bottom = 132.dp),
    ) {
        Row(
            modifier = Modifier.fillMaxWidth().padding(start = 20.dp, end = 20.dp, top = 12.dp, bottom = 4.dp),
            verticalAlignment = Alignment.CenterVertically,
            horizontalArrangement = Arrangement.SpaceBetween,
        ) {
            Text(
                text = stringResource(R.string.TabLibrary),
                style = MaterialTheme.typography.titleLarge,
                color = Color(palette.textPrimary),
            )
            PillLabel(
                text = stringResource(if (scanning) R.string.library_scanning else R.string.ScanMusic),
                onClick = { viewModel.scan() },
                enabled = !scanning,
            )
        }

        // Hero 卡：渐变底 + 4 个可点统计格（MAUI 的 HeroCard + st4）
        Box(
            modifier = Modifier
                .fillMaxWidth()
                .padding(horizontal = 18.dp, vertical = 6.dp)
                .clip(RoundedCornerShape(26.dp))
                .background(
                    Brush.linearGradient(
                        listOf(Color(palette.primary).copy(alpha = 0.40f), Color(0x40FFD9A8)),
                    ),
                )
                .border(1.dp, Color(palette.textPrimary).copy(alpha = 0.16f), RoundedCornerShape(26.dp))
                .padding(22.dp),
        ) {
            Column {
                Text(
                    text = stringResource(R.string.lib_my_library),
                    fontSize = 22.sp,
                    fontWeight = FontWeight.Bold,
                    color = Color(palette.textPrimary),
                )
                Text(
                    text = stringResource(R.string.lib_sources_count, 1),
                    fontSize = 12.5.sp,
                    // 半透明 Hero 上不用次要色（浅色主题实测 3.15:1），层级交给字号
                    color = Color(palette.textPrimary),
                    modifier = Modifier.padding(top = 4.dp),
                )
                Row(
                    modifier = Modifier.fillMaxWidth().padding(top = 18.dp),
                    horizontalArrangement = Arrangement.spacedBy(8.dp),
                ) {
                    StatTile(String.format("%,d", songs.size), stringResource(R.string.lib_stat_songs), palette) {
                        onOpenKind(LibraryKind.Songs)
                    }
                    StatTile(String.format("%,d", artists.size), stringResource(R.string.lib_stat_artists), palette) {
                        onOpenKind(LibraryKind.Artists)
                    }
                    StatTile(String.format("%,d", albums.size), stringResource(R.string.lib_stat_albums), palette) {
                        onOpenKind(LibraryKind.Albums)
                    }
                    StatTile(String.format("%.1f", hours), stringResource(R.string.lib_stat_hours), palette) {
                        onOpenKind(LibraryKind.Songs)
                    }
                }
            }
        }

        // 资料库卡：本端真实存在的两个来源（本地曲库 / 我喜欢的）。
        // 计数徽标读的是这张表的长度，加一行来源就不用再记得改那个数字。
        val sources = listOf(
            LibrarySourceRow(R.drawable.ic_library, stringResource(R.string.lib_local_name), songs.size) {
                onOpenKind(LibraryKind.Songs)
            },
            LibrarySourceRow(R.drawable.ic_favorite, stringResource(R.string.lib_favorites_name), favorites.size) {
                onOpenKind(LibraryKind.Favorites)
            },
        )
        Column(
            modifier = Modifier
                .fillMaxWidth()
                .padding(horizontal = 18.dp, vertical = 12.dp)
                .glassCard(palette = palette, corner = 26)
                .padding(horizontal = 16.dp, vertical = 14.dp),
        ) {
            Row(verticalAlignment = Alignment.CenterVertically) {
                Text(
                    text = stringResource(R.string.lib_sources_title),
                    fontSize = 16.sp,
                    fontWeight = FontWeight.Bold,
                    color = Color(palette.textPrimary),
                )
                Text(
                    text = sources.size.toString(),
                    fontSize = 10.sp,
                    color = Color(badgeTintOn(palette, palette.accent)),
                    modifier = Modifier
                        .padding(start = 8.dp)
                        .clip(RoundedCornerShape(999.dp))
                        .background(Color(palette.textPrimary).copy(alpha = 0.08f))
                        .padding(horizontal = 8.dp, vertical = 2.dp),
                )
            }
            Spacer(Modifier.height(6.dp))
            sources.forEach { SourceRow(it.iconRes, it.name, it.count, palette, it.onClick) }
        }
    }
}

@Composable
private fun RowScope.StatTile(
    value: String,
    label: String,
    palette: CatClawPalette,
    onClick: () -> Unit,
) {
    Box(
        modifier = Modifier
            .weight(1f)
            // 统计格坐在半透明 Hero 上，面板必须往墨色的反方向走：
            // 同色 α.10 时深色主题白字只剩 3.83:1，压黑 α.30（深色）/压白 α.42（浅色）后回到 6 以上
            .clip(RoundedCornerShape(16.dp))
            .background(
                if (isLightPalette(palette)) Color.White.copy(alpha = 0.42f) else Color.Black.copy(alpha = 0.30f),
            )
            .border(1.dp, Color(palette.textPrimary).copy(alpha = 0.16f), RoundedCornerShape(16.dp))
            .clickable(onClick = onClick)
            .padding(horizontal = 8.dp, vertical = 10.dp),
    ) {
        Column {
            Text(value, fontSize = 18.sp, fontWeight = FontWeight.Bold, color = Color(palette.textPrimary))
            Text(
                label,
                fontSize = 11.sp,
                // 层级交给字号而不是颜色：这层底上次要色只有 2.7:1
                color = Color(palette.textPrimary),
                modifier = Modifier.padding(top = 2.dp),
            )
        }
    }
}

/** 资料库卡的一行；徽标上那个数字就是这种行的条数 */
private data class LibrarySourceRow(
    val iconRes: Int,
    val name: String,
    val count: Int,
    val onClick: () -> Unit,
)

@Composable
private fun SourceRow(
    iconRes: Int,
    name: String,
    count: Int,
    palette: CatClawPalette,
    onClick: () -> Unit,
) {
    Row(
        modifier = Modifier
            .fillMaxWidth()
            .clickable(onClick = onClick)
            .padding(vertical = 12.dp),
        verticalAlignment = Alignment.CenterVertically,
        horizontalArrangement = Arrangement.spacedBy(13.dp),
    ) {
        Box(
            modifier = Modifier
                .size(50.dp)
                .clip(RoundedCornerShape(15.dp))
                .background(Color(palette.primary).copy(alpha = 0.30f)),
            contentAlignment = Alignment.Center,
        ) {
            Icon(
                painter = painterResource(iconRes),
                contentDescription = null,
                tint = Color(palette.textPrimary),
                modifier = Modifier.size(25.dp),
            )
        }
        Column(modifier = Modifier.weight(1f)) {
            Text(name, fontSize = 15.sp, fontWeight = FontWeight.Bold, color = Color(palette.textPrimary))
            Text(stringResource(R.string.playlist_count, count), fontSize = 12.sp, color = Color(palette.textSecondary))
        }
        Box(
            modifier = Modifier
                .size(34.dp)
                .clip(RoundedCornerShape(11.dp))
                .background(Color(palette.textPrimary).copy(alpha = 0.07f)),
            contentAlignment = Alignment.Center,
        ) {
            Icon(
                painter = painterResource(R.drawable.ic_arrow_forward),
                contentDescription = null,
                tint = Color(palette.textSecondary),
                modifier = Modifier.size(16.dp),
            )
        }
    }
}

/** 统计格点进去的独立列表页（MAUI 的 AllSongsPage / ArtistsPage / AlbumsPage） */
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
    val artists by viewModel.artists.collectAsStateWithLifecycle()
    val albums by viewModel.albums.collectAsStateWithLifecycle()
    val favorites by viewModel.favorites.collectAsStateWithLifecycle()
    val menu = rememberSongMenu(viewModel) { onOpenSong(it) }

    val title = stringResource(
        when (kind) {
            LibraryKind.Songs -> R.string.lib_stat_songs
            LibraryKind.Artists -> R.string.lib_stat_artists
            LibraryKind.Albums -> R.string.lib_stat_albums
            LibraryKind.Favorites -> R.string.lib_favorites_name
        },
    )
    val count = when (kind) {
        LibraryKind.Songs -> songs.size
        LibraryKind.Favorites -> favorites.size
        LibraryKind.Artists -> artists.size
        LibraryKind.Albums -> albums.size
    }

    Column(modifier = Modifier.fillMaxSize()) {
        Row(
            modifier = Modifier.fillMaxWidth().padding(start = 16.dp, end = 16.dp, top = 14.dp, bottom = 10.dp),
            verticalAlignment = Alignment.CenterVertically,
            horizontalArrangement = Arrangement.spacedBy(12.dp),
        ) {
            BackTile(onBack = onBack, description = stringResource(R.string.queue_back))
            Column(modifier = Modifier.weight(1f)) {
                Text(
                    text = title,
                    fontSize = 22.sp,
                    fontWeight = FontWeight.Bold,
                    color = Color(palette.textPrimary),
                    maxLines = 1,
                    overflow = TextOverflow.Ellipsis,
                )
                Text(
                    text = stringResource(R.string.playlist_count, count),
                    fontSize = 12.sp,
                    color = Color(palette.textSecondary),
                )
            }
        }

        when (kind) {
            LibraryKind.Songs -> SongList(
                songs, subtitleOf = { it.artist }, menu = menu,
                emptyHint = stringResource(R.string.library_empty, songs.size),
            ) { viewModel.playAt(it) }
            LibraryKind.Favorites -> SongList(
                favorites, subtitleOf = { it.artist }, menu = menu,
                // 这句以前是「曲库为空，点扫描曲库」—— 收藏为空和曲库为空是两件事，
                // 而且这里正是该教用户「长按」的地方（真机截图时发现的）
                emptyHint = stringResource(R.string.favorite_empty_hint),
            ) { viewModel.playFavoriteAt(it) }
            LibraryKind.Artists -> InfoList(
                rows = artists.map { it.name to stringResource(R.string.playlist_count, it.songCount) },
                onOpen = { index -> onOpen(LibraryDetail(LibraryKind.Artists, artists[index].id, artists[index].name)) },
            )
            LibraryKind.Albums -> InfoList(
                rows = albums.map { it.title to it.artist },
                onOpen = { index -> onOpen(LibraryDetail(LibraryKind.Albums, albums[index].id, albums[index].title)) },
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
@Composable
private fun LibraryDetailScreen(
    detail: LibraryDetail,
    onBack: () -> Unit,
    onOpenSong: (Int) -> Unit,
    viewModel: LibraryViewModel,
) {
    val palette = LocalCatClawPalette.current
    val byArtist = detail.kind == LibraryKind.Artists
    val rows by produceState<List<SongListItem>?>(initialValue = null, detail) {
        value = if (byArtist) viewModel.songsOfArtist(detail.id) else viewModel.songsOfAlbum(detail.id)
    }
    val loaded = rows.orEmpty()

    Column(modifier = Modifier.fillMaxSize()) {
        Row(
            modifier = Modifier.fillMaxWidth().padding(start = 16.dp, end = 16.dp, top = 14.dp, bottom = 10.dp),
            verticalAlignment = Alignment.CenterVertically,
            horizontalArrangement = Arrangement.spacedBy(12.dp),
        ) {
            BackTile(onBack = onBack, description = stringResource(R.string.queue_back))
            Column(modifier = Modifier.weight(1f)) {
                Text(
                    text = detail.name,
                    fontSize = 22.sp,
                    fontWeight = FontWeight.Bold,
                    color = Color(palette.textPrimary),
                    maxLines = 1,
                    overflow = TextOverflow.Ellipsis,
                )
                Text(
                    text = stringResource(R.string.playlist_count, loaded.size),
                    fontSize = 12.sp,
                    color = Color(palette.textSecondary),
                )
            }
        }
        SongList(
            rows = loaded,
            subtitleOf = { if (byArtist) it.album else it.artist },
            menu = rememberSongMenu(viewModel) { onOpenSong(it) },
            emptyHint = stringResource(R.string.song_list_empty_hint),
            onPlay = { viewModel.playRows(loaded, it) },
        )
    }
}

/** 艺人 / 专辑列表：每一行都能点开进二级页，所以行尾要给出可点的暗示 */
@Composable
private fun InfoList(rows: List<Pair<String, String>>, onOpen: (Int) -> Unit) {
    val palette = LocalCatClawPalette.current
    if (rows.isEmpty()) {
        EmptyHint(stringResource(R.string.library_none))
        return
    }
    LazyColumn(
        modifier = Modifier.fillMaxSize(),
        contentPadding = PaddingValues(start = 16.dp, end = 16.dp, bottom = 100.dp),
        verticalArrangement = Arrangement.spacedBy(8.dp),
    ) {
        itemsIndexed(rows) { index, (name, detail) ->
            Row(
                modifier = Modifier
                    .fillMaxWidth()
                    .glassCard(palette = palette, corner = 22)
                    .clickable { onOpen(index) }
                    .padding(horizontal = 10.dp, vertical = 10.dp),
                horizontalArrangement = Arrangement.spacedBy(12.dp),
                verticalAlignment = Alignment.CenterVertically,
            ) {
                Box(
                    modifier = Modifier
                        .size(40.dp)
                        .clip(RoundedCornerShape(13.dp))
                        .background(Color(palette.primary).copy(alpha = 0.22f)),
                    contentAlignment = Alignment.Center,
                ) {
                    Text(
                        text = name.take(1),
                        fontSize = 15.sp,
                        fontWeight = FontWeight.Bold,
                        color = Color(palette.textPrimary),
                    )
                }
                Text(
                    text = name,
                    style = MaterialTheme.typography.bodyMedium,
                    color = Color(palette.textPrimary),
                    maxLines = 1,
                    overflow = TextOverflow.Ellipsis,
                    modifier = Modifier.weight(1f),
                )
                Text(
                    text = detail,
                    style = MaterialTheme.typography.bodySmall,
                    color = Color(palette.textSecondary),
                )
                Box(
                    modifier = Modifier
                        .size(30.dp)
                        .clip(RoundedCornerShape(10.dp))
                        .background(Color(palette.textPrimary).copy(alpha = 0.07f)),
                    contentAlignment = Alignment.Center,
                ) {
                    Icon(
                        painter = painterResource(R.drawable.ic_arrow_forward),
                        contentDescription = null,
                        tint = Color(palette.textSecondary),
                        modifier = Modifier.size(15.dp),
                    )
                }
            }
        }
    }
}

@Composable
private fun EmptyHint(text: String) {
    Box(modifier = Modifier.fillMaxSize().padding(horizontal = 22.dp), contentAlignment = Alignment.Center) {
        EmptyState(hint = text, iconRes = R.drawable.ic_library)
    }
}
