package com.catclaw.music.kt.ui.song

import android.content.ClipData
import android.content.ClipboardManager
import android.content.Context
import android.content.res.Configuration
import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.BoxWithConstraints
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxHeight
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.navigationBarsPadding
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.statusBarsPadding
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.verticalScroll
import androidx.compose.material3.AlertDialog
import androidx.compose.material3.Icon
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.DisposableEffect
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableIntStateOf
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.produceState
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.draw.rotate
import androidx.compose.ui.graphics.Brush
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.platform.LocalConfiguration
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.res.painterResource
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.text.font.FontFamily
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.Dp
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import androidx.annotation.DrawableRes
import com.catclaw.music.kt.ui.LocalFullScreenRequest
import com.catclaw.music.kt.ui.theme.highlightOnGlassCard
import com.catclaw.music.kt.R
import com.catclaw.music.kt.core.model.SongDetailFormat
import com.catclaw.music.kt.data.db.SongDetailRow
import com.catclaw.music.kt.ui.player.CoverTile
import com.catclaw.music.kt.ui.theme.BackTile
import com.catclaw.music.kt.ui.theme.LocalCatClawPalette
import com.catclaw.music.kt.ui.theme.PillLabel
import com.catclaw.music.kt.ui.theme.glassCard

/**
 * 能不能写标签：只有真实本地文件（裸路径、SAF 的 `content://`）可以。
 *
 * 在线曲（网易云）拿到的是 http 直链，远程曲库是 http/smb，TagLib 都写不进去，
 * 详情页那三个「歌词匹配 / 封面匹配 / 编辑标签」入口对它们没有意义。
 * 用前缀判断而不是 `File.exists()`：这是 Compose 组合期，不能做磁盘 IO。
 */
private fun canEditTags(filePath: String?): Boolean {
    val path = filePath?.takeIf { it.isNotBlank() } ?: return false
    if (path.startsWith("content://")) return true
    val lower = path.lowercase()
    return !(
        lower.startsWith("http://") || lower.startsWith("https://") ||
            lower.startsWith("smb://") || lower.startsWith("outer/")
        )
}

/**
 * 来源 ordinal → 文案。ordinal 契约由 `SourceOrdinalParityTest` 钉住。
 *
 * 4 = 在线：DB 里只有 0..3 四种 [com.catclaw.music.kt.core.model.SongSource]，
 * 这个值只用于详情页拿播放快照合成的行（网易云这类不进本地库的曲目）。
 */
private fun sourceLabelRes(source: Int): Int = when (source) {
    0 -> R.string.detail_source_local
    1 -> R.string.detail_source_webdav
    2 -> R.string.detail_source_smb
    3 -> R.string.detail_source_cache
    else -> R.string.detail_source_online
}

/** 缺值统一画一个破折号：空行会让人以为页面坏了，`0` 又会被当成真实数据 */
private const val DASH = "—"

/**
 * 歌曲详情 —— 对应 MAUI `SongDetailPage`。
 *
 * 竖屏：左封面右标题的紧凑首屏 → 操作行 → 歌词预览 → 「歌曲信息」方块网格。
 * 横屏：封面 / 歌词 / 参数三段并置，各自独立滚动（412 的高度全给内容）。
 * 在线曲不画三个编辑入口，来源显示「在线」，其余字段按「—」占位。
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

    // 整页覆盖层：让外壳收起底栏与迷你条（歌词/封面匹配、元数据编辑也叠在这层里）
    val requestFullScreen = LocalFullScreenRequest.current
    val fullScreenToken = remember { Any() }
    DisposableEffect(fullScreenToken) {
        requestFullScreen(fullScreenToken, true)
        onDispose { requestFullScreen(fullScreenToken, false) }
    }

    val editEnabled = canEditTags(detail?.filePath)
    val lyricsMatch: () -> Unit = {
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
    }
    val isLandscape = LocalConfiguration.current.orientation == Configuration.ORIENTATION_LANDSCAPE

    if (isLandscape) {
        // ── 横屏：封面 / 歌词 / 参数 三段并置 ──
        // 外壳横屏会留出左侧导航栏（约 108dp），真正可用的宽度常常不到演示稿的 915，
        // 所以三段宽度按可用宽度取比例上限；右栏太窄时方块从 3 列退到 2 列，
        // 宁可多滚一点，也不把「3326 kbps」挤成三行。
        BoxWithConstraints(
            modifier = Modifier
                .fillMaxSize()
                .statusBarsPadding()
                .navigationBarsPadding(),
        ) {
            val leftWidth = (maxWidth * 0.30f).coerceAtMost(262.dp)
            val midWidth = (maxWidth * 0.34f).coerceAtMost(286.dp)
            val rightWidth = maxWidth - leftWidth - midWidth - 2.dp
            val gridColumns = if (rightWidth < 300.dp) 2 else 3
            Row(modifier = Modifier.fillMaxSize()) {
                Column(
                    modifier = Modifier
                        .width(leftWidth)
                        .fillMaxHeight()
                        .padding(start = 14.dp, end = 13.dp, top = 4.dp, bottom = 13.dp),
                    horizontalAlignment = Alignment.CenterHorizontally,
                ) {
                    Row(
                        modifier = Modifier.fillMaxWidth(),
                        verticalAlignment = Alignment.CenterVertically,
                    ) {
                        BackTile(onBack = onBack, description = stringResource(R.string.GoBack))
                        Spacer(Modifier.width(8.dp))
                        Text(
                            text = stringResource(R.string.song_detail_title),
                            fontSize = 16.sp,
                            fontWeight = FontWeight.Bold,
                            color = primary,
                            maxLines = 1,
                            overflow = TextOverflow.Ellipsis,
                        )
                        Spacer(Modifier.weight(1f))
                        if (detail != null) {
                            MoreTile(compact = true, onClick = { menuOpen = true })
                        }
                    }
                    if (copied) {
                        Text(
                            text = stringResource(R.string.detail_path_copied),
                            fontSize = 11.sp,
                            color = secondary,
                            modifier = Modifier
                                .fillMaxWidth()
                                .padding(top = 4.dp),
                        )
                    }
                    // 三个编辑入口在横屏挪到右栏「歌曲信息」的标题行（见下方 InfoCard）。
                    // 左栏这边把封面组在剩余高度里居中，底部不再压一排按钮
                    Column(
                        modifier = Modifier
                            .fillMaxWidth()
                            .weight(1f),
                        horizontalAlignment = Alignment.CenterHorizontally,
                        verticalArrangement = Arrangement.Center,
                    ) {
                        CoverArtBox(detail = detail, size = 152.dp, corner = 18.dp)
                        Spacer(Modifier.height(10.dp))
                        Text(
                            text = detail?.title ?: stringResource(R.string.detail_loading),
                            fontSize = 17.sp,
                            fontFamily = FontFamily.Serif,
                            fontWeight = FontWeight.SemiBold,
                            lineHeight = 22.sp,
                            color = primary,
                            textAlign = TextAlign.Center,
                            maxLines = 2,
                            overflow = TextOverflow.Ellipsis,
                        )
                        Spacer(Modifier.height(3.dp))
                        Text(
                            text = detail?.artist.orEmpty(),
                            fontSize = 11.sp,
                            color = secondary,
                            textAlign = TextAlign.Center,
                            maxLines = 1,
                            overflow = TextOverflow.Ellipsis,
                        )
                        Spacer(Modifier.height(8.dp))
                        MetaChips(detail = detail, compact = true)
                    }
                }
                PaneDivider()
                Column(
                    modifier = Modifier
                        .width(midWidth)
                        .fillMaxHeight()
                        .padding(start = 13.dp, end = 12.dp, top = 4.dp, bottom = 13.dp),
                ) {
                    LyricsCard(
                        lyrics = lyrics,
                        compact = true,
                        modifier = Modifier.fillMaxSize(),
                    )
                }
                PaneDivider()
                Column(
                    modifier = Modifier
                        .weight(1f)
                        .fillMaxHeight()
                        .padding(start = 12.dp, end = 14.dp, top = 4.dp, bottom = 13.dp),
                ) {
                    InfoCard(
                        detail = detail,
                        compact = true,
                        showPath = false,
                        columns = gridColumns,
                        modifier = Modifier.fillMaxSize(),
                        actions = if (editEnabled) {
                            {
                                InfoIconAction(
                                    icon = R.drawable.ic_nav_lyrics,
                                    description = stringResource(R.string.lyrics_match_entry),
                                    enabled = detail != null,
                                    onClick = lyricsMatch,
                                )
                                InfoIconAction(
                                    icon = R.drawable.ic_album,
                                    description = stringResource(R.string.cover_match_entry),
                                    enabled = detail != null,
                                    onClick = { coverMatchOpen = true },
                                )
                                InfoIconAction(
                                    icon = R.drawable.ic_edit,
                                    description = stringResource(R.string.tag_edit_entry),
                                    enabled = detail != null,
                                    onClick = { tagEditOpen = true },
                                )
                            }
                        } else {
                            null
                        },
                    )
                }
            }
        }
    } else {
        // ── 竖屏：紧凑纵向 ──
        Column(
            modifier = Modifier
                .fillMaxSize()
                .statusBarsPadding()
                .navigationBarsPadding()
                .verticalScroll(rememberScrollState())
                .padding(horizontal = 16.dp),
        ) {
            Spacer(Modifier.height(12.dp))
            Row(verticalAlignment = Alignment.CenterVertically) {
                BackTile(onBack = onBack, description = stringResource(R.string.GoBack))
                Spacer(Modifier.width(12.dp))
                Text(
                    text = stringResource(R.string.song_detail_title),
                    fontSize = 20.sp,
                    fontWeight = FontWeight.Bold,
                    color = primary,
                    maxLines = 1,
                    overflow = TextOverflow.Ellipsis,
                )
                Spacer(Modifier.weight(1f))
                // ⋮ 菜单：MAUI `SongDetailPage` 右上角那颗「更多」（播放此曲 / 复制文件路径）。
                // 详情行没到手就不画：那两项此刻都点不动
                if (detail != null) {
                    MoreTile(compact = false, onClick = { menuOpen = true })
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
            Spacer(Modifier.height(12.dp))

            // 封面与标题并排：封面不再独占首屏
            Row(
                modifier = Modifier.fillMaxWidth(),
                verticalAlignment = Alignment.Bottom,
            ) {
                CoverArtBox(detail = detail, size = 148.dp, corner = 18.dp)
                Spacer(Modifier.width(14.dp))
                Column(modifier = Modifier.weight(1f)) {
                    Text(
                        text = detail?.title ?: stringResource(R.string.detail_loading),
                        fontSize = 21.sp,
                        fontFamily = FontFamily.Serif,
                        fontWeight = FontWeight.SemiBold,
                        lineHeight = 27.sp,
                        color = primary,
                        maxLines = 2,
                        overflow = TextOverflow.Ellipsis,
                    )
                    Spacer(Modifier.height(4.dp))
                    Text(
                        text = detail?.artist.orEmpty(),
                        fontSize = 12.sp,
                        color = secondary,
                        maxLines = 1,
                        overflow = TextOverflow.Ellipsis,
                    )
                    Spacer(Modifier.height(9.dp))
                    MetaChips(detail = detail, compact = false)
                }
            }

            // 只有真实本地文件才画：在线曲（网易云）与远程曲库是 http/smb 流地址，
            // 写标签/封面/歌词一律失败，留着入口只会让用户白点一次。
            if (editEnabled) {
                Spacer(Modifier.height(12.dp))
                ActionsRow(
                    enabled = detail != null,
                    compact = false,
                    onLyricsMatch = lyricsMatch,
                    onCoverMatch = { coverMatchOpen = true },
                    onEditTags = { tagEditOpen = true },
                )
            }

            Spacer(Modifier.height(10.dp))
            LyricsCard(lyrics = lyrics, compact = false)
            Spacer(Modifier.height(10.dp))
            InfoCard(detail = detail, compact = false, showPath = true)
            // 整页覆盖时底栏与迷你条都已收起，只留一点呼吸空间
            Spacer(Modifier.height(20.dp))
        }
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

/** 封面 + 背后一圈呼吸光晕（演示稿里的暖调辉光，用主题主色画，避免写死颜色） */
@Composable
private fun CoverArtBox(detail: SongDetailRow?, size: Dp, corner: Dp) {
    val palette = LocalCatClawPalette.current
    val glow = Color(palette.primary)
    Box(modifier = Modifier.size(size), contentAlignment = Alignment.Center) {
        Box(
            modifier = Modifier
                .matchParentSize()
                .background(
                    Brush.radialGradient(
                        listOf(glow.copy(alpha = 0.20f), Color.Transparent),
                    ),
                    RoundedCornerShape(corner + 12.dp),
                ),
        )
        CoverTile(
            coverArtPath = detail?.coverArtPath,
            audioPath = detail?.filePath,
            maxPx = 512,
            modifier = Modifier
                .fillMaxSize()
                .clip(RoundedCornerShape(corner)),
        ) {
            Text(
                text = detail?.title?.take(1) ?: "?",
                fontSize = (size.value * 0.27f).sp,
                fontWeight = FontWeight.Bold,
                color = Color(palette.textPrimary),
            )
        }
    }
}

/** 来源 / 格式 / 时长：三个小胶囊，信息密度比一行「歌手 · 专辑」高 */
@Composable
private fun MetaChips(detail: SongDetailRow?, compact: Boolean) {
    val palette = LocalCatClawPalette.current
    Row(horizontalArrangement = Arrangement.spacedBy(if (compact) 5.dp else 6.dp)) {
        MetaChip(
            text = detail?.let { stringResource(sourceLabelRes(it.source)) } ?: DASH,
            dotColor = Color(palette.primary),
            compact = compact,
        )
        MetaChip(
            text = SongDetailFormat.fileFormat(detail?.filePath).ifBlank { DASH },
            compact = compact,
        )
        MetaChip(
            text = SongDetailFormat.duration(detail?.durationSeconds ?: 0).ifBlank { DASH },
            compact = compact,
        )
    }
}

@Composable
private fun MetaChip(text: String, compact: Boolean, dotColor: Color? = null) {
    val palette = LocalCatClawPalette.current
    val ink = Color(palette.textPrimary)
    val shape = RoundedCornerShape(999.dp)
    Row(
        modifier = Modifier
            .clip(shape)
            .background(ink.copy(alpha = 0.05f))
            .border(1.dp, ink.copy(alpha = 0.12f), shape)
            .height(if (compact) 21.dp else 23.dp)
            .padding(horizontal = if (compact) 8.dp else 9.dp),
        verticalAlignment = Alignment.CenterVertically,
        horizontalArrangement = Arrangement.spacedBy(5.dp),
    ) {
        if (dotColor != null) {
            Box(Modifier.size(6.dp).background(dotColor, CircleShape))
        }
        Text(
            text = text,
            fontSize = if (compact) 9.5.sp else 10.sp,
            fontFamily = FontFamily.Monospace,
            letterSpacing = 0.3.sp,
            color = Color(palette.textSecondary),
            maxLines = 1,
        )
    }
}

/** 三个等宽入口。在线曲不进来（调用方已按 canEditTags 判断） */
@Composable
private fun ActionsRow(
    enabled: Boolean,
    compact: Boolean,
    onLyricsMatch: () -> Unit,
    onCoverMatch: () -> Unit,
    onEditTags: () -> Unit,
    modifier: Modifier = Modifier,
) {
    Row(
        modifier = modifier.fillMaxWidth(),
        horizontalArrangement = Arrangement.spacedBy(if (compact) 6.dp else 7.dp),
    ) {
        DetailActionTile(
            icon = R.drawable.ic_nav_lyrics,
            label = stringResource(R.string.lyrics_match_entry),
            enabled = enabled,
            compact = compact,
            modifier = Modifier.weight(1f),
            onClick = onLyricsMatch,
        )
        DetailActionTile(
            icon = R.drawable.ic_album,
            label = stringResource(R.string.cover_match_entry),
            enabled = enabled,
            compact = compact,
            modifier = Modifier.weight(1f),
            onClick = onCoverMatch,
        )
        DetailActionTile(
            icon = R.drawable.ic_edit,
            label = stringResource(R.string.tag_edit_entry),
            enabled = enabled,
            compact = compact,
            modifier = Modifier.weight(1f),
            onClick = onEditTags,
        )
    }
}

/**
 * 歌词卡。
 *
 * 竖屏：预览 112dp + 底部渐隐 + 「查看全部 N 行」展开（这一页没有独立的歌词页可跳，
 * 展开就是全部歌词，收起再点一次）。
 * 横屏：卡片撑满中段，歌词区占满剩余高度并独立滚动。
 */
@Composable
private fun LyricsCard(
    lyrics: List<String>,
    compact: Boolean,
    modifier: Modifier = Modifier,
) {
    val palette = LocalCatClawPalette.current
    val primary = Color(palette.textPrimary)
    val secondary = Color(palette.textSecondary)
    var expanded by remember { mutableStateOf(false) }
    Column(
        modifier = modifier
            .fillMaxWidth()
            .glassCard(palette = palette, corner = if (compact) 18 else 20)
            .padding(
                horizontal = if (compact) 12.dp else 13.dp,
                vertical = if (compact) 11.dp else 12.dp,
            ),
    ) {
        Row(verticalAlignment = Alignment.CenterVertically) {
            Text(
                text = stringResource(R.string.detail_lyrics),
                fontSize = if (compact) 12.sp else 13.sp,
                fontWeight = FontWeight.SemiBold,
                letterSpacing = 0.5.sp,
                color = primary,
            )
            if (lyrics.isNotEmpty() && !compact) {
                Spacer(Modifier.width(8.dp))
                Text(
                    text = stringResource(R.string.detail_lyrics_scroll),
                    fontSize = 9.5.sp,
                    color = secondary,
                    modifier = Modifier
                        .clip(RoundedCornerShape(999.dp))
                        .background(primary.copy(alpha = 0.07f))
                        .padding(horizontal = 7.dp, vertical = 2.dp),
                )
            }
            Spacer(Modifier.weight(1f))
            if (lyrics.isNotEmpty()) {
                Text(
                    text = lyrics.size.toString(),
                    fontSize = 10.sp,
                    fontFamily = FontFamily.Monospace,
                    color = secondary.copy(alpha = 0.75f),
                )
            }
        }
        Spacer(Modifier.height(8.dp))

        if (lyrics.isEmpty()) {
            Text(
                text = stringResource(R.string.lyric_none),
                fontSize = 12.5.sp,
                color = secondary,
                textAlign = TextAlign.Center,
                modifier = Modifier
                    .fillMaxWidth()
                    .padding(vertical = 26.dp),
            )
        } else {
            val previewHeight = if (compact) 0.dp else if (expanded) 320.dp else 112.dp
            Box(
                modifier = if (compact) {
                    Modifier
                        .fillMaxWidth()
                        .weight(1f)
                } else {
                    Modifier
                        .fillMaxWidth()
                        .height(previewHeight)
                },
            ) {
                Column(
                    modifier = Modifier
                        .fillMaxSize()
                        .verticalScroll(rememberScrollState()),
                ) {
                    lyrics.forEach { line ->
                        Text(
                            text = line,
                            fontSize = if (compact) 12.sp else 12.5.sp,
                            lineHeight = if (compact) 20.sp else 22.sp,
                            color = secondary,
                            textAlign = TextAlign.Center,
                            modifier = Modifier
                                .fillMaxWidth()
                                .padding(horizontal = 4.dp, vertical = if (compact) 4.dp else 5.dp),
                        )
                    }
                }
                if (!compact && !expanded) {
                    // 底部渐隐：暗示下面还有词，而不是被裁断了
                    Box(
                        modifier = Modifier
                            .align(Alignment.BottomCenter)
                            .fillMaxWidth()
                            .height(52.dp)
                            .background(
                                Brush.verticalGradient(
                                    listOf(
                                        Color.Transparent,
                                        Color(palette.backgroundStops.last().argb).copy(alpha = 0.92f),
                                    ),
                                ),
                            ),
                    )
                }
            }
            if (!compact) {
                Spacer(Modifier.height(2.dp))
                Row(
                    modifier = Modifier
                        .fillMaxWidth()
                        .clip(RoundedCornerShape(10.dp))
                        .clickable { expanded = !expanded }
                        .padding(vertical = 6.dp),
                    horizontalArrangement = Arrangement.Center,
                    verticalAlignment = Alignment.CenterVertically,
                ) {
                    Text(
                        text = if (expanded) {
                            stringResource(R.string.detail_lyrics_collapse)
                        } else {
                            stringResource(R.string.detail_lyrics_view_all, lyrics.size)
                        },
                        fontSize = 11.sp,
                        color = secondary,
                    )
                    Spacer(Modifier.width(5.dp))
                    Icon(
                        painter = painterResource(R.drawable.ic_nav_chevron),
                        contentDescription = null,
                        tint = secondary.copy(alpha = 0.8f),
                        modifier = Modifier
                            .size(12.dp)
                            .rotate(if (expanded) -90f else 90f),
                    )
                }
            }
        }
    }
}

/** 一条信息格子：标签在上、值在下，值用等宽字，长内容最多两行 */
private data class InfoBlock(
    val label: String,
    val value: String,
    val wide: Boolean = false,
    val path: Boolean = false,
)

/**
 * 歌曲信息：基本信息与文件音质合成一张卡，每条一个方块。
 *
 * 竖屏 3 列，专辑 / 艺术家 / 添加时间 / 文件路径占整行；
 * 横屏同样 3 列铺满右栏，只省掉长路径（那行在这里没人读得完）。
 */
@Composable
private fun InfoCard(
    detail: SongDetailRow?,
    compact: Boolean,
    showPath: Boolean,
    columns: Int = 3,
    modifier: Modifier = Modifier,
    /** 横屏用：三个元数据入口跟着「歌曲信息」走，钉在标题行右侧，不随方块一起滚走 */
    actions: (@Composable () -> Unit)? = null,
) {
    val palette = LocalCatClawPalette.current
    val primary = Color(palette.textPrimary)
    val items = buildList {
        add(InfoBlock(stringResource(R.string.detail_album), detail?.album.orDash(), wide = true))
        add(InfoBlock(stringResource(R.string.detail_artist), detail?.artist.orDash(), wide = true))
        add(InfoBlock(stringResource(R.string.detail_genre), detail?.genre.orDash()))
        add(InfoBlock(stringResource(R.string.detail_year), SongDetailFormat.year(detail?.year ?: 0).orDash()))
        add(InfoBlock(stringResource(R.string.detail_track), SongDetailFormat.track(detail?.trackNumber ?: 0).orDash()))
        add(InfoBlock(stringResource(R.string.detail_duration), SongDetailFormat.duration(detail?.durationSeconds ?: 0).orDash()))
        add(InfoBlock(stringResource(R.string.detail_bitrate), SongDetailFormat.bitrate(detail?.bitrate ?: 0).orDash()))
        add(InfoBlock(stringResource(R.string.detail_format), SongDetailFormat.fileFormat(detail?.filePath).orDash()))
        add(InfoBlock(stringResource(R.string.detail_size), SongDetailFormat.bytes(detail?.fileSize ?: 0L).orDash()))
        add(
            InfoBlock(
                stringResource(R.string.detail_source),
                detail?.let { stringResource(sourceLabelRes(it.source)) } ?: DASH,
            ),
        )
        add(
            InfoBlock(
                stringResource(R.string.detail_play_count),
                detail?.playCount?.takeIf { it > 0 }?.toString() ?: DASH,
            ),
        )
        add(
            InfoBlock(
                stringResource(R.string.detail_date_added),
                SongDetailFormat.date(detail?.dateAdded ?: 0L).orDash(),
                wide = true,
            ),
        )
        if (showPath) {
            add(
                InfoBlock(
                    stringResource(R.string.detail_file_path),
                    detail?.filePath.orDash(),
                    wide = true,
                    path = true,
                ),
            )
        }
    }
    Column(
        modifier = modifier
            .fillMaxWidth()
            .glassCard(palette = palette, corner = if (compact) 18 else 20)
            .padding(horizontal = 13.dp, vertical = if (compact) 11.dp else 12.dp),
    ) {
        Row(verticalAlignment = Alignment.CenterVertically) {
            Text(
                text = stringResource(R.string.song_menu_detail),
                fontSize = if (compact) 12.sp else 13.sp,
                fontWeight = FontWeight.SemiBold,
                letterSpacing = 0.6.sp,
                color = primary,
            )
            if (actions != null) {
                Spacer(Modifier.weight(1f))
                Row(horizontalArrangement = Arrangement.spacedBy(6.dp)) {
                    actions()
                }
            }
        }
        Spacer(Modifier.height(if (compact) 8.dp else 9.dp))
        if (compact) {
            // 卡片撑满右栏，只有方块区滚动：标题行和三个入口始终可见
            InfoGrid(
                items = items,
                compact = compact,
                columns = columns,
                modifier = Modifier
                    .fillMaxWidth()
                    .weight(1f)
                    .verticalScroll(rememberScrollState()),
            )
        } else {
            InfoGrid(items = items, compact = compact, columns = columns)
        }
    }
}

@Composable
private fun InfoGrid(
    items: List<InfoBlock>,
    compact: Boolean,
    columns: Int,
    modifier: Modifier = Modifier,
) {
    val gap = if (compact) 6.dp else 7.dp
    val rows = remember(items, columns) { buildInfoRows(items, columns) }
    Column(modifier = modifier, verticalArrangement = Arrangement.spacedBy(gap)) {
        rows.forEach { row ->
            Row(
                modifier = Modifier.fillMaxWidth(),
                horizontalArrangement = Arrangement.spacedBy(gap),
            ) {
                row.forEach { cell ->
                    if (cell == null) {
                        Spacer(Modifier.weight(1f))
                    } else {
                        InfoBlockCell(block = cell, compact = compact, modifier = Modifier.weight(1f))
                    }
                }
            }
        }
    }
}

/** 满行条目单独占一行，零散条目按 [columns] 个一排；最后一排不足就用空权重补齐，宽度仍然对齐 */
private fun buildInfoRows(items: List<InfoBlock>, columns: Int): List<List<InfoBlock?>> {
    val rows = mutableListOf<List<InfoBlock?>>()
    var pending = mutableListOf<InfoBlock>()
    for (item in items) {
        if (item.wide) {
            if (pending.isNotEmpty()) {
                rows += pending.toList() + List(columns - pending.size) { null }
                pending = mutableListOf()
            }
            rows += listOf(item)
        } else {
            pending += item
            if (pending.size == columns) {
                rows += pending.toList()
                pending = mutableListOf()
            }
        }
    }
    if (pending.isNotEmpty()) rows += pending.toList() + List(columns - pending.size) { null }
    return rows
}

@Composable
private fun InfoBlockCell(block: InfoBlock, compact: Boolean, modifier: Modifier = Modifier) {
    val palette = LocalCatClawPalette.current
    val ink = Color(palette.textPrimary)
    val shape = RoundedCornerShape(if (compact) 12.dp else 14.dp)
    Column(
        modifier = modifier
            .clip(shape)
            .background(ink.copy(alpha = if (block.path) 0.04f else 0.06f))
            .border(1.dp, ink.copy(alpha = 0.10f), shape)
            // 定高而不是 minHeight：同一排里值有长有短，格子高度不齐会像没对齐的表格
            .height(if (compact) 50.dp else 58.dp)
            .padding(
                horizontal = if (compact) 9.dp else 10.dp,
                vertical = if (compact) 6.dp else 9.dp,
            ),
        verticalArrangement = Arrangement.SpaceBetween,
    ) {
        Text(
            text = block.label,
            fontSize = if (compact) 9.sp else 9.5.sp,
            letterSpacing = 0.4.sp,
            color = Color(palette.textSecondary).copy(alpha = 0.75f),
            maxLines = 1,
            overflow = TextOverflow.Ellipsis,
        )
        Text(
            text = block.value,
            fontSize = if (block.path) 10.sp else if (compact) 10.5.sp else 11.sp,
            fontFamily = FontFamily.Monospace,
            lineHeight = if (compact) 14.sp else 15.sp,
            color = Color(palette.textSecondary).copy(alpha = if (block.path) 0.7f else 1f),
            maxLines = 2,
            overflow = TextOverflow.Ellipsis,
        )
    }
}

@Composable
private fun PaneDivider() {
    val ink = Color(LocalCatClawPalette.current.textPrimary)
    Box(
        Modifier
            .width(1.dp)
            .fillMaxHeight()
            .background(ink.copy(alpha = 0.08f)),
    )
}

@Composable
private fun MoreTile(compact: Boolean, onClick: () -> Unit) {
    val palette = LocalCatClawPalette.current
    val ink = Color(palette.textPrimary)
    val shape = RoundedCornerShape(if (compact) 13.dp else 14.dp)
    Box(
        modifier = Modifier
            .size(if (compact) 36.dp else 40.dp)
            .clip(shape)
            .background(ink.copy(alpha = 0.08f))
            .border(1.dp, ink.copy(alpha = 0.14f), shape)
            .clickable(onClick = onClick),
        contentAlignment = Alignment.Center,
    ) {
        Icon(
            painter = painterResource(R.drawable.ic_more),
            contentDescription = stringResource(R.string.playing_more),
            tint = ink,
            modifier = Modifier.size(if (compact) 18.dp else 20.dp),
        )
    }
}

/**
 * 横屏右栏标题行里的元数据入口：只有图标 + contentDescription。
 *
 * 为什么从竖屏那排带文字的方块改成小图标：横屏左栏高度不够，一排按钮贴在底部
 * 既压住封面组、又和「歌曲信息」里的数据离得远。放到信息卡标题行后，
 * 「改的是这张卡里的东西」一眼可读，左栏也能把封面组居中。
 */
@Composable
private fun InfoIconAction(
    @DrawableRes icon: Int,
    description: String,
    enabled: Boolean,
    onClick: () -> Unit,
) {
    val palette = LocalCatClawPalette.current
    val ink = Color(palette.textPrimary)
    val shape = RoundedCornerShape(10.dp)
    val alpha = if (enabled) 1f else 0.38f
    Box(
        modifier = Modifier
            .size(34.dp)
            .clip(shape)
            .background(ink.copy(alpha = 0.06f))
            .border(1.dp, ink.copy(alpha = 0.12f), shape)
            .clickable(enabled = enabled, onClick = onClick),
        contentAlignment = Alignment.Center,
    ) {
        Icon(
            painter = painterResource(icon),
            contentDescription = description,
            tint = Color(highlightOnGlassCard(palette)).copy(alpha = alpha),
            modifier = Modifier.size(16.dp),
        )
    }
}

private fun String?.orDash(): String = this?.takeIf { it.isNotBlank() } ?: DASH

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
    compact: Boolean = false,
    modifier: Modifier = Modifier,
    onClick: () -> Unit,
) {
    val palette = LocalCatClawPalette.current
    val ink = Color(palette.textPrimary)
    val alpha = if (enabled) 1f else 0.38f
    val shape = RoundedCornerShape(if (compact) 12.dp else 14.dp)
    Column(
        modifier = modifier
            .clip(shape)
            .background(ink.copy(alpha = 0.06f))
            .border(1.dp, ink.copy(alpha = 0.12f), shape)
            .clickable(enabled = enabled, onClick = onClick)
            .padding(vertical = if (compact) 8.dp else 10.dp, horizontal = 3.dp),
        horizontalAlignment = Alignment.CenterHorizontally,
        verticalArrangement = Arrangement.Center,
    ) {
        Icon(
            painter = painterResource(icon),
            contentDescription = null,
            tint = Color(highlightOnGlassCard(palette)).copy(alpha = alpha),
            modifier = Modifier.size(if (compact) 14.dp else 15.dp),
        )
        Spacer(Modifier.height(if (compact) 5.dp else 6.dp))
        Text(
            text = label,
            fontSize = if (compact) 9.5.sp else 10.5.sp,
            fontWeight = FontWeight.Medium,
            color = ink.copy(alpha = 0.92f * alpha),
            maxLines = 1,
            overflow = TextOverflow.Ellipsis,
            textAlign = TextAlign.Center,
        )
    }
}
