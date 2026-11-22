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
import androidx.compose.foundation.layout.statusBarsPadding
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
import androidx.compose.runtime.mutableIntStateOf
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
import androidx.annotation.DrawableRes
import com.catclaw.music.kt.ui.theme.highlightOnGlassCard
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
    // 标签保存改的是同一个 songId 的数据，produceState 只以 songId 为 key 不会重载，
    // 页面就停在旧标题上（真机验收时被误认为「保存失败」）。这里加一个版本号当额外 key。
    var detailReloadTick by remember { mutableIntStateOf(0) }
    val appContext = LocalContext.current.applicationContext
    LaunchedEffect(songId) {
        val bus = runCatching {
            dagger.hilt.android.EntryPointAccessors.fromApplication(
                appContext,
                SongMetadataRefreshEntryPoint::class.java,
            ).songMetadataRefreshBus()
        }.getOrNull() ?: return@LaunchedEffect
        bus.events.collect { changed ->
            if (changed <= 0 || changed == songId) detailReloadTick++
        }
    }
    val row by produceState<SongDetailRow?>(initialValue = null, songId, detailReloadTick) {
        value = runCatching { load(songId) }.getOrNull()
    }
    val lyrics by produceState(initialValue = emptyList<String>(), songId) {
        value = runCatching { loadLyrics(songId) }.getOrDefault(emptyList())
    }
    val detail = row
    val context = LocalContext.current
    var menuOpen by remember { mutableStateOf(false) }
    // 歌词匹配覆盖层：有目标时整页换成匹配页，返回即回到详情
    var lyricsMatchTarget by remember {
        mutableStateOf<com.catclaw.music.kt.lyrico.ui.LyricsMatchTarget?>(null)
    }
    lyricsMatchTarget?.let { target ->
        com.catclaw.music.kt.ui.lyrics.LyricsMatchScreen(
            target = target,
            onBack = { lyricsMatchTarget = null },
        )
        return
    }
    // 封面匹配覆盖层（与歌词匹配并列入口）
    var coverMatchOpen by remember { mutableStateOf(false) }
    // 标签编辑覆盖层
    var tagEditOpen by remember { mutableStateOf(false) }
    if (tagEditOpen) {
        detail?.let { d ->
            com.catclaw.music.kt.ui.lyrics.MetadataEditScreen(
                target = com.catclaw.music.kt.lyrico.ui.LyricsMatchTarget(
                    songId = d.id,
                    title = d.title,
                    artist = d.artist,
                    album = d.album,
                    durationMs = d.durationSeconds * 1000L,
                    filePath = d.filePath,
                ),
                currentGenre = d.genre,
                currentYear = d.year,
                currentTrack = d.trackNumber,
                onBack = { tagEditOpen = false },
            )
            return
        }
    }
    if (coverMatchOpen) {
        detail?.let { d ->
            com.catclaw.music.kt.ui.lyrics.CoverMatchScreen(
                target = com.catclaw.music.kt.lyrico.ui.LyricsMatchTarget(
                    songId = d.id,
                    title = d.title,
                    artist = d.artist,
                    album = d.album,
                    durationMs = d.durationSeconds * 1000L,
                    filePath = d.filePath,
                ),
                onBack = { coverMatchOpen = false },
            )
            return
        }
    }
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
            // 顶部要躲状态栏：之前只有 12dp 的 Spacer，标题直接压在时间/电量那一行下面
            .statusBarsPadding()
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

        // 歌词卡片：标题行 + 操作行 + 预览区。
        // 原先把标题、滚动标记、三个入口全塞进同一行，"编辑标签" 在窄屏会被挤成两行
        // （真机截图确认），所以拆成上下两层。
        Column(
            modifier = Modifier
                .fillMaxWidth()
                .glassCard(palette = palette, corner = 24)
                .padding(horizontal = 16.dp, vertical = 14.dp),
            verticalArrangement = Arrangement.spacedBy(10.dp),
        ) {
            // ── 标题行：只有标题与行数，不再混入按钮 ──
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
                Spacer(Modifier.weight(1f))
                // 行数靠右：一眼知道这首歌有没有词、有多少
                if (lyrics.isNotEmpty()) {
                    Text(
                        text = "${lyrics.size}",
                        fontSize = 11.sp,
                        color = secondary,
                    )
                }
            }

            // ── 操作行：三个等宽图标按钮 ──
            // 用等宽 + 图标在上，比三个长短不一的 pill 更整齐，也不会因文字长短换行
            Row(
                modifier = Modifier.fillMaxWidth(),
                horizontalArrangement = Arrangement.spacedBy(8.dp),
            ) {
                DetailActionTile(
                    icon = R.drawable.ic_nav_lyrics,
                    label = stringResource(R.string.lyrics_match_entry),
                    enabled = detail != null,
                    modifier = Modifier.weight(1f),
                    onClick = {
                        detail?.let { d ->
                            lyricsMatchTarget = com.catclaw.music.kt.lyrico.ui.LyricsMatchTarget(
                                songId = d.id,
                                title = d.title,
                                artist = d.artist,
                                album = d.album,
                                durationMs = d.durationSeconds * 1000L,
                                filePath = d.filePath,
                            )
                        }
                    },
                )
                DetailActionTile(
                    icon = R.drawable.ic_album,
                    label = stringResource(R.string.cover_match_entry),
                    enabled = detail != null,
                    modifier = Modifier.weight(1f),
                    onClick = { coverMatchOpen = true },
                )
                DetailActionTile(
                    icon = R.drawable.ic_edit,
                    label = stringResource(R.string.tag_edit_entry),
                    enabled = detail != null,
                    modifier = Modifier.weight(1f),
                    onClick = { tagEditOpen = true },
                )
            }

            // ── 预览区 ──
            if (lyrics.isEmpty()) {
                Text(
                    text = stringResource(R.string.lyric_none),
                    fontSize = 13.sp,
                    color = secondary,
                    textAlign = TextAlign.Center,
                    modifier = Modifier.fillMaxWidth().padding(vertical = 26.dp),
                )
            } else {
                // 定高 230dp：预览不该把整页撑成一篇长文。
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
/** Hilt 入口：Composable 里没有注入点，只能从应用容器取单例 */
@dagger.hilt.EntryPoint
@dagger.hilt.InstallIn(dagger.hilt.components.SingletonComponent::class)
internal interface SongMetadataRefreshEntryPoint {
    fun songMetadataRefreshBus(): com.catclaw.music.kt.lyrico.write.SongMetadataRefreshBus
}
/**
 * 详情页的操作按钮：图标在上、文字在下，等宽排列。
 *
 * 为什么不用 [PillLabel]：pill 的宽度随文字长度变，三个并排时「歌词匹配」「封面匹配」
 * 「编辑标签」会长短不一，窄屏上还会把最后一个挤到换行（真机截图确认过）。
 * 等宽的方块按钮三列排开，既整齐也不会换行。
 */
@Composable
private fun DetailActionTile(
    @DrawableRes icon: Int,
    label: String,
    enabled: Boolean,
    modifier: Modifier = Modifier,
    onClick: () -> Unit,
) {
    val palette = LocalCatClawPalette.current
    val ink = Color(palette.textPrimary)
    val alpha = if (enabled) 1f else 0.38f
    Column(
        modifier = modifier
            .clip(RoundedCornerShape(14.dp))
            .background(ink.copy(alpha = 0.07f))
            .clickable(enabled = enabled, onClick = onClick)
            .padding(vertical = 9.dp, horizontal = 4.dp),
        horizontalAlignment = Alignment.CenterHorizontally,
        verticalArrangement = Arrangement.Center,
    ) {
        Icon(
            painter = painterResource(icon),
            contentDescription = null,
            tint = Color(highlightOnGlassCard(palette)).copy(alpha = alpha),
            modifier = Modifier.size(18.dp),
        )
        Spacer(Modifier.height(4.dp))
        Text(
            text = label,
            fontSize = 11.sp,
            fontWeight = FontWeight.Medium,
            color = ink.copy(alpha = 0.92f * alpha),
            maxLines = 1,
            overflow = TextOverflow.Ellipsis,
            textAlign = TextAlign.Center,
        )
    }
}