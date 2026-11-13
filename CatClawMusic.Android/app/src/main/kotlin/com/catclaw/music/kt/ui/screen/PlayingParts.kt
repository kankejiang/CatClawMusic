package com.catclaw.music.kt.ui.screen

import android.Manifest
import android.content.res.Configuration
import androidx.annotation.StringRes
import android.content.pm.PackageManager
import android.os.Build
import androidx.activity.compose.BackHandler
import androidx.activity.compose.rememberLauncherForActivityResult
import androidx.activity.result.contract.ActivityResultContracts
import androidx.core.content.ContextCompat
import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.Image
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.lazy.itemsIndexed
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.aspectRatio
import androidx.compose.foundation.layout.BoxWithConstraints
import androidx.compose.foundation.layout.fillMaxHeight
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.statusBarsPadding
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.gestures.detectHorizontalDragGestures
import androidx.compose.foundation.gestures.detectTapGestures
import androidx.compose.ui.draw.drawBehind
import androidx.compose.ui.geometry.CornerRadius
import androidx.compose.ui.geometry.Offset
import androidx.compose.ui.geometry.Size
import androidx.compose.ui.input.pointer.pointerInput
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material3.Icon
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Slider
import androidx.compose.material3.SliderDefaults
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.DisposableEffect
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableFloatStateOf
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.draw.clipToBounds
import androidx.compose.ui.draw.shadow
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.graphicsLayer
import androidx.compose.ui.graphics.asImageBitmap
import androidx.compose.ui.layout.ContentScale
import androidx.compose.ui.platform.LocalConfiguration
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.res.painterResource
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.Dp
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import androidx.hilt.navigation.compose.hiltViewModel
import kotlinx.coroutines.flow.StateFlow
import androidx.lifecycle.compose.collectAsStateWithLifecycle
import com.catclaw.music.kt.R
import com.catclaw.music.kt.ui.LocalSleepTimerOpener
import com.catclaw.music.kt.core.lyrics.LyricsState
import com.catclaw.music.kt.core.playback.PlayMode
import com.catclaw.music.kt.ui.library.LibraryViewModel
import com.catclaw.music.kt.ui.lyrics.PlayingLyricLine
import com.catclaw.music.kt.ui.lyrics.LyricsViewModel
import com.catclaw.music.kt.ui.playback.PlayingViewModel
import com.catclaw.music.kt.ui.player.Icon
import com.catclaw.music.kt.ui.player.PlayerIcon
import com.catclaw.music.kt.ui.player.PlayerIcons
import com.catclaw.music.kt.ui.player.SleepTimerDialog
import com.catclaw.music.kt.ui.player.rememberCover
import com.catclaw.music.kt.ui.song.SongDetailScreen
import com.catclaw.music.kt.ui.player.formatCountdown
import com.catclaw.music.kt.ui.song.SongMenuFor
import com.catclaw.music.kt.ui.song.SongMenuItem
import com.catclaw.music.kt.ui.song.SongMenuSection
import com.catclaw.music.kt.ui.song.SongMenuTarget
import com.catclaw.music.kt.ui.song.rememberSongMenu
import com.catclaw.music.kt.ui.song.songMenuSubtitle
import com.catclaw.music.kt.ui.theme.LocalCatClawPalette
import com.catclaw.music.kt.ui.theme.artworkBackdrop
import com.catclaw.music.kt.ui.theme.VinylCover
import com.catclaw.music.kt.ui.theme.artworkWash

/** 播放页的封面/标题/横屏体/进度条等组件（从 PlayingScreen.kt 拆出） */
@Composable
internal fun CoverSquare(side: Dp, cover: android.graphics.Bitmap?, placeholder: String) {
    val palette = LocalCatClawPalette.current
    if (cover == null) {
        // 没封面：画一张代码生成的**唱片碟**占位（MAUI DefaultCoverService.VinylDrawable 的移植）。
        // 原来是「半透明灰底 + 歌名首字」，看起来像图片加载失败（2026-10-19 用户反馈），
        // 而且那个「首字」取自「艺术家 - 歌名」拼串，本身也没有意义。
        // 唱片碟自带圆角与投影，所以这条分支不再套 clip/半透明底。
        VinylCover(
            modifier = Modifier
                .size(side)
                .shadow(26.dp, RoundedCornerShape(14.dp)),
            primary = Color(palette.primary),
        )
        return
    }
    Box(
        modifier = Modifier
            .size(side)
            // Apple 的封面是「小圆角 + 重投影」，不是描边；投影把封面从染色的环境里抬出来
            .shadow(26.dp, RoundedCornerShape(14.dp))
            .clip(RoundedCornerShape(14.dp))
            .background(Color(palette.textPrimary).copy(alpha = 0.10f)),
        contentAlignment = Alignment.Center,
    ) {
        Image(
            bitmap = cover.asImageBitmap(),
            contentDescription = null,
            contentScale = ContentScale.Crop,
            modifier = Modifier.matchParentSize(),
        )
    }
}

/** 标题 / 歌手 / 错误行：两栏布局共用，左对齐到所在栏的边距。[compact] 给横屏——那窗口只有 1440px 高 */
@Composable
internal fun TitleBlock(title: String?, artist: String?, error: String?, compact: Boolean = false) {
    val palette = LocalCatClawPalette.current
    Column(modifier = Modifier.fillMaxWidth()) {
        Text(
            text = title ?: stringResource(R.string.m1_nothing),
            fontSize = if (compact) 18.sp else 22.sp,
            // 播放页头部要压得住雾底：加粗到 ExtraBold（compact 分支是横屏右栏，保持 Bold）
            fontWeight = if (compact) FontWeight.Bold else FontWeight.ExtraBold,
            color = Color(palette.textPrimary),
            maxLines = 1,
            overflow = TextOverflow.Ellipsis,
        )
        Text(
            text = artist ?: "",
            fontSize = if (compact) 13.sp else 14.sp,
            color = Color(palette.textSecondary),
            maxLines = 1,
            overflow = TextOverflow.Ellipsis,
            modifier = Modifier.padding(top = 3.dp),
        )
        error?.let {
            Text(
                text = it,
                style = MaterialTheme.typography.bodySmall,
                color = Color(palette.textPrimary),
                maxLines = 2,
                overflow = TextOverflow.Ellipsis,
            )
        }
    }
}

/**
 * 横屏（窗口宽 > 高，含平板与折叠屏展开）的播放页中栏：左封面 + 右「标题·歌手 / 当前这一句词」。
 * 形状照 MAUI `NowPlayingPage` 的 `LandscapeRoot`（左封面、右信息），但右栏放的是**一句词而不是整首列表**：
 * 真机量过，1440px 高的横屏窗口里中间那一栏只剩 ~300px，标题块自己就占掉 216px，
 * 塞滚动列表（或带译文/罗马音副行的 [LyricLineText]）会被压成一条看不见的窄缝——
 * 整首词在横滑到的全屏歌词页里本来就有，那一页在横屏是通栏的，比在这里挤一个残破的列表更合适。
 * 歌词数据走与全屏歌词页同一个 [LyricsViewModel]（同一批 Room / 控制器流），点这一句 = seek 回这一句起点。
 */
@Composable
internal fun LandscapeBody(
    side: Dp,
    cover: android.graphics.Bitmap?,
    placeholder: String,
    title: String?,
    artist: String?,
    error: String?,
    positionMsFlow: StateFlow<Long>,
    lyricsViewModel: LyricsViewModel = hiltViewModel(),
) {
    val palette = LocalCatClawPalette.current
    val lyrics by lyricsViewModel.lyrics.collectAsStateWithLifecycle()
    val lineMode by lyricsViewModel.lineMode.collectAsStateWithLifecycle()
    val positionMs by positionMsFlow.collectAsStateWithLifecycle()
    val presentation = LyricsState.present(
        lyrics = lyricsViewModel.visibleLyrics(),
        positionMs = positionMs,
        lineMode = lineMode,
    )
    val line = presentation?.line?.takeIf { it.text.isNotBlank() }

    Row(
        modifier = Modifier.fillMaxSize(),
        verticalAlignment = Alignment.CenterVertically,
        horizontalArrangement = Arrangement.spacedBy(26.dp),
    ) {
        CoverSquare(side = side, cover = cover, placeholder = placeholder)
        Column(modifier = Modifier.weight(1f)) {
            TitleBlock(title = title, artist = artist, error = error, compact = true)
            Spacer(Modifier.height(6.dp))
            Text(
                text = line?.text ?: stringResource(R.string.lyric_none),
                fontSize = if (line == null) 13.sp else 17.sp,
                fontWeight = if (line == null) FontWeight.Normal else FontWeight.Medium,
                color = if (line == null) Color(palette.textSecondary) else Color(palette.textPrimary),
                maxLines = 1,
                overflow = TextOverflow.Ellipsis,
                modifier = Modifier
                    .fillMaxWidth()
                    .clip(RoundedCornerShape(8.dp))
                    .clickable(enabled = line != null) { line?.let { lyricsViewModel.seekTo(it.startMs) } }
                    .padding(vertical = 4.dp),
            )
        }
    }
}

// 这里原来有一个 ActionIcon（40dp 触达框的点亮式图标）。四颗粒子收进「更多」菜单后它没有调用方了，
// 一并删掉：留着它只会让下一个人以为播放页还有一排图标位可挂。

@Composable
internal fun IconButton40(
    icon: PlayerIcon,
    description: String,
    tint: Color,
    onClick: () -> Unit,
) {
    Box(
        modifier = Modifier
            .size(44.dp)
            .clickable(onClick = onClick),
        contentAlignment = Alignment.Center,
    ) {
        Icon(imageVector = icon, contentDescription = description, tint = tint, modifier = Modifier.size(22.dp))
    }
}

@Composable
internal fun IconButton52(
    icon: PlayerIcon,
    description: String,
    tint: Color,
    onClick: () -> Unit,
) {
    Box(
        modifier = Modifier
            .size(56.dp)
            .clickable(onClick = onClick),
        contentAlignment = Alignment.Center,
    ) {
        Icon(imageVector = icon, contentDescription = description, tint = tint, modifier = Modifier.size(32.dp))
    }
}

@Composable
internal fun modeIcon(mode: PlayMode, isFmMode: Boolean): PlayerIcon = when (mode) {
    PlayMode.Sequential -> if (isFmMode) PlayerIcons.Infinite else PlayerIcons.Sequential
    PlayMode.Shuffle -> PlayerIcons.Shuffle
    PlayMode.SingleRepeat -> PlayerIcons.RepeatOne
    PlayMode.ListRepeat -> PlayerIcons.Repeat
}

/** 播报当前档位而不是只说「切换播放模式」，否则读屏听到的永远是同一句 */
internal fun modeLabelRes(mode: PlayMode, isFmMode: Boolean): Int = when (mode) {
    PlayMode.Sequential -> if (isFmMode) R.string.mode_infinite else R.string.mode_sequential
    PlayMode.Shuffle -> R.string.mode_shuffle
    PlayMode.SingleRepeat -> R.string.mode_single
    PlayMode.ListRepeat -> R.string.mode_list
}

/**
 * 进度条：拖动期间只显示本地值，松手才 seek——
 * 与 MAUI 侧「Slider 拖动 OneWay、DragCompleted 后 seek 再恢复双向」同一条规则，
 * 否则拖动会被播放器回写的位置抢回去。
 */
@OptIn(androidx.compose.material3.ExperimentalMaterial3Api::class)
@Composable
internal fun ProgressRow(positionMsFlow: StateFlow<Long>, durationMs: Long, onSeek: (Long) -> Unit) {
    // 进度在自己的作用域里收集：每 250ms 的位置刷新只重画这一行，不再整页重组
    val positionMs by positionMsFlow.collectAsStateWithLifecycle()
    var dragFraction by remember { mutableFloatStateOf(-1f) }
    val shownMs = if (dragFraction >= 0) dragFraction * durationMs else positionMs.toFloat()
    val fraction = if (durationMs > 0) (shownMs / durationMs).coerceIn(0f, 1f) else 0f
    // 播放页在浅色主题下也被钉成深色（fixedDarkPages），所以这里不能沿用环境色 ——
    // 沿用会让时间文字拿到浅色方案的黑色，落在深底上直接看不见
    val palette = LocalCatClawPalette.current

    Column(
        // 独立图层：这一行每 16ms 变一次（位置流），不隔离的话整屏都会被拖进重录
        modifier = Modifier.fillMaxWidth().graphicsLayer(),
    ) {
        // 2026-11-03 用户：进度条比时间行短、时间和进度条之间太空。
        // 两者都是 Material3 Slider 的固有几何造成的：轨道被拇指槽左右各内缩 10dp（短一截），
        // 控件本身有 48dp 最小高度（把时间推远）。这里换成自绘的一条，几何完全可控。
        // 语义不变：拖动期间只显本地值（dragFraction），松手才 seek。
        SeekBar(
            fraction = fraction,
            enabled = durationMs > 0,
            onPreview = { dragFraction = it },
            onCommit = {
                val committed = dragFraction
                dragFraction = -1f
                if (committed >= 0f && durationMs > 0) onSeek((committed * durationMs).toLong())
            },
            // 与下面那行时间用同一个 4dp 内边距，两行左右严格对齐
            modifier = Modifier.fillMaxWidth().padding(horizontal = 4.dp),
        )
        // 时间放在进度条下方：左端当前时间、右端总时长（与进度条同一 4dp 内边距，左右对齐）
        Row(
            modifier = Modifier.fillMaxWidth().padding(horizontal = 4.dp),
            horizontalArrangement = Arrangement.SpaceBetween,
        ) {
            Text(
                text = formatTime(shownMs.toLong()),
                fontSize = 11.5.sp,
                color = Color(palette.textSecondary),
            )
            Text(
                text = formatTime(durationMs),
                fontSize = 11.5.sp,
                color = Color(palette.textSecondary),
            )
        }
    }
}

/**
 * 自绘进度条（2026-11-03 替换 Material3 Slider）。
 *
 * 为什么不用 Slider：
 * - 它的轨道会被拇指槽左右各内缩约 10dp —— 进度条看起来比下面那行时间短一截；
 * - 控件有 48dp 最小高度 —— 轨道离时间文字被推得太远；
 * - 拇指槽按测量尺寸和轨道对齐，自定义小圆点很容易偏心。
 *
 * 这里：轨道**铺满整宽**与时间行左右对齐，圆点在两端允许溢出半个（视觉上才走得满），
 * 垂直方向只占 [SeekBarHeight]，轨道中线就在正中，天然与圆点同心。
 */
internal val SeekBarHeight = 22.dp
internal val SeekBarTrackHeight = 3.dp
internal val SeekBarDotRadius = 5.dp

@Composable
internal fun SeekBar(
    fraction: Float,
    enabled: Boolean,
    onPreview: (Float) -> Unit,
    onCommit: () -> Unit,
    modifier: Modifier = Modifier,
) {
    val palette = LocalCatClawPalette.current
    val active = Color(palette.textPrimary)
    val inactive = Color(palette.textPrimary).copy(alpha = 0.22f)

    Box(
        modifier = modifier
            .fillMaxWidth()
            .height(SeekBarHeight)
            // 轻击直接定位（先预览再提交）
            .pointerInput(enabled) {
                if (!enabled) return@pointerInput
                detectTapGestures { offset ->
                    onPreview((offset.x / size.width).coerceIn(0f, 1f))
                    onCommit()
                }
            }
            // 拖动：按当前触点绝对位置换算比例，不做增量累加（更跟手）
            .pointerInput(enabled) {
                if (!enabled) return@pointerInput
                detectHorizontalDragGestures(
                    onDragStart = { offset ->
                        onPreview((offset.x / size.width).coerceIn(0f, 1f))
                    },
                    onDragEnd = { onCommit() },
                    onDragCancel = { onCommit() },
                    onHorizontalDrag = { change, _ ->
                        change.consume()
                        onPreview((change.position.x / size.width).coerceIn(0f, 1f))
                    },
                )
            }
            .drawBehind {
                val midY = size.height / 2f
                val trackH = SeekBarTrackHeight.toPx()
                val dotR = SeekBarDotRadius.toPx()
                val fullW = size.width
                val radius = CornerRadius(trackH / 2f, trackH / 2f)
                // 底轨：整宽
                drawRoundRect(
                    color = inactive,
                    topLeft = Offset(0f, midY - trackH / 2f),
                    size = Size(fullW, trackH),
                    cornerRadius = radius,
                )
                // 已播
                val cx = fullW * fraction.coerceIn(0f, 1f)
                if (cx > 0f) {
                    drawRoundRect(
                        color = active,
                        topLeft = Offset(0f, midY - trackH / 2f),
                        size = Size(cx, trackH),
                        cornerRadius = radius,
                    )
                }
                // 圆点：允许在两端各溢出半径，进度条才「走满」
                drawCircle(color = active, radius = dotR, center = Offset(cx, midY))
            },
    )
}

internal fun formatTime(ms: Long): String {
    val total = (ms / 1000).coerceAtLeast(0)
    return "%02d:%02d".format(total / 60, total % 60)
}

            // ④ 多行歌词面板：占满剩余高度（MAUI 那句 Grid 行定义里的 *）
            //
            // 只列「③ 之后要唱的」：面板筛掉当前句/兜底句，避免同一句出现两次（用户反馈「歌词重复了」）。
            // 用「还没唱到的」判定，而不是拿当前行去 lines 里 indexOf：
            // indexOf 依赖对象相等，presentation 的行与 lines 里的行一旦不是同一实例就查不到（真机就是这样，
            // 结果 currentIndex=-1、面板又从第一行开始，当前句在 ③ 和面板里出现两次 —— 用户说「歌词重复了」）。

/**
 * 横屏播放页 —— 按 MAUI `NowPlayingPage.xaml` 的 `LandscapeRoot` 1:1 移植。
 *
 * MAUI 原文：
 *   Grid RowDefinitions="Auto,*" ColumnDefinitions="1*,1.15*"
 *        ColumnSpacing=32  Padding="48,8,48,20"
 *   左栏（占两行）封面 Border 460×460 圆角 28 AspectFill
 *   右栏 RowDefinitions="Auto,Auto,Auto,*,Auto,Auto" RowSpacing=10
 *     ① 标题 28 + 艺人 14 + 音质徽章     ② 工具行（30dp）    ③ 当前歌词 16 粗体
 *     ④ * 多行歌词面板                    ⑤ 进度条 + 时间     ⑥ 控制行（左对齐，间距 12）
 *
 * 与 MAUI 的差异只有两处，都是端上能力不同：
 * - 工具行的第三颗：MAUI 是「旋转屏幕」，横屏里点了没有意义，换成「收起回发现页」；
 * - 控制行的 ♥ 用按下的实心/空心图标（MAUI 也是两套图，只是名字不同）。
 */
@Composable
internal fun LandscapeNowPlaying(
    cover: android.graphics.Bitmap?,
    title: String?,
    artist: String?,
    placeholder: String,
    isPlaying: Boolean,
    mode: PlayMode,
    isFmMode: Boolean = false,
    durationMs: Long,
    positionMsFlow: kotlinx.coroutines.flow.StateFlow<Long>,
    favorite: Boolean,
    onToggle: () -> Unit,
    onNext: () -> Unit,
    onPrevious: () -> Unit,
    onCycleMode: () -> Unit,
    onSeek: (Long) -> Unit,
    onToggleFavorite: () -> Unit,
    onOpenQueue: () -> Unit,
    onSleepTimer: () -> Unit,
    onMore: () -> Unit,
    onCollapse: () -> Unit,
    palette: com.catclaw.music.kt.core.theme.CatClawPalette,
) {
    val lyricsViewModel: LyricsViewModel = androidx.hilt.navigation.compose.hiltViewModel()
    val lyricsState by lyricsViewModel.lyrics.collectAsStateWithLifecycle()
    val lineMode by lyricsViewModel.lineMode.collectAsStateWithLifecycle()
    val positionMs by positionMsFlow.collectAsStateWithLifecycle()
    val lines = lyricsViewModel.visibleLyrics()?.lines.orEmpty()
    val presentation = LyricsState.present(
        lyrics = lyricsViewModel.visibleLyrics(),
        positionMs = positionMs,
        lineMode = lineMode,
    )
    val currentLine = presentation?.line?.takeIf { it.text.isNotBlank() }
    // 用时间戳判定「还没唱到的行」，不用 indexOf —— presentation 的行和 lines 里的行
    // 不保证是同一实例，查不到就会退化成「从第一行开始」，同一句出现两次（用户：歌词重复了）
    val upcoming = lines.filter { it.startMs > positionMs + 300L && it.text.isNotBlank() }
    // 当前句在整首里的位置（按时间戳算，不比对对象）
    val currentIndex = lines.indexOfFirst { it.startMs > positionMs }.let { if (it < 0) lines.lastIndex else it - 1 }
    val listState = androidx.compose.foundation.lazy.rememberLazyListState()

    Row(
        modifier = Modifier
            .fillMaxSize()
            .statusBarsPadding()
            .padding(start = 48.dp, end = 48.dp, top = 8.dp, bottom = 20.dp),
        horizontalArrangement = Arrangement.spacedBy(32.dp),
    ) {
        // 左栏：460 方形封面（高度不足时按高度收缩）
        BoxWithConstraints(modifier = Modifier.weight(1f).fillMaxHeight()) {
            val side = minOf(460.dp, maxWidth, maxHeight)
            Box(
                modifier = Modifier
                    .size(side)
                    .align(Alignment.Center)
                    .clip(RoundedCornerShape(28.dp))
                    .border(0.5.dp, Color.White.copy(alpha = 0.19f), RoundedCornerShape(28.dp)),
                contentAlignment = Alignment.Center,
            ) {
                if (cover != null) {
                    androidx.compose.foundation.Image(
                        bitmap = cover.asImageBitmap(),
                        contentDescription = null,
                        contentScale = androidx.compose.ui.layout.ContentScale.Crop,
                        modifier = Modifier.fillMaxSize(),
                    )
                } else {
                    Text(
                        text = placeholder,
                        fontSize = 72.sp,
                        color = Color(palette.textPrimary).copy(alpha = 0.5f),
                    )
                }
            }
        }

        // 右栏：Auto / Auto / Auto / * / Auto / Auto
        Column(modifier = Modifier.weight(1.15f).fillMaxHeight()) {
            Text(
                text = title.orEmpty(),
                fontSize = 28.sp,
                fontWeight = FontWeight.SemiBold,
                color = Color(palette.textPrimary),
                maxLines = 1,
                overflow = TextOverflow.Ellipsis,
            )
            Text(
                text = artist.orEmpty(),
                fontSize = 14.sp,
                color = Color(palette.textSecondary),
                maxLines = 1,
                overflow = TextOverflow.Ellipsis,
                modifier = Modifier.padding(top = 2.dp),
            )

            // 工具行（MAUI：播放列表 / 睡眠定时 / 旋转；旋转在横屏无意义，换成收起）
            Row(
                modifier = Modifier.padding(top = 10.dp),
                horizontalArrangement = Arrangement.spacedBy(12.dp),
            ) {
                LandscapeIconButton(PlayerIcons.QueueMusic, stringResource(R.string.queue_open), palette, onOpenQueue)
                LandscapeIconButton(PlayerIcons.Timer, stringResource(R.string.sleep_timer_title), palette, onSleepTimer)
                LandscapeIconButton(PlayerIcons.Collapse, stringResource(R.string.player_collapse), palette, onCollapse)
            }

            // 歌词：一条会自动往上滚的列表（MAUI 用的是同一个思路 —— 当前句居中，唱过就滚走）。
            // 之前是「大字当前句 + 下面静态列表」，换句时文字直接跳变，很硬（用户 2026-11-08：
            // 「歌词切换好僵硬，改成往上滚动」）。
            BoxWithConstraints(
                modifier = Modifier
                    .weight(1f)
                    .fillMaxWidth()
                    .padding(top = 6.dp),
            ) {
                val viewport = maxHeight
                val density = androidx.compose.ui.platform.LocalDensity.current
                // 当前句滚到视口 1/3 处：上方留两句「唱过的」，下方是「要唱的」
                LaunchedEffect(currentIndex) {
                    if (lines.isNotEmpty()) {
                        listState.animateScrollToItem(
                            index = currentIndex.coerceIn(0, lines.lastIndex),
                            scrollOffset = with(density) { -(viewport * 0.32f).roundToPx() },
                        )
                    }
                }
                androidx.compose.foundation.lazy.LazyColumn(
                    state = listState,
                    modifier = Modifier.fillMaxSize(),
                    verticalArrangement = Arrangement.spacedBy(4.dp),
                ) {
                    itemsIndexed(lines) { index, line ->
                        val distance = kotlin.math.abs(index - currentIndex)
                        val active = index == currentIndex
                        Text(
                            text = line.text.ifBlank { " " },
                            fontSize = when {
                                active -> 17.sp
                                distance == 1 -> 14.sp
                                else -> 13.sp
                            },
                            fontWeight = if (active) FontWeight.Bold else FontWeight.Normal,
                            color = when {
                                active -> Color(palette.textPrimary)
                                distance == 1 -> Color(palette.textSecondary).copy(alpha = 0.75f)
                                else -> Color(palette.textSecondary).copy(alpha = 0.35f)
                            },
                            maxLines = 1,
                            overflow = TextOverflow.Ellipsis,
                            modifier = Modifier
                                .fillMaxWidth()
                                .clip(RoundedCornerShape(6.dp))
                                .clickable { lyricsViewModel.seekTo(line.startMs) }
                                .padding(vertical = 3.dp, horizontal = 2.dp),
                        )
                    }
                }
            }

            ProgressRow(
                positionMsFlow = positionMsFlow,
                durationMs = durationMs,
                onSeek = onSeek,
            )

            // 控制行：左对齐、间距 12（MAUI LandscapeControlsRow）
            Row(
                modifier = Modifier.fillMaxWidth().height(72.dp),
                verticalAlignment = Alignment.CenterVertically,
                horizontalArrangement = Arrangement.spacedBy(12.dp),
            ) {
                Icon(
                    painter = painterResource(
                        if (favorite) R.drawable.ic_favorite else R.drawable.ic_favorite_border,
                    ),
                    contentDescription = stringResource(
                        if (favorite) R.string.favorite_remove else R.string.favorite_add,
                    ),
                    tint = if (favorite) Color(palette.textPrimary) else Color(palette.textSecondary),
                    modifier = Modifier
                        .size(44.dp)
                        .clip(CircleShape)
                        .clickable(onClick = onToggleFavorite)
                        .padding(10.dp),
                )
                LandscapeIconButton(PlayerIcons.Previous, stringResource(R.string.prev_song), palette, onPrevious)
                Box(
                    modifier = Modifier
                        .size(64.dp)
                        .clip(CircleShape)
                        .clickable(onClick = onToggle),
                    contentAlignment = Alignment.Center,
                ) {
                    Icon(
                        imageVector = if (isPlaying) PlayerIcons.Pause else PlayerIcons.Play,
                        contentDescription = stringResource(R.string.play_or_pause),
                        tint = Color(palette.textPrimary),
                        modifier = Modifier.size(34.dp),
                    )
                }
                LandscapeIconButton(PlayerIcons.Next, stringResource(R.string.next_song), palette, onNext)
                LandscapeIconButton(
                    icon = modeIcon(mode, isFmMode),
                    description = stringResource(R.string.mode_change) + "：" + stringResource(modeLabelRes(mode, isFmMode)),
                    palette = palette,
                    onClick = onCycleMode,
                )
                LandscapeIconButton(PlayerIcons.More, stringResource(R.string.playing_more), palette, onMore)
            }
        }
    }
}

/** 横屏的工具/控制按钮：44dp 触达框、无底色（MAUI 那两行都是纯图标） */
@Composable
internal fun LandscapeIconButton(
    icon: PlayerIcon,
    description: String,
    palette: com.catclaw.music.kt.core.theme.CatClawPalette,
    onClick: () -> Unit,
) {
    Icon(
        imageVector = icon,
        contentDescription = description,
        tint = Color(palette.textPrimary),
        modifier = Modifier
            .size(44.dp)
            .clip(CircleShape)
            .clickable(onClick = onClick)
            .padding(10.dp),
    )
}
