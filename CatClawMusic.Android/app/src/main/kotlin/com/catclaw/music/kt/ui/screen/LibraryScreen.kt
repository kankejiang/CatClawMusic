package com.catclaw.music.kt.ui.screen

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
    viewModel: LibraryViewModel = hiltViewModel(),
) {
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
    val recent by viewModel.recent.collectAsStateWithLifecycle()
    val scanning by viewModel.scanning.collectAsStateWithLifecycle()
    val hours = songs.sumOf { it.durationSeconds.toLong() } / 3600.0
    // 占用现读一次：曲库行数变了就该跟着变（扫完库回来要能看到新数）
    val storage by produceState(initialValue = 0L to 0L, songs.size) {
        value = runCatching { viewModel.storage() }.getOrDefault(value)
    }
    // 分档占用与剩余空间，与上面那次同一条"曲库行数变了就重读"的规则。
    // 分组规则在 :core（sizeBuckets），这里只负责画
    val stats by produceState(initialValue = emptyList<SongStat>(), songs.size) {
        value = runCatching { viewModel.localSongStats() }.getOrDefault(value)
    }
    val buckets = remember(stats) { sizeBuckets(stats) }
    val freeSpace = remember(songs.size) { viewModel.freeSpaceBytes() }
    // 数据洞察的分组（同样在 :core 算）与当前翻到第几组
    val qualityGroups = remember(stats) { qualityInsight(stats) }
    val artistGroups = remember(stats) { artistInsight(stats) }
    var insightIndex by remember { mutableStateOf(0) }

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
                // 曲库总大小搬到了下面那张「存储占用」卡（它还带分档占用与剩余空间），
                // 这里只留缓存 —— 它是本端独有的数字，那张卡上没有。
                // `bytes(0)` 回空串，所以缓存为 0 时整段不出现，而不是留一个空标签挂在行尾
                Row(
                    modifier = Modifier.fillMaxWidth().padding(top = 16.dp),
                    verticalAlignment = Alignment.CenterVertically,
                    horizontalArrangement = Arrangement.spacedBy(10.dp),
                ) {
                    if (storage.second > 0L) {
                        Text(
                            text = stringResource(R.string.lib_cache_label) + " " + SongDetailFormat.bytes(storage.second),
                            fontSize = 12.5.sp,
                            color = Color(palette.textPrimary),
                            maxLines = 1,
                            overflow = TextOverflow.Ellipsis,
                            modifier = Modifier.weight(1f),
                        )
                    } else {
                        Spacer(Modifier.weight(1f))
                    }
                    PillLabel(
                        text = stringResource(R.string.lib_open_stats),
                        onClick = { statsOpen = true },
                        filled = false,
                    )
                }
            }
        }

        // 存储占用 —— MAUI `LibraryPage` 的 StorageCard：总大小 + 分档占用 + 设备剩余空间。
        // 此前本端只有 Hero 里一行「曲库大小 · 缓存」，分档占用与剩余空间两个信息完全看不到。
        // 分档键换成了比特率（MAUI 用文件后缀），原因写在 `LibraryInsightRules` 的说明里
        Column(
            modifier = Modifier
                .fillMaxWidth()
                .padding(horizontal = 18.dp, vertical = 6.dp)
                .glassCard(palette = palette, corner = 26)
                .padding(16.dp),
        ) {
            Text(
                // 复用既有的 lib_stat_storage（「存储占用」）—— 新造一个同名 key 就是两份要一起维护的文案
                text = stringResource(R.string.lib_stat_storage),
                fontSize = 16.sp,
                fontWeight = FontWeight.Bold,
                color = Color(palette.textPrimary),
            )
            Row(modifier = Modifier.padding(top = 10.dp), verticalAlignment = Alignment.Bottom) {
                Text(
                    text = SongDetailFormat.bytes(storage.first),
                    fontSize = 26.sp,
                    fontWeight = FontWeight.Bold,
                    color = Color(palette.textPrimary),
                )
                Spacer(Modifier.width(8.dp))
                Text(
                    text = stringResource(R.string.storage_total_unit),
                    fontSize = 12.sp,
                    color = Color(palette.textSecondary),
                    modifier = Modifier.padding(bottom = 4.dp),
                )
            }
            if (buckets.isNotEmpty()) {
                Text(
                    text = stringResource(R.string.storage_buckets_title),
                    fontSize = 12.5.sp,
                    color = Color(palette.textSecondary),
                    modifier = Modifier.padding(top = 14.dp),
                )
                Text(
                    text = stringResource(R.string.storage_buckets_hint),
                    fontSize = 10.5.sp,
                    color = Color(palette.textSecondary),
                    modifier = Modifier.padding(top = 2.dp),
                )
                // 最长的那一档铺满轨道，其余按它取相对长度（MAUI 也是拿 MaxSizeBytes 当分母）
                val maxBytes = buckets.first().bytes
                buckets.forEach { item ->
                    Row(
                        modifier = Modifier.fillMaxWidth().padding(top = 10.dp),
                        verticalAlignment = Alignment.CenterVertically,
                        horizontalArrangement = Arrangement.spacedBy(10.dp),
                    ) {
                        Box(
                            modifier = Modifier
                                .size(9.dp)
                                .clip(RoundedCornerShape(3.dp))
                                .background(bucketColor(item.bucket)),
                        )
                        Text(
                            text = stringResource(qualityLabelRes(item.bucket)),
                            fontSize = 12.5.sp,
                            color = Color(palette.textSecondary),
                            maxLines = 1,
                            overflow = TextOverflow.Ellipsis,
                            modifier = Modifier.width(64.dp),
                        )
                        Box(
                            modifier = Modifier
                                .weight(1f)
                                .height(8.dp)
                                .clip(RoundedCornerShape(99.dp))
                                .background(Color(palette.textPrimary).copy(alpha = 0.10f)),
                        ) {
                            Box(
                                modifier = Modifier
                                    .fillMaxWidth((item.bytes.toFloat() / maxBytes).coerceIn(0f, 1f))
                                    .fillMaxHeight()
                                    .clip(RoundedCornerShape(99.dp))
                                    .background(bucketColor(item.bucket)),
                            )
                        }
                        Text(
                            text = SongDetailFormat.bytes(item.bytes),
                            fontSize = 11.5.sp,
                            color = Color(palette.textSecondary),
                            maxLines = 1,
                            modifier = Modifier.width(70.dp),
                        )
                    }
                }
            }
            Box(
                modifier = Modifier
                    .fillMaxWidth()
                    .padding(top = 14.dp)
                    .height(1.dp)
                    .background(Color(palette.textPrimary).copy(alpha = 0.10f)),
            )
            Row(modifier = Modifier.fillMaxWidth().padding(top = 12.dp), verticalAlignment = Alignment.CenterVertically) {
                Text(
                    text = stringResource(R.string.storage_free_space),
                    fontSize = 12.sp,
                    color = Color(palette.textSecondary),
                    modifier = Modifier.weight(1f),
                )
                Text(
                    // 取不到就明说 --：写成 0 B 会被读成「手机满了」，与事实相反
                    text = if (freeSpace > 0) SongDetailFormat.bytes(freeSpace) else "--",
                    fontSize = 12.sp,
                    fontWeight = FontWeight.Bold,
                    color = Color(palette.textPrimary),
                )
            }
        }

        // 数据洞察 —— MAUI `LibraryPage` 的 DataInsightCard：环形图 + 图例 + ‹ 圆点 › 翻页。
        // C# 的第一组是「音频格式」（按文件后缀分组），本端取不到后缀；这里只摆两组建得出来的分布，
        // 并在卡上把这件事写给用户看（原因见 `LibraryInsightRules` 的文件注释）
        val insightSets = listOf(
            stringResource(R.string.insight_quality) to qualityGroups.map {
                InsightSlice(stringResource(qualityLabelRes(it.bucket)), it.count, bucketColor(it.bucket))
            },
            stringResource(R.string.insight_artist) to buildList {
                artistGroups.top.forEachIndexed { i, slice ->
                    add(
                        InsightSlice(
                            label = slice.label.ifBlank { stringResource(R.string.insight_unknown_artist) },
                            count = slice.count,
                            color = artistSliceColor(i),
                        ),
                    )
                }
                if (artistGroups.otherCount > 0) {
                    // 复用 quality_other（「其他」）：为同一句话再开一个 key，就是两份要一起维护的文案
                    add(InsightSlice(stringResource(R.string.quality_other), artistGroups.otherCount, ARTIST_OTHER_COLOR))
                }
            },
        ).filter { it.second.isNotEmpty() }

        if (insightSets.isNotEmpty()) {
            val shown = insightIndex.coerceIn(0, insightSets.lastIndex)
            val current = insightSets[shown]
            Column(
                modifier = Modifier
                    .fillMaxWidth()
                    .padding(horizontal = 18.dp, vertical = 6.dp)
                    .glassCard(palette = palette, corner = 26)
                    .padding(16.dp),
            ) {
                Row(modifier = Modifier.fillMaxWidth(), verticalAlignment = Alignment.CenterVertically) {
                    Text(
                        text = stringResource(R.string.insight_title),
                        fontSize = 16.sp,
                        fontWeight = FontWeight.Bold,
                        color = Color(palette.textPrimary),
                    )
                    Spacer(Modifier.width(8.dp))
                    // 当前这组的名字做成小徽标，翻页时它跟着换 —— 光看圆环分不出哪组是哪组
                    Text(
                        text = current.first,
                        fontSize = 11.sp,
                        color = Color(palette.textPrimary),
                        modifier = Modifier
                            .clip(RoundedCornerShape(99.dp))
                            .border(1.dp, Color(palette.textPrimary).copy(alpha = 0.18f), RoundedCornerShape(99.dp))
                            .padding(horizontal = 8.dp, vertical = 2.dp),
                    )
                    Spacer(Modifier.weight(1f))
                    Text(
                        text = (shown + 1).toString() + " / " + insightSets.size,
                        fontSize = 11.sp,
                        fontWeight = FontWeight.Bold,
                        color = Color(palette.textSecondary),
                    )
                }
                Row(
                    modifier = Modifier.fillMaxWidth().padding(top = 14.dp),
                    verticalAlignment = Alignment.CenterVertically,
                    horizontalArrangement = Arrangement.spacedBy(16.dp),
                ) {
                    InsightDonut(current.second)
                    Column(modifier = Modifier.weight(1f), verticalArrangement = Arrangement.spacedBy(7.dp)) {
                        val total = current.second.sumOf { it.count }.coerceAtLeast(1)
                        current.second.forEach { slice ->
                            Row(verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                                Box(Modifier.size(9.dp).clip(RoundedCornerShape(3.dp)).background(slice.color))
                                Text(
                                    text = slice.label,
                                    fontSize = 12.sp,
                                    color = Color(palette.textPrimary),
                                    maxLines = 1,
                                    overflow = TextOverflow.Ellipsis,
                                    modifier = Modifier.weight(1f),
                                )
                                Text(
                                    text = slice.count.toString() + " · " + (slice.count * 100 / total) + "%",
                                    fontSize = 11.sp,
                                    color = Color(palette.textSecondary),
                                )
                            }
                        }
                    }
                }
                Row(
                    modifier = Modifier.fillMaxWidth().padding(top = 12.dp),
                    verticalAlignment = Alignment.CenterVertically,
                    horizontalArrangement = Arrangement.spacedBy(12.dp),
                ) {
                    DonutStep("‹") { insightIndex = (shown - 1 + insightSets.size) % insightSets.size }
                    Row(modifier = Modifier.weight(1f), horizontalArrangement = Arrangement.Center) {
                        insightSets.indices.forEach { i ->
                            Box(
                                modifier = Modifier
                                    .padding(horizontal = 3.dp)
                                    .size(6.dp)
                                    .clip(RoundedCornerShape(99.dp))
                                    .background(if (i == shown) Color(palette.primary) else Color(palette.textPrimary).copy(alpha = 0.25f)),
                            )
                        }
                    }
                    DonutStep("›") { insightIndex = (shown + 1) % insightSets.size }
                }
                Text(
                    text = stringResource(R.string.insight_hint),
                    fontSize = 10.5.sp,
                    color = Color(palette.textSecondary),
                    modifier = Modifier.padding(top = 10.dp),
                )
            }
        }

        // 资料库卡 —— MAUI `LibraryPage` 首屏那五张入口卡（本地 / 我喜欢的 / 最近播放 / 网络 / 下载）。
        // 副标题由这里给全：计数行是「N 首」，导航行是那句功能说明 —— 之前 SourceRow 只认一个 count，
        // 于是「跳别的页面」这种行只能在徽标里显示一个没有意义的数字。
        val sources = listOf(
            LibrarySourceRow(
                R.drawable.ic_library,
                stringResource(R.string.lib_local_name),
                stringResource(R.string.playlist_count, songs.size),
            ) { onOpenKind(LibraryKind.Songs) },
            LibrarySourceRow(
                R.drawable.ic_favorite,
                stringResource(R.string.lib_favorites_name),
                stringResource(R.string.playlist_count, favorites.size),
            ) { onOpenKind(LibraryKind.Favorites) },
            LibrarySourceRow(
                R.drawable.ic_history,
                stringResource(R.string.playlist_entry_recent),
                stringResource(R.string.playlist_count, recent.size),
            ) { onOpenKind(LibraryKind.Recent) },
            LibrarySourceRow(
                R.drawable.ic_search,
                stringResource(R.string.RemoteMusic),
                stringResource(R.string.RemoteMusicSubtitle),
                onOpenRemote,
            ),
            LibrarySourceRow(
                R.drawable.ic_download,
                stringResource(R.string.downloads_title),
                stringResource(R.string.downloads_subtitle),
                onOpenDownloads,
            ),
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
            sources.forEach { SourceRow(it.iconRes, it.name, it.subtitle, palette, it.onClick) }
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
                    itemsIndexed(fresh, key = { _, song -> song.id }) { index, song ->
                        RecentAddCard(song) { viewModel.playRows(fresh, index) }
                    }
                }
            }
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
private fun RecentAddCard(song: SongListItem, onClick: () -> Unit) {
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
/** 资料库卡的一行。副标题由调用处给全：计数行是「N 首」，导航行是功能说明 */
private data class LibrarySourceRow(
    val iconRes: Int,
    val name: String,
    val subtitle: String,
    val onClick: () -> Unit,
)

@Composable
private fun SourceRow(
    iconRes: Int,
    name: String,
    subtitle: String,
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
            Text(subtitle, fontSize = 12.sp, color = Color(palette.textSecondary), maxLines = 1, overflow = TextOverflow.Ellipsis)
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
                    text = countText,
                    fontSize = 12.sp,
                    color = Color(palette.textSecondary),
                )
            }
            // 宫格 / 列表切换只给这两张页：歌曲类页面的行是「序号 + 标题 + 歌手」，
            // 摊成宫格反而每个格子只能放下一行字（MAUI 也只在这两张页上给了这对按钮）
            when (kind) {
                LibraryKind.Artists -> ViewModeToggle(grid = artistsGrid) { artistsGrid = it }
                LibraryKind.Albums -> ViewModeToggle(grid = albumsGrid) { albumsGrid = it }
                else -> Unit
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
            // 来源筛选：MAUI `AllSongsPage` 的本地/网络/全部三档（`SourceFilterControl`）。
            // 此前这张页只读 Source = 0，「网络」曲目在库列表里根本看不见 ——
            // 从远程来源页导进来的歌只能回那一页找
            Row(
                modifier = Modifier
                    .fillMaxWidth()
                    .padding(horizontal = 16.dp, vertical = 4.dp),
                horizontalArrangement = Arrangement.spacedBy(8.dp),
            ) {
                SongSourceFilter.entries.forEach { mode ->
                    GlassChip(
                        label = stringResource(sourceFilterLabelRes(mode)),
                        selected = songSource == mode,
                        onClick = { viewModel.setSongSource(mode) },
                    )
                }
            }
            Row(
                modifier = Modifier
                    .fillMaxWidth()
                    // 内边距在滚动**外面**：英文 locale 下四颗 chip 会比屏幕宽，横滑时首尾那颗
                    // 不该贴到屏幕边缘（先 padding 再 horizontalScroll 才是"带边距的窗口"）
                    .padding(horizontal = 16.dp, vertical = 4.dp)
                    .horizontalScroll(rememberScrollState()),
                horizontalArrangement = Arrangement.spacedBy(8.dp),
            ) {
                SongSortKey.entries.forEach { mode ->
                    GlassChip(
                        // 活动那档带方向箭头（箭头是符号，不进翻译表）：点它就是翻方向，
                        // 与 C# `ToggleSort` 的交互一致，只是这边真的会翻
                        label = stringResource(sortLabelRes(mode)) +
                            if (sort == mode) (if (sortDescending) " ↓" else " ↑") else "",
                        selected = sort == mode,
                        onClick = { viewModel.setSongSort(mode) },
                    )
                }
            }
            GlassSearchField(
                query = query,
                onChange = viewModel::setSongQuery,
                placeholder = stringResource(R.string.library_search_hint),
                modifier = Modifier.padding(horizontal = 16.dp, vertical = 6.dp),
            )
        }
        // 艺人 / 专辑页的搜索（MAUI 那两张页顶部也有）。没有排序档：
        // 这两页的行是名字列表，DAO 已经按名字升序给出来，再摆四颗排不动的 chip 就是假控件
        if (kind == LibraryKind.Artists || kind == LibraryKind.Albums) {
            GlassSearchField(
                query = infoQuery,
                onChange = viewModel::setInfoQuery,
                placeholder = stringResource(
                    if (kind == LibraryKind.Artists) R.string.library_search_artist_hint else R.string.library_search_album_hint,
                ),
                modifier = Modifier.padding(horizontal = 16.dp, vertical = 6.dp),
            )
        }

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

    Column(modifier = Modifier.fillMaxSize()) {
        Row(
            modifier = Modifier.fillMaxWidth().padding(start = 16.dp, end = 16.dp, top = 14.dp, bottom = 10.dp),
            verticalAlignment = Alignment.CenterVertically,
            horizontalArrangement = Arrangement.spacedBy(12.dp),
        ) {
            BackTile(onBack = onBack, description = stringResource(R.string.queue_back))
            // 专辑头图 —— 对应 MAUI `AlbumDetailPage.xaml:13-65` 的 HeroCard（72dp 封面 + 专辑名/歌手/年份）。
            // 取不到封面就退回专辑首字（CoverTile 的 fallback），不是空框
            val head = albumHeader
            if (head != null) {
                CoverTile(
                    coverArtPath = head.coverArtPath,
                    audioPath = head.sampleFilePath,
                    maxPx = 256,
                    modifier = Modifier
                        .size(64.dp)
                        .clip(RoundedCornerShape(16.dp))
                        .background(Color(palette.primary).copy(alpha = 0.22f)),
                ) {
                    Text(
                        text = detail.name.take(1),
                        fontSize = 26.sp,
                        fontWeight = FontWeight.Bold,
                        color = Color(palette.textPrimary),
                    )
                }
            }
            Column(modifier = Modifier.weight(1f)) {
                Text(
                    text = detail.name,
                    fontSize = 22.sp,
                    fontWeight = FontWeight.Bold,
                    color = Color(palette.textPrimary),
                    maxLines = 1,
                    overflow = TextOverflow.Ellipsis,
                )
                if (head != null && head.artist.isNotBlank()) {
                    Text(
                        text = head.artist,
                        fontSize = 13.sp,
                        color = Color(palette.textSecondary),
                        maxLines = 1,
                        overflow = TextOverflow.Ellipsis,
                    )
                }
                // 年份 0 表示库里没有这一列的值：不画，而不是显示一个 0（MAUI 那张卡同样留空）
                if (head != null && head.year > 0) {
                    Text(text = head.year.toString(), fontSize = 12.sp, color = Color(palette.textSecondary))
                }
                Text(
                    text = stringResource(R.string.playlist_count, loaded.size),
                    fontSize = 12.sp,
                    color = Color(palette.textSecondary),
                )
            }
        }
        // 整张播 / 随机播 / 加进歌单：单开一行（MAUI 的 HeroCard 里这几颗也在标题下面另起一行）。
        // 挤在页头那一行里、再加上封面之后，窄屏会把标题压成两个字
        if (loaded.isNotEmpty() && !(byArtist && albumTab)) {
            Row(
                modifier = Modifier.fillMaxWidth().padding(horizontal = 16.dp),
                horizontalArrangement = Arrangement.spacedBy(10.dp),
            ) {
                // 粒子与全部歌曲页同一套文案、同一条路径（播的就是屏幕上这份 loaded）
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
                // 只有专辑页给这一颗：艺人页那份列表是「这个人名下的歌」、跨专辑，
                // 与 MAUI HeroCard 上那颗按钮的语义不是一回事
                if (!byArtist) {
                    PillLabel(
                        text = stringResource(R.string.song_menu_add_to_playlist),
                        onClick = { addToPlaylistOpen = true },
                        filled = false,
                    )
                }
            }
            Spacer(Modifier.height(8.dp))
        }
        if (byArtist) {
            Row(
                modifier = Modifier.fillMaxWidth().padding(horizontal = 16.dp),
                horizontalArrangement = Arrangement.spacedBy(8.dp),
            ) {
                GlassChip(
                    label = stringResource(R.string.disc_cat_album),
                    selected = albumTab,
                    onClick = { albumTab = true },
                )
                GlassChip(
                    label = stringResource(R.string.TabSongs),
                    selected = !albumTab,
                    onClick = { albumTab = false },
                )
            }
            Spacer(Modifier.height(6.dp))
        }
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
private data class InfoRow(
    val title: String,
    val subtitle: String,
    val coverArtPath: String? = null,
    val sampleFilePath: String? = null,
)

/**
 * 宫格 / 列表切换 —— MAUI 列表页顶部那对图标按钮（选中项用主色底 + 对比墨色）。
 * 64dp 的两颗按钮挤不下，所以做成一个胶囊里并排两颗 34dp 的图标。
 */
@Composable
private fun ViewModeToggle(grid: Boolean, onChange: (Boolean) -> Unit) {
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
private fun InfoList(rows: List<InfoRow>, emptyHint: String, onOpen: (Int) -> Unit, grid: Boolean = false) {
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
private fun EmptyHint(text: String) {
    Box(modifier = Modifier.fillMaxSize().padding(horizontal = 22.dp), contentAlignment = Alignment.Center) {
        EmptyState(hint = text, iconRes = R.drawable.ic_library)
    }
}

/** 环形图的一段 */
private data class InsightSlice(val label: String, val count: Int, val color: Color)

/** 艺人那组前 5 名的配色：定序取色，多一段也不会没有颜色 */
private val ARTIST_SLICE_COLORS = listOf(
    Color(0xFF8C7BFF), Color(0xFF55D6FF), Color(0xFF7ED8C3), Color(0xFFF56FA0), Color(0xFFFFB74D),
)
private val ARTIST_OTHER_COLOR = Color(0xFF8D93B7)

private fun artistSliceColor(index: Int): Color = ARTIST_SLICE_COLORS[index % ARTIST_SLICE_COLORS.size]

/**
 * 甜甜圈：用 Canvas 的 drawArc 逐段画，不给每一段建节点（六段环建六个 Box 是白搭）。
 * 调用方已经滤掉空集合，这里仍然兜一个 total >= 1，免得除零。
 */
@Composable
private fun InsightDonut(slices: List<InsightSlice>, diameter: Dp = 132.dp) {
    val stroke = 20.dp
    Canvas(modifier = Modifier.size(diameter)) {
        val ring = diameter.toPx() - stroke.toPx()
        val topLeft = Offset((size.width - ring) / 2f, (size.height - ring) / 2f)
        val total = slices.sumOf { it.count }.coerceAtLeast(1)
        // 从 12 点方向顺时针：-90° 起手，与 C# DonutView 的起始角一致
        var start = -90f
        slices.forEach { slice ->
            val sweep = 360f * slice.count / total
            drawArc(
                color = slice.color,
                startAngle = start,
                sweepAngle = sweep,
                useCenter = false,
                topLeft = topLeft,
                size = Size(ring, ring),
                style = Stroke(width = stroke.toPx(), cap = StrokeCap.Butt),
            )
            start += sweep
        }
    }
}

/** 翻页小按钮（‹ ›）：触达 44dp、视觉 30dp 的圆 —— 可点节点在 padding 之外 */
@Composable
private fun DonutStep(glyph: String, onClick: () -> Unit) {
    val palette = LocalCatClawPalette.current
    Box(
        modifier = Modifier
            .size(44.dp)
            .clip(RoundedCornerShape(99.dp))
            .clickable(onClick = onClick),
        contentAlignment = Alignment.Center,
    ) {
        Box(
            modifier = Modifier
                .size(30.dp)
                .clip(RoundedCornerShape(99.dp))
                .border(1.dp, Color(palette.textPrimary).copy(alpha = 0.18f), RoundedCornerShape(99.dp)),
            contentAlignment = Alignment.Center,
        ) {
            Text(glyph, fontSize = 17.sp, color = Color(palette.textPrimary))
        }
    }
}

/** 音质档位的文案。六档都有，所以这个 `when` 不带 else —— 加一档编译就会停在这 */
@StringRes
private fun qualityLabelRes(bucket: QualityBucket): Int = when (bucket) {
    QualityBucket.Unknown -> R.string.quality_unknown
    QualityBucket.Kbps320 -> R.string.quality_320
    QualityBucket.Kbps256 -> R.string.quality_256
    QualityBucket.Kbps192 -> R.string.quality_192
    QualityBucket.Kbps128 -> R.string.quality_128
    QualityBucket.Other -> R.string.quality_other
}

/**
 * 档位配色。按**档位**取色而不是按行序：行序会随占用变化，行序取色的话
 * 同一档今天紫、明天蓝，看着像数据变了。
 */
private fun bucketColor(bucket: QualityBucket): Color = when (bucket) {
    QualityBucket.Kbps320 -> Color(0xFF8C7BFF)
    QualityBucket.Kbps256 -> Color(0xFF55D6FF)
    QualityBucket.Kbps192 -> Color(0xFF7ED8C3)
    QualityBucket.Kbps128 -> Color(0xFFF56FA0)
    QualityBucket.Other -> Color(0xFFFFB74D)
    QualityBucket.Unknown -> Color(0xFF8D93B7)
}

/** 来源筛选 chip 的文案。三档都有，所以这个 `when` 不带 else —— 加一档编译就会停在这 */
@StringRes
private fun sourceFilterLabelRes(filter: SongSourceFilter): Int = when (filter) {
    SongSourceFilter.All -> R.string.song_source_all
    SongSourceFilter.Local -> R.string.song_source_local
    SongSourceFilter.Network -> R.string.song_source_network
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
