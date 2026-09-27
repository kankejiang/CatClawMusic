package com.catclaw.music.kt.ui.screen

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
import androidx.compose.material3.Icon
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.Color
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
import com.catclaw.music.kt.ui.theme.LocalCatClawPalette

/**
 * 播放队列页 —— 对应 MAUI `NowPlayingQueuePage.xaml`。
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

    Column(modifier = Modifier.fillMaxSize()) {
        Row(
            modifier = Modifier
                .fillMaxWidth()
                .padding(start = 8.dp, end = 16.dp, top = 10.dp, bottom = 10.dp),
            verticalAlignment = Alignment.CenterVertically,
        ) {
            Icon(
                imageVector = PlayerIcons.ArrowBack,
                contentDescription = stringResource(R.string.queue_back),
                tint = Color(palette.textPrimary),
                modifier = Modifier
                    .size(40.dp)
                    .padding(10.dp)
                    .clickable(onClick = onBack),
            )
            Spacer(Modifier.width(4.dp))
            Text(
                text = stringResource(R.string.queue_title),
                fontSize = 17.sp,
                fontWeight = FontWeight.SemiBold,
                color = Color(palette.textPrimary),
            )
            Spacer(Modifier.width(8.dp))
            Text(
                text = if (songs.isEmpty()) {
                    stringResource(R.string.queue_empty)
                } else {
                    stringResource(R.string.queue_songs, songs.size)
                },
                fontSize = 12.sp,
                color = Color(palette.textSecondary),
            )
        }

        LazyColumn(
            modifier = Modifier.fillMaxSize(),
            contentPadding = PaddingValues(start = 12.dp, end = 12.dp, bottom = 12.dp),
        ) {
            itemsIndexed(songs, key = { _, s -> s.id }) { index, song ->
                val isCurrent = song.id == state.currentSong?.id
                Row(
                    modifier = Modifier
                        .fillMaxWidth()
                        .height(56.dp)
                        .clickable {
                            viewModel.playAt(index)
                            onBack()
                        }
                        .padding(horizontal = 2.dp),
                    verticalAlignment = Alignment.CenterVertically,
                ) {
                    Box(modifier = Modifier.size(16.dp), contentAlignment = Alignment.Center) {
                        if (isCurrent) {
                            Icon(
                                imageVector = PlayerIcons.Play,
                                contentDescription = null,
                                tint = Color(palette.primary),
                                modifier = Modifier.size(14.dp),
                            )
                        }
                    }
                    Spacer(Modifier.width(10.dp))
                    Column(
                        modifier = Modifier.weight(1f),
                        verticalArrangement = Arrangement.spacedBy(2.dp),
                    ) {
                        Text(
                            text = song.title,
                            fontSize = 14.sp,
                            fontWeight = if (isCurrent) FontWeight.Bold else FontWeight.SemiBold,
                            color = if (isCurrent) Color(palette.primary) else Color(palette.textPrimary),
                            maxLines = 1,
                            overflow = TextOverflow.Ellipsis,
                        )
                        Text(
                            text = song.artist,
                            fontSize = 12.sp,
                            color = Color(palette.textSecondary),
                            maxLines = 1,
                            overflow = TextOverflow.Ellipsis,
                        )
                    }
                    Icon(
                        imageVector = PlayerIcons.Close,
                        contentDescription = stringResource(R.string.queue_remove),
                        tint = Color(palette.textSecondary),
                        modifier = Modifier
                            .size(32.dp)
                            .padding(6.dp)
                            .clickable { viewModel.removeAt(song.id) },
                    )
                }
            }
        }
    }
}
