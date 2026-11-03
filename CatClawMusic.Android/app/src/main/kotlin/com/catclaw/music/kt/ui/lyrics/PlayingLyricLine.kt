package com.catclaw.music.kt.ui.lyrics

import androidx.compose.animation.AnimatedContent
import androidx.compose.animation.core.tween
import androidx.compose.animation.fadeIn
import androidx.compose.animation.fadeOut
import androidx.compose.animation.scaleIn
import androidx.compose.animation.scaleOut
import androidx.compose.animation.slideInVertically
import androidx.compose.animation.slideOutVertically
import androidx.compose.animation.togetherWith
import androidx.compose.ui.draw.clipToBounds
import androidx.compose.ui.text.TextMeasurer
import androidx.compose.ui.unit.TextUnit
import androidx.compose.foundation.clickable
import androidx.compose.foundation.interaction.MutableInteractionSource
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.derivedStateOf
import androidx.compose.runtime.getValue
import androidx.compose.runtime.remember
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.text.TextStyle
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.sp
import kotlinx.coroutines.launch
import androidx.compose.ui.unit.dp
import androidx.compose.ui.text.AnnotatedString
import androidx.compose.ui.text.rememberTextMeasurer
import androidx.compose.ui.layout.onSizeChanged
import androidx.compose.runtime.mutableIntStateOf
import androidx.compose.runtime.setValue
import androidx.hilt.navigation.compose.hiltViewModel
import androidx.lifecycle.compose.collectAsStateWithLifecycle
import com.catclaw.music.kt.R
import com.catclaw.music.kt.core.lyrics.LyricFillCalculator
import com.catclaw.music.kt.core.lyrics.LyricsState
import com.catclaw.music.kt.ui.theme.LocalCatClawPalette

/**
 * 播放页的那一行歌词 —— **自己的排版，不复用全屏歌词页的组件**。
 *
 * 2026-10-18 用户定的形状：一行、**和图标一样大**、**靠左**、贴在进度条上方（与收藏/更多同一排）。
 * 那之前它是 `LyricLineText` 的薄包装：一行行 override 把"多行滚动页"的组件压成页脚注脚，
 * 每改一次都要在别人的参数表里找位置。拆开之后这一页只有下面这些规则，全屏页那套（多行、放大 1.25、
 * 译文/罗马音、可设字号与对齐）一个都带不过来。
 *
 * 与全屏页**只共用两样东西**，都在 [KaraokeFill]：纯算术（[karaokeFilledRows]）与文本原语（[KaraokeText]）。
 * 数据侧同样只共用核心逻辑：哪一句是当前句（[LyricsState]）与逐字进度（[LyricFillCalculator]）。
 *
 * 对唱（`line.secondaryText`）在播放页**只显示主句**：这行要跟图标齐平，分栏会让它高出一截；
 * 想看两边对唱去全屏页（那里左右分栏）。
 */

/** 播放页这一行的字号：与那排图标同高（40dp 触达框里的图标 22dp） */
private const val PlayingLyricFontSize = 15f

/** 长句自动缩字号的下限：再小就不如让它换行/被切了 */
private const val MinPlayingLyricFontSize = 10f

@Composable
fun PlayingLyricLine(
    onOpenLyrics: () -> Unit,
    modifier: Modifier = Modifier,
    viewModel: LyricsViewModel = hiltViewModel(),
) {
    // 订阅歌词本体（它变了才需要重取下面那份）；位置与设置各有自己的流，不牵动这里
    val lyricsValue by viewModel.lyrics.collectAsStateWithLifecycle()
    val removeEmptyLines by viewModel.removeEmptyLines.collectAsStateWithLifecycle()
    val shown = remember(lyricsValue, removeEmptyLines) { viewModel.visibleLyrics() }
    val lines = shown?.lines.orEmpty()
    val palette = LocalCatClawPalette.current

    // 点进全屏歌词页：播放页上"点词 seek"让位给"看整首词"这个更常用的意图
    val open: () -> Unit = onOpenLyrics

    if (lines.isEmpty()) {
        Text(
            text = stringResource(R.string.lyric_none),
            style = MaterialTheme.typography.bodyMedium,
            color = Color(palette.textSecondary),
            maxLines = 1,
            overflow = TextOverflow.Ellipsis,
            modifier = modifier
                .fillMaxWidth()
                .clickable(onClick = open)
                .padding(vertical = 2.dp),
        )
        return
    }

    val lineMode by viewModel.lineMode.collectAsStateWithLifecycle()
    val positionMsState = viewModel.positionMs.collectAsStateWithLifecycle()

    // 行号用 derivedStateOf 现算：位置每 16ms 变一次，但只有换句才让屏幕重组
    val currentIndex by remember(shown, lineMode) {
        derivedStateOf {
            LyricsState.present(
                lyrics = shown,
                positionMs = positionMsState.value,
                lineMode = lineMode,
            )?.index ?: -1
        }
    }

    val index = currentIndex
    if (index !in lines.indices) return
    val line = lines[index]

    // 不换行之后，长句必须靠**缩字号**才不会被切掉（成熟实现也是这条：一句一行、长了就缩）。
    // 只在「换句 / 宽度变化」时量一次，不进每帧路径。
    var availWidthPx by remember { mutableIntStateOf(0) }
    val measurer = rememberTextMeasurer()

    Box(
        modifier = modifier
            .fillMaxWidth()
            // 换句动画里文字会上下滑动，不裁的话会压到上面那条「作词/收藏/更多」那排
            .clipToBounds()
            .onSizeChanged { availWidthPx = it.width }
            // 空白处轻击也进全屏歌词页（文本自身的 clickable 会先消费字上的点击）
            .clickable(
                interactionSource = remember { MutableInteractionSource() },
                indication = null,
                onClickLabel = stringResource(R.string.PageTitleFullLyrics),
            ) { open() },
        contentAlignment = Alignment.CenterStart,
    ) {
        // 2026-11-03 用户要求：换句加动画。样式见 [LyricSwitchAnimation]，改那一行常量就能换风格。
        // 用 index + 歌词对象当 targetState：换歌时即使行号撞上也会过渡。
        AnimatedContent(
            targetState = index to shown,
            transitionSpec = {
                val forward = targetState.first >= initialState.first
                // 基准时长都写「原值」，再由 LyricSwitchSpeed 统一放大 —— 觉得快/慢只改那一个数
                when (LyricSwitchStyle) {
                    // 上移淡入：旧句往上飘走、新句从下面浮上来（Apple Music 那一档，最稳）
                    LyricSwitchAnimation.SlideUp ->
                        (
                            fadeIn(tween(lyricMs(220))) +
                                slideInVertically(tween(lyricMs(320))) { if (forward) it * 2 / 3 else -it * 2 / 3 } +
                                scaleIn(tween(lyricMs(320)), initialScale = 0.96f)
                            ).togetherWith(
                            fadeOut(tween(lyricMs(130))) +
                                slideOutVertically(tween(lyricMs(320))) { if (forward) -it / 2 else it / 2 },
                        )
                    // 纯淡入淡出：最克制，慢歌/R&B 不抢戏
                    LyricSwitchAnimation.Fade ->
                        fadeIn(tween(lyricMs(260))).togetherWith(fadeOut(tween(lyricMs(200))))
                    // 弹出：新句从小放大+淡入，有一点点「砸下来」的力量感，适合快歌
                    LyricSwitchAnimation.Pop -> (
                        fadeIn(tween(lyricMs(200))) + scaleIn(tween(lyricMs(280)), initialScale = 0.82f)
                        ).togetherWith(
                        fadeOut(tween(lyricMs(140))) + scaleOut(tween(lyricMs(200)), targetScale = 0.94f),
                    )
                    // 遮罩上滑：不淡入，纯靠裁切滑动，像翻页机（配合外层 clipToBounds 才有味道）
                    LyricSwitchAnimation.Wipe ->
                        slideInVertically(tween(lyricMs(300))) { if (forward) it else -it }
                            .togetherWith(slideOutVertically(tween(lyricMs(300))) { if (forward) -it else it })
                }
            },
            label = "playingLyricSwitch",
        ) { target ->
            val targetIndex = target.first
            val targetLine = lines.getOrNull(targetIndex) ?: return@AnimatedContent
            KaraokeText(
                text = targetLine.text,
                // 进度在**绘制期**读：位置流每 16ms 一发，写成值就会每帧重组整棵子树
                // （真机 20fps / 57% janky 的来源）。这样这一页只在换句时重组。
                progress = {
                    LyricFillCalculator.computeFillProgress(
                        line = targetLine,
                        lineIndex = targetIndex,
                        allLines = lines,
                        positionMs = positionMsState.value,
                        lineMode = lineMode,
                    ).toFloat()
                },
                // 高亮 = 正文色（深色主题下即白色），未唱 = 同色降透明度（不靠色相表进度）
                baseColor = Color(palette.textPrimary).copy(alpha = 0.45f),
                fillColor = Color(palette.textPrimary),
                textAlign = TextAlign.Start,
                style = TextStyle(fontSize = rememberFittedLyricSize(targetLine.text, availWidthPx, measurer)),
                modifier = Modifier.fillMaxWidth(),
                // 一行就是一行：不换行 + 自动缩字号（上面量好的尺寸）
                singleLine = true,
            )
        }
    }
}

/**
 * 播放页换句动画的四种备选 —— 改下面那一行常量就能整体换风格（参数都是实测后可调的经验值）。
 *
 * - [SlideUp] 旧句上飘淡出、新句下方浮入：最通用，信息连贯，不会让人眼重新找位置。
 * - [Fade]    纯交叉淡入淡出：最克制，适合慢歌/纯音乐，几乎不打扰。
 * - [Pop]     新句缩小→放大弹出：力量感强，快歌/鼓点重时好用，慢歌会显得吵。
 * - [Wipe]    无淡化、靠裁切滑动：像机械翻页，个性最强，但连续快句时容易晕。
 */
private enum class LyricSwitchAnimation { SlideUp, Fade, Pop, Wipe }

/** 当前使用的换句动画 */
private val LyricSwitchStyle = LyricSwitchAnimation.SlideUp

/**
 * 换句动画的整体速度倍率：1.0 = 代码里的基准值（当前 SlideUp 约 320ms）。
 * 2026-11-03 用户反馈「有动画但太快了」，放到 1.7（≈540ms）。
 * 再快改 1.3，再慢改 2.2。四个样式的时长都走这里，不用逐个改。
 */
private const val LyricSwitchSpeed = 1.7f

/** 把基准毫秒按 [LyricSwitchSpeed] 放大 */
private fun lyricMs(base: Int): Int = (base * LyricSwitchSpeed).toInt()

/** 长句自动缩字号的结果：只在「文本或可用宽度变化」时重算一次。 */
@Composable
private fun rememberFittedLyricSize(text: String, availWidthPx: Int, measurer: TextMeasurer): TextUnit {
    return remember(text, availWidthPx, measurer) {
        if (availWidthPx <= 0) {
            PlayingLyricFontSize.sp
        } else {
            var size = PlayingLyricFontSize
            while (size > MinPlayingLyricFontSize) {
                val w = measurer.measure(
                    text = AnnotatedString(text),
                    style = TextStyle(fontSize = size.sp),
                    maxLines = 1,
                    softWrap = false,
                ).size.width
                if (w <= availWidthPx) break
                size -= 0.5f
            }
            size.sp
        }
    }
}
