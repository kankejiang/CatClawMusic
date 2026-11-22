package com.catclaw.music.kt.ui.screen

import androidx.compose.animation.AnimatedVisibility
import androidx.compose.animation.core.tween
import androidx.compose.animation.fadeIn
import androidx.compose.animation.slideInVertically
import androidx.compose.foundation.background
import androidx.compose.foundation.clickable
import androidx.compose.foundation.interaction.MutableInteractionSource
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.BoxWithConstraints
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.heightIn
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.verticalScroll
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import androidx.compose.ui.window.Dialog
import androidx.compose.ui.window.DialogProperties
import com.catclaw.music.kt.R
import com.catclaw.music.kt.core.model.Song
import com.catclaw.music.kt.ui.player.CoverTile
import com.catclaw.music.kt.ui.song.MenuTile
import com.catclaw.music.kt.ui.song.MenuTileGrid
import com.catclaw.music.kt.ui.theme.LocalCatClawPalette
import com.catclaw.music.kt.ui.theme.glassCard
import com.catclaw.music.kt.ui.theme.highlightOnGlassCard

/**
 * 播放页「更多」—— 图标网格面板（对齐主流播放器的底部操作面板形态）。
 *
 * ## 和列表页长按菜单的区别
 *
 * 列表页那份（[com.catclaw.music.kt.ui.song.SongMenuFor]）是**列表**：一长串条目，
 * 用于「对这首歌做某件不常用的操作」。播放页用户已经知道在放哪首、想要的是
 * 「快速跳去做某件事」，图标网格一屏摆下更能扫读，也更符合播放页的视觉重心。
 *
 * 两者内容有重叠（收藏/分享/查看专辑），但形态不同：这里的通用项只保留
 * 最常点的几个，其余仍在详情页。
 */
@Composable
fun PlayingMoreSheet(
    song: Song,
    /** 音质/格式标签，如 ["FLAC", "44.1kHz", "24 bit"]；空则不画这一行 */
    formatChips: List<String>,
    tiles: List<MenuTile>,
    onDismiss: () -> Unit,
) {
    val palette = LocalCatClawPalette.current
    val ink = Color(palette.textPrimary)
    val accent = Color(highlightOnGlassCard(palette))

    Dialog(
        onDismissRequest = onDismiss,
        properties = DialogProperties(usePlatformDefaultWidth = false),
    ) {
        var entered by remember { mutableStateOf(false) }
        LaunchedEffect(Unit) { entered = true }

        Box(
            modifier = Modifier
                .fillMaxSize()
                .background(Color.Black.copy(alpha = 0.42f))
                .clickable(
                    interactionSource = remember { MutableInteractionSource() },
                    indication = null,
                    onClick = onDismiss,
                ),
        ) {
            AnimatedVisibility(
                visible = entered,
                enter = slideInVertically(tween(180)) { it / 4 } + fadeIn(tween(140)),
                modifier = Modifier.align(Alignment.BottomCenter),
            ) {
                BoxWithConstraints(modifier = Modifier.fillMaxWidth()) {
                    val panelCap = maxHeight - 20.dp
                    Column(
                        modifier = Modifier
                            .fillMaxWidth()
                            .padding(horizontal = 10.dp, vertical = 12.dp)
                            .heightIn(max = panelCap)
                            // 面板要先有不透明底再做玻璃层：glassCard 只有 10% 墨色，
                            // 铺在整页上会让底下的进度条/播放控件透上来，
                            // 图块上的文字与它们重叠（真机截图里「下一首播放」就是这样糊掉的）。
                            .background(
                                color = Color(palette.backgroundStops.last().argb),
                                shape = RoundedCornerShape(28.dp),
                            )
                            .glassCard(palette = palette, corner = 28)
                            .clickable(
                                interactionSource = remember { MutableInteractionSource() },
                                indication = null,
                                onClick = {},
                            )
                            .padding(top = 8.dp, bottom = 12.dp),
                        horizontalAlignment = Alignment.CenterHorizontally,
                    ) {
                        PanelGrabber()
                        // 头部：封面 + 歌名/歌手。网格面板没有这层会显得空
                        Row(
                            modifier = Modifier
                                .fillMaxWidth()
                                .padding(horizontal = 16.dp, vertical = 8.dp),
                            verticalAlignment = Alignment.CenterVertically,
                        ) {
                            CoverTile(
                                coverArtPath = song.coverArtPath,
                                audioPath = song.filePath,
                                modifier = Modifier
                                    .size(56.dp)
                                    .clip(RoundedCornerShape(12.dp)),
                                maxPx = 168,
                                songId = song.id,
                            ) {
                                Box(
                                    modifier = Modifier
                                        .size(56.dp)
                                        .clip(RoundedCornerShape(12.dp))
                                        .background(ink.copy(alpha = 0.10f)),
                                )
                            }
                            Spacer(Modifier.width(12.dp))
                            Column(modifier = Modifier.weight(1f)) {
                                Text(
                                    text = song.title,
                                    fontSize = 15.sp,
                                    fontWeight = FontWeight.SemiBold,
                                    color = ink,
                                    maxLines = 1,
                                    overflow = TextOverflow.Ellipsis,
                                )
                                Text(
                                    text = listOf(song.artist, song.album)
                                        .filter { it.isNotBlank() }
                                        .joinToString(" · "),
                                    fontSize = 11.5.sp,
                                    color = Color(palette.textSecondary),
                                    maxLines = 1,
                                    overflow = TextOverflow.Ellipsis,
                                )
                            }
                        }

                        // 音质标签行（原始截图里那排 SQ / FLAC / 44100Hz）
                        if (formatChips.isNotEmpty()) {
                            Row(
                                modifier = Modifier
                                    .fillMaxWidth()
                                    .padding(horizontal = 16.dp, vertical = 2.dp),
                                horizontalArrangement = Arrangement.spacedBy(6.dp),
                            ) {
                                formatChips.take(6).forEach { chip ->
                                    Text(
                                        text = chip,
                                        fontSize = 9.5.sp,
                                        color = accent,
                                        maxLines = 1,
                                        modifier = Modifier
                                            .clip(RoundedCornerShape(6.dp))
                                            .background(ink.copy(alpha = 0.07f))
                                            .padding(horizontal = 6.dp, vertical = 2.dp),
                                    )
                                }
                            }
                        }

                        Spacer(Modifier.height(10.dp))

                        Column(
                            modifier = Modifier
                                .fillMaxWidth()
                                .verticalScroll(rememberScrollState())
                                .padding(horizontal = 12.dp),
                        ) {
                            MenuTileGrid(tiles = tiles, columns = 4)
                            Spacer(Modifier.height(8.dp))
                        }
                    }
                }
            }
        }
    }
}

/** 面板顶部的抓杆（与歌单菜单一致） */
@Composable
private fun PanelGrabber() {
    val palette = LocalCatClawPalette.current
    Box(
        modifier = Modifier
            .padding(top = 2.dp, bottom = 4.dp)
            .size(width = 34.dp, height = 4.dp)
            .clip(RoundedCornerShape(999.dp))
            .background(Color(palette.textPrimary).copy(alpha = 0.22f)),
    )
}
