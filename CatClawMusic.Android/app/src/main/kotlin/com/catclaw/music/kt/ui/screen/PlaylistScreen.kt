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
) {
    val palette = LocalCatClawPalette.current
    val playlists by viewModel.playlists.collectAsStateWithLifecycle()
    val neteaseAccount by viewModel.neteaseAccount.collectAsStateWithLifecycle()
    val neteasePlaylists by viewModel.neteasePlaylists.collectAsStateWithLifecycle()
    // 登录发生在网易云页；从那里返回时本页会重新 START，借这个时机刷新歌单列表
    LaunchedEffect(Unit) { viewModel.refreshNetease() }
    LifecycleEventEffect(Lifecycle.Event.ON_START) { viewModel.refreshNetease() }
    val shown by viewModel.displayItems.collectAsStateWithLifecycle()
    val itemSort by viewModel.itemSort.collectAsStateWithLifecycle()
    val itemSortDescending by viewModel.itemSortDescending.collectAsStateWithLifecycle()
    val itemQuery by viewModel.itemQuery.collectAsStateWithLifecycle()
    val openId by viewModel.openId.collectAsStateWithLifecycle()
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
        onRemoveFromPlaylist = { viewModel.removeItem(it) },
        onOpenDetail = { songDetailId = it },
    )
    // 这四层整屏覆盖层各自画了返回方块，系统返回键必须逐层退掉而不是直接退出应用
    // （真机验过：在歌单详情按返回是掉回桌面）。组合在后的优先，所以按「下层 → 上层」排。
    // 歌单详情这一层别漏：它是 VM 的 openId，不像另外三层是页面里的 var
    BackHandler(enabled = openId != null) { viewModel.close() }
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
            onOpen = { viewModel.open(it) },
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
    } else if (open != null) {
        // 歌单详情横竖屏共用同一份骨架：头图 + 三颗动作 + 五档排序 + 搜索 + 歌曲列表。
        // 横屏由 SongCollectionScreen 压成两行，竖屏仍是纵向堆叠。
        open?.let { playlist ->
            SongCollectionScreen(
                title = playlist.name,
                countText = stringResource(R.string.playlist_count, shown.size),
                onBack = { viewModel.close() },
                cover = CollectionCover(
                    coverArtPath = covers[playlist.id]?.coverArtPath,
                    audioPath = covers[playlist.id]?.filePath,
                    fallback = playlist.name,
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
                chipGroups = listOf(
                    SongSortKey.entries.map { mode ->
                        CollectionChip(
                            label = stringResource(sortLabelRes(mode)) +
                                if (itemSort == mode) (if (itemSortDescending) " ↓" else " ↑") else "",
                            selected = itemSort == mode,
                            onClick = { viewModel.setItemSort(mode) },
                        )
                    },
                ),
                search = CollectionSearch(
                    itemQuery,
                    stringResource(R.string.playlist_search_hint),
                    viewModel::setItemQuery,
                ),
            ) {
                PlaylistSongRows(
                    rows = shown,
                    query = itemQuery,
                    compact = landscape,
                    primary = primary,
                    secondary = secondary,
                    onPlay = { viewModel.playAt(it) },
                    onOpenMenu = { rowMenu = it },
                )
            }
        }
    } else {
    Column(modifier = Modifier.fillMaxSize().padding(horizontal = 20.dp)) {
        Spacer(Modifier.height(14.dp))
        if (open == null) {
            // Hero：标题 + 副标题 + 加号（MAUI 的 GlassIconContainer #26FFFFFF）
            Row(
                modifier = Modifier
                    .fillMaxWidth()
                    .glassCard(palette = palette, corner = 28)
                    .padding(horizontal = 18.dp, vertical = 16.dp),
                verticalAlignment = Alignment.CenterVertically,
            ) {
                Column(modifier = Modifier.weight(1f), verticalArrangement = Arrangement.spacedBy(4.dp)) {
                    Text(
                        text = stringResource(R.string.MyPlaylists),
                        fontSize = 26.sp,
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
                EmptyState(hint = stringResource(R.string.playlist_none), iconRes = R.drawable.ic_playlist)
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
                onOpen = { viewModel.open(it) },
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
@Composable
private fun PlaylistCard(
    playlist: PlaylistEntity,
    /** 歌单内第一首的封面来源（没有曲就是 null），MAUI `PlaylistPage.xaml:132-144` 那张缩略图 */
    cover: PlaylistCover?,
    onOpen: () -> Unit,
    onDelete: () -> Unit,
    onRename: () -> Unit,
    /** 拖拽排序要在这张卡最外层挂手势与位移（见调用处），所以由调用方给 */
    modifier: Modifier = Modifier,
    /** 横屏货架只有 ~230dp 高，行要压到 52dp 才露得出四五行 */
    compact: Boolean = false,
) {
    val palette = LocalCatClawPalette.current
    val primary = Color(palette.textPrimary)
    val secondary = Color(palette.textSecondary)
    val corner = if (compact) 16 else 22
    Row(
        modifier = modifier
            .fillMaxWidth()
            .padding(vertical = if (compact) 2.dp else 5.dp)
            .glassCard(palette = palette, corner = corner)
            .clickable(onClick = onOpen)
            .padding(horizontal = 12.dp, vertical = if (compact) 5.dp else 10.dp),
        verticalAlignment = Alignment.CenterVertically,
        horizontalArrangement = Arrangement.spacedBy(12.dp),
    ) {
        // 取不到图就退回原来的首字 / 系统图标方块（CoverTile 的 fallback）
        CoverTile(
            coverArtPath = cover?.coverArtPath,
            audioPath = cover?.filePath,
            maxPx = 128,
            modifier = Modifier
                .size(if (compact) 38.dp else 46.dp)
                .clip(RoundedCornerShape(if (compact) 11.dp else 14.dp))
                .background(
                    if (playlist.isSystem) Color(palette.primary).copy(alpha = 0.22f)
                    else Color(palette.textPrimary).copy(alpha = 0.12f),
                ),
        ) {
            if (playlist.isSystem) {
                Icon(
                    painter = painterResource(
                        if (playlist.name.contains("历史") || playlist.name.contains("最近")) R.drawable.ic_history
                        else R.drawable.ic_favorite,
                    ),
                    contentDescription = null,
                    tint = Color(palette.primary),
                    modifier = Modifier.size(22.dp),
                )
            } else {
                Text(text = playlist.name.take(1), fontSize = 20.sp, fontWeight = FontWeight.Bold, color = primary)
            }
        }
        Column(modifier = Modifier.weight(1f), verticalArrangement = Arrangement.spacedBy(2.dp)) {
            Text(text = playlist.name, fontSize = if (compact) 13.5.sp else 15.sp, fontWeight = FontWeight.SemiBold, color = primary, maxLines = 1, overflow = TextOverflow.Ellipsis)
            Text(text = stringResource(R.string.playlist_count, playlist.songCount), fontSize = if (compact) 10.5.sp else 12.sp, color = secondary)
        }
        if (!playlist.isSystem) {
            // MAUI 是 ⋮ 菜单里两项（重命名 / 删除）；这里摊成两个 44dp 按钮，
            // 少一层弹窗、也不用在一行里再嵌一个弹层去管它的生命周期
            CardAction(R.drawable.ic_edit, stringResource(R.string.playlist_rename), onRename, compact)
            CardAction(R.drawable.ic_close, stringResource(R.string.Delete), onDelete, compact)
        }
    }
}

/** 行尾那个 44×44 的图标按钮。尺寸与触达面走全 app 同一档 */
@Composable
private fun CardAction(iconRes: Int, description: String, onClick: () -> Unit, compact: Boolean = false) {
    val palette = LocalCatClawPalette.current
    Box(
        modifier = Modifier
            .size(if (compact) 34.dp else 44.dp)
            .clip(RoundedCornerShape(if (compact) 11.dp else 14.dp))
            .clickable(onClickLabel = description, onClick = onClick),
        contentAlignment = Alignment.Center,
    ) {
        Icon(
            painter = painterResource(iconRes),
            contentDescription = description,
            tint = Color(palette.textSecondary),
            modifier = Modifier.size(if (compact) 15.dp else 18.dp),
        )
    }
}

/** 行内改名：与「新建歌单」同一套写法（输入框 + 实心确认胶囊 + 次要色取消） */
@Composable
private fun RenameRow(
    draft: String,
    taken: Boolean,
    onDraft: (String) -> Unit,
    onCancel: () -> Unit,
    onSave: () -> Unit,
) {
    val palette = LocalCatClawPalette.current
    val primary = Color(palette.textPrimary)
    val secondary = Color(palette.textSecondary)
    Column(
        modifier = Modifier
            .fillMaxWidth()
            .padding(vertical = 5.dp)
            .glassCard(palette = palette, corner = 22)
            .padding(horizontal = 12.dp, vertical = 8.dp),
    ) {
        Row(verticalAlignment = Alignment.CenterVertically) {
            TextField(
                value = draft,
                onValueChange = onDraft,
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
                text = stringResource(R.string.playlist_rename_save),
                fontSize = 14.sp,
                fontWeight = FontWeight.SemiBold,
                color = Color(inkOn(palette.primary)),
                modifier = Modifier
                    .heightIn(min = 44.dp)
                    .clip(RoundedCornerShape(14.dp))
                    .background(Color(palette.primary))
                    .clickable(onClick = onSave)
                    .padding(horizontal = 14.dp, vertical = 10.dp),
            )
            Spacer(Modifier.width(6.dp))
            Text(
                text = stringResource(R.string.playlist_rename_cancel),
                fontSize = 14.sp,
                color = secondary,
                modifier = Modifier
                    .heightIn(min = 44.dp)
                    .clip(RoundedCornerShape(14.dp))
                    .clickable(onClick = onCancel)
                    .padding(horizontal = 10.dp, vertical = 10.dp),
            )
        }
        if (taken) {
            // 提示语要落在"玻璃卡上可读的强调前景"上：直接拿 palette.accent 当字色会被
            // 对比度闸门拦下（品牌粉压在粉底上量不到 3:1），这个 helper 就是那条判据的出口
            Text(
                text = stringResource(R.string.playlist_name_taken),
                fontSize = 12.sp,
                color = Color(highlightOnGlassCard(palette)),
                modifier = Modifier.padding(top = 4.dp),
            )
        }
    }
}

/** 歌单页顶部那两个入口。它们是虚拟入口，不是 Playlists 表里的行 */
enum class PlaylistEntry { Favorites, Recent }

@Composable
private fun EntryCard(
    iconRes: Int,
    label: String,
    count: Int,
    modifier: Modifier = Modifier,
    onClick: () -> Unit,
) {
    val palette = LocalCatClawPalette.current
    Column(
        modifier = modifier
            .glassCard(palette = palette, corner = 22)
            .clickable(onClick = onClick)
            .padding(horizontal = 14.dp, vertical = 12.dp),
        verticalArrangement = Arrangement.spacedBy(8.dp),
    ) {
        Box(
            modifier = Modifier
                .size(36.dp)
                .clip(RoundedCornerShape(12.dp))
                .background(Color(palette.primary).copy(alpha = 0.22f)),
            contentAlignment = Alignment.Center,
        ) {
            Icon(
                painter = painterResource(iconRes),
                contentDescription = null,
                tint = Color(palette.textPrimary),
                modifier = Modifier.size(19.dp),
            )
        }
        Text(
            text = label,
            fontSize = 14.sp,
            fontWeight = FontWeight.SemiBold,
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

/** 入口点进去的列表。两张列表共用同一份行组件，所以长按菜单、收藏标记、封面规则都一致 */
@Composable
private fun PlaylistEntryScreen(
    kind: PlaylistEntry,
    viewModel: LibraryViewModel,
    onBack: () -> Unit,
    onOpenDetail: (Int) -> Unit,
    onOpenBelongs: ((SongBelongs) -> Unit)?,
) {
    val favorites by viewModel.favorites.collectAsStateWithLifecycle()
    val recent by viewModel.recent.collectAsStateWithLifecycle()
    val isFavorites = kind == PlaylistEntry.Favorites
    val rows = if (isFavorites) favorites else recent

    SongCollectionScreen(
        title = stringResource(
            if (isFavorites) R.string.playlist_entry_favorites else R.string.playlist_entry_recent,
        ),
        countText = stringResource(R.string.playlist_count, rows.size),
        onBack = onBack,
    ) {
        SongList(
            rows = rows,
            subtitleOf = { it.artist },
            menu = rememberSongMenu(viewModel, onOpenBelongs = onOpenBelongs, onOpenDetail = onOpenDetail),
            emptyHint = stringResource(
                if (isFavorites) R.string.favorite_empty_hint else R.string.recent_empty_hint,
            ),
            onPlay = { if (isFavorites) viewModel.playFavoriteAt(it) else viewModel.playRecentAt(it) },
        )
    }
}

// ─────────────────────────── 横屏舞台 ───────────────────────────
// 与发现页（SearchScreen.LandscapeDiscover）同一套语言：44dp 窄头 + 左实图主卡 +
// 右玻璃货架，底部 80dp 留给迷你条。竖屏完全不走这里的任何一个组件。

private const val COLLECTION_BLUE = 0xFF7FA6D8L
private const val COLLECTION_AMBER = 0xFFD8AA6AL
/** 迷你条浮在底部（60 高 + 12 下边距 + 呼吸） */
private val PlaylistChromeReserve = 80.dp

@Composable
private fun LandscapePlaylistStage(
    playlists: List<PlaylistEntity>,
    covers: Map<Int, PlaylistCover>,
    favoriteCount: Int,
    recentCount: Int,
    draft: String,
    onDraftChange: (String) -> Unit,
    creating: Boolean,
    onToggleCreate: () -> Unit,
    onCreate: () -> Unit,
    onOpenFavorites: () -> Unit,
    onOpenRecent: () -> Unit,
    editing: Int?,
    draftName: String,
    renameTaken: Boolean,
    onRenameDraft: (String) -> Unit,
    onCancelRename: () -> Unit,
    onSaveRename: (Int) -> Unit,
    onOpen: (Int) -> Unit,
    onDelete: (Int) -> Unit,
    onStartRename: (Int, String) -> Unit,
    onCommitOrder: (List<Int>) -> Unit,
    primary: Color,
    secondary: Color,
) {
    val palette = LocalCatClawPalette.current
    Column(Modifier.fillMaxSize()) {
        Row(
            modifier = Modifier
                .fillMaxWidth()
                .padding(start = 18.dp, end = 18.dp, top = 6.dp, bottom = 6.dp),
            verticalAlignment = Alignment.CenterVertically,
        ) {
            Text(
                text = stringResource(R.string.MyPlaylists),
                fontSize = 21.sp,
                fontWeight = FontWeight.Bold,
                color = primary,
            )
            Box(
                modifier = Modifier
                    .padding(horizontal = 12.dp)
                    .size(width = 1.dp, height = 15.dp)
                    .background(primary.copy(alpha = 0.22f)),
            )
            Text(
                text = stringResource(R.string.PlaylistSubtitle),
                fontSize = 12.sp,
                color = secondary,
                maxLines = 1,
                overflow = TextOverflow.Ellipsis,
                modifier = Modifier.weight(1f),
            )
            Box(
                modifier = Modifier
                    .size(38.dp)
                    .glassCard(palette = palette, corner = 13)
                    .clickable(onClick = onToggleCreate),
                contentAlignment = Alignment.Center,
            ) {
                Icon(
                    painter = painterResource(if (creating) R.drawable.ic_close else R.drawable.ic_add),
                    contentDescription = stringResource(R.string.playlist_new),
                    tint = primary,
                    modifier = Modifier.size(19.dp),
                )
            }
        }

        if (creating) {
            LandscapeCreateRow(draft, onDraftChange, onCreate, primary, secondary)
        }

        Row(
            modifier = Modifier
                .fillMaxWidth()
                .weight(1f)
                .padding(start = 18.dp, end = 18.dp, top = 2.dp, bottom = PlaylistChromeReserve),
            horizontalArrangement = Arrangement.spacedBy(14.dp),
        ) {
            LandscapeCollectionCard(
                playlists = playlists,
                covers = covers,
                favoriteCount = favoriteCount,
                recentCount = recentCount,
                onOpenFavorites = onOpenFavorites,
                onOpenRecent = onOpenRecent,
                modifier = Modifier.weight(0.6f).fillMaxHeight(),
            )
            LandscapePlaylistShelf(
                count = playlists.size,
                modifier = Modifier.weight(1f).fillMaxHeight(),
            ) {
                if (playlists.isEmpty()) {
                    Column(
                        modifier = Modifier.fillMaxWidth().weight(1f),
                        verticalArrangement = Arrangement.Center,
                        horizontalAlignment = Alignment.CenterHorizontally,
                    ) {
                        Text(
                            text = stringResource(R.string.playlist_none),
                            fontSize = 11.5.sp,
                            color = secondary,
                        )
                        // 空面板不能只剩一句话：把新建动作摆在正中间，省得用户去找头部那颗 38dp 圆钮
                        Box(modifier = Modifier.padding(top = 12.dp)) {
                            PillLabel(text = stringResource(R.string.playlist_new), onClick = onToggleCreate)
                        }
                    }
                } else {
                    PlaylistRows(
                        playlists = playlists,
                        covers = covers,
                        editingId = editing,
                        draftName = draftName,
                        renameTaken = renameTaken,
                        onDraft = onRenameDraft,
                        onCancelRename = onCancelRename,
                        onSaveRename = onSaveRename,
                        onOpen = onOpen,
                        onDelete = onDelete,
                        onStartRename = onStartRename,
                        onCommitOrder = onCommitOrder,
                        compact = true,
                        modifier = Modifier.fillMaxWidth().weight(1f),
                    )
                }
            }
        }
    }
}

/** 横屏的「新建歌单」输入行：与竖屏同一套写法，只是压到一条 48dp 的窄轨里 */
@Composable
private fun LandscapeCreateRow(
    draft: String,
    onDraftChange: (String) -> Unit,
    onCreate: () -> Unit,
    primary: Color,
    secondary: Color,
) {
    val palette = LocalCatClawPalette.current
    Row(
        modifier = Modifier
            .fillMaxWidth()
            .padding(horizontal = 18.dp, vertical = 3.dp)
            .glassCard(palette = palette, corner = 18)
            .padding(horizontal = 12.dp, vertical = 2.dp),
        verticalAlignment = Alignment.CenterVertically,
    ) {
        TextField(
            value = draft,
            onValueChange = onDraftChange,
            placeholder = { Text(stringResource(R.string.playlist_name_hint), fontSize = 13.sp, color = secondary) },
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
            fontSize = 13.sp,
            fontWeight = FontWeight.SemiBold,
            color = Color(inkOn(palette.primary)),
            modifier = Modifier
                .clip(RoundedCornerShape(13.dp))
                .background(Color(palette.primary))
                .clickable(onClick = onCreate)
                .padding(horizontal = 14.dp, vertical = 8.dp),
        )
    }
}

/**
 * 左栏实图主卡：拿第一张有封面的歌单铺满，压渐变与玻璃徽标，底部是总数与两个虚拟入口。
 * 没有封面时退回玻璃 + 主色渐变，不出现空白方块。
 */
@Composable
private fun LandscapeCollectionCard(
    playlists: List<PlaylistEntity>,
    covers: Map<Int, PlaylistCover>,
    favoriteCount: Int,
    recentCount: Int,
    onOpenFavorites: () -> Unit,
    onOpenRecent: () -> Unit,
    modifier: Modifier = Modifier,
) {
    val palette = LocalCatClawPalette.current
    val ink = Color(palette.textPrimary)
    val hero = playlists.firstNotNullOfOrNull { covers[it.id] }
    Box(modifier = modifier.clip(RoundedCornerShape(24.dp))) {
        if (hero != null) {
            CoverTile(
                coverArtPath = hero.coverArtPath,
                audioPath = hero.filePath,
                maxPx = 640,
                modifier = Modifier.fillMaxSize(),
            ) {}
        } else {
            Box(Modifier.fillMaxSize().glassCard(palette = palette, corner = 24))
            Box(
                Modifier.fillMaxSize().background(
                    Brush.linearGradient(
                        listOf(Color(palette.primary).copy(alpha = 0.46f), Color.Transparent),
                    ),
                ),
            )
            // 一张自建歌单都没有时别留一块纯玻璃：压一个低对比的歌单水印
            Icon(
                painter = painterResource(R.drawable.ic_playlist),
                contentDescription = null,
                tint = ink.copy(alpha = 0.10f),
                modifier = Modifier.size(112.dp).align(Alignment.Center),
            )
        }
        Box(
            modifier = Modifier.fillMaxSize().background(
                Brush.verticalGradient(
                    listOf(
                        Color.Black.copy(alpha = 0.16f),
                        Color.Transparent,
                        Color.Black.copy(alpha = 0.42f),
                        Color.Black.copy(alpha = 0.90f),
                    ),
                ),
            ),
        )
        Text(
            text = stringResource(R.string.MyPlaylists),
            fontSize = 10.sp,
            color = ink,
            maxLines = 1,
            modifier = Modifier
                .padding(14.dp)
                .clip(RoundedCornerShape(50))
                .background(ink.copy(alpha = 0.16f))
                .border(1.dp, ink.copy(alpha = 0.26f), RoundedCornerShape(50))
                .padding(horizontal = 10.dp, vertical = 5.dp),
        )
        Column(
            modifier = Modifier.align(Alignment.BottomStart).fillMaxWidth().padding(14.dp),
        ) {
            Text(
                text = playlists.size.toString(),
                fontSize = 31.sp,
                fontWeight = FontWeight.Bold,
                color = ink,
            )
            Text(
                text = stringResource(R.string.playlist_created) + " · " +
                    stringResource(R.string.playlist_entry_favorites) + " " +
                    stringResource(R.string.playlist_count, favoriteCount),
                fontSize = 10.5.sp,
                color = ink.copy(alpha = 0.72f),
                maxLines = 1,
                overflow = TextOverflow.Ellipsis,
                modifier = Modifier.padding(top = 2.dp),
            )
            Row(
                modifier = Modifier.padding(top = 10.dp),
                horizontalArrangement = Arrangement.spacedBy(8.dp),
            ) {
                LandscapeMiniEntry(
                    R.drawable.ic_favorite,
                    stringResource(R.string.playlist_entry_favorites),
                    favoriteCount,
                    COLLECTION_BLUE,
                    Modifier.weight(1f),
                    onOpenFavorites,
                )
                LandscapeMiniEntry(
                    R.drawable.ic_history,
                    stringResource(R.string.playlist_entry_recent),
                    recentCount,
                    COLLECTION_AMBER,
                    Modifier.weight(1f),
                    onOpenRecent,
                )
            }
        }
    }
}

/** 主卡底部那两颗带计数的彩色入口：冷蓝 / 琥珀，与发现页四入口同一档饱和度 */
@Composable
private fun LandscapeMiniEntry(
    iconRes: Int,
    label: String,
    count: Int,
    accentArgb: Long,
    modifier: Modifier = Modifier,
    onClick: () -> Unit,
) {
    val ink = Color(LocalCatClawPalette.current.textPrimary)
    val accent = Color(accentArgb)
    val shape = RoundedCornerShape(13.dp)
    Row(
        modifier = modifier
            .height(44.dp)
            .clip(shape)
            .background(
                Brush.linearGradient(
                    listOf(accent.copy(alpha = 0.52f), accent.copy(alpha = 0.09f)),
                ),
            )
            .border(1.dp, ink.copy(alpha = 0.16f), shape)
            .clickable(onClick = onClick)
            .padding(horizontal = 9.dp),
        verticalAlignment = Alignment.CenterVertically,
        horizontalArrangement = Arrangement.spacedBy(7.dp),
    ) {
        Icon(
            painter = painterResource(iconRes),
            contentDescription = null,
            tint = ink,
            modifier = Modifier.size(16.dp),
        )
        Text(
            text = label,
            fontSize = 11.sp,
            fontWeight = FontWeight.SemiBold,
            color = ink,
            maxLines = 1,
            overflow = TextOverflow.Ellipsis,
            modifier = Modifier.weight(1f),
        )
        Text(
            text = stringResource(R.string.playlist_count, count),
            fontSize = 9.5.sp,
            color = ink.copy(alpha = 0.72f),
            maxLines = 1,
        )
    }
}

/** 右栏玻璃货架：标题一行 + 列表槽。列表仍是竖屏那份（含长按拖拽），只是行压矮 */
@Composable
private fun LandscapePlaylistShelf(
    count: Int,
    modifier: Modifier = Modifier,
    content: @Composable androidx.compose.foundation.layout.ColumnScope.() -> Unit,
) {
    val palette = LocalCatClawPalette.current
    Column(
        modifier = modifier
            .glassCard(palette = palette, corner = 24)
            .padding(horizontal = 10.dp, vertical = 4.dp),
    ) {
        Row(modifier = Modifier.fillMaxWidth(), verticalAlignment = Alignment.CenterVertically) {
            Text(
                text = stringResource(R.string.playlist_created) + " · " + count,
                fontSize = 11.5.sp,
                fontWeight = FontWeight.Bold,
                color = Color(palette.textPrimary),
            )
            Spacer(Modifier.weight(1f))
            if (count > 0) {
                Text(
                    text = stringResource(R.string.playlist_drag_hint),
                    fontSize = 10.sp,
                    color = Color(palette.textSecondary),
                    maxLines = 1,
                )
            }
        }
        content()
    }
}

/**
 * 「网易云歌单」横向货架：登录后在歌单 tab 顶部展示账号里已有的歌单。
 *
 * 用横向 LazyRow 而不是纵向铺开：账号里几十个歌单会把自建歌单挤出首屏，
 * 而这一区的定位是"快速取用"，不是主列表。
 */
@Composable
private fun NeteaseShelf(
    accountName: String,
    playlists: List<OnlinePlaylist>,
    onOpen: (OnlinePlaylist) -> Unit,
) {
    val palette = LocalCatClawPalette.current
    val primary = Color(palette.textPrimary)
    val secondary = Color(palette.textSecondary)
    Column(verticalArrangement = Arrangement.spacedBy(8.dp)) {
        Row(verticalAlignment = Alignment.CenterVertically) {
            Text(
                text = stringResource(R.string.netease_playlist_section),
                fontSize = 13.sp,
                fontWeight = FontWeight.SemiBold,
                color = primary,
            )
            if (accountName.isNotBlank()) {
                Text(
                    text = accountName,
                    fontSize = 11.sp,
                    color = secondary,
                    maxLines = 1,
                    overflow = TextOverflow.Ellipsis,
                    modifier = Modifier.padding(start = 8.dp).weight(1f),
                )
            }
        }
        LazyRow(
            horizontalArrangement = Arrangement.spacedBy(10.dp),
            contentPadding = PaddingValues(end = 4.dp),
        ) {
            items(playlists.size, key = { "nt-" + playlists[it].id }) { index ->
                val playlist = playlists[index]
                Column(
                    modifier = Modifier
                        .width(96.dp)
                        .clickable { onOpen(playlist) },
                ) {
                    CoverTile(
                        coverArtPath = playlist.coverUrl,
                        audioPath = null,
                        maxPx = 256,
                        modifier = Modifier
                            .size(96.dp)
                            .clip(RoundedCornerShape(14.dp))
                            .background(Color(palette.primary).copy(alpha = 0.18f)),
                    ) {
                        Text(
                            text = playlist.name.take(1),
                            fontSize = 24.sp,
                            fontWeight = FontWeight.Bold,
                            color = primary,
                        )
                    }
                    Spacer(Modifier.height(4.dp))
                    Text(
                        text = playlist.name,
                        fontSize = 11.5.sp,
                        color = primary,
                        maxLines = 1,
                        overflow = TextOverflow.Ellipsis,
                    )
                    Text(
                        text = stringResource(R.string.netease_songs_unit, playlist.songCount),
                        fontSize = 9.5.sp,
                        color = secondary,
                    )
                }
            }
        }
    }
}

/**
 * 竖屏 / 横屏共用的一份歌单列表：长按拖拽排序、行内改名、打开与删除都在这里，
 * 只是横屏用 compact 把行压到 52dp、并把底部留白从 96dp 收到 2dp（舞台外已留了 80dp）。
 */
@Composable
private fun PlaylistRows(
    playlists: List<PlaylistEntity>,
    covers: Map<Int, PlaylistCover>,
    editingId: Int?,
    draftName: String,
    renameTaken: Boolean,
    onDraft: (String) -> Unit,
    onCancelRename: () -> Unit,
    onSaveRename: (Int) -> Unit,
    onOpen: (Int) -> Unit,
    onDelete: (Int) -> Unit,
    onStartRename: (Int, String) -> Unit,
    onCommitOrder: (List<Int>) -> Unit,
    modifier: Modifier = Modifier,
    compact: Boolean = false,
) {
    val listState = rememberLazyListState()
    val haptics = LocalHapticFeedback.current
    var draggingId by remember { mutableStateOf<Int?>(null) }
    var dragOffset by remember { mutableFloatStateOf(0f) }
    var dragOrder by remember { mutableStateOf<List<PlaylistEntity>?>(null) }
    val shownPlaylists = dragOrder ?: playlists

    LazyColumn(
        state = listState,
        modifier = modifier.fillMaxSize(),
        contentPadding = PaddingValues(bottom = if (compact) 2.dp else 96.dp),
    ) {
        itemsIndexed(shownPlaylists, key = { index, p -> "pl-$index-${p.id}" }) { _, playlist ->
            if (editingId == playlist.id) {
                RenameRow(
                    draft = draftName,
                    taken = renameTaken,
                    onDraft = onDraft,
                    onCancel = onCancelRename,
                    onSave = { onSaveRename(playlist.id) },
                )
            } else {
                val isDragging = draggingId == playlist.id
                PlaylistCard(
                    playlist = playlist,
                    cover = covers[playlist.id],
                    compact = compact,
                    // 手势的键里**不能**放列表本身：拖拽中每次换位都会重组，
                    // 键一变 pointerInput 就重启，手势当场断掉
                    modifier = Modifier
                        .graphicsLayer {
                            if (isDragging) {
                                translationY = dragOffset
                                scaleX = 1.02f
                                scaleY = 1.02f
                                alpha = 0.94f
                            }
                        }
                        .pointerInput(playlist.id, playlist.isSystem) {
                            if (playlist.isSystem) return@pointerInput
                            detectDragGesturesAfterLongPress(
                                onDragStart = {
                                    haptics.performHapticFeedback(HapticFeedbackType.LongPress)
                                    draggingId = playlist.id
                                    dragOrder = playlists
                                    dragOffset = 0f
                                },
                                onDrag = { change, delta ->
                                    change.consume()
                                    dragOffset += delta.y
                                    val list = dragOrder
                                    val from = list?.indexOfFirst { it.id == draggingId } ?: -1
                                    val rowHeight = if (from >= 0) {
                                        listState.layoutInfo.visibleItemsInfo
                                            .firstOrNull { it.index == from }?.size ?: 0
                                    } else {
                                        0
                                    }
                                    if (list != null && from >= 0 && rowHeight > 0) {
                                        // 越过半行就与邻居换位，并把累计位移减掉一行
                                        if (dragOffset > rowHeight / 2f && from < list.lastIndex && !list[from + 1].isSystem) {
                                            dragOrder = list.toMutableList().apply { add(from + 1, removeAt(from)) }
                                            dragOffset -= rowHeight
                                        } else if (dragOffset < -rowHeight / 2f && from > 0 && !list[from - 1].isSystem) {
                                            dragOrder = list.toMutableList().apply { add(from - 1, removeAt(from)) }
                                            dragOffset += rowHeight
                                        }
                                    }
                                },
                                onDragEnd = {
                                    // 松手按最终顺序一次性落库
                                    dragOrder?.let { onCommitOrder(it.map { p -> p.id }) }
                                    draggingId = null
                                    dragOrder = null
                                    dragOffset = 0f
                                },
                                onDragCancel = {
                                    draggingId = null
                                    dragOrder = null
                                    dragOffset = 0f
                                },
                            )
                        },
                    onOpen = { onOpen(playlist.id) },
                    onDelete = { onDelete(playlist.id) },
                    onRename = { onStartRename(playlist.id, playlist.name) },
                )
            }
        }
    }
}

/**
 * 歌单详情的歌曲行：横竖屏共用一份，长按菜单由页面托管（菜单里带「从歌单移除」）。
 * 只有行高按 compact 收一档 —— 横屏 411dp 的画布要尽量多露几行。
 */
@Composable
private fun PlaylistSongRows(
    rows: List<SongListItem>,
    query: String,
    compact: Boolean,
    primary: Color,
    secondary: Color,
    onPlay: (Int) -> Unit,
    onOpenMenu: (SongMenuTarget) -> Unit,
) {
    val palette = LocalCatClawPalette.current
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
            val index = rows.indexOf(song)
            PlaylistSongRowUi(
                song = PlaylistSongUi(
                    key = song.id,
                    title = song.title,
                    artist = song.artist.orEmpty(),
                    album = song.album.orEmpty(),
                    cover = song.coverArtPath,
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
