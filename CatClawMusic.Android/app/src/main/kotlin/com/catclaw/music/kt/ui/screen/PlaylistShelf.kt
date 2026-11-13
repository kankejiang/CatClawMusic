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

/** 网易云歌单货架（从 PlaylistScreen.kt 拆出） */
@Composable
internal fun NeteaseShelf(
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
                fontSize = 15.sp,
                fontWeight = FontWeight.Bold,
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
                        .width(132.dp)
                        .clickable { onOpen(playlist) },
                ) {
                    CoverTile(
                        coverArtPath = playlist.coverUrl,
                        audioPath = null,
                        maxPx = 320,
                        modifier = Modifier
                            .size(132.dp)
                            .clip(RoundedCornerShape(16.dp))
                            .background(Color(palette.primary).copy(alpha = 0.18f)),
                    ) {
                        Text(
                            text = playlist.name.take(1),
                            fontSize = 34.sp,
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
internal fun PlaylistRows(
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
