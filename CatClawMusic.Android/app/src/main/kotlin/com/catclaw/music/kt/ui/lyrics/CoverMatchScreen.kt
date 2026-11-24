package com.catclaw.music.kt.ui.lyrics

import androidx.compose.foundation.Image
import androidx.compose.foundation.background
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.aspectRatio
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.navigationBarsPadding
import androidx.compose.foundation.layout.statusBarsPadding
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.lazy.grid.GridCells
import androidx.compose.foundation.lazy.grid.LazyVerticalGrid
import androidx.compose.foundation.lazy.grid.items
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.asImageBitmap
import androidx.compose.ui.layout.ContentScale
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import androidx.hilt.navigation.compose.hiltViewModel
import androidx.lifecycle.compose.collectAsStateWithLifecycle
import com.catclaw.music.kt.R
import com.catclaw.music.kt.lyrico.ui.CoverMatchViewModel
import com.catclaw.music.kt.lyrico.ui.LyricsMatchTarget
import com.catclaw.music.kt.ui.theme.BackTile
import com.catclaw.music.kt.ui.theme.LocalCatClawPalette
import com.catclaw.music.kt.ui.theme.PillLabel
import com.catclaw.music.kt.ui.theme.glassCard
import com.catclaw.music.kt.ui.theme.highlightOnGlassCard

/**
 * 封面匹配页：按当前歌曲搜封面 → 选一张 → 写进音频内嵌封面。
 *
 * 与歌词匹配页同一套骨架（入口也挨着），只是把候选换成封面网格。
 */
@Composable
fun CoverMatchScreen(
    target: LyricsMatchTarget,
    onBack: () -> Unit,
    viewModel: CoverMatchViewModel = hiltViewModel(),
) {
    val palette = LocalCatClawPalette.current
    val state by viewModel.state.collectAsStateWithLifecycle()
    val ink = Color(palette.textPrimary)
    val dim = Color(palette.textSecondary)
    val accent = Color(highlightOnGlassCard(palette))

    LaunchedEffect(target.songId) { viewModel.prepare(target) }

    Column(
        modifier = Modifier
            .fillMaxSize()
            .statusBarsPadding()
            // 覆盖层底部压着「迷你播放栏 + 底部导航」，不预留会被挡住
            // （真机验收时点内容实际点到了播放栏上）
            // 底栏在这层之上是隐藏的（外壳按整页覆盖收起），只需避让系统导航条
            .navigationBarsPadding()
            .padding(bottom = 8.dp)

            .padding(horizontal = 16.dp),
    ) {
        Spacer(Modifier.height(12.dp))
        Row(verticalAlignment = Alignment.CenterVertically) {
            BackTile(onBack = onBack, description = stringResource(R.string.GoBack))
            Spacer(Modifier.width(12.dp))
            Column {
                Text(
                    text = stringResource(R.string.cover_match_title),
                    fontSize = 20.sp,
                    fontWeight = FontWeight.Bold,
                    color = ink,
                )
                Text(text = target.title, fontSize = 12.sp, color = dim, maxLines = 1)
            }
        }
        Spacer(Modifier.height(12.dp))
        Row(horizontalArrangement = Arrangement.spacedBy(10.dp)) {
            PillLabel(
                text = if (state.searching) {
                    stringResource(R.string.lyrics_match_searching)
                } else {
                    stringResource(R.string.lyrics_match_search)
                },
                onClick = viewModel::search,
                filled = true,
                enabled = !state.searching && !state.writing,
            )
            if (state.covers.isNotEmpty()) {
                PillLabel(
                    text = if (state.writing) {
                        stringResource(R.string.cover_match_writing)
                    } else {
                        stringResource(R.string.cover_match_write)
                    },
                    onClick = viewModel::write,
                    filled = true,
                    enabled = !state.writing && state.selectedUrl != null,
                )
            }
        }
        state.message?.let { msg ->
            Spacer(Modifier.height(8.dp))
            Text(
                text = msg,
                fontSize = 12.sp,
                color = accent,
                modifier = Modifier
                    .fillMaxWidth()
                    .clip(RoundedCornerShape(12.dp))
                    .background(ink.copy(alpha = 0.08f))
                    .padding(horizontal = 12.dp, vertical = 8.dp),
            )
            LaunchedEffect(msg) {
                kotlinx.coroutines.delay(2600)
                viewModel.consumeMessage()
            }
        }
        Spacer(Modifier.height(10.dp))

        if (state.covers.isEmpty() && !state.searching) {
            Text(
                text = stringResource(R.string.cover_match_empty),
                fontSize = 13.sp,
                color = dim,
                modifier = Modifier.fillMaxWidth().padding(vertical = 24.dp),
            )
        }

        LazyVerticalGrid(
            columns = GridCells.Fixed(3),
            modifier = Modifier.fillMaxSize(),
            horizontalArrangement = Arrangement.spacedBy(10.dp),
            verticalArrangement = Arrangement.spacedBy(10.dp),
        ) {
            items(state.covers, key = { it.url }) { cover ->
                val selected = cover.url == state.selectedUrl
                Column(
                    modifier = Modifier
                        .glassCard(palette = palette, corner = 14, selected = selected)
                        .clickable { viewModel.select(cover.url) }
                        .padding(6.dp),
                ) {
                    Box(
                        modifier = Modifier
                            .fillMaxWidth()
                            .aspectRatio(1f)
                            .clip(RoundedCornerShape(10.dp))
                            .background(ink.copy(alpha = 0.08f)),
                        contentAlignment = Alignment.Center,
                    ) {
                        val bmp = state.thumbs[cover.url]
                        if (bmp != null) {
                            Image(
                                bitmap = bmp.asImageBitmap(),
                                contentDescription = null,
                                contentScale = ContentScale.Crop,
                                modifier = Modifier.fillMaxSize(),
                            )
                        } else {
                            Text(text = "…", color = dim, fontSize = 14.sp)
                        }
                    }
                    Spacer(Modifier.height(4.dp))
                    Text(text = cover.sourceName, fontSize = 10.sp, color = accent, maxLines = 1)
                    Text(
                        text = "${cover.artist} · %.2f".format(cover.score),
                        fontSize = 9.sp,
                        color = dim,
                        maxLines = 1,
                    )
                }
            }
        }
    }
}
