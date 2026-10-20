package com.catclaw.music.kt.ui.screen

import android.graphics.Bitmap

import androidx.compose.animation.core.EaseInOutCubic
import androidx.compose.animation.core.animateFloatAsState
import androidx.compose.animation.core.tween
import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.clickable
import androidx.compose.foundation.gestures.animateScrollBy
import androidx.compose.foundation.interaction.DragInteraction
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.BoxWithConstraints
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.PaddingValues
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxHeight
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.statusBarsPadding
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.rememberLazyListState
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material3.Icon
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Slider
import androidx.compose.material3.SliderDefaults
import androidx.compose.material3.Switch
import androidx.compose.material3.SwitchDefaults
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.produceState
import androidx.compose.runtime.DisposableEffect
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableIntStateOf
import androidx.compose.runtime.derivedStateOf
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.State
import androidx.compose.runtime.remember
import androidx.compose.runtime.rememberUpdatedState
import androidx.compose.runtime.setValue
import androidx.compose.runtime.withFrameNanos
import androidx.compose.runtime.snapshotFlow
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.BlurredEdgeTreatment
import androidx.compose.ui.draw.blur
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.toArgb
import androidx.compose.ui.graphics.graphicsLayer
import androidx.compose.ui.layout.onSizeChanged
import androidx.compose.ui.platform.LocalDensity
import androidx.compose.ui.platform.LocalLifecycleOwner
import androidx.compose.ui.res.painterResource
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import androidx.hilt.navigation.compose.hiltViewModel
import androidx.lifecycle.Lifecycle
import androidx.lifecycle.LifecycleEventObserver
import androidx.lifecycle.compose.collectAsStateWithLifecycle
import com.catclaw.music.kt.R
import com.catclaw.music.kt.data.lyrics.LyricsSettings
import com.catclaw.music.kt.ui.lyrics.LyricLineText
import com.catclaw.music.kt.ui.lyrics.LyricsViewModel
import com.catclaw.music.kt.ui.lyrics.lyricBlurDp
import com.catclaw.music.kt.ui.player.CoverTile
import com.catclaw.music.kt.ui.player.rememberCover
import com.catclaw.music.kt.ui.theme.GlassChip
import com.catclaw.music.kt.core.theme.CoverFlowProcessor
import com.catclaw.music.kt.ui.theme.FrostedFlowBackground
import com.catclaw.music.kt.ui.theme.glassCard
import com.catclaw.music.kt.ui.theme.highlightOnGlassCard
import com.catclaw.music.kt.ui.theme.inkOnGradient
import com.catclaw.music.kt.ui.theme.LocalCatClawPalette
import com.catclaw.music.kt.ui.theme.artworkWash
import com.catclaw.music.kt.ui.theme.catClawBackground
import com.catclaw.music.kt.ui.theme.rememberArtworkTint
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.delay
import kotlinx.coroutines.withContext
import kotlinx.coroutines.flow.distinctUntilChanged
import kotlinx.coroutines.flow.drop
import kotlin.math.abs
import kotlin.math.roundToInt

// 行视觉参数（MAUI FullLyricsPage.xaml.cs:45-47）：当前行放大 1.25、邻居呼吸 6dp、滚动缓动 380ms
// 这三个只服务本页：播放页那一行已独立（`PlayingLyricLine` —— 图标大小、靠左、不放大），
// 2026-10-18 用户要求「播放页别完全复用全屏页」，于是不再放开到模块内。
// 放开过就会有人从外面依赖（当时的理由正是"两页共用一档放大"），最后两页又被一根看不见的线捆上。
private const val LyricCurrentScale = 1.25f
private val LyricGapExtra = 6.dp
private const val LyricScrollMs = 380

/**
 * 全屏歌词页 —— 完全对齐 MAUI `FullLyricsPage`（2026-10-03 重写，2026-10-07 对齐滚动/视觉细节）：
 * - 竖屏：顶部 HeroCard（40dp 封面点按返回 + 标题/歌手 + LRC 徽标 + ⚙ 设置）+ 全宽歌词
 * - 横屏：左封面（圆角 28 + 亮描边）+ 右歌词，无顶部卡片
 * - 当前行按 1/1.25 宽布局再放大 1.25（断行不跳字），钉在裁剪区 30% 处；进页/回前台立即钉行，
 *   切句才走 380ms EaseInOutCubic 缓动；用户拖动期间自动跟随让位
 * - 非当前行统一 α0.35 + 按距离分档高斯模糊（API 31+）；译文/罗马音所有行常显
 * - 无歌词时底部玻璃徽标提示（MAUI NoLyricsBadge 同位）
 *
 * [visible] 由宿主传入（pager 已停稳在本页）：控制位置流提频与钉行时机，
 * 对应 MAUI OnAppearing/OnDisappearing + PlayerSurfaceTracker 令牌。
 */
@Composable
fun FullLyricsScreen(onClose: () -> Unit, visible: Boolean, viewModel: LyricsViewModel = hiltViewModel()) {
    val palette = LocalCatClawPalette.current
    val lyrics by viewModel.lyrics.collectAsStateWithLifecycle()
    val loading by viewModel.loading.collectAsStateWithLifecycle()
    val lineMode by viewModel.lineMode.collectAsStateWithLifecycle()
    // 位置流只收集为 State：逐字填充的每帧更新下沉到当前行 item 内读取，
    // 整页不再随 16ms 位置轮询重组（旧写法是逐字帧率低的根因）
    val positionMsState = viewModel.positionMs.collectAsStateWithLifecycle()
    val fontSize by viewModel.fontSizeSp.collectAsStateWithLifecycle()
    val alignment by viewModel.alignment.collectAsStateWithLifecycle()
    val showTranslation by viewModel.showTranslation.collectAsStateWithLifecycle()
    val showRoma by viewModel.showRoma.collectAsStateWithLifecycle()
    var panelOpen by remember { mutableStateOf(false) }

    val shown = viewModel.visibleLyrics()
    val lines = shown?.lines.orEmpty()
    // currentIndex 用 derivedStateOf 现算：位置每 16ms 变一次，但只有换句才让屏幕重组
    val currentIndex by remember(shown, lineMode) {
        androidx.compose.runtime.derivedStateOf {
            com.catclaw.music.kt.core.lyrics.LyricsState.present(
                lyrics = shown,
                positionMs = positionMsState.value,
                lineMode = lineMode,
            )?.index ?: -1
        }
    }
    val listState = rememberLazyListState()

    // 用户拖动检测（MAUI _userScrolling 对应物）：拖动期间自动跟随让位，不跟用户抢滚动
    var userScrolling by remember { mutableStateOf(false) }
    LaunchedEffect(listState.interactionSource) {
        listState.interactionSource.interactions.collect { interaction ->
            when (interaction) {
                is DragInteraction.Start -> userScrolling = true
                is DragInteraction.Stop, is DragInteraction.Cancel -> userScrolling = false
            }
        }
    }

    // 歌词页可见 → 位置流提到 ~60fps（逐字着色平滑）；离开降回 120ms 让出主线程
    // （MAUI OnAppearing/OnDisappearing 里的 PlayerSurfaceTracker 令牌，FullLyricsPage.xaml.cs:576-609）
    DisposableEffect(visible) {
        viewModel.setLyricsSurfaceVisible(visible)
        onDispose { viewModel.setLyricsSurfaceVisible(false) }
    }

    // 回前台重钉（MAUI OnAppResumed → ForcePinActiveLine）：横向 Pager 相邻页常驻组合，
    // ON_RESUME 对所有页同时触发，必须再用 visible 挡一道
    var isResumed by remember { mutableStateOf(false) }
    val lifecycleOwner = LocalLifecycleOwner.current
    DisposableEffect(lifecycleOwner) {
        val observer = LifecycleEventObserver { _, event ->
            when (event) {
                Lifecycle.Event.ON_RESUME -> isResumed = true
                Lifecycle.Event.ON_PAUSE -> isResumed = false
                else -> Unit
            }
        }
        lifecycleOwner.lifecycle.addObserver(observer)
        onDispose { lifecycleOwner.lifecycle.removeObserver(observer) }
    }

    // ── 钉行计算（MAUI ComputePinnedTargetY，LyricEngine.cs:686-707）──
    // 顶部安全边距（MAUI = stack.Padding.Top + 4 ≈ 14dp，这里取 12dp）
    val density = LocalDensity.current
    val pinMarginPx = remember(density) { with(density) { 12.dp.toPx() } }
    // 行布局高缓存（含呼吸/预留 padding）：滚动前 LazyColumn 拿不到未布局行的实测高，
    // 用上次实测值估算目标 offset
    val rowContentHeights = remember { mutableMapOf<Int, Int>() }
    val onRowMeasured = remember { { index: Int, heightPx: Int -> rowContentHeights[index] = heightPx } }

    // 300ms 补钉循环执行期间切句要钉新行号（MAUI 每次重读 CurrentLyricIndexObservable），所以经
    // rememberUpdatedState 取最新值而不是闭包里捕获旧参数
    val latestIndex by rememberUpdatedState(currentIndex)
    val latestLines by rememberUpdatedState(lines)

    /** 把当前行【中心】钉在裁剪区 30% 处（无动画，MAUI PinActiveLineNow）：先用缓存行高定位，
     *  布局完成后用实测行高（layoutInfo.size，含当行呼吸/预留的真实状态）再钉一次 */
    suspend fun pinActiveLineNow() {
        val linesNow = latestLines
        val idx = latestIndex
        if (idx < 0 || linesNow.isEmpty()) return
        val target = idx.coerceAtMost(linesNow.lastIndex)
        val viewport = listState.layoutInfo.viewportSize.height
        if (viewport <= 0) return
        val beforePadding = listState.layoutInfo.beforeContentPadding
        val estimated = rowContentHeights[target] ?: (viewport / 8)
        listState.scrollToItem(target, pinnedScrollOffsetPx(estimated, viewport, beforePadding, pinMarginPx))
        withFrameNanos { }
        // 布局完成后用实测行高再钉一次（MAUI ForcePinActiveLine 的重测思路）：
        // 估算用的缓存可能是该行未成为当前行时的高度（无预留、满宽分行）
        val actual = listState.layoutInfo.visibleItemsInfo.firstOrNull { it.index == target }?.size
        if (actual != null && actual != estimated) {
            listState.scrollToItem(target, pinnedScrollOffsetPx(actual, viewport, beforePadding, pinMarginPx))
        }
    }

    /** 切句缓动（MAUI ScrollToLine：380ms CubicInOut 整体平移）；拖动中不抢（MAUI _userScrolling） */
    suspend fun animatePinActiveLine() {
        if (userScrolling) return
        val linesNow = latestLines
        val idx = latestIndex
        if (idx < 0 || linesNow.isEmpty()) return
        val target = idx.coerceAtMost(linesNow.lastIndex)
        val viewport = listState.layoutInfo.viewportSize.height
        if (viewport <= 0) return
        val beforePadding = listState.layoutInfo.beforeContentPadding
        val estimated = rowContentHeights[target] ?: (viewport / 8)
        val targetOffset = pinnedScrollOffsetPx(estimated, viewport, beforePadding, pinMarginPx)
        val info = listState.layoutInfo.visibleItemsInfo.firstOrNull { it.index == target }
        if (info != null) {
            // 目标行在视口内：animateScrollToItem 是固定 spring 改不了曲线，
            // 改用 animateScrollBy 按 MAUI 同款 380ms EaseInOutCubic 平移到目标位。
            // 滚动 delta = info.offset + targetOffset：滚动后 item.offset = −targetOffset，
            // 摆放顶 = beforePadding − (−targetOffset)，行中心恰落在 30% 处
            val delta = (info.offset + targetOffset).toFloat()
            if (abs(delta) > 0.5f) {
                listState.animateScrollBy(delta, tween(LyricScrollMs, easing = EaseInOutCubic))
            }
        } else {
            // 目标行不在视口内（跳句/跳歌）：退回 LazyColumn 的默认长距滚动
            listState.animateScrollToItem(target, targetOffset)
        }
        withFrameNanos { }
        // 当前行换宽度重分行后行高变了（MAUI RelayoutAndRepinAfterHighlightChange）：
        // 用实测行高再校正一次
        val actual = listState.layoutInfo.visibleItemsInfo.firstOrNull { it.index == target }?.size
        if (actual != null && actual != estimated) {
            val corrected = pinnedScrollOffsetPx(actual, viewport, beforePadding, pinMarginPx)
            val info2 = listState.layoutInfo.visibleItemsInfo.firstOrNull { it.index == target }
            if (info2 != null) {
                val delta2 = (info2.offset + corrected).toFloat()
                if (abs(delta2) > 0.5f) {
                    listState.animateScrollBy(delta2, tween(LyricScrollMs, easing = EaseInOutCubic))
                }
            } else {
                listState.animateScrollToItem(target, corrected)
            }
        }
    }

    // 进页 / 回前台 / 歌词表变化：【立即】钉行（无缓动，MAUI OnAppearing ForcePinActiveLine /
    // OnAppResumed），随后 300ms×3 补钉（ScheduleRetriedPinActive(3)）覆盖二次布局晚于进页的场景
    var pinnedOnce by remember { mutableStateOf(false) }
    LaunchedEffect(visible, isResumed, lines) {
        if (!visible || !isResumed || lines.isEmpty()) return@LaunchedEffect
        pinActiveLineNow()
        pinnedOnce = true
        repeat(3) {
            delay(300)
            pinActiveLineNow()
        }
    }
    // 横竖屏切换后立即重钉（MAUI ApplyLayoutForOrientation 的 orientationChanged →
    // ForcePinActiveLine + ScheduleRetriedPinActive(3)，xaml.cs:407-416/434-442）：
    // 本 Activity 在 manifest 自管 configChanges 不重建，旋转后视口尺寸大变而组合/滚动状态存活，
    // 必须监听视口尺寸变化重钉；行高缓存跨旋转可能过期，钉行内的实测校正兜底
    LaunchedEffect(visible) {
        if (!visible) return@LaunchedEffect
        snapshotFlow { listState.layoutInfo.viewportSize }
            .distinctUntilChanged()
            .drop(1) // 首个值与进页钉行重复，跳过
            .collect {
                pinActiveLineNow()
                repeat(3) {
                    delay(300)
                    pinActiveLineNow()
                }
            }
    }
    // 切句跟随才走缓动；钉过一次之前不动画，避免首次组合长距离飞入
    // （MAUI 构建期用 HighlightLineWithoutScroll + 立即钉行）；页面不可见时不跟
    // （相邻 Pager 页常驻组合，MAUI 的 PlatformView attached 守卫同思路）
    LaunchedEffect(currentIndex, lines, userScrolling) {
        if (!visible || !pinnedOnce || userScrolling) return@LaunchedEffect
        animatePinActiveLine()
    }

    // 与播放页同一套语言：底色由当前这首歌的封面染出来
    val song = viewModel.currentSong()
    val cover = rememberCover(song?.coverArtPath, song?.filePath, maxPx = 256)
    // 流光背景的两个 gate：暂停即停动画（MAUI IsActive 绑 IsPlaying）、雾面开关整体关闭
    val isPlaying by viewModel.isPlaying.collectAsStateWithLifecycle()
    val frostedEnabled by viewModel.frostedBgEnabled.collectAsStateWithLifecycle()

    Box(
        modifier = Modifier
            .fillMaxSize()
            // 雾面动态背景关闭时退回静态底：主题渐变 + 封面均色洗色
            .then(if (frostedEnabled) Modifier else Modifier.catClawBackground().artworkWash(cover)),
    ) {
        if (frostedEnabled) {
            // 流光喷发动态背景（MAUI FullLyricsPage.xaml:14-22 的 FrostedBackground）：
            // IsActive 绑 IsPlaying、歌词列表拖动时暂停省电（IsScrolling 绑 IsUserScrolling）、
            // 深色预设（IsDark=true）、TintOpacity 0.60、DimAmount 0.12，色调源 = 封面均色
            // 封面流源：当前封面（≤256 长边的 ARGB）。MAUI 是「封面源非空 → 切封面流模式」
            // （FrostedBackgroundHandler.cs:378），所以取不到封面时自然退回流光模式。
            // 取像素放后台线程做一次（封面解码出来是原图，getPixels 在主线会卡一帧）。
            val coverFlowSource by produceState<CoverFlowProcessor.CoverSource?>(null, cover) {
                value = withContext(Dispatchers.Default) {
                    cover?.let { bmp ->
                        val side = maxOf(bmp.width, bmp.height)
                        val scaled = if (side > CoverFlowProcessor.SOURCE_MAX_DIM) {
                            val k = CoverFlowProcessor.SOURCE_MAX_DIM.toFloat() / side
                            Bitmap.createScaledBitmap(
                                bmp,
                                (bmp.width * k).toInt().coerceAtLeast(1),
                                (bmp.height * k).toInt().coerceAtLeast(1),
                                true,
                            )
                        } else {
                            bmp
                        }
                        val px = IntArray(scaled.width * scaled.height)
                        scaled.getPixels(px, 0, scaled.width, 0, 0, scaled.width, scaled.height)
                        CoverFlowProcessor.scaleSource(px, scaled.width, scaled.height)
                    }
                }
            }
            FrostedFlowBackground(
                isActive = isPlaying,
                isScrolling = userScrolling,
                tint = rememberArtworkTint(cover),
                modifier = Modifier.fillMaxSize(),
                coverSource = coverFlowSource,
                accentArgb = rememberArtworkTint(cover).toArgb() and 0xFFFFFF,
            )
        }
        BoxWithConstraints(modifier = Modifier.fillMaxSize().statusBarsPadding()) {
            val landscape = maxWidth > maxHeight

            if (landscape) {
                // ── 横屏：左封面 + 右歌词（MAUI LandscapeContent：1*/1.15*、ColumnSpacing 32、
                // 无顶部卡片，FullLyricsPage.xaml:113-115）──
                // 几何对齐 xaml.cs:336-353/389-393：封面尺寸 = clamp(min(可用高, 可用宽), 240, 800)、
                // 顶对齐状态栏+5、左缘 = 状态栏+5。本根已 statusBarsPadding（顶=状态栏下缘），
                // 故上下各留 5dp、左缘 5dp；MAUI 的底部 max(inset)+5 并入可用高公式的 10dp 余量
                val rowContentWidth = maxWidth - 5.dp - 48.dp
                val leftColumnWidth = (rowContentWidth - 32.dp) / 2.15f
                val coverSize = minOf(
                    maxHeight - 10.dp,
                    leftColumnWidth - 5.dp,
                ).coerceIn(240.dp, 800.dp)
                Row(
                    modifier = Modifier
                        .fillMaxSize()
                        .padding(start = 5.dp, top = 5.dp, end = 48.dp, bottom = 5.dp),
                    horizontalArrangement = Arrangement.spacedBy(32.dp),
                ) {
                    Box(modifier = Modifier.weight(1f).fillMaxHeight()) {
                        CoverTileOrb(
                            coverArtPath = song?.coverArtPath,
                            audioPath = song?.filePath,
                            title = song?.title ?: "",
                            glow = rememberArtworkTint(cover),
                            modifier = Modifier
                                .align(Alignment.TopStart)
                                .size(coverSize),
                        )
                    }
                    Box(modifier = Modifier.weight(1.15f).fillMaxHeight()) {
                        LyricList(
                            lines = lines,
                            currentIndex = currentIndex,
                            positionMsState = positionMsState,
                            lineMode = lineMode,
                            fontSizeSp = fontSize,
                            alignment = alignment,
                            showTranslation = showTranslation,
                            showRoma = showRoma,
                            onSeek = { viewModel.seekTo(it) },
                            onBack = onClose,
                            listState = listState,
                            onRowMeasured = onRowMeasured,
                            edge = 40.dp,
                            modifier = Modifier.fillMaxSize(),
                        )
                        if (lines.isEmpty()) NoLyricsBadge(text = stringResource(if (loading) R.string.lyric_loading else R.string.lyric_none), modifier = Modifier.align(Alignment.BottomCenter))
                    }
                }
            } else {
                // ── 竖屏：顶部 HeroCard + 全宽歌词 ──
                Column(modifier = Modifier.fillMaxSize()) {
                    // HeroCard（MAUI 同构）：40dp 封面点按返回 + 标题/歌手 + LRC 徽标 + ⚙ 设置
                    Row(
                        modifier = Modifier
                            .fillMaxWidth()
                            .padding(horizontal = 18.dp, vertical = 12.dp),
                        verticalAlignment = Alignment.CenterVertically,
                        horizontalArrangement = Arrangement.spacedBy(12.dp),
                    ) {
                        Box(
                            modifier = Modifier
                                .size(40.dp)
                                .clip(RoundedCornerShape(10.dp))
                                .clickable(onClickLabel = stringResource(R.string.GoBack)) { onClose() },
                        ) {
                            CoverTile(
                                coverArtPath = song?.coverArtPath,
                                audioPath = song?.filePath,
                                maxPx = 256,
                                modifier = Modifier.fillMaxSize(),
                            ) {}
                        }
                        Column(modifier = Modifier.weight(1f), verticalArrangement = Arrangement.spacedBy(2.dp)) {
                            Text(
                                text = viewModel.currentSongTitle().ifEmpty { stringResource(R.string.m1_nothing) },
                                fontSize = 16.sp,
                                fontWeight = FontWeight.SemiBold,
                                color = Color.White,
                                maxLines = 1,
                                overflow = TextOverflow.Ellipsis,
                            )
                            Text(
                                text = viewModel.currentSongArtist(),
                                fontSize = 12.sp,
                                color = Color(0xFFF3F5FF),
                                maxLines = 1,
                                overflow = TextOverflow.Ellipsis,
                            )
                        }
                        // LRC 徽标（MAUI 同款：白 α.14 底、圆角 8）
                        Text(
                            text = "LRC",
                            fontSize = 10.sp,
                            color = Color.White,
                            modifier = Modifier
                                .clip(RoundedCornerShape(8.dp))
                                .background(Color.White.copy(alpha = 0.14f))
                                .padding(horizontal = 8.dp, vertical = 4.dp),
                        )
                        // ⚙ 歌词设置（MAUI：36dp 圆形、白 α.14 底）
                        Box(
                            modifier = Modifier
                                .size(36.dp)
                                .clip(CircleShape)
                                .background(Color.White.copy(alpha = 0.14f))
                                .clickable { panelOpen = true },
                            contentAlignment = Alignment.Center,
                        ) {
                            Icon(
                                painter = painterResource(R.drawable.ic_settings),
                                contentDescription = stringResource(R.string.Settings),
                                tint = Color.White,
                                modifier = Modifier.size(18.dp),
                            )
                        }
                    }

                    BoxWithConstraints(modifier = Modifier.weight(1f)) {
                        // 首行留白按视口高度比例取（横屏窗口矮，写死 120dp 会白掉四成）
                        val edge = (maxHeight * 0.15f).coerceIn(40.dp, 140.dp)
                        LyricList(
                            lines = lines,
                            currentIndex = currentIndex,
                            positionMsState = positionMsState,
                            lineMode = lineMode,
                            fontSizeSp = fontSize,
                            alignment = alignment,
                            showTranslation = showTranslation,
                            showRoma = showRoma,
                            onSeek = { viewModel.seekTo(it) },
                            onBack = onClose,
                            listState = listState,
                            onRowMeasured = onRowMeasured,
                            edge = edge,
                            modifier = Modifier.fillMaxSize(),
                        )
                        if (lines.isEmpty()) NoLyricsBadge(text = stringResource(if (loading) R.string.lyric_loading else R.string.lyric_none), modifier = Modifier.align(Alignment.BottomCenter).padding(horizontal = 20.dp))
                    }
                }
            }

            if (panelOpen) {
                LyricsSettingsPanel(
                    lineMode = lineMode,
                    sourceMode = viewModel.sourceMode.collectAsStateWithLifecycle().value,
                    alignment = alignment,
                    fontSizeSp = fontSize,
                    removeEmpty = viewModel.removeEmptyLines.collectAsStateWithLifecycle().value,
                    translation = showTranslation,
                    roma = showRoma,
                    onLineMode = { viewModel.toggleLineMode() },
                    onSource = viewModel::setSourceMode,
                    onAlignment = viewModel::setAlignment,
                    onFontSize = viewModel::setFontSize,
                    onRemoveEmpty = viewModel::setRemoveEmptyLines,
                    onTranslation = viewModel::setShowTranslation,
                    onRoma = viewModel::setShowRoma,
                    onClose = { panelOpen = false },
                )
            }
        }
    }
}

/** 歌词列表：行间距 14dp（MAUI LyricStack Spacing=14），当前行缩放 1.25 + 行距呼吸（380ms） */
@Composable
private fun LyricList(
    lines: List<com.catclaw.music.kt.core.lyrics.LyricLine>,
    currentIndex: Int,
    positionMsState: androidx.compose.runtime.State<Long>,
    lineMode: Boolean,
    fontSizeSp: Float,
    alignment: Int,
    showTranslation: Boolean,
    showRoma: Boolean,
    onSeek: (Long) -> Unit,
    onBack: () -> Unit,
    listState: androidx.compose.foundation.lazy.LazyListState,
    onRowMeasured: (Int, Int) -> Unit,
    edge: androidx.compose.ui.unit.Dp,
    modifier: Modifier = Modifier,
) {
    LazyColumn(
        state = listState,
        modifier = modifier
            // 轻击歌词裁剪区任意处返回播放页（MAUI LyricClip 的 TapGestureRecognizer，
            // FullLyricsPage.xaml:83-95 → OnLyricsAreaTapped → OnBack）：无涟漪整片接住空白点击，
            // 行自身的 seek clickable 会先行消费行上的点击（seek 属 Compose 增强行为，见交接说明）
            .clickable(
                interactionSource = remember { androidx.compose.foundation.interaction.MutableInteractionSource() },
                indication = null,
                onClickLabel = stringResource(R.string.GoBack),
            ) { onBack() },
        contentPadding = PaddingValues(
            start = 30.dp,
            end = 30.dp,
            top = edge,
            bottom = edge * 1.6f,
        ),
        verticalArrangement = Arrangement.spacedBy(14.dp),
    ) {
        items(count = lines.size, key = { it }) { index ->
            val isCurrent = index == currentIndex
            val dist = abs(index - currentIndex)
            // 实测主行高（不含译文/罗马音副行、不含呼吸预留）：当前行放大溢出的布局补偿以它算
            var mainHeightPx by remember { mutableIntStateOf(0) }
            val density = LocalDensity.current
            // 当前行布局上要预留放大溢出（视觉高 = 主行高×1.25，上下各溢出 主行高×0.125）+ 呼吸 6dp
            //（MAUI ApplyLyricRowGap：容器高 = rowH×1.25 + LyricGapExtra×2）；邻居行朝当前行各让 6dp。
            // 这层 padding 在缩放层外，不被放大
            val overflowHalf = with(density) { (mainHeightPx * (LyricCurrentScale - 1f) / 2f).toDp() }
            val topGap = (if (index == currentIndex + 1) LyricGapExtra else 0.dp) +
                if (isCurrent) overflowHalf + LyricGapExtra else 0.dp
            val bottomGap = (if (index == currentIndex - 1) LyricGapExtra else 0.dp) +
                if (isCurrent) overflowHalf + LyricGapExtra else 0.dp
            // 行级高斯模糊**按距离渐变**（MAUI ApplyRowBlur 的加强版）：每多隔一行 +0.9dp、6dp 封顶。
            // 原来只有两档（±1 行 1dp、≥2 行 2dp），两行开外完全一样，看不出「越远越虚」；
            // Modifier.blur 走 API 31+ RenderEffect，低版本自动无操作，靠透明度分层兜底。
            val rowBlur = lyricBlurDp(dist).dp
            // 当前行 1.25 倍放大（MAUI LyricCurrentScale=1.25 / 380ms）；只作用主行，见 LyricLineText
            val scale by animateFloatAsState(
                targetValue = if (isCurrent) LyricCurrentScale else 1f,
                animationSpec = tween(LyricScrollMs),
                label = "lyricScale",
            )
            Box(
                modifier = Modifier
                    .fillParentMaxWidth()
                    // 整行布局高（含呼吸/预留 padding）：钉行目标 offset 的估算基准
                    .onSizeChanged { onRowMeasured(index, it.height) }
                    .padding(top = topGap, bottom = bottomGap),
            ) {
                LyricLineText(
                    line = lines[index],
                    isCurrent = isCurrent,
                    // 逐字进度在行内读位置流：每帧只重组当前行，其他行跳过
                    progress = if (isCurrent) {
                        com.catclaw.music.kt.core.lyrics.LyricFillCalculator.computeFillProgress(
                            line = lines[index],
                            lineIndex = index,
                            allLines = lines,
                            positionMs = positionMsState.value,
                            lineMode = lineMode,
                        )
                    } else 0.0,
                    // 译文/罗马音是行的一部分：所有行只要有就常显（MAUI BuildLyricStack），
                    // 行高参与滚动锚点；显示与否由设置开关控制
                    showExtras = true,
                    onClick = { onSeek(lines[index].startMs) },
                    fontSizeSp = fontSizeSp,
                    alignmentOverride = alignment,
                    showTranslation = showTranslation,
                    showRoma = showRoma,
                    currentScale = scale,
                    currentScaleTarget = if (isCurrent) LyricCurrentScale else 1f,
                    onMainSizeChanged = { mainHeightPx = it },
                    // 行级模糊罩住主行+副行整体（MAUI ApplyRowBlur 对主歌词与译文同步失焦）
                    modifier = Modifier
                        .fillMaxWidth()
                        // 恢复行级雾化（此前是 `if (false)` 直通的 TEMP-PROFILE）。
                        // Unbounded：文字亚像素/降部不因控件边界被裁掉，模糊可以溢出到行外
                        .then(
                            if (rowBlur > 0.dp) {
                                Modifier.blur(rowBlur, BlurredEdgeTreatment.Unbounded)
                            } else {
                                Modifier
                            },
                        ),
                )
            }
        }
    }
}

/**
 * 钉行目标 offset（MAUI ComputePinnedTargetY，LyricEngine.cs:686-707）：
 * 当前行【中心】钉在裁剪区 30% 处，并用整行布局高（已含放大溢出预留+呼吸，对应 MAUI 的
 * 容器高 rowH）做上下夹紧：
 * - 放得下：中心在 [minC, maxC] 区间内贴近 30% 处（保证整行完整可见）
 * - 超高行（行高 ≥ 裁剪区）：顶格显示，头部优先可见
 *
 * LazyColumn 放置模型（foundation 1.12.1 实源核实）：LazyList.kt 把 contentPadding 顶作为
 * 恒定 visualOffset 传给每个 LazyListMeasuredItem，place() 时叠加 —— 即 scrollToItem(i, S) 后
 * item.offset = −S，而**摆放顶 = beforeContentPadding − S**。行中心钉在 center：
 * padding − S + h/2 = center → S = padding + h/2 − center（补上曾漏掉的 padding 项）。
 * 列表头部有夹紧（scrollToItem 负 offset 在 index 0 被回填到列表起点，LazyListMeasure.kt:151），
 * 最前几行可能钉不满 30%——MAUI TranslateTo 无夹紧，此为 LazyColumn 固有差异。
 * 抽成纯函数与 [karaokeFillBounds] 同理：只有算术可对证，像素效果没法截图。
 */
internal fun pinnedScrollOffsetPx(
    itemHeightPx: Int,
    viewportH: Int,
    beforeContentPaddingPx: Int,
    marginPx: Float,
): Int {
    val h = itemHeightPx.toFloat().coerceAtLeast(1f)
    val minC = marginPx + h / 2f                   // 顶部不溢出的最小中心
    val maxC = viewportH - marginPx - h / 2f       // 底部不溢出的最大中心
    val ideal = viewportH * 0.30f
    val center = if (minC <= maxC) ideal.coerceIn(minC, maxC) else minC
    return (beforeContentPaddingPx + h / 2f - center).roundToInt()
}

/** 无歌词徽标（MAUI NoLyricsBadge：玻璃样式、贴底居中） */
@Composable
private fun NoLyricsBadge(text: String, modifier: Modifier = Modifier) {
    val palette = LocalCatClawPalette.current
    Box(
        modifier = modifier
            .glassCard(palette = palette, corner = 20)
            .padding(horizontal = 18.dp, vertical = 10.dp),
        contentAlignment = Alignment.Center,
    ) {
        Text(
            text = text,
            fontSize = 14.sp,
            color = Color(palette.textSecondary),
        )
    }
}

/** 封面圆角块（横屏左栏）：底层辉光 + 圆角 28 封面本体，取不到封面退回主色渐变 + 首字母。
 *  辉光对齐 MAUI LandscapeCoverGlow（FullLyricsPage.xaml:120-128，尺寸随封面同步见 xaml.cs:380-393）：
 *  同尺寸同圆角 28、Opacity 0.35、Scale 1.08、TranslationY 10，颜色取封面均色（MAUI 那层
 *  BackgroundColor 停在 Transparent，按本移植的 how 取均色补上），叠 blur 做柔光。 */
@Composable
private fun CoverTileOrb(
    coverArtPath: String?,
    audioPath: String?,
    title: String,
    glow: Color,
    modifier: Modifier = Modifier,
) {
    val palette = LocalCatClawPalette.current
    Box(modifier = modifier) {
        Box(
            modifier = Modifier
                .matchParentSize()
                .graphicsLayer {
                    scaleX = 1.08f
                    scaleY = 1.08f
                    translationY = 10.dp.toPx()
                    alpha = 0.35f
                }
                .blur(24.dp)
                .clip(RoundedCornerShape(28.dp))
                .background(glow),
        )
        CoverTile(
            coverArtPath = coverArtPath,
            audioPath = audioPath,
            maxPx = 720,
            modifier = Modifier
                .fillMaxSize()
                .clip(RoundedCornerShape(28.dp))
                .border(0.5.dp, Color.White.copy(alpha = 0.30f), RoundedCornerShape(28.dp)),
        ) {
            Text(
                text = title.take(1),
                fontSize = 40.sp,
                fontWeight = FontWeight.Bold,
                color = Color(inkOnGradient(listOf(palette.primaryLight, palette.primary))),
            )
        }
    }
}

/** 全屏歌词页的设置面板：档位与默认值对齐 MAUI `LyricsSettingsService` */
@Composable
private fun LyricsSettingsPanel(
    lineMode: Boolean,
    sourceMode: com.catclaw.music.kt.data.lyrics.LyricsSourceMode,
    alignment: Int,
    fontSizeSp: Float,
    removeEmpty: Boolean,
    translation: Boolean,
    roma: Boolean,
    onLineMode: () -> Unit,
    onSource: (com.catclaw.music.kt.data.lyrics.LyricsSourceMode) -> Unit,
    onAlignment: (Int) -> Unit,
    onFontSize: (Float) -> Unit,
    onRemoveEmpty: (Boolean) -> Unit,
    onTranslation: (Boolean) -> Unit,
    onRoma: (Boolean) -> Unit,
    onClose: () -> Unit,
) {
    val palette = LocalCatClawPalette.current
    val surface = Color(palette.backgroundStops.last().argb)
    val primary = Color(palette.textPrimary)

    val sheetShape = RoundedCornerShape(topStart = 26.dp, topEnd = 26.dp)
    Box(modifier = Modifier.fillMaxSize().clickable(onClick = onClose)) {
        Column(
            modifier = Modifier
                .align(Alignment.BottomCenter)
                .fillMaxWidth()
                .clip(sheetShape)
                .background(surface)
                .border(1.dp, primary.copy(alpha = 0.16f), sheetShape)
                .clickable(enabled = false) {}
                .padding(horizontal = 20.dp, vertical = 8.dp),
        ) {
            Box(
                modifier = Modifier.fillMaxWidth().padding(bottom = 8.dp),
                contentAlignment = Alignment.Center,
            ) {
                Box(
                    modifier = Modifier
                        .size(width = 38.dp, height = 4.dp)
                        .clip(RoundedCornerShape(999.dp))
                        .background(primary.copy(alpha = 0.22f)),
                )
            }
            Text(stringResource(R.string.lyric_panel), style = MaterialTheme.typography.titleMedium, color = primary)
            Spacer(Modifier.height(10.dp))
            Text(stringResource(R.string.lyric_mode), style = MaterialTheme.typography.labelMedium, color = Color(palette.textSecondary))
            ChipRow(
                labels = listOf(
                    stringResource(R.string.lyric_mode_word) to 1,
                    stringResource(R.string.lyric_mode_line) to 0,
                ),
                selected = if (lineMode) 1 else 0,
                onPick = { onLineMode() },
            )
            Text(stringResource(R.string.lyric_source), style = MaterialTheme.typography.labelMedium, color = Color(palette.textSecondary))
            ChipRow(
                labels = listOf(
                    stringResource(R.string.lyric_source_auto) to 0,
                    stringResource(R.string.lyric_source_embedded) to 1,
                    stringResource(R.string.lyric_source_external) to 2,
                ),
                selected = sourceMode.ordinal,
                onPick = { onSource(com.catclaw.music.kt.data.lyrics.LyricsSourceMode.entries[it]) },
            )
            Text(stringResource(R.string.lyric_align), style = MaterialTheme.typography.labelMedium, color = Color(palette.textSecondary))
            ChipRow(
                labels = listOf(
                    stringResource(R.string.lyric_align_left) to 0,
                    stringResource(R.string.lyric_align_center) to 1,
                    stringResource(R.string.lyric_align_right) to 2,
                ),
                selected = alignment,
                onPick = onAlignment,
            )
            // 字号滑杆对齐 MAUI BuildFontSizeSlider（FullLyricsPage.Settings.cs:224-280）：
            // 章节标题 + 右对齐主色粗体当前值独立一行 + 底部「A」/最大值两端标注
            Text(
                text = stringResource(R.string.lyric_font),
                style = MaterialTheme.typography.labelMedium,
                color = Color(palette.textSecondary),
            )
            Text(
                text = fontSizeSp.toInt().toString(),
                style = MaterialTheme.typography.labelLarge,
                fontWeight = FontWeight.Bold,
                // 主色当字色必须过可读性判据（VisualLanguageTest）：不足 3:1 退回正文色
                color = Color(highlightOnGlassCard(palette)),
                textAlign = TextAlign.End,
                modifier = Modifier.fillMaxWidth(),
            )
            Slider(
                value = fontSizeSp.coerceIn(LyricsSettings.MIN_FONT, LyricsSettings.MAX_FONT),
                onValueChange = onFontSize,
                valueRange = LyricsSettings.MIN_FONT..LyricsSettings.MAX_FONT,
                steps = (LyricsSettings.MAX_FONT - LyricsSettings.MIN_FONT).toInt() - 1,
                // 这一页在浅色主题下也钉成深色，所以滑杆不能沿用环境色
                colors = SliderDefaults.colors(
                    thumbColor = Color(palette.primary),
                    activeTrackColor = Color(palette.primary),
                    inactiveTrackColor = Color(palette.textPrimary).copy(alpha = 0.18f),
                ),
            )
            // MAUI rangeGrid：左「A」右最大值（textHint 11sp；本端调色板无 textHint 档，取 textSecondary 降透明）
            Row(modifier = Modifier.fillMaxWidth()) {
                Text(text = "A", fontSize = 11.sp, color = Color(palette.textSecondary).copy(alpha = 0.6f))
                Spacer(Modifier.weight(1f))
                Text(
                    text = LyricsSettings.MAX_FONT.toInt().toString(),
                    fontSize = 11.sp,
                    color = Color(palette.textSecondary).copy(alpha = 0.6f),
                )
            }
            ToggleRow(
                label = stringResource(R.string.lyric_remove_empty),
                checked = removeEmpty,
                onChecked = onRemoveEmpty,
                description = stringResource(R.string.lyric_remove_empty_desc),
            )
            ToggleRow(stringResource(R.string.lyric_show_translation), translation, onTranslation)
            ToggleRow(stringResource(R.string.lyric_show_roma), roma, onRoma)
        }
    }
}

@Composable
private fun ChipRow(labels: List<Pair<String, Int>>, selected: Int, onPick: (Int) -> Unit) {
    Row(horizontalArrangement = Arrangement.spacedBy(8.dp), modifier = Modifier.padding(vertical = 6.dp)) {
        labels.forEach { (label, value) ->
            GlassChip(label = label, selected = value == selected, onClick = { onPick(value) })
        }
    }
}

@Composable
private fun ToggleRow(label: String, checked: Boolean, onChecked: (Boolean) -> Unit, description: String? = null) {
    val palette = LocalCatClawPalette.current
    Row(verticalAlignment = Alignment.CenterVertically, modifier = Modifier.fillMaxWidth()) {
        Column(modifier = Modifier.weight(1f)) {
            Text(
                text = label,
                style = MaterialTheme.typography.bodyMedium,
                color = Color(palette.textPrimary),
            )
            // 灰色副描述（MAUI BuildToggleSwitch 的 descLabel：13sp textSecondary）
            if (description != null) {
                Text(
                    text = description,
                    style = MaterialTheme.typography.bodySmall,
                    color = Color(palette.textSecondary),
                )
            }
        }
        Switch(
            checked = checked,
            onCheckedChange = onChecked,
            // 这一页在浅色主题下也钉成深色，沿用环境色会让开关拿浅色方案的底色，落在深底上像别人的组件
            colors = SwitchDefaults.colors(
                checkedThumbColor = Color(palette.primary),
                checkedTrackColor = Color(palette.primary).copy(alpha = 0.32f),
                uncheckedThumbColor = Color(palette.textSecondary),
                uncheckedTrackColor = Color(palette.textPrimary).copy(alpha = 0.18f),
            ),
        )
    }
}
