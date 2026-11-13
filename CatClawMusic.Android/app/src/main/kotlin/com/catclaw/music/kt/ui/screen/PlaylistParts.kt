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

/** 歌单列表的卡片/改名行/入口页（从 PlaylistScreen.kt 拆出） */
@Composable
internal fun PlaylistCard(
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
internal fun CardAction(iconRes: Int, description: String, onClick: () -> Unit, compact: Boolean = false) {
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
internal fun RenameRow(
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
internal fun EntryCard(
    iconRes: Int,
    label: String,
    count: Int,
    modifier: Modifier = Modifier,
    onClick: () -> Unit,
) {
    val palette = LocalCatClawPalette.current
    val ink = Color(palette.textPrimary)
    val sub = Color(palette.textSecondary)
    // 横排紧凑卡：圆底图标 + 标题/数量 + 箭头（原来是竖排灰块，占地且廉价）
    Row(
        modifier = modifier
            .glassCard(palette = palette, corner = 20)
            .clickable(onClick = onClick)
            .padding(horizontal = 14.dp, vertical = 13.dp),
        verticalAlignment = Alignment.CenterVertically,
        horizontalArrangement = Arrangement.spacedBy(12.dp),
    ) {
        Box(
            modifier = Modifier
                .size(40.dp)
                .clip(CircleShape)
                .background(Color(palette.primary).copy(alpha = 0.20f)),
            contentAlignment = Alignment.Center,
        ) {
            Icon(
                painter = painterResource(iconRes),
                contentDescription = null,
                tint = Color(palette.primary),
                modifier = Modifier.size(20.dp),
            )
        }
        Column(Modifier.weight(1f), verticalArrangement = Arrangement.spacedBy(2.dp)) {
            Text(
                text = label,
                fontSize = 15.sp,
                fontWeight = FontWeight.SemiBold,
                color = ink,
                maxLines = 1,
                overflow = TextOverflow.Ellipsis,
            )
            Text(
                text = stringResource(R.string.playlist_count, count),
                fontSize = 11.5.sp,
                color = sub,
            )
        }
        Text(text = "›", fontSize = 20.sp, color = sub.copy(alpha = 0.65f))
    }
}

/** 入口点进去的列表。两张列表共用同一份行组件，所以长按菜单、收藏标记、封面规则都一致 */
@Composable
internal fun PlaylistEntryScreen(
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
        cover = rows.firstOrNull()?.let {
            CollectionCover(coverArtPath = it.coverArtPath, audioPath = it.filePath, fallback = "")
        },
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

internal const val COLLECTION_BLUE = 0xFF7FA6D8L
internal const val COLLECTION_AMBER = 0xFFD8AA6AL
/** 迷你条浮在底部（60 高 + 12 下边距 + 呼吸） */
internal val PlaylistChromeReserve = 80.dp
