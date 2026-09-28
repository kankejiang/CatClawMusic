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
import com.catclaw.music.kt.ui.theme.LocalCatClawPalette
import com.catclaw.music.kt.ui.theme.PillLabel

/** 菜单要对哪首歌动手：id 用来查收藏与写库，两行文字用来说给用户听 */
data class SongMenuTarget(val id: Int, val title: String, val subtitle: String)

/**
 * 歌曲长按菜单 —— MAUI `SongContextMenu` 的安卓对应物。
 *
 * MAUI 那份是五项：播放 / 下一首播放 / 收藏 / 添加到歌单 / 歌曲信息。这里只放**本端做得到**的两项，
 * 因为另外三项今天点了没反应：
 * - 「下一首播放」要播放队列支持定点插入，`PlayerController` 没有这个口子；
 * - 「歌曲信息」是 `SongDetailPage`，本端没有这首歌的详情数据面；
 * - 「播放」和点一行重复。
 * 摆三个点了不动的项，比只摆两个能用的更糟。
 */
@Composable
fun SongActionMenu(
    target: SongMenuTarget,
    isFavorite: Boolean,
    playlists: List<Pair<Int, String>>,
    onToggleFavorite: () -> Unit,
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
