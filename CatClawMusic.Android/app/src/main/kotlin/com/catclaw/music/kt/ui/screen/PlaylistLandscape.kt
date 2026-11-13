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

/** 歌单页横屏舞台（从 PlaylistScreen.kt 拆出） */
@Composable
internal fun LandscapePlaylistStage(
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
internal fun LandscapeCreateRow(
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
internal fun LandscapeCollectionCard(
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
internal fun LandscapeMiniEntry(
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
internal fun LandscapePlaylistShelf(
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
