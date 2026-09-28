package com.catclaw.music.kt.ui.player

import androidx.compose.foundation.background
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxHeight
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
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
import com.catclaw.music.kt.ui.theme.BackdropState
import com.catclaw.music.kt.ui.theme.BarGlass
import com.catclaw.music.kt.ui.theme.GlassTokens
import com.catclaw.music.kt.ui.theme.LocalCatClawPalette
import com.catclaw.music.kt.ui.theme.inkOn

/**
 * 迷你播放条 —— 内容尺寸按 64dp 高的胶囊排：封面 36 圆角 10、标题 13 SemiBold / 艺人 11、
 * 上一曲与下一曲 22、播放暂停 30（透明底）、底部 3dp 主题色进度条。
 *
 * 表面走 [BarGlass]：与 TabBar 同一个组件、同一组 token（高度 64dp、圆角 16、着色、1.5dp 描边、
 * 同一条 vibrancy + blur 20dp + lens 液态玻璃路径）—— 2026-09-28 用户要求两栏效果完全一致。
 *
 * 显隐与位置由外壳决定（MAUI：Row1 在 TabBar 上方 2dp，且只在非播放/歌词页、队列非空时出现）。
 *
 * [backdrop] 必须由外壳把宿主那一层传进来：`layerBackdrop` 只录宿主自己的子节点，
 * 自己 `rememberBackdropState()` 会拿到一个从没被写入的空 layer —— 表现就是「只剩着色、完全不糊」。
 */
@Composable
fun MiniPlayer(
    song: Song,
    isPlaying: Boolean,
    progress: Float,
    backdrop: BackdropState,
    modifier: Modifier = Modifier,
    onOpen: () -> Unit,
    onToggle: () -> Unit,
    onPrevious: () -> Unit,
    onNext: () -> Unit,
) {
    val palette = LocalCatClawPalette.current
    val fraction = progress.coerceIn(0f, 1f)

    BarGlass(state = backdrop, modifier = modifier) {
        Column(
            modifier = Modifier
                .fillMaxSize()
                .clip(RoundedCornerShape(GlassTokens.BarCornerRadius)),
        ) {
            Row(
                modifier = Modifier
                    .weight(1f)
                    .fillMaxWidth()
                    .clip(RoundedCornerShape(GlassTokens.BarCornerRadius))
                    .clickable(onClickLabel = stringResource(R.string.mini_player_open), onClick = onOpen)
                    .padding(horizontal = 12.dp, vertical = 8.dp),
                horizontalArrangement = Arrangement.spacedBy(12.dp),
                verticalAlignment = Alignment.CenterVertically,
            ) {
                Box(
                    modifier = Modifier
                        .size(36.dp)
                        .clip(RoundedCornerShape(10.dp))
                        .background(Color(palette.primary)),
                    contentAlignment = Alignment.Center,
                ) {
                    Text(
                        text = song.title.take(1),
                        fontSize = 15.sp,
                        fontWeight = FontWeight.Bold,
                        color = Color(inkOn(palette.primary)),
                    )
                }
                Column(modifier = Modifier.weight(1f), verticalArrangement = Arrangement.spacedBy(1.dp)) {
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
                Icon(
                    imageVector = PlayerIcons.Previous,
                    contentDescription = stringResource(R.string.prev_song),
                    tint = Color(palette.textPrimary),
                    modifier = Modifier
                        // 图标仍是 22dp（Fit 居中），只把可点高度撑到 44dp——条形栏高 64dp，纵向不要钱
                        .size(width = 22.dp, height = 44.dp)
                        .clip(CircleShape)
                        .clickable(onClick = onPrevious),
                )
                Icon(
                    imageVector = if (isPlaying) PlayerIcons.Pause else PlayerIcons.Play,
                    contentDescription = stringResource(R.string.play_or_pause),
                    tint = Color(palette.textPrimary),
                    modifier = Modifier
                        .size(width = 30.dp, height = 44.dp)
                        .clip(CircleShape)
                        .clickable(onClick = onToggle),
                )
                Icon(
                    imageVector = PlayerIcons.Next,
                    contentDescription = stringResource(R.string.next_song),
                    tint = Color(palette.textPrimary),
                    modifier = Modifier
                        // 图标仍是 22dp（Fit 居中），只把可点高度撑到 44dp——条形栏高 64dp，纵向不要钱
                        .size(width = 22.dp, height = 44.dp)
                        .clip(CircleShape)
                        .clickable(onClick = onNext),
                )
            }
            Box(
                modifier = Modifier
                    .fillMaxWidth()
                    .height(3.dp),
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
