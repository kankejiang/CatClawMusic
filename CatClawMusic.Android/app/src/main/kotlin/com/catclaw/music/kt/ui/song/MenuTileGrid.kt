package com.catclaw.music.kt.ui.song

import androidx.compose.foundation.background
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.aspectRatio
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material3.Icon
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.vector.ImageVector
import androidx.compose.ui.res.painterResource
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import com.catclaw.music.kt.ui.theme.LocalCatClawPalette
import com.catclaw.music.kt.ui.theme.highlightOnGlassCard

/**
 * 播放页「更多」的图标网格。
 *
 * ## 为什么单独做一个组件而不改造 [SongMenuSection]
 *
 * 列表页的长按菜单和播放页的「更多」是**两种场景**：
 * - 列表页：一长串条目（收藏、加歌单、分享、查看歌手…），列表最易扫读
 * - 播放页：用户已经知道在放哪首，想要的是「快速跳去做某件事」，
 *   图标网格一屏放下 8~12 项，比滚动列表快
 *
 * 所以两者各有形态，不强行统一。这里只做网格版，供播放页使用。
 *
 * ## 视觉
 *
 * 沿用本工程既有的玻璃语言：图块是墨色半透明圆角 + 主色描边图标，
 * 不做彩色实心填充（与 [SongActionMenu] 的列表版一致）。
 */
data class MenuTile(
    val label: String,
    /** 优先用矢量图标；为 null 时用 [iconRes] */
    val iconVector: ImageVector? = null,
    val iconRes: Int? = null,
    /** 右上角状态文字（如「已开」「3:20」），没有则不画 */
    val value: String? = null,
    val onClick: () -> Unit,
)

/**
 * 3 列图标网格。
 *
 * @param columns 每行几个（截图那种是 4 列；我们的图块文字偏长，默认 3 列更稳）
 */
@Composable
fun MenuTileGrid(
    tiles: List<MenuTile>,
    modifier: Modifier = Modifier,
    columns: Int = 3,
) {
    if (tiles.isEmpty()) return
    val rows = tiles.chunked(columns)
    Column(
        modifier = modifier.fillMaxWidth(),
        verticalArrangement = Arrangement.spacedBy(8.dp),
    ) {
        rows.forEach { rowTiles ->
            Row(
                modifier = Modifier.fillMaxWidth(),
                horizontalArrangement = Arrangement.spacedBy(8.dp),
            ) {
                rowTiles.forEach { tile ->
                    MenuTileItem(
                        tile = tile,
                        modifier = Modifier.weight(1f),
                    )
                }
                // 最后一行不满时补空位，避免图块被拉伸成不同宽度
                repeat(columns - rowTiles.size) {
                    Spacer(Modifier.weight(1f))
                }
            }
        }
    }
}

@Composable
private fun MenuTileItem(tile: MenuTile, modifier: Modifier = Modifier) {
    val palette = LocalCatClawPalette.current
    val ink = Color(palette.textPrimary)
    val accent = Color(highlightOnGlassCard(palette))
    val shape = RoundedCornerShape(16.dp)

    Column(
        modifier = modifier
            .clip(shape)
            .background(ink.copy(alpha = 0.07f))
            .clickable(onClick = tile.onClick)
            .padding(vertical = 10.dp, horizontal = 4.dp),
        horizontalAlignment = Alignment.CenterHorizontally,
        verticalArrangement = Arrangement.Center,
    ) {
        // 图标 22dp：比列表内联图标略大，网格里才有「图块」的存在感
        val iconModifier = Modifier.size(20.dp)
        when {
            tile.iconVector != null -> Icon(
                imageVector = tile.iconVector,
                contentDescription = null,
                tint = accent,
                modifier = iconModifier,
            )
            tile.iconRes != null -> Icon(
                painter = painterResource(tile.iconRes),
                contentDescription = null,
                tint = accent,
                modifier = iconModifier,
            )
        }
        Spacer(Modifier.height(6.dp))
        Text(
            text = tile.label,
            fontSize = 10.5.sp,
            lineHeight = 14.sp,
            fontWeight = FontWeight.Medium,
            color = ink.copy(alpha = 0.92f),
            textAlign = TextAlign.Center,
            maxLines = 2,
            overflow = TextOverflow.Ellipsis,
        )
        // 状态值单独一行：网格里塞进标签后面会挤成两行，不好看
        if (tile.value != null) {
            Text(
                text = tile.value,
                fontSize = 9.5.sp,
                color = accent,
                maxLines = 1,
                overflow = TextOverflow.Ellipsis,
            )
        }
    }
}

/**
 * 把一个图块做成正方形比例（用于需要严格对齐的场合）。
 * 当前网格用固定内边距，高度随内容自然撑开，所以默认不用。
 */
@Composable
internal fun Modifier.squareTile(): Modifier = this.aspectRatio(1f)

/** 网格前的小标题（「播放」「更多」之类） */
@Composable
internal fun MenuGridLabel(text: String) {
    val palette = LocalCatClawPalette.current
    Box(modifier = Modifier.fillMaxWidth().padding(start = 4.dp, bottom = 2.dp)) {
        Text(
            text = text,
            fontSize = 10.5.sp,
            fontWeight = FontWeight.SemiBold,
            color = Color(palette.textSecondary).copy(alpha = 0.85f),
        )
    }
}
