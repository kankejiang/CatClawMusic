package com.catclaw.music.kt.ui.lyrics

import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.wrapContentWidth
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableFloatStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.drawWithContent
import androidx.compose.ui.graphics.TransformOrigin
import androidx.compose.ui.graphics.drawscope.clipPath
import androidx.compose.ui.graphics.drawscope.clipRect
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.graphicsLayer
import androidx.compose.ui.layout.onSizeChanged
import androidx.compose.ui.graphics.Path
import androidx.compose.ui.graphics.RectangleShape
import androidx.compose.ui.text.TextLayoutResult
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.geometry.Rect
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import com.catclaw.music.kt.core.lyrics.LyricLine
import com.catclaw.music.kt.ui.theme.LocalCatClawPalette
import kotlin.math.max

/**
 * 一行歌词（**全屏歌词页**专用）：对唱左右分栏、当前行逐字填充、可选译文/罗马音副行。
 *
 * 播放页不再复用本组件（2026-10-18 用户要求）：那一页只有一行、图标大小、靠左，走 `PlayingLyricLine`。
 * 两边只共用 `KaraokeFill.kt` 里的纯算术与 `KaraokeText` 文本原语。
 *
 * 当前行的放大只作用**主行**（对齐 MAUI：Scale 只设在主歌词 KaraokeLabel 上，
 * LyricEngine.cs:353-372；roma/trans 副行永不缩放、按原字号并入行高，:531-543）：
 * - 主行按 1/[currentScaleTarget] 宽布局再放大 currentScale，视觉回到满宽、断行前后一致；
 * - 缩放锚点跟随对齐档位（MAUI AnchorX：左 0 / 中 0.5 / 右 1，:188-189），
 *   左对齐从左缘向右生长、右对齐从右缘向左生长；
 * - 非当前行主行统一 α0.35（MAUI HighlightLine 的 Opacity=0.35）。
 */
@Composable
fun LyricLineText(
    line: LyricLine,
    isCurrent: Boolean,
    progress: Double,
    showExtras: Boolean,
    onClick: () -> Unit,
    modifier: Modifier = Modifier,
    fontSizeSp: Float = 0f,
    alignmentOverride: Int = -1,
    showTranslation: Boolean = true,
    showRoma: Boolean = true,
    currentScale: Float = 1f,
    currentScaleTarget: Float = 1f,
    onMainSizeChanged: (Int) -> Unit = {},
) {
    val palette = LocalCatClawPalette.current
    // 非当前行的淡出走字色 alpha（原来是 graphicsLayer 的 alpha .35，那会给每行一个离屏图层）
    // 逐字高亮**不用主题强调色（粉）**：高亮 = 正文色（深色主题下就是白色），
    // 未唱 = 同色降透明度 —— 靠「白 vs 半透明白」表现进度，而不是靠色相。
    // 理由：粉色高亮会和封面染色/封面流背景混在一起（浅色主题下也不够醒目）；
    // 白字对白字的对比在两种主题下都成立（浅色主题 textPrimary 是深墨色，照样可辨）。
    val baseColor = if (isCurrent) {
        Color(palette.textPrimary).copy(alpha = 0.45f)
    } else {
        Color(palette.textSecondary).copy(alpha = 0.35f)
    }
    val fillColor = Color(palette.textPrimary)
    val base = if (isCurrent) MaterialTheme.typography.titleLarge else MaterialTheme.typography.titleMedium
    // 字号档位来自 LyricsSettings；0 表示沿用主题字号
    val style = if (fontSizeSp > 0f) base.copy(fontSize = fontSizeSp.sp) else base
    // 副行字号随主字号走：MAUI transSize = max(10, 主字号−2)（LyricEngine.cs:161-162），随字号滑杆重建
    val subFontSize = if (fontSizeSp > 0f) max(10f, fontSizeSp - 2f).sp else null
    val romaStyle = MaterialTheme.typography.labelMedium.let { if (subFontSize != null) it.copy(fontSize = subFontSize) else it }
    val transStyle = MaterialTheme.typography.bodyMedium.let { if (subFontSize != null) it.copy(fontSize = subFontSize) else it }
    val alignOf: (Int) -> TextAlign = { textAlignOf(if (alignmentOverride >= 0) alignmentOverride else it) }
    // 对齐档位 → 主行缩放锚点与列内对齐（MAUI AnchorX：Center?0.5:(End?1.0:0.0)，LyricEngine.cs:188-189）
    val alignValue = if (alignmentOverride >= 0) alignmentOverride else line.alignment
    val (boxAlign, anchorX) = when (alignValue) {
        0 -> Alignment.Start to 0f
        2 -> Alignment.End to 1f
        else -> Alignment.CenterHorizontally to 0.5f
    }

    Column(
        modifier = modifier
            .fillMaxWidth()
            .clickable(onClick = onClick)
            .padding(horizontal = 18.dp, vertical = 9.dp),
    ) {
        Box(
            modifier = Modifier
                // 当前行按 1/倍率 宽布局（窄宽立即生效，MAUI ApplyLabelWidthRole），放大后视觉满宽
                .fillMaxWidth(if (isCurrent) 1f / currentScaleTarget else 1f)
                .align(boxAlign)
                .onSizeChanged { onMainSizeChanged(it.height) }
                // 只有**当前行**要图层（它在做 1.25 倍缩放动画，唯一需要单独合成的行）。
                // 非当前行原来也挂 graphicsLayer，只为那层 alpha .35 —— 歌词页一屏十几行，
                // 就是十几个离屏缓冲，GPU 直接吃满（120Hz 每帧只有 8.3ms，2026-10-18 实测）。
                // 透明度改成字色自带 alpha（[baseColor]），效果一样但不需要图层。
                .then(
                    if (isCurrent) {
                        Modifier.graphicsLayer {
                            scaleX = currentScale
                            scaleY = currentScale
                            transformOrigin = TransformOrigin(anchorX, 0.5f)
                        }
                    } else {
                        Modifier
                    },
                ),
        ) {
            val secondary = line.secondaryText
            if (secondary != null) {
                // 对唱先后渲染：主句先填满（行时长的前半段），再轮到副句（后半段）——
                // 两栏同步填充是错误观感（2026-10-03 用户反馈）
                val primaryFill = (progress * 2).coerceAtMost(1.0)
                val secondaryFill = ((progress - 0.5) * 2).coerceIn(0.0, 1.0)
                Row(modifier = Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.SpaceBetween) {
                    KaraokeText(line.text, { (primaryFill.takeIf { isCurrent } ?: 0.0).toFloat() }, baseColor, fillColor, alignOf(line.alignment), style, Modifier.weight(1f))
                    KaraokeText(secondary, { (secondaryFill.takeIf { isCurrent } ?: 0.0).toFloat() }, baseColor, fillColor, alignOf(line.secondaryAlignment), style, Modifier.weight(1f))
                }
            } else {
                KaraokeText(line.text, { (progress.takeIf { isCurrent } ?: 0.0).toFloat() }, baseColor, fillColor, alignOf(line.alignment), style, Modifier.fillMaxWidth())
            }
        }

        if (showExtras) {
            // 副行满宽、永不缩放；无行数上限（MAUI LineBreakMode.WordWrap，LyricEngine.cs:231/267）
            // 罗马音在原文与译文之间（MAUI BuildLyricStack 的 vStack 顺序：原文 → 罗马音 → 译文）
            if (showRoma) {
                line.roma?.takeIf { it.isNotBlank() }?.let {
                    Text(
                        text = it,
                        style = romaStyle,
                        color = Color(palette.textSecondary).copy(alpha = 0.7f),
                        textAlign = alignOf(line.alignment),
                        modifier = Modifier.fillMaxWidth().padding(top = 2.dp),
                    )
                }
            }
            if (showTranslation) {
                line.translation?.takeIf { it.isNotBlank() }?.let {
                    Text(
                        text = it,
                        style = transStyle,
                        color = Color(palette.textSecondary),
                        textAlign = alignOf(line.alignment),
                        modifier = Modifier.fillMaxWidth().padding(top = 3.dp),
                    )
                }
            }
        }
    }
}


/** 对齐档位 → TextAlign（MAUI 0 左 / 1 中 / 2 右） */
private fun textAlignOf(alignment: Int): TextAlign = when (alignment) {
    0 -> TextAlign.Start
    2 -> TextAlign.End
    else -> TextAlign.Center
}
