package com.catclaw.music.kt.ui.song

import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.material3.AlertDialog
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.unit.dp
import com.catclaw.music.kt.R
import com.catclaw.music.kt.core.model.Song
import com.catclaw.music.kt.ui.theme.LocalCatClawPalette
import com.catclaw.music.kt.ui.theme.PillLabel
import kotlinx.coroutines.launch

/**
 * 菜单要对哪首歌动手：id 用来查收藏与写库，两行文字用来说给用户听，
 * [song] 是给「下一首播放」直接插队列用的（队列要的是 Song，不是 id）。
 */
data class SongMenuTarget(val id: Int, val title: String, val subtitle: String, val song: Song)

/** 这首歌"属于谁"——菜单要跳去的那一页；由各页映射成自己的下钻类型 */
enum class SongBelongsKind { Artist, Album }

data class SongBelongs(val kind: SongBelongsKind, val id: Int, val name: String)

/**
 * 歌曲长按菜单 —— MAUI `SongContextMenu` 与播放页「更多」的安卓对应物。
 *
 * MAUI 的菜单是五项：播放 / 下一首播放 / 收藏 / 添加到歌单 / 歌曲信息，
 * 播放页的「更多」另有查看歌手 / 查看专辑 / 分享。这里合成一个菜单。
 * 「播放」不放——它和点一行重复。
 * 三个可选项传 null 就不画：摆一个点了不动的项，比少摆一项更糟。
 */
@Composable
fun SongActionMenu(
    target: SongMenuTarget,
    isFavorite: Boolean,
    playlists: List<Pair<Int, String>>,
    onToggleFavorite: () -> Unit,
    onOpenDetail: () -> Unit,
    onPlayNext: () -> Unit,
    /** 为 null 表示这首歌给不出一个对方真能读到的地址 —— 那就**不画这一项** */
    onShare: (() -> Unit)?,
    onOpenArtist: (() -> Unit)?,
    onOpenAlbum: (() -> Unit)?,
    onAddToPlaylist: (Int) -> Unit,
    onDismiss: () -> Unit,
) {
    val palette = LocalCatClawPalette.current
    AlertDialog(
        onDismissRequest = onDismiss,
        containerColor = Color(palette.backgroundStops.last().argb),
        title = { Text(text = target.title, color = Color(palette.textPrimary)) },
        text = {
            Column(verticalArrangement = Arrangement.spacedBy(10.dp)) {
                Text(
                    text = target.subtitle,
                    style = MaterialTheme.typography.bodySmall,
                    color = Color(palette.textSecondary),
                )
                PillLabel(
                    text = stringResource(if (isFavorite) R.string.favorite_remove else R.string.favorite_add),
                    onClick = {
                        onToggleFavorite()
                        onDismiss()
                    },
                    filled = false,
                )
                PillLabel(
                    text = stringResource(R.string.song_menu_play_next),
                    onClick = {
                        onPlayNext()
                        onDismiss()
                    },
                    filled = false,
                )
                PillLabel(
                    text = stringResource(R.string.song_menu_detail),
                    onClick = {
                        onOpenDetail()
                        onDismiss()
                    },
                    filled = false,
                )
                onShare?.let { share ->
                    PillLabel(
                        text = stringResource(R.string.song_menu_share),
                        onClick = {
                            share()
                            onDismiss()
                        },
                        filled = false,
                    )
                }
                onOpenArtist?.let { open ->
                    PillLabel(
                        text = stringResource(R.string.song_menu_open_artist),
                        onClick = {
                            open()
                            onDismiss()
                        },
                        filled = false,
                    )
                }
                onOpenAlbum?.let { open ->
                    PillLabel(
                        text = stringResource(R.string.song_menu_open_album),
                        onClick = {
                            open()
                            onDismiss()
                        },
                        filled = false,
                    )
                }
                Text(
                    text = stringResource(R.string.song_menu_add_to_playlist),
                    style = MaterialTheme.typography.labelMedium,
                    color = Color(palette.textSecondary),
                )
                if (playlists.isEmpty()) {
                    Text(
                        text = stringResource(R.string.song_menu_no_playlist),
                        style = MaterialTheme.typography.bodySmall,
                        color = Color(palette.textSecondary),
                    )
                } else {
                    playlists.forEach { (playlistId, name) ->
                        PillLabel(
                            text = name,
                            onClick = {
                                onAddToPlaylist(playlistId)
                                onDismiss()
                            },
                            filled = false,
                        )
                    }
                }
            }
        },
        confirmButton = {
            PillLabel(text = stringResource(R.string.song_menu_close), onClick = onDismiss, filled = false)
        },
    )
}

/**
 * 「手里已经有一首歌 → 弹出该给的菜单」这一段，列表页和播放页都要用，所以连带的两处判断一起收在这儿：
 * - 分享：这一刻判一次能不能给出对方真能读到的地址，判不出就不画这一项；
 * - 歌手/专辑：行里只有名字没有 id，点下去才现读一次详情行（为这两项给每条列表 SQL 加两列不划算）。
 *
 * 解析跑在 [SongMenuActions.scope]（ViewModel 的 scope）而不是这里 `rememberCoroutineScope()`：
 * 菜单项点完立刻 dismiss，菜单自己的 scope 当场被取消，真机上表现成「点了没反应」。
 */
@Composable
fun SongMenuFor(target: SongMenuTarget, menu: SongMenuActions, onDismiss: () -> Unit) {
    val context = LocalContext.current
    val shareUri = shareUriFor(context, target.song.filePath)
    val belongs = menu.onOpenBelongs

    fun openBelongs(kind: SongBelongsKind, name: String) {
        if (belongs == null) return
        menu.scope.launch {
            val detail = menu.loadDetail(target.id) ?: return@launch
            val id = if (kind == SongBelongsKind.Artist) detail.artistId else detail.albumId
            if (id > 0) belongs(SongBelongs(kind, id, name))
        }
    }

    SongActionMenu(
        target = target,
        isFavorite = target.id in menu.favoriteIds,
        playlists = menu.playlists,
        onToggleFavorite = { menu.onToggleFavorite(target.id) },
        onAddToPlaylist = { playlistId -> menu.onAddToPlaylist(playlistId, target.id) },
        onPlayNext = { menu.onPlayNext(target.song) },
        onShare = if (shareUri == null) {
            null
        } else {
            { openShareSheet(context, shareUri, target.title, target.song.artist) }
        },
        onOpenArtist = if (belongs != null && target.song.artist.isNotBlank()) {
            { openBelongs(SongBelongsKind.Artist, target.song.artist) }
        } else {
            null
        },
        onOpenAlbum = if (belongs != null && target.song.album.isNotBlank()) {
            { openBelongs(SongBelongsKind.Album, target.song.album) }
        } else {
            null
        },
        onOpenDetail = { menu.onOpenDetail(target.id) },
        onDismiss = onDismiss,
    )
}
