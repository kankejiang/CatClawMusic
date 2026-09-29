package com.catclaw.music.kt.ui.song

import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.material3.AlertDialog
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.unit.dp
import com.catclaw.music.kt.R
import com.catclaw.music.kt.core.model.Song
import com.catclaw.music.kt.ui.theme.LocalCatClawPalette
import com.catclaw.music.kt.ui.theme.PillLabel

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
