package com.catclaw.music.kt.ui.song

import android.content.ClipData
import android.content.ClipboardManager
import android.content.Context
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
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.verticalScroll
import androidx.compose.material3.AlertDialog
import androidx.compose.material3.Icon
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.produceState
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.res.painterResource
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import com.catclaw.music.kt.R
import com.catclaw.music.kt.core.model.SongDetailFormat
import com.catclaw.music.kt.data.db.SongDetailRow
import com.catclaw.music.kt.ui.player.CoverTile
import com.catclaw.music.kt.ui.theme.BackTile
import com.catclaw.music.kt.ui.theme.LocalCatClawPalette
import com.catclaw.music.kt.ui.theme.PillLabel
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
 * 只列**取得到的**字段：C# 那页还有一个「均衡器」入口，本端的音效页挂在播放页上
 * （同一台机器只有一套音效，从哪页进都是改它），歌词这里给"有/无 + 路径"
 * 而不是把整篇贴进来（那是歌词页的活）。
 * 缺值的行显示「无」而不是 0 或空行 —— 空行会让人以为页面坏了。
 */
@Composable
fun SongDetailScreen(
    songId: Int,
    load: suspend (Int) -> SongDetailRow?,
    /** 歌词预览的取词入口。由调用方给：它要与歌词页走同一条链路（缓存 → 侧车 → 内嵌） */
    loadLyrics: suspend (Int) -> List<String>,
    /** 「播放此曲」（MAUI 详情页右上角 ⋮ 的第一项）。由调用方给：起播要走各自的队列入口 */
    onPlay: (Int) -> Unit,
    onBack: () -> Unit,
) {
    val palette = LocalCatClawPalette.current
    val primary = Color(palette.textPrimary)
    val secondary = Color(palette.textSecondary)
    val row by produceState<SongDetailRow?>(initialValue = null, songId) {
        value = runCatching { load(songId) }.getOrNull()
    }
    val lyrics by produceState(initialValue = emptyList<String>(), songId) {
        value = runCatching { loadLyrics(songId) }.getOrDefault(emptyList())
    }
    val detail = row
    val context = LocalContext.current
    var menuOpen by remember { mutableStateOf(false) }
    // 复制路径的回执：这一页没有 Toast，给一行会自己消失的提示（与诊断日志页同一做法）
    var copied by remember { mutableStateOf(false) }
    LaunchedEffect(copied) {
        if (copied) {
            kotlinx.coroutines.delay(1800)
            copied = false
        }
    }

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
            Spacer(Modifier.weight(1f))
            // ⋮ 菜单：MAUI `SongDetailPage` 右上角那颗「更多」（播放此曲 / 复制文件路径）。
            // 详情行没到手就不画：那两项此刻都点不动
            if (detail != null) {
                Box(
                    modifier = Modifier
                        .size(44.dp)
                        .clip(RoundedCornerShape(99.dp))
                        .clickable { menuOpen = true },
                    contentAlignment = Alignment.Center,
                ) {
                    Icon(
                        painter = painterResource(R.drawable.ic_more),
                        contentDescription = stringResource(R.string.playing_more),
                        tint = primary,
                        modifier = Modifier.size(22.dp),
                    )
                }
            }
        }
        if (copied) {
            Text(
                text = stringResource(R.string.detail_path_copied),
                fontSize = 12.sp,
                color = secondary,
                modifier = Modifier.padding(top = 6.dp),
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

        // 歌词预览 —— MAUI `SongDetailPage` 那张卡：定高可滚动列表 + 无词时的空态提示。
        // 只贴行文本（与 C# 的模板一致）：逐字填充 / 译文 / 罗马音是歌词页的活
        Column(
            modifier = Modifier
                .fillMaxWidth()
                .glassCard(palette = palette, corner = 24)
                .padding(horizontal = 16.dp, vertical = 14.dp),
            verticalArrangement = Arrangement.spacedBy(10.dp),
        ) {
            Row(verticalAlignment = Alignment.CenterVertically) {
                Text(
                    text = stringResource(R.string.detail_lyrics),
                    fontSize = 15.sp,
                    fontWeight = FontWeight.Bold,
                    color = primary,
                )
                if (lyrics.isNotEmpty()) {
                    Spacer(Modifier.width(8.dp))
                    Text(
                        text = stringResource(R.string.detail_lyrics_scroll),
                        fontSize = 10.sp,
                        color = primary,
                        modifier = Modifier
                            .clip(RoundedCornerShape(999.dp))
                            .background(Color(palette.textPrimary).copy(alpha = 0.08f))
                            .padding(horizontal = 8.dp, vertical = 2.dp),
                    )
                }
            }
            if (lyrics.isEmpty()) {
                Text(
                    text = stringResource(R.string.lyric_none),
                    fontSize = 13.sp,
                    color = secondary,
                    textAlign = TextAlign.Center,
                    modifier = Modifier.fillMaxWidth().padding(vertical = 30.dp),
                )
            } else {
                // 定高 230dp（MAUI HeightRequest=230）：预览不该把整页撑成一篇长文。
                // 内层 Scroll 有确定高度，所以套在外层 Scroll 里也不会被无限高度量到
                Column(
                    modifier = Modifier
                        .fillMaxWidth()
                        .height(230.dp)
                        .verticalScroll(rememberScrollState()),
                ) {
                    lyrics.forEach { line ->
                        Text(
                            text = line,
                            fontSize = 14.sp,
                            color = secondary,
                            textAlign = TextAlign.Center,
                            modifier = Modifier.fillMaxWidth().padding(horizontal = 14.dp, vertical = 7.dp),
                        )
                    }
                }
            }
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
                // 文件路径在 MAUI 那张卡里是跨整行的最后一行：路径很长，
                // 挤在 96dp 标签右边看不全；这里值列已经能占满剩余宽度并按需折行
                stringResource(R.string.detail_file_path) to detail?.filePath.orEmpty(),
                stringResource(R.string.detail_date_added) to SongDetailFormat.date(detail?.dateAdded ?: 0L),
                stringResource(R.string.detail_date_modified) to SongDetailFormat.date(detail?.dateModified ?: 0L),
            ),
        )
        // 这页是横滑容器里的一页，迷你播放器 + 底栏是浮在内容上的：
        // 不留这段高度，「文件与音质」最后几行永远被压住且滚不出来
        Spacer(Modifier.height(240.dp))
    }

    if (menuOpen && detail != null) {
        SongDetailMenu(
            title = detail.title,
            onPlay = {
                onPlay(songId)
                menuOpen = false
            },
            onCopyPath = {
                val clip = context.getSystemService(Context.CLIPBOARD_SERVICE) as? ClipboardManager
                clip?.setPrimaryClip(ClipData.newPlainText("catclaw-file-path", detail.filePath))
                copied = true
                menuOpen = false
            },
            onDismiss = { menuOpen = false },
        )
    }
}

/**
 * 详情页的 ⋮ 菜单 —— MAUI `SongDetailPage.OnMoreClicked` 那张 ActionSheet，就两项。
 *
 * 为什么不复用 `SongActionMenu`：那张菜单里「歌曲信息」在这一页上点了不动，
 * 而它有意不放的「播放」恰恰是本页唯一真要给的项（这页没有"点一行就播"这回事）。
 * 项目里既有的规矩是「不画点了不动的项」，与其把共用菜单改成到处传 null，不如这里就两项。
 */
@Composable
private fun SongDetailMenu(
    title: String,
    onPlay: () -> Unit,
    onCopyPath: () -> Unit,
    onDismiss: () -> Unit,
) {
    val palette = LocalCatClawPalette.current
    AlertDialog(
        onDismissRequest = onDismiss,
        containerColor = Color(palette.backgroundStops.last().argb),
        title = { Text(text = title, color = Color(palette.textPrimary)) },
        text = {
            Column(verticalArrangement = Arrangement.spacedBy(10.dp)) {
                PillLabel(text = stringResource(R.string.detail_play_this), onClick = onPlay, filled = false)
                PillLabel(text = stringResource(R.string.detail_copy_path), onClick = onCopyPath, filled = false)
            }
        },
        confirmButton = {
            PillLabel(text = stringResource(R.string.song_menu_close), onClick = onDismiss, filled = false)
        },
    )
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
