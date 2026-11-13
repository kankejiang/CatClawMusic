package com.catclaw.music.kt.ui.screen

import android.content.res.Configuration
import androidx.activity.compose.BackHandler
import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.clickable
import androidx.compose.foundation.combinedClickable
import androidx.compose.foundation.gestures.detectDragGesturesAfterLongPress
import androidx.compose.foundation.horizontalScroll
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.PaddingValues
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxHeight
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.heightIn
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.lazy.LazyRow
import androidx.compose.runtime.LaunchedEffect
import androidx.lifecycle.Lifecycle
import androidx.lifecycle.compose.LifecycleEventEffect
import com.catclaw.music.kt.data.netease.OnlinePlaylist
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.items
import androidx.compose.foundation.lazy.itemsIndexed
import androidx.compose.foundation.lazy.rememberLazyListState
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material3.Icon
import androidx.compose.material3.Text
import androidx.compose.material3.TextField
import androidx.compose.material3.TextFieldDefaults
import androidx.compose.runtime.Composable
import androidx.compose.runtime.remember
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableFloatStateOf
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.saveable.rememberSaveable
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.Brush
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.graphicsLayer
import androidx.compose.ui.hapticfeedback.HapticFeedbackType
import androidx.compose.ui.input.pointer.pointerInput
import androidx.compose.ui.platform.LocalConfiguration
import androidx.compose.ui.platform.LocalHapticFeedback
import androidx.compose.ui.res.painterResource
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import androidx.hilt.navigation.compose.hiltViewModel
import androidx.lifecycle.compose.collectAsStateWithLifecycle
import com.catclaw.music.kt.R
import com.catclaw.music.kt.ui.player.CoverTile
import com.catclaw.music.kt.data.db.toSong
import com.catclaw.music.kt.ui.library.LibraryViewModel
import com.catclaw.music.kt.ui.song.SongBelongs
import com.catclaw.music.kt.ui.song.SongDetailScreen
import com.catclaw.music.kt.data.db.SongListItem
import com.catclaw.music.kt.ui.song.CollectionAction
import com.catclaw.music.kt.ui.song.CollectionChip
import com.catclaw.music.kt.ui.song.CollectionCover
import com.catclaw.music.kt.ui.song.CollectionSearch
import com.catclaw.music.kt.ui.song.SongCollectionScreen
import com.catclaw.music.kt.ui.song.SongList
import com.catclaw.music.kt.ui.song.SongMenuFor
import com.catclaw.music.kt.ui.song.SongMenuTarget
import com.catclaw.music.kt.ui.song.rememberSongMenu
import com.catclaw.music.kt.ui.song.songMenuSubtitle
import com.catclaw.music.kt.data.db.PlaylistCover
import com.catclaw.music.kt.data.db.entity.PlaylistEntity
import com.catclaw.music.kt.ui.playlist.PlaylistViewModel
import com.catclaw.music.kt.ui.playlist.PlaylistDetailContent
import com.catclaw.music.kt.ui.theme.LocalCatClawPalette
import com.catclaw.music.kt.ui.theme.BackTile
import com.catclaw.music.kt.ui.theme.inkOn
import com.catclaw.music.kt.ui.theme.EmptyState
import com.catclaw.music.kt.ui.theme.GlassChip
import com.catclaw.music.kt.ui.theme.GlassSearchField
import com.catclaw.music.kt.ui.theme.PillLabel
import com.catclaw.music.kt.core.library.SongSortKey
import com.catclaw.music.kt.ui.theme.glassCard
import com.catclaw.music.kt.ui.theme.highlightOnGlassCard

/**
 * 歌单 tab —— 照 MAUI `PlaylistPage.xaml`：Hero 卡（标题/副标题 + 右上 44dp 玻璃加号）、
 * 系统歌单与自建歌单各是一张带 46dp 图标块的玻璃卡、详情层是返回块 + 两个胶囊按钮 + 歌曲行。
 */
@Composable
fun PlaylistScreen(
    /** 点开网易云账号里的歌单：整页在线音乐由壳层导航，这里只把歌单对象交出去 */
    onOpenNeteasePlaylist: (OnlinePlaylist) -> Unit = {},
    viewModel: PlaylistViewModel = hiltViewModel(),
    detailViewModel: com.catclaw.music.kt.ui.playlist.PlaylistDetailViewModel = hiltViewModel(),
) {
    val palette = LocalCatClawPalette.current
    val playlists by viewModel.playlists.collectAsStateWithLifecycle()
    val neteaseAccount by viewModel.neteaseAccount.collectAsStateWithLifecycle()
    val neteasePlaylists by viewModel.neteasePlaylists.collectAsStateWithLifecycle()
    // 登录发生在网易云页；从那里返回时本页会重新 START，借这个时机刷新歌单列表
    LaunchedEffect(Unit) { viewModel.refreshNetease() }
    LifecycleEventEffect(Lifecycle.Event.ON_START) { viewModel.refreshNetease() }
    val shown by detailViewModel.displayItems.collectAsStateWithLifecycle()
    val itemSort by detailViewModel.itemSort.collectAsStateWithLifecycle()
    val itemSortDescending by detailViewModel.itemSortDescending.collectAsStateWithLifecycle()
    val itemQuery by detailViewModel.itemQuery.collectAsStateWithLifecycle()
    val openId by detailViewModel.openId.collectAsStateWithLifecycle()
    val covers by viewModel.covers.collectAsStateWithLifecycle()
    var draft by rememberSaveable { mutableStateOf("") }
    var creating by rememberSaveable { mutableStateOf(false) }
    /** 正在改名的歌单 id；同一时刻只有一行在编辑 */
    var editing by rememberSaveable { mutableStateOf<Int?>(null) }
    var draftName by rememberSaveable { mutableStateOf("") }
    var renameTaken by rememberSaveable { mutableStateOf(false) }
    // 收藏 / 最近播放这两个入口的数据与播放动作都在 LibraryViewModel 里，
    // 两个 VM 读的是同一批 Room 单例流，不会因为多拿一个 VM 而出现两份状态
    val libraryViewModel: LibraryViewModel = hiltViewModel()
    var entry by remember { mutableStateOf<PlaylistEntry?>(null) }
    var songDetailId by remember { mutableStateOf<Int?>(null) }
    // 收藏/最近列表里的歌也能跳到它的歌手页或专辑页——那两页是 LibraryScreen 的下钻页，
    // 在这里用同一份组件托管一份，省掉跨页导航（返回键行为与从音乐库进去时一致）
    var belongsDetail by remember { mutableStateOf<LibraryDetail?>(null) }
    // 歌单详情的行此前只有"点=播放"一个动作，能加进来却移不掉。长按补上菜单，
    // 能力与音乐库/播放页同一份（收藏/下一首/详情/分享/下钻），多出来的是「从歌单移除」
    var rowMenu by remember { mutableStateOf<SongMenuTarget?>(null) }
    val detailMenu = rememberSongMenu(
        viewModel = libraryViewModel,
        onOpenBelongs = { belongsDetail = it.toLibraryDetail() },
        onRemoveFromPlaylist = { detailViewModel.removeItem(it) },
        onOpenDetail = { songDetailId = it },
    )
    // 这四层整屏覆盖层各自画了返回方块，系统返回键必须逐层退掉而不是直接退出应用
    // （真机验过：在歌单详情按返回是掉回桌面）。组合在后的优先，所以按「下层 → 上层」排。
    // 歌单详情这一层别漏：它是 VM 的 openId，不像另外三层是页面里的 var
    BackHandler(enabled = openId != null) { detailViewModel.close() }
    BackHandler(enabled = entry != null) { entry = null }
    BackHandler(enabled = belongsDetail != null) { belongsDetail = null }
    BackHandler(enabled = songDetailId != null) { songDetailId = null }

    // 早退结构里**先命中的那一块才是看得见的那一层**：歌曲信息必须排在歌手/专辑层之前判。
    // 之前是 belongsDetail 在前，于是在歌手页点「歌曲信息」把 id 存好了却仍然画歌手页——纹丝不动
    songDetailId?.let { id ->
        SongDetailScreen(
            songId = id,
            load = { libraryViewModel.songDetail(it) },
            loadLyrics = { libraryViewModel.lyricsFor(it) },
            onPlay = { libraryViewModel.playSongById(it) },
            onBack = { songDetailId = null },
        )
        return
    }
    belongsDetail?.let { d ->
        LibraryDetailScreen(
            detail = d,
            onBack = { belongsDetail = null },
            onOpenSong = { songDetailId = it },
            onOpenDetail = { belongsDetail = it },
            viewModel = libraryViewModel,
        )
        return
    }

    val primary = Color(palette.textPrimary)
    val secondary = Color(palette.textSecondary)
    val open = openId?.let { id -> playlists.firstOrNull { it.id == id } }
    // 两个虚拟入口的计数在竖屏/横屏都要用，提到分支外，避免两边各自订阅一份流
    val favorites by libraryViewModel.favorites.collectAsStateWithLifecycle()
    val recent by libraryViewModel.recent.collectAsStateWithLifecycle()
    val landscape = LocalConfiguration.current.orientation == Configuration.ORIENTATION_LANDSCAPE
    entry?.let { kind ->
        PlaylistEntryScreen(
            kind = kind,
            viewModel = libraryViewModel,
            onBack = { entry = null },
            onOpenDetail = { songDetailId = it },
            onOpenBelongs = { belongsDetail = it.toLibraryDetail() },
        )
        return
    }

    // 横屏 411dp 高的画布堆不下「大 Hero + 两张 130dp 入口卡 + 列表」，列表会被迷你条切掉。
    // 与发现页同一套「双栏舞台」：左边实图主卡、右边玻璃货架，首屏自足；竖屏完全走原来的 Column。
    if (landscape && open == null) {
        LandscapePlaylistStage(
            playlists = playlists,
            covers = covers,
            favoriteCount = favorites.size,
            recentCount = recent.size,
            draft = draft,
            onDraftChange = { draft = it },
            creating = creating,
            onToggleCreate = { creating = !creating },
            onCreate = {
                viewModel.create(draft)
                draft = ""
                creating = false
            },
            onOpenFavorites = { entry = PlaylistEntry.Favorites },
            onOpenRecent = { entry = PlaylistEntry.Recent },
            editing = editing,
            draftName = draftName,
            renameTaken = renameTaken,
            onRenameDraft = { draftName = it; renameTaken = false },
            onCancelRename = { editing = null },
            onSaveRename = { id ->
                if (viewModel.rename(id, draftName)) editing = null else renameTaken = true
            },
            onOpen = { detailViewModel.open(it) },
            onDelete = { viewModel.delete(it) },
            onStartRename = { id, name ->
                draftName = name
                renameTaken = false
                editing = id
            },
            onCommitOrder = { viewModel.commitPlaylistOrder(it) },
            primary = primary,
            secondary = secondary,
        )
    } else if (openId != null) {
        // 独立歌单详情页：一行调用，内部自己收集状态
        com.catclaw.music.kt.ui.playlist.PlaylistDetailScreen(
            playlistId = openId!!,
            onBack = { detailViewModel.close() },
            onOpenMenu = { rowMenu = it },
            viewModel = detailViewModel,
        )
    } else {
    Column(modifier = Modifier.fillMaxSize().padding(horizontal = 20.dp)) {
        Spacer(Modifier.height(14.dp))
        if (open == null) {
            // Hero：标题 + 副标题 + 加号（MAUI 的 GlassIconContainer #26FFFFFF）
            // 无卡片底的页头：标题 + 副标题 + 加号（与发现页/音乐库同一套层级）
            Row(
                modifier = Modifier
                    .fillMaxWidth()
                    .padding(vertical = 6.dp),
                verticalAlignment = Alignment.CenterVertically,
            ) {
                Column(modifier = Modifier.weight(1f), verticalArrangement = Arrangement.spacedBy(2.dp)) {
                    Text(
                        text = stringResource(R.string.MyPlaylists),
                        fontSize = 30.sp,
                        fontWeight = FontWeight.Bold,
                        color = primary,
                    )
                    Text(text = stringResource(R.string.PlaylistSubtitle), fontSize = 13.sp, color = secondary)
                }
                Box(
                    modifier = Modifier
                        .size(44.dp)
                        .clip(RoundedCornerShape(14.dp))
                        .background(Color(palette.textPrimary).copy(alpha = 0.15f))
                        .clickable { creating = !creating },
                    contentAlignment = Alignment.Center,
                ) {
                    Icon(
                        painter = painterResource(if (creating) R.drawable.ic_close else R.drawable.ic_add),
                        contentDescription = stringResource(R.string.playlist_new),
                        tint = primary,
                        modifier = Modifier.size(22.dp),
                    )
                }
            }
            Spacer(Modifier.height(12.dp))

            // MAUI 的 PlaylistPage 顶部就是这两张带计数的入口卡片。本端此前一张都没有，
            // 于是收藏在歌单页完全不可达，而 `PlaylistCard` 里的 isSystem 分支一直是死代码 ——
            // 系统歌单从来没被种进 Playlists 表（MAUI 也没有），它们本来就是虚拟入口而不是虚拟行。
            Row(horizontalArrangement = Arrangement.spacedBy(10.dp)) {
                EntryCard(
                    iconRes = R.drawable.ic_favorite,
                    label = stringResource(R.string.playlist_entry_favorites),
                    count = favorites.size,
                    modifier = Modifier.weight(1f),
                ) { entry = PlaylistEntry.Favorites }
                EntryCard(
                    iconRes = R.drawable.ic_history,
                    label = stringResource(R.string.playlist_entry_recent),
                    count = recent.size,
                    modifier = Modifier.weight(1f),
                ) { entry = PlaylistEntry.Recent }
            }
            Spacer(Modifier.height(12.dp))

            if (creating) {
                Row(
                    modifier = Modifier
                        .fillMaxWidth()
                        .glassCard(palette = palette, corner = 20)
                        .padding(horizontal = 12.dp, vertical = 8.dp),
                    verticalAlignment = Alignment.CenterVertically,
                ) {
                    TextField(
                        value = draft,
                        onValueChange = { draft = it },
                        placeholder = { Text(stringResource(R.string.playlist_name_hint), color = secondary) },
                        singleLine = true,
                        colors = TextFieldDefaults.colors(
                            focusedContainerColor = Color.Transparent,
                            unfocusedContainerColor = Color.Transparent,
                            focusedIndicatorColor = Color.Transparent,
                            unfocusedIndicatorColor = Color.Transparent,
                            focusedTextColor = primary,
                            unfocusedTextColor = primary,
                            cursorColor = Color(palette.primary),
                        ),
                        modifier = Modifier.weight(1f),
                    )
                    Spacer(Modifier.width(8.dp))
                    Text(
                        text = stringResource(R.string.playlist_new),
                        fontSize = 14.sp,
                        fontWeight = FontWeight.SemiBold,
                        color = Color(inkOn(palette.primary)),
                        modifier = Modifier
                            .clip(RoundedCornerShape(14.dp))
                            .background(Color(palette.primary))
                            .clickable {
                                viewModel.create(draft)
                                draft = ""
                                creating = false
                            }
                            .padding(horizontal = 14.dp, vertical = 9.dp),
                    )
                }
                Spacer(Modifier.height(12.dp))
            }

            if (neteasePlaylists.isNotEmpty()) {
                NeteaseShelf(
                    accountName = neteaseAccount?.nickname.orEmpty(),
                    playlists = neteasePlaylists,
                    onOpen = onOpenNeteasePlaylist,
                )
                Spacer(Modifier.height(12.dp))
            }

            if (playlists.isEmpty() && !creating) {
                Row(
                    modifier = Modifier
                        .fillMaxWidth()
                        .glassCard(palette = palette, corner = 20)
                        .clickable { creating = true }
                        .padding(horizontal = 16.dp, vertical = 16.dp),
                    verticalAlignment = Alignment.CenterVertically,
                    horizontalArrangement = Arrangement.spacedBy(12.dp),
                ) {
                    Box(
                        modifier = Modifier
                            .size(40.dp)
                            .clip(CircleShape)
                            .background(Color(palette.primary).copy(alpha = 0.18f)),
                        contentAlignment = Alignment.Center,
                    ) {
                        Icon(
                            painter = painterResource(R.drawable.ic_add),
                            contentDescription = null,
                            tint = Color(palette.primary),
                            modifier = Modifier.size(20.dp),
                        )
                    }
                    Column(Modifier.weight(1f), verticalArrangement = Arrangement.spacedBy(2.dp)) {
                        Text(
                            text = stringResource(R.string.playlist_new),
                            fontSize = 15.sp,
                            fontWeight = FontWeight.SemiBold,
                            color = primary,
                        )
                        Text(
                            text = stringResource(R.string.playlist_none),
                            fontSize = 11.5.sp,
                            color = secondary,
                        )
                    }
                }
            }
            // 竖屏与横屏共用同一份列表（拖拽排序、行内改名、打开/删除都在这里），
            // 横屏只是把它放进玻璃货架并让行更矮（compact）。
            PlaylistRows(
                playlists = playlists,
                covers = covers,
                editingId = editing,
                draftName = draftName,
                renameTaken = renameTaken,
                onDraft = { draftName = it; renameTaken = false },
                onCancelRename = { editing = null },
                onSaveRename = { id ->
                    // 改成才收框：同名/空名时留着编辑框并给原因，不然用户以为改好了
                    if (viewModel.rename(id, draftName)) editing = null else renameTaken = true
                },
                onOpen = { detailViewModel.open(it) },
                onDelete = { viewModel.delete(it) },
                onStartRename = { id, name ->
                    draftName = name
                    renameTaken = false
                    editing = id
                },
                onCommitOrder = { viewModel.commitPlaylistOrder(it) },
                modifier = Modifier.fillMaxSize(),
            )
        }
    }
    } // else：竖屏 Column / 横屏详情层
    rowMenu?.let { target -> SongMenuFor(target = target, menu = detailMenu, onDismiss = { rowMenu = null }) }
}

/** 一张歌单卡 —— MAUI 行：46dp 图标块（系统单用 ic_favorite/ic_history，自建单用首字）+ 名称 + 计数 + 44dp 改名 / 删除 */
