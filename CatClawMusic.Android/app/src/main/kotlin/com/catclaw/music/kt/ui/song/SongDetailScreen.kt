package com.catclaw.music.kt.ui.song

import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.aspectRatio
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.verticalScroll
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.produceState
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
import com.catclaw.music.kt.core.model.SongDetailFormat
import com.catclaw.music.kt.data.db.SongDetailRow
import com.catclaw.music.kt.ui.player.CoverTile
import com.catclaw.music.kt.ui.theme.BackTile
import com.catclaw.music.kt.ui.theme.LocalCatClawPalette
import com.catclaw.music.kt.ui.theme.glassCard

/** 来源 ordinal → 文案。ordinal 契约由 `SourceOrdinalParityTest` 钉住 */
private fun sourceLabelRes(source: Int): Int = when (source) {
    0 -> R.string.detail_source_local
    1 -> R.string.detail_source_webdav
    2 -> R.string.detail_source_smb
    else -> R.string.detail_source_cache
}

/**
 * 歌曲详情 —— 对应 MAUI `SongDetailPage`。
 *
 * 只列**取得到的**字段：C# 那页还有「均衡器」入口与「歌词全文滚动」两块，
 * 本端没有均衡器，歌词这里给"有/无 + 路径"而不是把整篇贴进来（那是歌词页的活）。
 * 缺值的行显示「无」而不是 0 或空行 —— 空行会让人以为页面坏了。
 */
@Composable
fun SongDetailScreen(songId: Int, load: suspend (Int) -> SongDetailRow?, onBack: () -> Unit) {
    val palette = LocalCatClawPalette.current
    val primary = Color(palette.textPrimary)
    val secondary = Color(palette.textSecondary)
    val row by produceState<SongDetailRow?>(initialValue = null, songId) {
        value = runCatching { load(songId) }.getOrNull()
    }
    val detail = row

    Column(
        modifier = Modifier
            .fillMaxSize()
            .verticalScroll(rememberScrollState())
            .padding(horizontal = 16.dp),
    ) {
        Spacer(Modifier.height(12.dp))
        Row(verticalAlignment = Alignment.CenterVertically) {
            BackTile(onBack = onBack, description = stringResource(R.string.GoBack))
            Spacer(Modifier.width(12.dp))
            Text(
                text = stringResource(R.string.song_detail_title),
                fontSize = 24.sp,
                fontWeight = FontWeight.Bold,
                color = primary,
            )
        }
        Spacer(Modifier.height(14.dp))

        Column(
            modifier = Modifier
                .fillMaxWidth()
                .glassCard(palette = palette, corner = 26)
                .padding(16.dp),
            horizontalAlignment = Alignment.CenterHorizontally,
        ) {
            CoverTile(
                coverArtPath = detail?.coverArtPath,
                audioPath = detail?.filePath,
                maxPx = 512,
                modifier = Modifier
                    .fillMaxWidth(0.5f)
                    .aspectRatio(1f)
                    .clip(RoundedCornerShape(20.dp)),
            ) {
                Text(
                    text = detail?.title?.take(1) ?: "?",
                    fontSize = 40.sp,
                    fontWeight = FontWeight.Bold,
                    color = Color(palette.textPrimary),
                )
            }
            Spacer(Modifier.height(12.dp))
            Text(
                text = detail?.title ?: stringResource(R.string.detail_loading),
                fontSize = 18.sp,
                fontWeight = FontWeight.Bold,
                color = primary,
                maxLines = 2,
                overflow = TextOverflow.Ellipsis,
            )
            Text(
                text = detail?.artist.orEmpty(),
                fontSize = 13.sp,
                color = secondary,
                modifier = Modifier.padding(top = 2.dp),
            )
        }
        Spacer(Modifier.height(12.dp))

        DetailCard(
            title = stringResource(R.string.detail_section_basic),
            none = stringResource(R.string.detail_none),
            rows = listOf(
                stringResource(R.string.detail_album) to detail?.album?.ifBlank { "" }.orEmpty(),
                stringResource(R.string.detail_artist) to detail?.artist.orEmpty(),
                stringResource(R.string.detail_genre) to detail?.genre.orEmpty(),
                stringResource(R.string.detail_year) to SongDetailFormat.year(detail?.year ?: 0),
                stringResource(R.string.detail_track) to SongDetailFormat.track(detail?.trackNumber ?: 0),
                stringResource(R.string.detail_duration) to SongDetailFormat.duration(detail?.durationSeconds ?: 0),
            ),
        )
        Spacer(Modifier.height(12.dp))
        DetailCard(
            title = stringResource(R.string.detail_section_file),
            none = stringResource(R.string.detail_none),
            rows = listOf(
                stringResource(R.string.detail_bitrate) to SongDetailFormat.bitrate(detail?.bitrate ?: 0),
                stringResource(R.string.detail_format) to SongDetailFormat.fileFormat(detail?.filePath),
                stringResource(R.string.detail_size) to SongDetailFormat.bytes(detail?.fileSize ?: 0L),
                stringResource(R.string.detail_source) to detail?.let {
                    stringResource(sourceLabelRes(it.source))
                }.orEmpty(),
                stringResource(R.string.detail_play_count) to (detail?.playCount ?: 0).toString(),
                stringResource(R.string.detail_lyrics) to detail?.lyricsPath.orEmpty(),
            ),
        )
        // 这页是横滑容器里的一页，迷你播放器 + 底栏是浮在内容上的：
        // 不留这段高度，「文件与音质」最后几行永远被压住且滚不出来
        Spacer(Modifier.height(240.dp))
    }
}

@Composable
private fun DetailCard(title: String, none: String, rows: List<Pair<String, String>>) {
    val palette = LocalCatClawPalette.current
    Column(
        modifier = Modifier
            .fillMaxWidth()
            .glassCard(palette = palette, corner = 24)
            .padding(horizontal = 16.dp, vertical = 14.dp),
        verticalArrangement = Arrangement.spacedBy(10.dp),
    ) {
        Text(text = title, fontSize = 15.sp, fontWeight = FontWeight.Bold, color = Color(palette.textPrimary))
        rows.forEach { (label, value) ->
            Row(modifier = Modifier.fillMaxWidth(), verticalAlignment = Alignment.Top) {
                Text(
                    text = label,
                    fontSize = 13.sp,
                    color = Color(palette.textSecondary),
                    modifier = Modifier.width(96.dp),
                )
                Text(
                    text = value.ifBlank { none },
                    fontSize = 13.sp,
                    color = Color(palette.textPrimary),
                    modifier = Modifier.weight(1f),
                    maxLines = 3,
                    overflow = TextOverflow.Ellipsis,
                )
            }
        }
    }
}
