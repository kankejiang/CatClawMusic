package com.catclaw.music.kt.ui.screen

import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.Image
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.PaddingValues
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.itemsIndexed
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material3.Icon
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.asImageBitmap
import androidx.compose.ui.layout.ContentScale
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import androidx.lifecycle.compose.collectAsStateWithLifecycle
import com.catclaw.music.kt.R
import com.catclaw.music.kt.ui.playback.PlayingViewModel
import com.catclaw.music.kt.ui.player.Icon
import com.catclaw.music.kt.ui.player.PlayerIcons
import com.catclaw.music.kt.ui.player.rememberCover
import com.catclaw.music.kt.ui.theme.HeroScrimDeepArgb
import com.catclaw.music.kt.ui.theme.LocalCatClawPalette
import com.catclaw.music.kt.ui.theme.BackTile
import com.catclaw.music.kt.ui.theme.highlightOnSelectedCard
import com.catclaw.music.kt.ui.theme.EmptyState

/**
 * 播放队列页 —— 对应 MAUI `NowPlayingQueuePage.xaml`，外观对齐本 App 的玻璃卡语言。
 *
 * 两条是查证过的行为，不是简化：
 * 1. 行内容只有**播放指示 + 歌名 + 歌手 + 移除**，没有序号也没有时长（XAML 的 `BuildRow` 就这三列）；
 * 2. 点任意一行是「播它然后关页」（`PlaySongFromQueueCommand` 后 `GoBack()`），不是留在本页。
 *
 * 它不在播放页里内嵌 —— 之前把队列塞进播放页是我在 Kotlin 侧自己加的，已按用户的 MAUI 版改回独立页。
 */
@Composable
fun QueueScreen(
    viewModel: PlayingViewModel,
    onBack: () -> Unit,
) {
    val state by viewModel.state.collectAsStateWithLifecycle()
    val songs by viewModel.queueSongs.collectAsStateWithLifecycle()
    val palette = LocalCatClawPalette.current
    val primary = Color(palette.textPrimary)
    val secondary = Color(palette.textSecondary)

    Column(modifier = Modifier.fillMaxSize()) {
        Row(
            modifier = Modifier
                .fillMaxWidth()
                .padding(start = 20.dp, end = 20.dp, top = 14.dp, bottom = 10.dp),
            verticalAlignment = Alignment.CenterVertically,
            horizontalArrangement = Arrangement.spacedBy(12.dp),
        ) {
            BackTile(onBack = onBack, description = stringResource(R.string.queue_back))
            Column(modifier = Modifier.weight(1f), verticalArrangement = Arrangement.spacedBy(2.dp)) {
                Text(text = stringResource(R.string.queue_title), fontSize = 22.sp, fontWeight = FontWeight.Bold, color = primary)
                Text(
                    text = stringResource(R.string.queue_songs, songs.size),
                    fontSize = 12.sp,
                    color = secondary,
                )
            }
        }

        LazyColumn(
            modifier = Modifier.fillMaxSize(),
            contentPadding = PaddingValues(start = 20.dp, end = 20.dp, bottom = 110.dp),
        ) {
            if (songs.isEmpty()) {
                // 空队列原来只有一行副标题提示，页面本体是空白——给一张同款空状态卡
                item(key = "queue_empty") {
                    EmptyState(hint = stringResource(R.string.queue_empty), iconRes = R.drawable.ic_playlist)
                }
            }
            itemsIndexed(songs, key = { _, s -> s.id }) { index, song ->
                val isCurrent = song.id == state.currentSong?.id
                Row(
                    modifier = Modifier
                        .fillMaxWidth()
                        .padding(vertical = 4.dp)
                        .height(64.dp)
                        .clip(RoundedCornerShape(18.dp))
                        .background(
                            if (isCurrent) Color(palette.primary).copy(alpha = 0.16f)
                            else primary.copy(alpha = 0.08f),
                        )
                        .then(
                            if (isCurrent) Modifier.border(1.dp, Color(palette.primary).copy(alpha = 0.45f), RoundedCornerShape(18.dp))
                            else Modifier,
                        )
                        .clickable {
                            viewModel.playAt(index)
                            onBack()
                        }
                        .padding(horizontal = 10.dp),
                    verticalAlignment = Alignment.CenterVertically,
                ) {
                    val cover = rememberCover(song.coverArtPath, song.filePath, maxPx = 128)
                    Box(
                        modifier = Modifier
                            .size(40.dp)
                            .clip(RoundedCornerShape(12.dp))
                            .background(primary.copy(alpha = if (isCurrent) 0.18f else 0.10f)),
                        contentAlignment = Alignment.Center,
                    ) {
                        if (cover != null) {
                            Image(
                                bitmap = cover.asImageBitmap(),
                                contentDescription = null,
                                contentScale = ContentScale.Crop,
                                modifier = Modifier.matchParentSize(),
                            )
                        }
                        if (isCurrent) {
                            // 「正在播放」这块线索不能因为换了封面就丢掉：压在照片上必须自带垫层，
                            // 否则遇到浅色封面就看不出来了
                            Box(
                                modifier = Modifier
                                    .size(22.dp)
                                    .clip(CircleShape)
                                    .background(Color(HeroScrimDeepArgb)),
                                contentAlignment = Alignment.Center,
                            ) {
                                Icon(
                                    imageVector = PlayerIcons.Play,
                                    contentDescription = null,
                                    tint = Color.White,
                                    modifier = Modifier.size(12.dp),
                                )
                            }
                        } else if (cover == null) {
                            Text(text = (index + 1).toString(), fontSize = 13.sp, fontWeight = FontWeight.SemiBold, color = secondary)
                        }
                    }
                    Spacer(Modifier.width(12.dp))
                    Column(
                        modifier = Modifier.weight(1f),
                        verticalArrangement = Arrangement.spacedBy(2.dp),
                    ) {
                        Text(
                            text = song.title,
                            fontSize = 14.sp,
                            fontWeight = if (isCurrent) FontWeight.Bold else FontWeight.SemiBold,
                            color = if (isCurrent) Color(highlightOnSelectedCard(palette)) else primary,
                            maxLines = 1,
                            overflow = TextOverflow.Ellipsis,
                        )
                        Text(
                            text = song.artist,
                            fontSize = 12.sp,
                            color = secondary,
                            maxLines = 1,
                            overflow = TextOverflow.Ellipsis,
                        )
                    }
                    Box(
                        modifier = Modifier
                            .size(44.dp)
                            .clip(RoundedCornerShape(14.dp))
                            .clickable { viewModel.removeAt(song.id) },
                        contentAlignment = Alignment.Center,
                    ) {
                        Icon(
                            imageVector = PlayerIcons.Close,
                            contentDescription = stringResource(R.string.queue_remove),
                            tint = secondary,
                            modifier = Modifier.size(16.dp),
                        )
                    }
                }
            }
        }
    }
}
