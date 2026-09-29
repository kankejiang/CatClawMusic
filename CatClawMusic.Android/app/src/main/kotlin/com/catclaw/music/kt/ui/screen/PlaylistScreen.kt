package com.catclaw.music.kt.ui.screen

import androidx.activity.compose.BackHandler
import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.clickable
import androidx.compose.foundation.combinedClickable
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.PaddingValues
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.heightIn
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.items
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material3.Icon
import androidx.compose.material3.Text
import androidx.compose.material3.TextField
import androidx.compose.material3.TextFieldDefaults
import androidx.compose.runtime.Composable
import androidx.compose.runtime.remember
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.saveable.rememberSaveable
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
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
import com.catclaw.music.kt.ui.player.CoverTile
import com.catclaw.music.kt.data.db.toSong
import com.catclaw.music.kt.ui.library.LibraryViewModel
import com.catclaw.music.kt.ui.song.SongBelongs
import com.catclaw.music.kt.ui.song.SongDetailScreen
import com.catclaw.music.kt.ui.song.SongList
import com.catclaw.music.kt.ui.song.SongMenuFor
import com.catclaw.music.kt.ui.song.SongMenuTarget
import com.catclaw.music.kt.ui.song.rememberSongMenu
import com.catclaw.music.kt.ui.song.songMenuSubtitle
import com.catclaw.music.kt.data.db.entity.PlaylistEntity
import com.catclaw.music.kt.ui.playlist.PlaylistViewModel
import com.catclaw.music.kt.ui.theme.LocalCatClawPalette
import com.catclaw.music.kt.ui.theme.BackTile
import com.catclaw.music.kt.ui.theme.inkOn
import com.catclaw.music.kt.ui.theme.EmptyState
import com.catclaw.music.kt.ui.theme.glassCard
import com.catclaw.music.kt.ui.theme.highlightOnGlassCard

/**
 * 歌单 tab —— 照 MAUI `PlaylistPage.xaml`：Hero 卡（标题/副标题 + 右上 44dp 玻璃加号）、
 * 系统歌单与自建歌单各是一张带 46dp 图标块的玻璃卡、详情层是返回块 + 两个胶囊按钮 + 歌曲行。
 */
@Composable
fun PlaylistScreen(viewModel: PlaylistViewModel = hiltViewModel()) {
    val palette = LocalCatClawPalette.current
    val playlists by viewModel.playlists.collectAsStateWithLifecycle()
    val items by viewModel.items.collectAsStateWithLifecycle()
    val openId by viewModel.openId.collectAsStateWithLifecycle()
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
        SongDetailScreen(songId = id, load = { libraryViewModel.songDetail(it) }, onBack = { songDetailId = null })
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
            val favorites by libraryViewModel.favorites.collectAsStateWithLifecycle()
            val recent by libraryViewModel.recent.collectAsStateWithLifecycle()
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

            if (playlists.isEmpty() && !creating) {
                EmptyState(hint = stringResource(R.string.playlist_none), iconRes = R.drawable.ic_playlist)
            }
            LazyColumn(modifier = Modifier.fillMaxSize(), contentPadding = PaddingValues(bottom = 96.dp)) {
                items(playlists, key = { it.id }) { playlist ->
                    if (editing == playlist.id) {
                        RenameRow(
                            draft = draftName,
                            taken = renameTaken,
                            onDraft = { draftName = it; renameTaken = false },
                            onCancel = { editing = null },
                            onSave = {
                                // 改成才收框：同名/空名时留着编辑框并给原因，不然用户以为改好了
                                if (viewModel.rename(playlist.id, draftName)) editing = null else renameTaken = true
                            },
                        )
                    } else {
                        PlaylistCard(
                            playlist = playlist,
                            onOpen = { viewModel.open(playlist.id) },
                            onDelete = { viewModel.delete(playlist.id) },
                            onRename = {
                                draftName = playlist.name
                                renameTaken = false
                                editing = playlist.id
                            },
                        )
                    }
                }
            }
        } else {
            // 详情层：返回块 + 标题 + 计数
            Row(verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(12.dp)) {
                BackTile(onBack = { viewModel.close() }, description = stringResource(R.string.GoBack))
                Column(modifier = Modifier.weight(1f), verticalArrangement = Arrangement.spacedBy(2.dp)) {
                    Text(text = open.name, fontSize = 22.sp, fontWeight = FontWeight.Bold, color = primary, maxLines = 1, overflow = TextOverflow.Ellipsis)
                    Text(text = stringResource(R.string.playlist_count, open.songCount), fontSize = 12.sp, color = secondary)
                }
            }
            Spacer(Modifier.height(12.dp))
            Row(horizontalArrangement = Arrangement.spacedBy(10.dp)) {
                Row(
                    modifier = Modifier
                        .clip(RoundedCornerShape(16.dp))
                        .background(Color(palette.primary))
                        .clickable { viewModel.playAll() }
                        .padding(horizontal = 16.dp, vertical = 10.dp),
                    verticalAlignment = Alignment.CenterVertically,
                    horizontalArrangement = Arrangement.spacedBy(8.dp),
                ) {
                    Icon(
                        painter = painterResource(R.drawable.ic_play),
                        contentDescription = null,
                        tint = Color(inkOn(palette.primary)),
                        modifier = Modifier.size(16.dp),
                    )
                    Text(stringResource(R.string.playlist_play_all), fontSize = 14.sp, fontWeight = FontWeight.SemiBold, color = Color(inkOn(palette.primary)))
                }
                Text(
                    text = stringResource(R.string.playlist_add_current),
                    fontSize = 14.sp,
                    fontWeight = FontWeight.SemiBold,
                    color = secondary,
                    modifier = Modifier
                        .clip(RoundedCornerShape(16.dp))
                        .border(1.dp, Color(palette.textPrimary).copy(alpha = 0.16f), RoundedCornerShape(16.dp))
                        .clickable { viewModel.addCurrentSong() }
                        .padding(horizontal = 16.dp, vertical = 10.dp),
                )
            }
            Spacer(Modifier.height(10.dp))
            if (items.isEmpty()) {
                EmptyState(hint = stringResource(R.string.playlist_empty), iconRes = R.drawable.ic_add)
            }
            LazyColumn(modifier = Modifier.fillMaxSize(), contentPadding = PaddingValues(bottom = 96.dp)) {
                items(items, key = { it.id }) { song ->
                    val index = items.indexOf(song)
                    Row(
                        modifier = Modifier
                            .fillMaxWidth()
                            .padding(vertical = 4.dp)
                            .clip(RoundedCornerShape(18.dp))
                            .combinedClickable(
                                onClick = { viewModel.playAt(index) },
                                onLongClick = {
                                    rowMenu = SongMenuTarget(
                                        id = song.id,
                                        title = song.title,
                                        subtitle = songMenuSubtitle(song.artist, song.album),
                                        song = song.toSong(),
                                    )
                                },
                            )
                            .padding(horizontal = 12.dp, vertical = 10.dp),
                        verticalAlignment = Alignment.CenterVertically,
                        horizontalArrangement = Arrangement.spacedBy(12.dp),
                    ) {
                        CoverTile(
                            coverArtPath = song.coverArtPath,
                            audioPath = song.filePath,
                            modifier = Modifier
                                .size(40.dp)
                                .clip(RoundedCornerShape(12.dp))
                                .background(Color(palette.textPrimary).copy(alpha = 0.10f)),
                        ) {
                            Text(text = (index + 1).toString(), fontSize = 14.sp, fontWeight = FontWeight.SemiBold, color = secondary)
                        }
                        Column(modifier = Modifier.weight(1f), verticalArrangement = Arrangement.spacedBy(2.dp)) {
                            Text(text = song.title, fontSize = 14.sp, fontWeight = FontWeight.SemiBold, color = primary, maxLines = 1, overflow = TextOverflow.Ellipsis)
                            Text(text = song.artist.orEmpty(), fontSize = 12.sp, color = secondary, maxLines = 1, overflow = TextOverflow.Ellipsis)
                        }
                    }
                }
            }
        }
    }
    rowMenu?.let { target -> SongMenuFor(target = target, menu = detailMenu, onDismiss = { rowMenu = null }) }
}

/** 一张歌单卡 —— MAUI 行：46dp 图标块（系统单用 ic_favorite/ic_history，自建单用首字）+ 名称 + 计数 + 44dp 改名 / 删除 */
@Composable
private fun PlaylistCard(
    playlist: PlaylistEntity,
    onOpen: () -> Unit,
    onDelete: () -> Unit,
    onRename: () -> Unit,
) {
    val palette = LocalCatClawPalette.current
    val primary = Color(palette.textPrimary)
    val secondary = Color(palette.textSecondary)
    Row(
        modifier = Modifier
            .fillMaxWidth()
            .padding(vertical = 5.dp)
            .glassCard(palette = palette, corner = 22)
            .clickable(onClick = onOpen)
            .padding(horizontal = 12.dp, vertical = 10.dp),
        verticalAlignment = Alignment.CenterVertically,
        horizontalArrangement = Arrangement.spacedBy(12.dp),
    ) {
        Box(
            modifier = Modifier
                .size(46.dp)
                .clip(RoundedCornerShape(14.dp))
                .background(
                    if (playlist.isSystem) Color(palette.primary).copy(alpha = 0.22f)
                    else Color(palette.textPrimary).copy(alpha = 0.12f),
                ),
            contentAlignment = Alignment.Center,
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
            Text(text = playlist.name, fontSize = 15.sp, fontWeight = FontWeight.SemiBold, color = primary, maxLines = 1, overflow = TextOverflow.Ellipsis)
            Text(text = stringResource(R.string.playlist_count, playlist.songCount), fontSize = 12.sp, color = secondary)
        }
        if (!playlist.isSystem) {
            // MAUI 是 ⋮ 菜单里两项（重命名 / 删除）；这里摊成两个 44dp 按钮，
            // 少一层弹窗、也不用在一行里再嵌一个弹层去管它的生命周期
            CardAction(R.drawable.ic_edit, stringResource(R.string.playlist_rename), onRename)
            CardAction(R.drawable.ic_close, stringResource(R.string.Delete), onDelete)
        }
    }
}

/** 行尾那个 44×44 的图标按钮。尺寸与触达面走全 app 同一档 */
@Composable
private fun CardAction(iconRes: Int, description: String, onClick: () -> Unit) {
    val palette = LocalCatClawPalette.current
    Box(
        modifier = Modifier
            .size(44.dp)
            .clip(RoundedCornerShape(14.dp))
            .clickable(onClickLabel = description, onClick = onClick),
        contentAlignment = Alignment.Center,
    ) {
        Icon(
            painter = painterResource(iconRes),
            contentDescription = description,
            tint = Color(palette.textSecondary),
            modifier = Modifier.size(18.dp),
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
    val palette = LocalCatClawPalette.current
    val favorites by viewModel.favorites.collectAsStateWithLifecycle()
    val recent by viewModel.recent.collectAsStateWithLifecycle()
    val isFavorites = kind == PlaylistEntry.Favorites
    val rows = if (isFavorites) favorites else recent

    Column(modifier = Modifier.fillMaxSize()) {
        Row(
            modifier = Modifier.fillMaxWidth().padding(start = 16.dp, end = 16.dp, top = 14.dp, bottom = 10.dp),
            verticalAlignment = Alignment.CenterVertically,
            horizontalArrangement = Arrangement.spacedBy(12.dp),
        ) {
            BackTile(onBack = onBack, description = stringResource(R.string.queue_back))
            Column(modifier = Modifier.weight(1f)) {
                Text(
                    text = stringResource(
                        if (isFavorites) R.string.playlist_entry_favorites else R.string.playlist_entry_recent,
                    ),
                    fontSize = 22.sp,
                    fontWeight = FontWeight.Bold,
                    color = Color(palette.textPrimary),
                    maxLines = 1,
                    overflow = TextOverflow.Ellipsis,
                )
                Text(
                    text = stringResource(R.string.playlist_count, rows.size),
                    fontSize = 12.sp,
                    color = Color(palette.textSecondary),
                )
            }
        }
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
