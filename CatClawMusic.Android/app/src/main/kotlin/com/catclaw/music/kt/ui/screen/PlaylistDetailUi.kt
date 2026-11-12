package com.catclaw.music.kt.ui.screen

import androidx.compose.foundation.background
import androidx.compose.foundation.ExperimentalFoundationApi
import androidx.compose.foundation.clickable
import androidx.compose.foundation.combinedClickable
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.RowScope
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material3.Icon
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.res.painterResource
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import com.catclaw.music.kt.R
import com.catclaw.music.kt.ui.player.CoverTile
import com.catclaw.music.kt.ui.theme.LocalCatClawPalette
import com.catclaw.music.kt.ui.theme.PillLabel

/**
 * 歌单详情统一 UI（在线歌单与本地歌单共用）。
 *
 * 对齐网易云音乐歌单页：
 * - **竖屏**：封面 + 标题 + 创建者/播放数 → 操作胶囊 → 播放全部行 → 歌曲列表（封面/标题/标签/歌手-专辑/时长/更多）；
 * - **横屏**：PC 端布局，左侧大封面 + 右侧标题与操作，下方「歌曲 | 评论 | 收藏者」+ 表格（序号/标题/专辑/喜欢/时长）。
 */
data class PlaylistSongUi(
    val key: Any,
    val title: String,
    val artist: String,
    val album: String,
    val cover: String?,
    val durationSec: Int,
    val vip: Boolean = false,
    val blocked: Boolean = false,
    val favorite: Boolean = false,
)

private fun durationText(seconds: Int): String {
    if (seconds <= 0) return "--:--"
    val m = seconds / 60
    val s = seconds % 60
    return "%02d:%02d".format(m, s)
}

/** 歌单头：竖屏手机样式 / 横屏 PC 样式 */
@Composable
fun PlaylistDetailHeader(
    title: String,
    cover: String?,
    creator: String?,
    playCount: String?,
    songCount: Int,
    isLandscape: Boolean,
    /** 可空：没有「播放全部」动作的页面（如专辑/艺人详情）圆键置灰但仍展示头部 */
    onPlayAll: (() -> Unit)?,
    /** 直接给计数文本（如「618 首」）；非空时优先于 songCount 显示 */
    countText: String? = null,
    actions: @Composable RowScope.() -> Unit = {},
) {
    val palette = LocalCatClawPalette.current
    val primary = Color(palette.textPrimary)
    val secondary = Color(palette.textSecondary)

    if (isLandscape) {
        Row(
            modifier = Modifier.fillMaxWidth().padding(horizontal = 18.dp, vertical = 10.dp),
            horizontalArrangement = Arrangement.spacedBy(18.dp),
        ) {
            CoverTile(
                coverArtPath = cover,
                audioPath = null,
                maxPx = 400,
                modifier = Modifier
                    .size(160.dp)
                    .clip(RoundedCornerShape(12.dp))
                    .background(Color(palette.primary).copy(alpha = 0.16f)),
            ) {
                Text(title.take(1), fontSize = 42.sp, fontWeight = FontWeight.Bold, color = primary)
            }
            Column(verticalArrangement = Arrangement.spacedBy(8.dp)) {
                Text(title, fontSize = 26.sp, fontWeight = FontWeight.Bold, color = primary, maxLines = 2, overflow = TextOverflow.Ellipsis)
                Row(verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(10.dp)) {
                    if (!creator.isNullOrBlank()) {
                        Text(creator, fontSize = 12.sp, color = primary)
                    }
                    Text(
                        text = listOfNotNull(
                            countText ?: stringResourceSongs(songCount),
                            playCount,
                        ).joinToString(" · "),
                        fontSize = 12.sp,
                        color = secondary,
                    )
                }
                Row(verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(10.dp)) {
                    PillLabel(text = stringResource(R.string.netease_play_all), onClick = { onPlayAll?.invoke() }, enabled = onPlayAll != null, filled = true)
                    actions()
                }
            }
        }
    } else {
        Row(
            modifier = Modifier.fillMaxWidth().padding(horizontal = 18.dp, vertical = 10.dp),
            horizontalArrangement = Arrangement.spacedBy(14.dp),
        ) {
            CoverTile(
                coverArtPath = cover,
                audioPath = null,
                maxPx = 300,
                modifier = Modifier
                    .size(104.dp)
                    .clip(RoundedCornerShape(12.dp))
                    .background(Color(palette.primary).copy(alpha = 0.16f)),
            ) {
                Text(title.take(1), fontSize = 30.sp, fontWeight = FontWeight.Bold, color = primary)
            }
            Column(
                modifier = Modifier.weight(1f),
                verticalArrangement = Arrangement.spacedBy(6.dp),
            ) {
                Text(title, fontSize = 20.sp, fontWeight = FontWeight.Bold, color = primary, maxLines = 2, overflow = TextOverflow.Ellipsis)
                Row(verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                    if (!creator.isNullOrBlank()) {
                        Text(creator, fontSize = 12.sp, color = primary, maxLines = 1)
                    }
                    Text(
                        text = playCount ?: countText ?: stringResourceSongs(songCount),
                        fontSize = 12.sp,
                        color = secondary,
                    )
                }
                Row(verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                    actions()
                }
            }
        }
        // 播放全部行：大播放键 + 曲目数 + 右侧工具图标
        Row(
            modifier = Modifier.fillMaxWidth().padding(horizontal = 18.dp, vertical = 6.dp),
            verticalAlignment = Alignment.CenterVertically,
            horizontalArrangement = Arrangement.spacedBy(10.dp),
        ) {
            Box(
                modifier = Modifier
                    .size(44.dp)
                    .clip(CircleShape)
                    .background(Color(palette.primary))
                    .clickable(enabled = onPlayAll != null) { onPlayAll?.invoke() },
                contentAlignment = Alignment.Center,
            ) {
                Icon(
                    painter = painterResource(R.drawable.ic_play),
                    contentDescription = stringResource(R.string.netease_play_all),
                    tint = Color.White,
                    modifier = Modifier.size(20.dp),
                )
            }
            Column(Modifier.weight(1f)) {
                Text(stringResource(R.string.netease_play_all), fontSize = 15.sp, fontWeight = FontWeight.Bold, color = primary)
                Text(stringResourceSongs(songCount), fontSize = 11.sp, color = secondary)
            }
        }
    }
}

/** 竖屏歌曲行：封面 + 标题/标签 + 歌手-专辑 + 时长 + 更多 */
@OptIn(ExperimentalFoundationApi::class)
@Composable
fun PlaylistSongRowUi(
    song: PlaylistSongUi,
    index: Int,
    isLandscape: Boolean,
    onClick: () -> Unit,
    onLongClick: (() -> Unit)? = null,
    onMore: (() -> Unit)? = null,
) {
    val palette = LocalCatClawPalette.current
    val primary = Color(palette.textPrimary)
    val secondary = Color(palette.textSecondary)

    if (isLandscape) {
        Row(
            modifier = Modifier
                .fillMaxWidth()
                .clip(RoundedCornerShape(8.dp))
                .combinedClickable(onClick = onClick, onLongClick = onLongClick)
                .padding(horizontal = 10.dp, vertical = 5.dp),
            verticalAlignment = Alignment.CenterVertically,
            horizontalArrangement = Arrangement.spacedBy(10.dp),
        ) {
            Text(
                text = "%02d".format(index + 1),
                fontSize = 11.sp,
                color = secondary,
                modifier = Modifier.width(26.dp),
            )
            CoverTile(
                coverArtPath = song.cover,
                audioPath = null,
                maxPx = 96,
                modifier = Modifier.size(34.dp).clip(RoundedCornerShape(6.dp)),
            ) { }
            Column(Modifier.weight(2f)) {
                Text(song.title, fontSize = 13.sp, color = primary, maxLines = 1, overflow = TextOverflow.Ellipsis)
                if (song.artist.isNotBlank()) {
                    Text(song.artist, fontSize = 10.sp, color = secondary, maxLines = 1, overflow = TextOverflow.Ellipsis)
                }
            }
            Text(
                text = song.album,
                fontSize = 12.sp,
                color = secondary,
                maxLines = 1,
                overflow = TextOverflow.Ellipsis,
                modifier = Modifier.weight(1.4f),
            )
            Icon(
                painter = painterResource(if (song.favorite) R.drawable.ic_favorite else R.drawable.ic_favorite_border),
                contentDescription = null,
                tint = if (song.favorite) Color(palette.primary) else secondary,
                modifier = Modifier.size(18.dp),
            )
            Text(
                text = durationText(song.durationSec),
                fontSize = 11.sp,
                color = secondary,
                modifier = Modifier.width(48.dp),
            )
        }
    } else {
        Row(
            modifier = Modifier
                .fillMaxWidth()
                .clip(RoundedCornerShape(14.dp))
                .combinedClickable(onClick = onClick, onLongClick = onLongClick)
                .padding(horizontal = 8.dp, vertical = 6.dp),
            verticalAlignment = Alignment.CenterVertically,
            horizontalArrangement = Arrangement.spacedBy(10.dp),
        ) {
            CoverTile(
                coverArtPath = song.cover,
                audioPath = null,
                maxPx = 140,
                modifier = Modifier.size(46.dp).clip(RoundedCornerShape(8.dp)),
            ) { }
            Column(Modifier.weight(1f), verticalArrangement = Arrangement.spacedBy(2.dp)) {
                Row(verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(4.dp)) {
                    Text(
                        text = song.title,
                        fontSize = 14.sp,
                        color = if (song.blocked) secondary else primary,
                        maxLines = 1,
                        overflow = TextOverflow.Ellipsis,
                        modifier = Modifier.weight(1f, fill = false),
                    )
                    if (song.vip) {
                        Text(
                            text = "VIP",
                            fontSize = 9.sp,
                            color = Color(0xFFF0B34D),
                            modifier = Modifier
                                .clip(RoundedCornerShape(3.dp))
                                .background(Color(0xFFF0B34D).copy(alpha = 0.14f))
                                .padding(horizontal = 3.dp, vertical = 1.dp),
                        )
                    }
                }
                Text(
                    text = listOf(song.artist, song.album).filter { it.isNotBlank() }.joinToString(" - "),
                    fontSize = 11.sp,
                    color = secondary,
                    maxLines = 1,
                    overflow = TextOverflow.Ellipsis,
                )
            }
            Text(durationText(song.durationSec), fontSize = 11.sp, color = secondary)
            if (onMore != null) {
                Icon(
                    painter = painterResource(R.drawable.ic_more),
                    contentDescription = null,
                    tint = secondary,
                    modifier = Modifier
                        .size(22.dp)
                        .clickable(onClick = onMore),
                )
            }
        }
    }
}

@Composable
private fun stringResourceSongs(count: Int): String = androidx.compose.ui.res.stringResource(R.string.netease_songs_unit, count)
