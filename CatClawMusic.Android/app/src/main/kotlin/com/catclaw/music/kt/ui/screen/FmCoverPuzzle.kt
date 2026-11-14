package com.catclaw.music.kt.ui.screen

import android.graphics.Bitmap
import androidx.compose.foundation.Image
import androidx.compose.foundation.background
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.BoxWithConstraints
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.aspectRatio
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.shape.RoundedCornerShape
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
import androidx.compose.ui.graphics.asImageBitmap
import androidx.compose.ui.layout.ContentScale
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp

/**
 * 私人漫游「拼图模式」：封面切 3×3，留一个空格，点相邻碎片滑动拼回。
 *
 * 与官方同款「边听边拼」：从还原态随机走 60 步打乱（保证有解），拼完显示完整封面。
 * 换歌时按 [cover] 自动重开一局。接口没有 puzzle mode，取歌仍走默认电台。
 */
@Composable
fun FmCoverPuzzle(
    cover: Bitmap?,
    modifier: Modifier = Modifier,
) {
    var tiles by remember(cover) { mutableStateOf(shuffledTiles()) }
    val solved = tiles == (0..8).toList()
    var showSolved by remember(cover) { mutableStateOf(false) }
    LaunchedEffect(solved) { if (solved) showSolved = true }

    BoxWithConstraints(
        modifier = modifier.aspectRatio(1f),
        contentAlignment = Alignment.Center,
    ) {
        val cell = maxWidth / 3
        if (cover == null) {
            Text("♪", fontSize = 48.sp, color = Color(0x88FFFFFF))
            return@BoxWithConstraints
        }
        if (showSolved && solved) {
            Image(
                bitmap = cover.asImageBitmap(),
                contentDescription = "拼图完成",
                contentScale = ContentScale.Crop,
                modifier = Modifier
                    .size(maxWidth)
                    .clip(RoundedCornerShape(12.dp))
                    .clickable { showSolved = false },
            )
            return@BoxWithConstraints
        }
        val blank = tiles.indexOf(BLANK)
        Column {
            for (row in 0 until GRID) {
                Row {
                    for (col in 0 until GRID) {
                        val pos = row * GRID + col
                        val tile = tiles[pos]
                        Box(
                            modifier = Modifier
                                .size(cell)
                                .padding(1.dp)
                                .clip(RoundedCornerShape(2.dp))
                                .background(if (tile == BLANK) BlankTile else Color(0x11000000))
                                .clickable(enabled = isAdjacent(pos, blank)) {
                                    val next = tiles.toMutableList()
                                    next[blank] = tile
                                    next[pos] = BLANK
                                    tiles = next
                                },
                            contentAlignment = Alignment.Center,
                        ) {
                            if (tile != BLANK) {
                                val srcW = cover.width / GRID
                                val srcH = cover.height / GRID
                                val piece = remember(cover, tile) {
                                    Bitmap.createBitmap(
                                        cover,
                                        (tile % GRID) * srcW,
                                        (tile / GRID) * srcH,
                                        srcW,
                                        srcH,
                                    )
                                }
                                Image(
                                    bitmap = piece.asImageBitmap(),
                                    contentDescription = null,
                                    contentScale = ContentScale.Crop,
                                    modifier = Modifier.fillMaxWidth().aspectRatio(1f),
                                )
                            }
                        }
                    }
                }
            }
        }
    }
}

private const val GRID = 3
private const val BLANK = 8
private val BlankTile = Color(0xFF9FB8A8)

private fun neighbors(pos: Int): List<Int> {
    val r = pos / GRID
    val c = pos % GRID
    return buildList {
        if (r > 0) add(pos - GRID)
        if (r < GRID - 1) add(pos + GRID)
        if (c > 0) add(pos - 1)
        if (c < GRID - 1) add(pos + 1)
    }
}

private fun isAdjacent(pos: Int, blank: Int) = pos in neighbors(blank)

/** 从还原态随机走 60 步，保证一定可解 */
private fun shuffledTiles(): List<Int> {
    val t = (0..8).toMutableList()
    var blank = BLANK
    repeat(60) {
        val pick = neighbors(blank).random()
        t[blank] = t[pick]
        t[pick] = BLANK
        blank = pick
    }
    return t
}
