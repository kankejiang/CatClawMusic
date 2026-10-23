package com.catclaw.music.kt.ui.player

import androidx.compose.foundation.background
import androidx.compose.foundation.Image
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
import androidx.compose.ui.draw.drawWithContent
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.asImageBitmap
import androidx.compose.ui.graphics.graphicsLayer
import androidx.compose.ui.layout.ContentScale
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
import com.catclaw.music.kt.ui.theme.VinylCover
import com.catclaw.music.kt.ui.theme.inkOn

/**
 * 迷你播放条 —— 内容尺寸按 64dp 高的胶囊排：封面 36 圆角 7、标题 13 SemiBold / 艺人 11、
 * 上一曲与下一曲 22、播放暂停 30（透明底）、底部 2dp 细白进度线（与播放页那条同档）。
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
    /**
     * 播放进度，**必须是 lambda**：这是全 app 频率最高的一份状态（每 16ms 一变）。
     * 传值时读发生在**组合期** —— 外壳（`CatClawRoot`）会跟着每帧重组，
     * 而壳里还挂着 backdrop 录制层，于是每帧重录一整屏（2026-10-18 实测的卡顿来源之一）。
     * 改成绘制期读之后，位置变化只让这条 2dp 的线重画。
     */
    progress: () -> Float,
    backdrop: BackdropState,
    modifier: Modifier = Modifier,
    /** 动画进行中传 false（玻璃切实心）——理由见 `BarGlass` 的注释 */
    glassOn: Boolean = true,
    onOpen: () -> Unit,
    onToggle: () -> Unit,
    onPrevious: () -> Unit,
    onNext: () -> Unit,
) {
    val palette = LocalCatClawPalette.current

    BarGlass(state = backdrop, modifier = modifier, glassOn = glassOn) {
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
                    // 18dp：圆角从 16 提到 30 之后，12dp 的内边距会让封面被圆角切到
                    .padding(start = 18.dp, end = 12.dp, top = 8.dp, bottom = 8.dp),
                horizontalArrangement = Arrangement.spacedBy(12.dp),
                verticalAlignment = Alignment.CenterVertically,
            ) {
                Box(
                    modifier = Modifier
                        .size(36.dp)
                        // 10dp 圆角压在 36dp 的块上已经接近圆角胶囊，和播放页那颗「小圆角 + 重投影」的封面
                        // 不是一种形状；按同一形状语言收到 7dp（等比换算是 1.7dp，小到读不出来，取能看圆的下限）
                        .clip(RoundedCornerShape(7.dp))
                        .background(Color(palette.primary)),
                    contentAlignment = Alignment.Center,
                ) {
                    val cover = rememberCover(song.coverArtPath, song.filePath, maxPx = 128)
                    if (cover != null) {
                        Image(
                            bitmap = cover.asImageBitmap(),
                            contentDescription = null,
                            contentScale = ContentScale.Crop,
                            modifier = Modifier.matchParentSize(),
                        )
                    } else {
                        // 没封面：与播放页同一张唱片碟占位（原来是「首字」方块，两处风格不一致）。
                        // 这枚只有 36dp，碟心那两行字实际读不出来，但作为纹理正合适 —— MAUI 也是
                        // 720px 渲染后降采样用，观感一致。
                        VinylCover(modifier = Modifier.matchParentSize(), primary = Color(palette.primary))
                    }
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
                    .height(2.dp)
                    // 与播放页那条进度线同一档：细、白、未走到的部分留一层淡痕。
                    // 原来是 3dp 实心品牌粉——页面已经改成纯白字形之后，这条粉线就是页面上唯一
                    // 还在喊主色的地方，两栏之间看着像两套东西
                    .background(Color(palette.textPrimary).copy(alpha = 0.22f))
                    // 独立图层：这条线每帧都在动，不隔离的话整屏（含玻璃）都会跟着重录
                    .graphicsLayer()
                    // 已走过的部分在**绘制期**按当前进度画：不参与重组，也不产生新的布局节点
                    .drawWithContent {
                        drawContent()
                        val fraction = progress().coerceIn(0f, 1f)
                        if (fraction > 0f) {
                            drawRect(
                                color = Color(palette.textPrimary),
                                size = androidx.compose.ui.geometry.Size(size.width * fraction, size.height),
                            )
                        }
                    },
            )
        }
    }
}
