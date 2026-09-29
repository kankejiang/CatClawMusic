package com.catclaw.music.kt.ui.screen

import androidx.activity.compose.BackHandler
import androidx.annotation.StringRes
import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.clickable
import androidx.compose.foundation.combinedClickable
import androidx.compose.foundation.horizontalScroll
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
import androidx.compose.material3.TextField
import androidx.compose.material3.TextFieldDefaults
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.produceState
import com.catclaw.music.kt.core.model.SongDetailFormat
import com.catclaw.music.kt.ui.stats.ListeningStatsScreen
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
import com.catclaw.music.kt.ui.song.SongBelongs
import com.catclaw.music.kt.ui.song.SongBelongsKind
import com.catclaw.music.kt.ui.song.SongDetailScreen
import com.catclaw.music.kt.ui.song.SongList
import com.catclaw.music.kt.ui.song.rememberSongMenu
import com.catclaw.music.kt.ui.library.LibraryViewModel
import com.catclaw.music.kt.ui.theme.BackTile
import com.catclaw.music.kt.ui.theme.EmptyState
import com.catclaw.music.kt.ui.theme.GlassChip
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

/** 歌曲菜单给的归属目标 → 本页的下钻类型 */
internal fun SongBelongs.toLibraryDetail(): LibraryDetail = LibraryDetail(
    kind = if (kind == SongBelongsKind.Artist) LibraryKind.Artists else LibraryKind.Albums,
    id = id,
    name = name,
)

@Composable
fun LibraryScreen(viewModel: LibraryViewModel = hiltViewModel()) {
    // 统计格 / 资料库行的下钻。MAUI 是 push 独立页（AllSongsPage 等），这里在页内切换：
    // 少一条路由与一次 VM 作用域切换，返回行为一致
    var opened by remember { mutableStateOf<LibraryKind?>(null) }
    var detail by remember { mutableStateOf<LibraryDetail?>(null) }
    // 歌曲详情是整页，托管在页面这一层而不是列表里：否则它会叠在列表页头下面，出现两个返回键
    var songDetailId by remember { mutableStateOf<Int?>(null) }
    // 听歌统计此前只藏在发现页的统计格里；音乐库这页是看曲库的地方，入口也该在这
    var statsOpen by remember { mutableStateOf(false) }
    // 三层都是页内整屏层，各自画了返回方块：系统返回键要逐层退，不能一把退出应用。
    // 组合在后的优先，所以按「下层 → 上层」排（早退块也是这个顺序，先命中的才是看得见的那层）
    BackHandler(enabled = opened != null) { opened = null }
    BackHandler(enabled = detail != null) { detail = null }
    BackHandler(enabled = songDetailId != null) { songDetailId = null }
    BackHandler(enabled = statsOpen) { statsOpen = false }
    if (statsOpen) {
        ListeningStatsScreen(onBack = { statsOpen = false })
        return
    }
    songDetailId?.let { id ->
        SongDetailScreen(songId = id, load = { viewModel.songDetail(it) }, onBack = { songDetailId = null })
        return
    }
    detail?.let {
        LibraryDetailScreen(
            detail = it,
            onBack = { detail = null },
            onOpenSong = { songDetailId = it },
            onOpenDetail = { detail = it },
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
    // 占用现读一次：曲库行数变了就该跟着变（扫完库回来要能看到新数）
    val storage by produceState(initialValue = 0L to 0L, songs.size) {
        value = runCatching { viewModel.storage() }.getOrDefault(value)
    }

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
                // MAUI 的 Hero 下面挂着「存储占用」与「数据洞察」。两个数分开报：
                // 曲库是声明式文件大小、缓存是实际落盘字节，合成一个总数就变成谁也对不上的数
                Row(
                    modifier = Modifier.fillMaxWidth().padding(top = 16.dp),
                    verticalAlignment = Alignment.CenterVertically,
                    horizontalArrangement = Arrangement.spacedBy(10.dp),
                ) {
                    Text(
                        // `bytes(0)` 回空串（那语义是"没有这个信息"），所以缓存为 0 时整段不出现，
                        // 而不是留一个后面什么都没有的「缓存」标签挂在行尾
                        text = stringResource(R.string.lib_stat_storage) + " " + SongDetailFormat.bytes(storage.first) +
                            if (storage.second > 0L) {
                                " · " + stringResource(R.string.lib_cache_label) + " " + SongDetailFormat.bytes(storage.second)
                            } else {
                                ""
                            },
                        fontSize = 12.5.sp,
                        color = Color(palette.textPrimary),
                        maxLines = 1,
                        overflow = TextOverflow.Ellipsis,
                        modifier = Modifier.weight(1f),
                    )
                    PillLabel(
                        text = stringResource(R.string.lib_open_stats),
                        onClick = { statsOpen = true },
                        filled = false,
                    )
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
    val displaySongs by viewModel.displaySongs.collectAsStateWithLifecycle()
    val sort by viewModel.songSort.collectAsStateWithLifecycle()
    val query by viewModel.songQuery.collectAsStateWithLifecycle()
    val menu = rememberSongMenu(viewModel, onOpenBelongs = { onOpen(it.toLibraryDetail()) }) { onOpenSong(it) }
    // MAUI 的 AllSongsPage 顶部就是「播放全部 / 随机播放」两个按钮，本端此前只能一行一行点着播。
    // 艺人/专辑页没有"整张表"这张列表，所以那两个按钮只在歌曲类页面上画。
    // 这里绑的是**屏幕上那份列表**：搜索框里有字、或排的是时长时，「播放全部」要播的是看到的那些，
    // 而不是数据库里的全部——否则按钮和列表就成了两件事
    val songRows: List<SongListItem>? = when (kind) {
        LibraryKind.Songs -> displaySongs
        LibraryKind.Favorites -> favorites
        else -> null
    }

    val title = stringResource(
        when (kind) {
            LibraryKind.Songs -> R.string.lib_stat_songs
            LibraryKind.Artists -> R.string.lib_stat_artists
            LibraryKind.Albums -> R.string.lib_stat_albums
            LibraryKind.Favorites -> R.string.lib_favorites_name
        },
    )
    val count = when (kind) {
        // 头部那个数字跟着列表走：搜出 3 首却还写着「128 首」，读起来像是搜索没生效
        LibraryKind.Songs -> displaySongs.size
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
            songRows?.let { rows ->
                PillLabel(
                    text = stringResource(R.string.playlist_play_all),
                    onClick = {
                        // 「播放全部」就是"从这份可见列表的第一首开始顺序播"，与点第一行同一条路
                        when (kind) {
                            LibraryKind.Favorites -> viewModel.playFavoriteAt(0)
                            else -> viewModel.playRows(rows, 0)
                        }
                    },
                    filled = false,
                )
                PillLabel(
                    text = stringResource(R.string.shuffle_play),
                    onClick = { viewModel.playShuffled(rows) },
                    filled = false,
                )
            }
        }

        // 全部歌曲页的搜索与排序（MAUI AllSongsPage 顶部那两条）。
        // 只对歌曲这一类开：艺人/专辑页的行是名字列表，按"标题/时长"排没有意义
        if (kind == LibraryKind.Songs) {
            Row(
                modifier = Modifier
                    .fillMaxWidth()
                    // 内边距在滚动**外面**：英文 locale 下四颗 chip 会比屏幕宽，横滑时首尾那颗
                    // 不该贴到屏幕边缘（先 padding 再 horizontalScroll 才是"带边距的窗口"）
                    .padding(horizontal = 16.dp, vertical = 4.dp)
                    .horizontalScroll(rememberScrollState()),
                horizontalArrangement = Arrangement.spacedBy(8.dp),
            ) {
                LibraryViewModel.SongSort.entries.forEach { mode ->
                    GlassChip(
                        label = stringResource(sortLabelRes(mode)),
                        selected = sort == mode,
                        onClick = { viewModel.setSongSort(mode) },
                    )
                }
            }
            SearchField(
                query = query,
                onChange = viewModel::setSongQuery,
                placeholder = stringResource(R.string.library_search_hint),
                modifier = Modifier.padding(horizontal = 16.dp, vertical = 6.dp),
            )
        }

        when (kind) {
            LibraryKind.Songs -> SongList(
                displaySongs, subtitleOf = { it.artist }, menu = menu,
                emptyHint = if (query.isBlank()) {
                    stringResource(R.string.library_empty, songs.size)
                } else {
                    // 搜出来是空的时候不能说"曲库为空"——那是两件事，用户会以为歌掉了
                    stringResource(R.string.search_none, query.trim())
                },
            ) { viewModel.playRows(displaySongs, it) }
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
            // 艺人/专辑页此前只能一行一行点着播：整张列表摆在这儿却没有"整张播"的入口。
            // 两颗粒子与全部歌曲页同一套文案、同一条路径（播的就是屏幕上这份 loaded）
            if (loaded.isNotEmpty()) {
                PillLabel(
                    text = stringResource(R.string.playlist_play_all),
                    onClick = { viewModel.playRows(loaded, 0) },
                    filled = false,
                )
                PillLabel(
                    text = stringResource(R.string.shuffle_play),
                    onClick = { viewModel.playShuffled(loaded) },
                    filled = false,
                )
            }
        }
        SongList(
            rows = loaded,
            subtitleOf = { if (byArtist) it.album else it.artist },
            menu = rememberSongMenu(viewModel, onOpenBelongs = { onOpenDetail(it.toLibraryDetail()) }) { onOpenSong(it) },
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

/** 排序 chip 的文案。四档都有，所以这个 `when` 不带 else —— 加一档编译就会停在这 */
@StringRes
internal fun sortLabelRes(sort: LibraryViewModel.SongSort): Int = when (sort) {
    LibraryViewModel.SongSort.Title -> R.string.sort_title
    LibraryViewModel.SongSort.Artist -> R.string.sort_artist
    LibraryViewModel.SongSort.Album -> R.string.sort_album
    LibraryViewModel.SongSort.Duration -> R.string.sort_duration
}

/**
 * 曲库列表页的搜索框，配方与发现页那条一致（同一支墨色底 + 圆角 22 + 无下划线）——
 * 输入框是全 app 唯一还留着 M3 默认样式的地方，两处就该长得一样。
 */
@Composable
private fun SearchField(
    query: String,
    onChange: (String) -> Unit,
    placeholder: String,
    modifier: Modifier = Modifier,
) {
    val palette = LocalCatClawPalette.current
    val ink = Color(palette.textPrimary)
    TextField(
        value = query,
        onValueChange = onChange,
        placeholder = { Text(placeholder, color = Color(palette.textSecondary), fontSize = 14.sp) },
        singleLine = true,
        colors = TextFieldDefaults.colors(
            focusedContainerColor = ink.copy(alpha = 0.10f),
            unfocusedContainerColor = ink.copy(alpha = 0.08f),
            focusedIndicatorColor = Color.Transparent,
            unfocusedIndicatorColor = Color.Transparent,
            focusedTextColor = ink,
            unfocusedTextColor = ink,
            cursorColor = Color(palette.primary),
        ),
        shape = RoundedCornerShape(22.dp),
        modifier = modifier.fillMaxWidth(),
    )
}
