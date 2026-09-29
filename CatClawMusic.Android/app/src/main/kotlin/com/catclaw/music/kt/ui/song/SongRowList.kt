package com.catclaw.music.kt.ui.song

import androidx.compose.foundation.background
import androidx.compose.foundation.combinedClickable
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.PaddingValues
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.itemsIndexed
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import androidx.lifecycle.compose.collectAsStateWithLifecycle
import com.catclaw.music.kt.R
import com.catclaw.music.kt.core.model.Song
import com.catclaw.music.kt.data.db.SongDetailRow
import com.catclaw.music.kt.data.db.SongListItem
import com.catclaw.music.kt.data.db.toSong
import com.catclaw.music.kt.ui.library.LibraryViewModel
import com.catclaw.music.kt.ui.theme.EmptyState
import com.catclaw.music.kt.ui.theme.LocalCatClawPalette
import com.catclaw.music.kt.ui.theme.glassCard

/**
 * 长按菜单的能力由页面注入：列表只管弹菜单，不知道收藏怎么存、歌单从哪来。
 * 这样音乐库页与歌单页可以共用同一份行组件，而不必共享同一个 ViewModel。
 */
class SongMenuActions(
    val playlists: List<Pair<Int, String>>,
    val favoriteIds: Set<Int>,
    val onToggleFavorite: (Int) -> Unit,
    val onAddToPlaylist: (Int, Int) -> Unit,
    /** 插到当前曲目之后 */
    val onPlayNext: (Song) -> Unit,
    /** 详情页要读的那一行；null 表示这首歌已从库里删掉 */
    val loadDetail: suspend (Int) -> SongDetailRow?,
    /**
     * 打开详情页。由**页面**而不是列表自己托管 —— 详情是整页，
     * 在列表内部渲染会叠在列表页的页头下面，出现两个返回键。
     */
    val onOpenDetail: (Int) -> Unit,
)

@Composable
fun rememberSongMenu(viewModel: LibraryViewModel, onOpenDetail: (Int) -> Unit): SongMenuActions {
    val playlists by viewModel.playlists.collectAsStateWithLifecycle()
    val favorites by viewModel.favorites.collectAsStateWithLifecycle()
    return SongMenuActions(
        playlists = playlists.map { it.id to it.name },
        favoriteIds = remember(favorites) { favorites.map { it.id }.toSet() },
        onToggleFavorite = viewModel::toggleFavorite,
        onAddToPlaylist = viewModel::addToPlaylist,
        onPlayNext = viewModel::playNext,
        loadDetail = viewModel::songDetail,
        onOpenDetail = onOpenDetail,
    )
}

/**
 * 一首歌一行的列表：点=从该位置起播这张列表，长按=菜单。
 * 序号方块同时是收藏标记（已收藏的底色更实），因为这几页的行里没有别的地方放 ♥。
 */
@Composable
fun SongList(
    rows: List<SongListItem>,
    subtitleOf: (SongListItem) -> String,
    menu: SongMenuActions,
    emptyHint: String,
    onPlay: (Int) -> Unit,
) {
    val palette = LocalCatClawPalette.current
    var target by remember { mutableStateOf<SongMenuTarget?>(null) }
    if (rows.isEmpty()) {
        Box(modifier = Modifier.fillMaxSize().padding(horizontal = 22.dp), contentAlignment = Alignment.Center) {
            EmptyState(hint = emptyHint, iconRes = R.drawable.ic_library)
        }
        return
    }
    LazyColumn(
        modifier = Modifier.fillMaxSize(),
        contentPadding = PaddingValues(start = 16.dp, end = 16.dp, bottom = 100.dp),
        verticalArrangement = Arrangement.spacedBy(8.dp),
    ) {
        itemsIndexed(rows) { index, row ->
            val subtitle = subtitleOf(row)
            Row(
                modifier = Modifier
                    .fillMaxWidth()
                    .glassCard(palette = palette, corner = 22)
                    .combinedClickable(
                        onClick = { onPlay(index) },
                        onLongClick = { target = SongMenuTarget(row.id, row.title, subtitle, row.toSong()) },
                    )
                    .padding(horizontal = 10.dp, vertical = 10.dp),
                verticalAlignment = Alignment.CenterVertically,
                horizontalArrangement = Arrangement.spacedBy(12.dp),
            ) {
                Box(
                    modifier = Modifier
                        .size(40.dp)
                        .clip(RoundedCornerShape(13.dp))
                        .background(
                            if (row.id in menu.favoriteIds) Color(palette.primary).copy(alpha = 0.55f)
                            else Color(palette.primary).copy(alpha = 0.22f),
                        ),
                    contentAlignment = Alignment.Center,
                ) {
                    Text(
                        text = (index + 1).toString(),
                        fontSize = 13.sp,
                        fontWeight = FontWeight.Bold,
                        color = Color(palette.textPrimary),
                    )
                }
                Column(modifier = Modifier.weight(1f)) {
                    Text(
                        text = row.title,
                        style = MaterialTheme.typography.bodyMedium,
                        color = Color(palette.textPrimary),
                        maxLines = 1,
                        overflow = TextOverflow.Ellipsis,
                    )
                    Text(
                        text = subtitle,
                        style = MaterialTheme.typography.bodySmall,
                        color = Color(palette.textSecondary),
                        maxLines = 1,
                        overflow = TextOverflow.Ellipsis,
                    )
                }
            }
        }
    }
    target?.let { song ->
        val context = LocalContext.current
        // 能不能分享在弹菜单这一刻判一次：判不出可分享地址就不画这一项
        val shareUri = shareUriFor(context, song.song.filePath)
        SongActionMenu(
            target = song,
            isFavorite = song.id in menu.favoriteIds,
            playlists = menu.playlists,
            onToggleFavorite = { menu.onToggleFavorite(song.id) },
            onAddToPlaylist = { playlistId -> menu.onAddToPlaylist(playlistId, song.id) },
            onPlayNext = { menu.onPlayNext(song.song) },
            onShare = if (shareUri == null) {
                null
            } else {
                { openShareSheet(context, shareUri, song.title, song.song.artist) }
            },
            onOpenDetail = { menu.onOpenDetail(song.id) },
            onDismiss = { target = null },
        )
    }
}
