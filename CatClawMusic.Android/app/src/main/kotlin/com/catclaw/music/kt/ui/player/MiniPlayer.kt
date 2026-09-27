package com.catclaw.music.kt.ui.player

import androidx.compose.foundation.background
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxHeight
import androidx.compose.foundation.layout.fillMaxSize
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
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import com.catclaw.music.kt.R
import com.catclaw.music.kt.core.model.Song
import com.catclaw.music.kt.ui.theme.BackdropGlass
import com.catclaw.music.kt.ui.theme.LocalCatClawPalette
import com.catclaw.music.kt.ui.theme.rememberBackdropState

/**
 * 迷你播放条 —— 对应 MAUI `MainPage.xaml` 的 `MiniPlayer`：悬浮胶囊、圆角 12、
 * 标题 15 / 艺人 12、三个图标 20/26/20、底部 3px 进度条（色 PrimaryColor）。
 *
 * 显隐与位置由外壳决定（MAUI：Row1 在 TabBar 上方，且只在非播放/歌词页、队列非空时出现）。
 */
@Composable
fun MiniPlayer(
    song: Song,
    isPlaying: Boolean,
    progress: Float,
    modifier: Modifier = Modifier,
    onOpen: () -> Unit,
    onToggle: () -> Unit,
    onPrevious: () -> Unit,
    onNext: () -> Unit,
) {
    val palette = LocalCatClawPalette.current
    val backdrop = rememberBackdropState()
    val fraction = progress.coerceIn(0f, 1f)

    BackdropGlass(state = backdrop, modifier = modifier.height(52.dp), cornerRadius = 12.dp) {
        Column(modifier = Modifier.fillMaxSize()) {
            Row(
                modifier = Modifier
                    .weight(1f)
                    .fillMaxWidth()
                    .clip(RoundedCornerShape(12.dp))
                    .clickable(onClick = onOpen)
                    .padding(horizontal = 12.dp),
                verticalAlignment = Alignment.CenterVertically,
            ) {
                Box(
                    modifier = Modifier
                        .size(36.dp)
                        .clip(RoundedCornerShape(9.dp))
                        .background(Color(palette.primary)),
                    contentAlignment = Alignment.Center,
                ) {
                    Text(
                        text = song.title.take(1),
                        fontSize = 15.sp,
                        fontWeight = FontWeight.Bold,
                        color = Color(palette.textPrimary),
                    )
                }
                Spacer(Modifier.width(12.dp))
                Column(modifier = Modifier.weight(1f)) {
                    Text(
                        text = song.title,
                        fontSize = 13.sp,
                        fontWeight = FontWeight.SemiBold,
                        color = Color(palette.textPrimary),
                        maxLines = 1,
                        overflow = TextOverflow.Ellipsis,
                    )
                    Text(
                        text = song.artist,
                        fontSize = 11.sp,
                        color = Color(palette.textSecondary),
                        maxLines = 1,
                        overflow = TextOverflow.Ellipsis,
                    )
                }
                Row(
                    horizontalArrangement = Arrangement.spacedBy(10.dp),
                    verticalAlignment = Alignment.CenterVertically,
                ) {
                    Icon(
                        imageVector = PlayerIcons.Previous,
                        contentDescription = stringResource(R.string.prev_song),
                        tint = Color(palette.textPrimary),
                        modifier = Modifier
                            .size(20.dp)
                            .clickable(onClick = onPrevious),
                    )
                    Box(
                        modifier = Modifier
                            .size(36.dp)
                            .clip(CircleShape)
                            .clickable(onClick = onToggle),
                        contentAlignment = Alignment.Center,
                    ) {
                        Icon(
                            imageVector = if (isPlaying) PlayerIcons.Pause else PlayerIcons.Play,
                            contentDescription = stringResource(R.string.play_or_pause),
                            tint = Color(palette.textPrimary),
                            modifier = Modifier.size(26.dp),
                        )
                    }
                    Icon(
                        imageVector = PlayerIcons.Next,
                        contentDescription = stringResource(R.string.next_song),
                        tint = Color(palette.textPrimary),
                        modifier = Modifier
                            .size(20.dp)
                            .clickable(onClick = onNext),
                    )
                }
            }
            Box(
                modifier = Modifier
                    .fillMaxWidth()
                    .height(3.dp)
                    .background(Color(palette.textPrimary).copy(alpha = 0.12f)),
            ) {
                Box(
                    modifier = Modifier
                        .fillMaxHeight()
                        .fillMaxWidth(fraction)
                        .background(Color(palette.primary)),
                )
            }
        }
    }
}
