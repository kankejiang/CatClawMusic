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
// 宫格版的 itemsIndexed 与列表版同名不同包：起别名，避免歧义导入
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
import androidx.compose.runtime.DisposableEffect
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
import com.catclaw.music.kt.core.library.NetworkProtocolFilter

/**
 * 音乐库 —— 照 MAUI `LibraryPage.xaml` 做成**枢纽页**：Hero 卡 + 4 个统计格 + 资料库列表。
 * 之前这里的 歌曲/艺人/专辑/收藏 四个 tab 是我自己加的，MAUI 没有：统计格才是入口，
 * 点进去是独立列表页（对应 `AllSongsPage` / `ArtistsPage` / `AlbumsPage`）。
 *
 * 只放**取得到**的数据：小时数由本地曲库时长求和；MAUI 的「存储占用」「数据洞察环形图」
 * 需要文件大小与格式分布，本端没有这两个聚合查询，就不摆空壳（宁缺毋假）。
 */
enum class LibraryKind { Songs, Artists, Albums, Favorites, Recent }

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
fun LibraryScreen(
    // 资料库卡里「网络音乐库」「下载管理」两行的去处：它们不是本页的列表，是别处的整页（MAUI 那五张卡也是各自跳页面）
    onOpenRemote: () -> Unit = {},
    onOpenDownloads: () -> Unit = {},
    /** 资料库卡里的「网易云音乐」：整页在线音乐（搜索 / 推荐 / 榜单 / 歌单） */
    onOpenNeteaseMusic: () -> Unit = {},
    /** 发现页货架的「查看全部」：进来直接打开这个列表，消费一次即清空 */
    initialKind: LibraryKind? = null,
    onInitialKindConsumed: () -> Unit = {},
    /** 页内整屏层开合时回调，交外壳决定是否锁住 Pager 横滑 */
    onSubPageOpenChange: (Boolean) -> Unit = {},
    /** 批量处理是整页二级界面：通知外壳隐藏底栏与迷你播放条 */
    onImmersiveChange: (Boolean) -> Unit = {},
    viewModel: LibraryViewModel = hiltViewModel(),
) {
    // 统计格 / 资料库行的下钻。MAUI 是 push 独立页（AllSongsPage 等），这里在页内切换：
    // 少一条路由与一次 VM 作用域切换，返回行为一致
    var opened by remember { mutableStateOf<LibraryKind?>(null) }
    /**
     * 从哪个入口进的「歌曲」页。
     *
     * `All` = 顶部「全部歌曲」入口 → 显示「全部 / 本地 / 网络」来源筛选；
     * `Local` = 资料库「本地曲库」→ **不显示来源筛选**，因为这页就是本地的。
     *
     * 对齐 MAUI `AllSongsViewModel.IsSourceFilterVisible = source == "all"`：
     * 那边进入时按 `source` 决定筛选器是否出现，而不是让用户在一个已经限定来源的页面里
     * 再看到"全部/本地/网络"三个选项。
     */
    var entrySource by remember { mutableStateOf(SongSourceFilter.All) }
    // 从发现页货架跳进来：先把它要的列表打开，再把这个一次性指令清掉
    LaunchedEffect(initialKind) {
        if (initialKind != null) {
            opened = initialKind
            onInitialKindConsumed()
        }
    }
    var detail by remember { mutableStateOf<LibraryDetail?>(null) }
    // 歌曲详情是整页，托管在页面这一层而不是列表里：否则它会叠在列表页头下面，出现两个返回键
    var songDetailId by remember { mutableStateOf<Int?>(null) }
    // 听歌统计此前只藏在发现页的统计格里；音乐库这页是看曲库的地方，入口也该在这
    var statsOpen by remember { mutableStateOf(false) }
    var batchOpen by remember { mutableStateOf(false) }
    // 页内整屏层（统计 / 批量处理 / 列表 / 详情）打开时通知外壳锁住 Pager：
    // 否则左右一滑就切走页签，而这一层还留在库里，切回来还是它，很突兀
    val subPageOpen = statsOpen || batchOpen || opened != null || detail != null || songDetailId != null
    LaunchedEffect(subPageOpen) { onSubPageOpenChange(subPageOpen) }
    LaunchedEffect(batchOpen) { onImmersiveChange(batchOpen) }
    DisposableEffect(Unit) {
        onDispose {
            onSubPageOpenChange(false)
            onImmersiveChange(false)
        }
    }
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
    BackHandler(enabled = batchOpen) { batchOpen = false }
    if (batchOpen) {
        com.catclaw.music.kt.ui.lyrics.BatchScreen(onBack = { batchOpen = false })
        return
    }
    songDetailId?.let { id ->
        SongDetailScreen(
            songId = id,
            load = { viewModel.songDetail(it) },
            loadLyrics = { viewModel.lyricsFor(it) },
            onPlay = { viewModel.playSongById(it) },
            onBack = { songDetailId = null },
        )
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
            entrySource = entrySource,
            viewModel = viewModel,
        )
        return
    }
    /**
     * 打开二级列表。
     *
     * [source] 只在 [LibraryKind.Songs] 有意义：这个页面是「全部歌曲」，本地与网络合在一份列表里，
     * 靠来源筛选区分。**入口的标题必须与进来后的来源一致** —— MAUI 就是这么做的：
     *   "local"   → AllSongsPage?source=local
     *   "network" → AllSongsPage?source=network
     *   "all"     → AllSongsPage?source=all
     * 之前 Android 的「本地曲库」只说打开 Songs 页、不带来源，于是副标题写着「10 首」，
     * 点进去却是 623 首（本地+网络）—— 用户看到的就是「本地曲库里有网络音乐」。
     */
    val onOpenKind: (LibraryKind, SongSourceFilter) -> Unit = { kind, source ->
        if (kind == LibraryKind.Songs) {
            viewModel.setSongSource(source)
            // 记下从哪个入口进来的：「全部歌曲」要显示来源筛选，「本地曲库」不显示
            // （对齐 MAUI `AllSongsViewModel.IsSourceFilterVisible = source == "all"`）
            entrySource = source
        }
        opened = kind
    }
    val palette = LocalCatClawPalette.current
    val songs by viewModel.songs.collectAsStateWithLifecycle()
    // 网络曲库那行的计数（受「停用连接」影响，和点进去看到的列表一致）
    val networkSongCount by viewModel.networkSongCount.collectAsStateWithLifecycle()
    val artists by viewModel.artists.collectAsStateWithLifecycle()
    val albums by viewModel.albums.collectAsStateWithLifecycle()
    val recent by viewModel.recent.collectAsStateWithLifecycle()
    val scanning by viewModel.scanning.collectAsStateWithLifecycle()
    val hours = songs.sumOf { it.durationSeconds.toLong() } / 3600.0

    val landscape = LocalConfiguration.current.orientation ==
        Configuration.ORIENTATION_LANDSCAPE

    /**
     * 「资料库」分组的行。顺序对齐 HTML 原型：本地 → 网络 → 网易云 → 最近播放。
     * 「我喜欢的」与顶部「收藏」入口重复，已去掉；「远程音乐服务」是配置项，移到了「工具」分组。
     */
    val sources = listOf(
        LibrarySourceRow(
            iconRes = R.drawable.ic_library,
            name = stringResource(R.string.lib_local_name),
            subtitle = stringResource(R.string.playlist_count, songs.size),
            onClick = { onOpenKind(LibraryKind.Songs, SongSourceFilter.Local) },
        ),
        // 网络曲库：本地曲库的镜像入口，直接看**网络来的歌**（MAUI 资料库两张并列卡同义）
        LibrarySourceRow(
            iconRes = R.drawable.ic_network_library,
            name = stringResource(R.string.lib_network_name),
            subtitle = stringResource(R.string.playlist_count, networkSongCount),
            onClick = { onOpenKind(LibraryKind.Songs, SongSourceFilter.Network) },
        ),
        // 网易云：品牌红圆标，tinted=false 保留原色（MAUI ic_netease.svg）
        LibrarySourceRow(
            iconRes = R.drawable.ic_netease_app,
            name = stringResource(R.string.netease_music_entry),
            subtitle = stringResource(R.string.netease_music_entry_sub),
            tinted = false,
            onClick = onOpenNeteaseMusic,
        ),
        LibrarySourceRow(
            iconRes = R.drawable.ic_history,
            name = stringResource(R.string.playlist_entry_recent),
            subtitle = stringResource(R.string.playlist_count, recent.size),
            onClick = { onOpenKind(LibraryKind.Recent, SongSourceFilter.All) },
        ),
    )

    // 横屏左右两栏共用同一份内容：左栏回答「有多少 / 去哪」，右栏放资料库与管理卡。
    // 竖屏仍是一列纵向滚动，两栏只在 landscape 分支里成立。
    val headContent: @Composable () -> Unit = {
        Column(modifier = Modifier.fillMaxWidth()) {
        // 顶栏：标题 + 副标题。动作入口全部下移到「工具」分组，标题不再和按钮抢一行
        Column(
            modifier = Modifier.fillMaxWidth().padding(start = 20.dp, end = 20.dp, top = 12.dp, bottom = 4.dp),
        ) {
            Text(
                text = stringResource(R.string.TabLibrary),
                fontSize = 30.sp,
                fontWeight = FontWeight.Bold,
                color = Color(palette.textPrimary),
            )
            Text(
                text = stringResource(R.string.lib_sources_count, sources.size),
                fontSize = 12.sp,
                color = Color(palette.textSecondary).copy(alpha = 0.75f),
                modifier = Modifier.padding(top = 4.dp),
            )
        }

        // 四个数字：四张独立玻璃格（原来这四格坐在渐变 Hero 上，靠压黑/压白维持对比度）
        // 横屏左栏 360dp 也排得下四格：75dp/格，20sp 的数字不顶，入口同理
        Row(
            modifier = Modifier.fillMaxWidth().padding(horizontal = 18.dp, vertical = 10.dp),
            horizontalArrangement = Arrangement.spacedBy(8.dp),
        ) {
            StatTile(String.format("%,d", songs.size), stringResource(R.string.lib_stat_songs), palette) {
                onOpenKind(LibraryKind.Songs, SongSourceFilter.Local)
            }
            StatTile(String.format("%,d", artists.size), stringResource(R.string.lib_stat_artists), palette) {
                onOpenKind(LibraryKind.Artists, SongSourceFilter.All)
            }
            StatTile(String.format("%,d", albums.size), stringResource(R.string.lib_stat_albums), palette) {
                onOpenKind(LibraryKind.Albums, SongSourceFilter.All)
            }
            StatTile(String.format("%.1f", hours), stringResource(R.string.lib_stat_hours), palette) {
                onOpenKind(LibraryKind.Songs, SongSourceFilter.Local)
            }
        }

        // 四个入口：统计格回答「有多少」，这里回答「去哪」
        Row(
            modifier = Modifier.fillMaxWidth().padding(horizontal = 18.dp).padding(bottom = 6.dp),
            horizontalArrangement = Arrangement.spacedBy(8.dp),
        ) {
            EntryTile(R.drawable.ic_nav_lyrics, stringResource(R.string.lib_all_songs), palette) {
                onOpenKind(LibraryKind.Songs, SongSourceFilter.All)
            }
            EntryTile(R.drawable.ic_nav_appearance, stringResource(R.string.lib_stat_albums), palette) {
                onOpenKind(LibraryKind.Albums, SongSourceFilter.All)
            }
            EntryTile(R.drawable.ic_nav_permission, stringResource(R.string.lib_stat_artists), palette) {
                onOpenKind(LibraryKind.Artists, SongSourceFilter.All)
            }
            EntryTile(R.drawable.ic_favorite, stringResource(R.string.playlist_entry_favorites), palette) {
                onOpenKind(LibraryKind.Favorites, SongSourceFilter.All)
            }
        }
    }
}

    val sectionContent: @Composable () -> Unit = {
        Column(modifier = Modifier.fillMaxWidth()) {
        // 资料库 —— 扁平列表（取消外层玻璃卡）：分组标题 + 细分隔线。
        // 行本身就是信息单元，再套一层卡片就是「卡片里再卡片」，这是原来页面显乱的主因。
        Column(modifier = Modifier.fillMaxWidth().padding(horizontal = 20.dp, vertical = 14.dp)) {
            GroupHead(stringResource(R.string.lib_sources_title), sources.size, palette)
            sources.forEach { row -> SourceRow(row.iconRes, row.name, row.subtitle, palette, row.tinted, row.onClick) }
        }

        // 工具：听歌统计与扫描从顶栏下移；远程音乐服务与批量处理从别处收进来 —— 都是低频维护动作
        Column(modifier = Modifier.fillMaxWidth().padding(horizontal = 20.dp, vertical = 6.dp)) {
            GroupHead(stringResource(R.string.lib_tools_title), 5, palette)
            SourceRow(
                R.drawable.ic_stats,
                stringResource(R.string.lib_open_stats),
                stringResource(R.string.lib_tools_stats_hint),
                palette,
            ) { statsOpen = true }
            SourceRow(
                R.drawable.ic_cloud,
                stringResource(R.string.RemoteMusic),
                stringResource(R.string.RemoteMusicSubtitle),
                palette,
                // 直接用 MAUI 导出的 PNG，保留原色（云朵灰），不跟着主题 tint
                tinted = false,
                onClick = onOpenRemote,
            )
            SourceRow(R.drawable.ic_download, stringResource(R.string.downloads_title), stringResource(R.string.downloads_subtitle), palette, onClick = onOpenDownloads)
            SourceRow(
                R.drawable.ic_search,
                stringResource(if (scanning) R.string.library_scanning else R.string.ScanMusic),
                stringResource(R.string.lib_tools_scan_hint),
                palette,
            ) { viewModel.scan() }
            // 批量处理：原来在顶栏右侧菜单里，独占一个按钮只为一项功能；挪到工具组后顶栏只剩标题
            SourceRow(
                R.drawable.ic_edit,
                stringResource(R.string.batch_entry),
                stringResource(R.string.lib_tools_batch_hint),
                palette,
            ) { batchOpen = true }
        }

        // 最近添加 —— MAUI `LibraryPage.xaml:197-216` 那张横滑卡（标题 + 「本周」徽标 + 横滑卡片）。
        // C# 那些卡片是纯展示的（`CreateRecentCard` 上没有挂任何手势），这里补上：
        // 点一张就从这张卡开始播 —— 摆着一排点不动的封面，用户只会以为界面卡了
        val fresh by viewModel.newArrivals.collectAsStateWithLifecycle()
        if (fresh.isNotEmpty()) {
            Column(
                modifier = Modifier
                    .fillMaxWidth()
                    .padding(horizontal = 18.dp)
                    .padding(top = 12.dp)
                    .glassCard(palette = palette, corner = 26)
                    .padding(vertical = 14.dp),
            ) {
                Row(
                    verticalAlignment = Alignment.CenterVertically,
                    modifier = Modifier.padding(horizontal = 16.dp),
                ) {
                    Text(
                        text = stringResource(R.string.lib_recent_added),
                        fontSize = 16.sp,
                        fontWeight = FontWeight.Bold,
                        color = Color(palette.textPrimary),
                    )
                    Text(
                        text = stringResource(R.string.lib_recent_added_week),
                        fontSize = 10.sp,
                        color = Color(badgeTintOn(palette, palette.accent)),
                        modifier = Modifier
                            .padding(start = 8.dp)
                            .clip(RoundedCornerShape(999.dp))
                            .background(Color(palette.textPrimary).copy(alpha = 0.08f))
                            .padding(horizontal = 8.dp, vertical = 2.dp),
                    )
                }
                Spacer(Modifier.height(12.dp))
                LazyRow(
                    contentPadding = PaddingValues(horizontal = 16.dp),
                    horizontalArrangement = Arrangement.spacedBy(12.dp),
                ) {
                    itemsIndexed(fresh, key = { index, song -> "fresh-$index-${song.id}" }) { index, song ->
                        RecentAddCard(song) { viewModel.playRows(fresh, index) }
                    }
                }
            }
        }
        }
    }

    if (landscape) {
        Row(modifier = Modifier.fillMaxSize()) {
            Column(
                modifier = Modifier
                    .width(360.dp)
                    .fillMaxHeight()
                    .verticalScroll(rememberScrollState())
                    .padding(bottom = 92.dp),
            ) { headContent() }
            Column(
                modifier = Modifier
                    .weight(1f)
                    .fillMaxHeight()
                    .verticalScroll(rememberScrollState())
                    .padding(bottom = 92.dp),
            ) { sectionContent() }
        }
    } else {
        Column(
            modifier = Modifier
                .fillMaxSize()
                .verticalScroll(rememberScrollState())
                .padding(bottom = 132.dp),
        ) {
            headContent()
            sectionContent()
        }
    }
}

/**
 * 最近添加横滑卡里的一张：132dp 封面 + 曲名 + 歌手（尺寸与字号照 MAUI `CreateRecentCard`）。
 *
 * 出图仍走 `CoverTile`（读内嵌封面）—— C# 那张卡画的是一块按标题哈希出来的渐变色 + 音符图标，
 * 本端从 ⑬ 起所有卡片都能拿到真封面，没有理由退回占位色块。取不到图才落回音符。
 */
@Composable
internal fun RecentAddCard(song: SongListItem, onClick: () -> Unit) {
    val palette = LocalCatClawPalette.current
    Column(
        modifier = Modifier
            .width(132.dp)
            .clip(RoundedCornerShape(18.dp))
            .clickable(onClick = onClick),
    ) {
        CoverTile(
            coverArtPath = song.coverArtPath,
            audioPath = song.filePath,
            songId = song.id,
            maxPx = 256,
            modifier = Modifier
                .size(132.dp)
                .clip(RoundedCornerShape(16.dp))
                .background(Color(palette.primary).copy(alpha = 0.22f)),
        ) {
            Text(
                text = "♪",
                fontSize = 34.sp,
                color = Color(palette.textPrimary),
            )
        }
        Text(
            text = song.title,
            fontSize = 13.sp,
            fontWeight = FontWeight.Bold,
            color = Color(palette.textPrimary),
            maxLines = 1,
            overflow = TextOverflow.Ellipsis,
            modifier = Modifier.padding(top = 8.dp),
        )
        Text(
            text = song.artist,
            fontSize = 11.5.sp,
            color = Color(palette.textSecondary),
            maxLines = 1,
            overflow = TextOverflow.Ellipsis,
            modifier = Modifier.padding(top = 2.dp),
        )
    }
}

/**
 * 统计格（2026-11-08 重设计）：从「压在渐变 Hero 上的深色块」改成**独立玻璃格**。
 *
 * 以前它必须往墨色反方向压（深色主题压黑 α.30）才能在半透明 Hero 上保住对比度；
 * 现在 Hero 没了，格子自己就是一块玻璃 —— 沿用 glassCard 那套面 + 描边，
 * 文字层级交给字号（数字 20sp / 标签 11sp），不再把次要色压到 2.7:1。
 */
@Composable
internal fun RowScope.StatTile(
    value: String,
    label: String,
    palette: CatClawPalette,
    onClick: () -> Unit,
) {
    Column(
        modifier = Modifier
            .weight(1f)
            .clip(RoundedCornerShape(16.dp))
            .background(Color(palette.textPrimary).copy(alpha = 0.045f))
            .border(1.dp, Color(palette.textPrimary).copy(alpha = 0.085f), RoundedCornerShape(16.dp))
            .clickable(onClick = onClick)
            .padding(horizontal = 11.dp, vertical = 13.dp),
    ) {
        Text(value, fontSize = 20.sp, fontWeight = FontWeight.Bold, color = Color(palette.textPrimary))
        Text(
            text = label,
            fontSize = 11.sp,
            color = Color(palette.textSecondary).copy(alpha = 0.85f),
            modifier = Modifier.padding(top = 4.dp),
        )
    }
}

/** 入口格：图标 + 文字，四格一行（统计格回答「有多少」，这里回答「去哪」） */
@Composable
internal fun RowScope.EntryTile(
    iconRes: Int,
    label: String,
    palette: CatClawPalette,
    onClick: () -> Unit,
) {
    Column(
        modifier = Modifier
            .weight(1f)
            .clip(RoundedCornerShape(16.dp))
            .background(Color(palette.textPrimary).copy(alpha = 0.045f))
            .border(1.dp, Color(palette.textPrimary).copy(alpha = 0.085f), RoundedCornerShape(16.dp))
            .clickable(onClick = onClick)
            .padding(vertical = 15.dp),
        horizontalAlignment = Alignment.CenterHorizontally,
    ) {
        Icon(
            painter = painterResource(iconRes),
            contentDescription = null,
            tint = Color(palette.textPrimary),
            modifier = Modifier.size(20.dp),
        )
        Text(
            text = label,
            fontSize = 11.5.sp,
            color = Color(palette.textSecondary),
            modifier = Modifier.padding(top = 8.dp),
        )
    }
}

/** 资料库卡的一行。副标题由调用处给全：计数行是「N 首」，导航行是功能说明 */
internal data class LibrarySourceRow(
    val iconRes: Int,
    val name: String,
    val subtitle: String,
    /** 品牌图标（如网易云红圆标）不能 tint，否则会被染成主题单色 */
    val tinted: Boolean = true,
    val onClick: () -> Unit,
)

/**
 * 扁平列表的一行（2026-11 重设计）：36dp 图标 + 名称/副标题 + 细箭头。
 * 原来行里还套着 34dp 的方块箭头按钮，和外层玻璃卡叠成「卡片套卡片、方块套方块」，是页面显乱的主因。
 * 现在行直接铺在背景上，只用一条 5% 的细线分隔。
 */
@Composable
internal fun SourceRow(
    iconRes: Int,
    name: String,
    subtitle: String,
    palette: CatClawPalette,
    tinted: Boolean = true,
    onClick: () -> Unit,
) {
    Column(modifier = Modifier.fillMaxWidth()) {
        Row(
            modifier = Modifier
                .fillMaxWidth()
                .clickable(onClick = onClick)
                .padding(horizontal = 2.dp, vertical = 11.dp),
            verticalAlignment = Alignment.CenterVertically,
            horizontalArrangement = Arrangement.spacedBy(12.dp),
        ) {
            Box(
                modifier = Modifier
                    .size(36.dp)
                    .clip(RoundedCornerShape(11.dp))
                    .background(Color(palette.textPrimary).copy(alpha = 0.06f))
                    .border(1.dp, Color(palette.textPrimary).copy(alpha = 0.07f), RoundedCornerShape(11.dp)),
                contentAlignment = Alignment.Center,
            ) {
                Icon(
                    painter = painterResource(iconRes),
                    contentDescription = null,
                    // 品牌图标保留原色（Color.Unspecified 不做 tint 混合）
                    tint = if (tinted) Color(palette.textPrimary) else Color.Unspecified,
                    modifier = Modifier.size(18.dp),
                )
            }
            Column(modifier = Modifier.weight(1f)) {
                Text(name, fontSize = 14.5.sp, fontWeight = FontWeight.SemiBold, color = Color(palette.textPrimary))
                Text(
                    text = subtitle,
                    fontSize = 11.sp,
                    color = Color(palette.textSecondary).copy(alpha = 0.8f),
                    maxLines = 1,
                    overflow = TextOverflow.Ellipsis,
                    modifier = Modifier.padding(top = 2.dp),
                )
            }
            Icon(
                painter = painterResource(R.drawable.ic_arrow_forward),
                contentDescription = null,
                tint = Color(palette.textSecondary).copy(alpha = 0.55f),
                modifier = Modifier.size(14.dp),
            )
        }
        Box(
            modifier = Modifier
                .fillMaxWidth()
                .padding(start = 48.dp)
                .height(1.dp)
                .background(Color(palette.textPrimary).copy(alpha = 0.05f)),
        )
    }
}

/** 分组标题 + 计数：扁平列表靠它建立层级（原来的层级由外层玻璃卡提供） */
@Composable
internal fun GroupHead(title: String, count: Int, palette: CatClawPalette) {
    Row(
        modifier = Modifier.fillMaxWidth().padding(start = 2.dp, bottom = 2.dp),
        verticalAlignment = Alignment.CenterVertically,
    ) {
        Text(title, fontSize = 14.sp, fontWeight = FontWeight.Bold, color = Color(palette.textPrimary))
        Text(
            text = count.toString(),
            fontSize = 10.sp,
            fontWeight = FontWeight.Bold,
            color = Color(palette.textSecondary).copy(alpha = 0.7f),
            modifier = Modifier.padding(start = 8.dp),
        )
    }
}

/** 统计格点进去的独立列表页（MAUI 的 AllSongsPage / ArtistsPage / AlbumsPage） */
@Composable
internal fun ViewModeToggle(grid: Boolean, onChange: (Boolean) -> Unit) {
    val palette = LocalCatClawPalette.current
    Row(
        modifier = Modifier
            .clip(RoundedCornerShape(999.dp))
            .background(Color(palette.textPrimary).copy(alpha = 0.08f))
            .padding(3.dp),
        horizontalArrangement = Arrangement.spacedBy(2.dp),
    ) {
        listOf(true to R.drawable.ic_grid, false to R.drawable.ic_list).forEach { (isGrid, icon) ->
            val selected = grid == isGrid
            Box(
                modifier = Modifier
                    .size(34.dp)
                    .clip(RoundedCornerShape(999.dp))
                    .background(if (selected) Color(palette.primary) else Color.Transparent)
                    .clickable { onChange(isGrid) },
                contentAlignment = Alignment.Center,
            ) {
                Icon(
                    painter = painterResource(icon),
                    contentDescription = stringResource(if (isGrid) R.string.view_grid else R.string.view_list),
                    // 选中项坐在主色面上，前景必须走 inkOn（与 PillLabel(filled = true) 同一条规则）
                    tint = if (selected) Color(inkOn(palette.primary)) else Color(palette.textSecondary),
                    modifier = Modifier.size(18.dp),
                )
            }
        }
    }
}

/**
 * 艺人 / 专辑列表。列表模式每一行都能点开进二级页，所以行尾给出可点的暗示；
 * 宫格模式一屏能扫过三倍的内容，封面也是真的（[CoverTile]）。
 */
@Composable
internal fun InfoList(rows: List<InfoRow>, emptyHint: String, onOpen: (Int) -> Unit, grid: Boolean = false) {
    val palette = LocalCatClawPalette.current
    if (rows.isEmpty()) {
        EmptyHint(emptyHint)
        return
    }
    if (grid) {
        LazyVerticalGrid(
            columns = GridCells.Fixed(3),
            modifier = Modifier.fillMaxSize(),
            contentPadding = PaddingValues(start = 16.dp, end = 16.dp, bottom = 100.dp),
            horizontalArrangement = Arrangement.spacedBy(10.dp),
            verticalArrangement = Arrangement.spacedBy(14.dp),
        ) {
            gridItemsIndexed(rows) { index, row ->
                Column(modifier = Modifier.clickable { onOpen(index) }) {
                    CoverTile(
                        coverArtPath = row.coverArtPath,
                        audioPath = row.sampleFilePath,
                        maxPx = 256,
                        modifier = Modifier
                            .fillMaxWidth()
                            .aspectRatio(1f)
                            .clip(RoundedCornerShape(18.dp))
                            .background(Color(palette.primary).copy(alpha = 0.22f)),
                    ) {
                        Text(
                            text = row.title.take(1),
                            fontSize = 26.sp,
                            fontWeight = FontWeight.Bold,
                            color = Color(palette.textPrimary),
                        )
                    }
                    Text(
                        text = row.title,
                        fontSize = 12.5.sp,
                        fontWeight = FontWeight.SemiBold,
                        color = Color(palette.textPrimary),
                        maxLines = 1,
                        overflow = TextOverflow.Ellipsis,
                        modifier = Modifier.padding(top = 6.dp),
                    )
                    Text(
                        text = row.subtitle,
                        fontSize = 10.5.sp,
                        color = Color(palette.textSecondary),
                        maxLines = 1,
                        overflow = TextOverflow.Ellipsis,
                    )
                }
            }
        }
        return
    }
    LazyColumn(
        modifier = Modifier.fillMaxSize(),
        contentPadding = PaddingValues(start = 16.dp, end = 16.dp, bottom = 100.dp),
        verticalArrangement = Arrangement.spacedBy(8.dp),
    ) {
        itemsIndexed(rows) { index, row ->
            Row(
                modifier = Modifier
                    .fillMaxWidth()
                    .glassCard(palette = palette, corner = 22)
                    .clickable { onOpen(index) }
                    .padding(horizontal = 10.dp, vertical = 10.dp),
                horizontalArrangement = Arrangement.spacedBy(12.dp),
                verticalAlignment = Alignment.CenterVertically,
            ) {
                // 取不到图就退回原来的首字母方块（CoverTile 的 fallback）
                CoverTile(
                    coverArtPath = row.coverArtPath,
                    audioPath = row.sampleFilePath,
                    maxPx = 128,
                    modifier = Modifier
                        .size(44.dp)
                        .clip(RoundedCornerShape(13.dp))
                        .background(Color(palette.primary).copy(alpha = 0.22f)),
                ) {
                    Text(
                        text = row.title.take(1),
                        fontSize = 15.sp,
                        fontWeight = FontWeight.Bold,
                        color = Color(palette.textPrimary),
                    )
                }
                Text(
                    text = row.title,
                    style = MaterialTheme.typography.bodyMedium,
                    color = Color(palette.textPrimary),
                    maxLines = 1,
                    overflow = TextOverflow.Ellipsis,
                    modifier = Modifier.weight(1f),
                )
                Text(
                    text = row.subtitle,
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
internal fun EmptyHint(text: String) {
    Box(modifier = Modifier.fillMaxSize().padding(horizontal = 22.dp), contentAlignment = Alignment.Center) {
        EmptyState(hint = text, iconRes = R.drawable.ic_library)
    }
}

/** 来源筛选 chip 的文案。三档都有，所以这个 `when` 不带 else —— 加一档编译就会停在这 */
@StringRes
internal fun sourceFilterLabelRes(filter: SongSourceFilter): Int = when (filter) {
    SongSourceFilter.All -> R.string.song_source_all
    SongSourceFilter.Local -> R.string.song_source_local
    SongSourceFilter.Network -> R.string.song_source_network
}

/**
 * 「网络服务来源」chip 的文案。四档都有，所以 `when` 不带 else —— 加一档编译就会停在这。
 *
 * 用协议**全名**而不是徽章上那种缩写（DAV/NDR）：菜单里一行一个、空间够，
 * 而缩写只有配着彩色徽章才认得出。
 */
@StringRes
internal fun networkProtocolLabelRes(filter: NetworkProtocolFilter): Int = when (filter) {
    NetworkProtocolFilter.WebDAV -> R.string.net_proto_webdav
    NetworkProtocolFilter.Navidrome -> R.string.net_proto_navidrome
    NetworkProtocolFilter.SMB -> R.string.net_proto_smb
}

/** 排序 chip 的文案。五档都有，所以这个 `when` 不带 else —— 加一档编译就会停在这 */
@StringRes
internal fun sortLabelRes(sort: SongSortKey): Int = when (sort) {
    SongSortKey.Title -> R.string.sort_title
    SongSortKey.Artist -> R.string.sort_artist
    SongSortKey.Album -> R.string.sort_album
    SongSortKey.Duration -> R.string.sort_duration
    SongSortKey.DateAdded -> R.string.sort_date_added
}
