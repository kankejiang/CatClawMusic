package com.catclaw.music.kt.ui.lyrics

import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.wrapContentWidth
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableFloatStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.draw.drawWithContent
import androidx.compose.ui.graphics.drawscope.clipRect
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.layout.onSizeChanged
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import com.catclaw.music.kt.core.lyrics.LyricLine
import com.catclaw.music.kt.ui.theme.LocalCatClawPalette

/** 一行歌词：对唱左右分栏、当前行逐字填充、可选译文/罗马音副行 */
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
) {
    val palette = LocalCatClawPalette.current
    val baseColor = if (isCurrent) Color(palette.textPrimary) else Color(palette.textSecondary)
    val fillColor = Color(palette.primary)
    val base = if (isCurrent) MaterialTheme.typography.titleLarge else MaterialTheme.typography.titleMedium
    // 字号档位来自 LyricsSettings；0 表示沿用主题字号
    val style = if (fontSizeSp > 0f) base.copy(fontSize = fontSizeSp.sp) else base
    val alignOf: (Int) -> TextAlign = { textAlignOf(if (alignmentOverride >= 0) alignmentOverride else it) }

    Column(
        modifier = modifier
            .fillMaxWidth()
            .clip(RoundedCornerShape(10.dp))
            .clickable(onClick = onClick)
            .padding(horizontal = 18.dp, vertical = 9.dp),
    ) {
        val secondary = line.secondaryText
        if (secondary != null) {
            Row(modifier = Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.SpaceBetween) {
                KaraokeSpan(line.text, progress.takeIf { isCurrent } ?: 0.0, baseColor, fillColor, alignOf(line.alignment), style, Modifier.weight(1f))
                KaraokeSpan(secondary, progress.takeIf { isCurrent } ?: 0.0, baseColor, fillColor, alignOf(line.secondaryAlignment), style, Modifier.weight(1f))
            }
        } else {
            KaraokeSpan(line.text, progress.takeIf { isCurrent } ?: 0.0, baseColor, fillColor, alignOf(line.alignment), style, Modifier.fillMaxWidth())
        }

        if (showExtras && showTranslation) {
            line.translation?.takeIf { it.isNotBlank() }?.let {
                Text(
                    text = it,
                    style = MaterialTheme.typography.bodyMedium,
                    color = Color(palette.textSecondary),
                    textAlign = alignOf(line.alignment),
                    maxLines = 2,
                    overflow = TextOverflow.Ellipsis,
                    modifier = Modifier.fillMaxWidth().padding(top = 3.dp),
                )
            }
            if (showRoma) line.roma?.takeIf { it.isNotBlank() }?.let {
                Text(
                    text = it,
                    style = MaterialTheme.typography.labelMedium,
                    color = Color(palette.textSecondary).copy(alpha = 0.7f),
                    textAlign = alignOf(line.alignment),
                    maxLines = 2,
                    overflow = TextOverflow.Ellipsis,
                    modifier = Modifier.fillMaxWidth().padding(top = 2.dp),
                )
            }
        }
    }
}

/**
 * 逐字填充：底层整行原色，上层彩字按进度裁切宽度。
 *
 * 裁切区间按**这一行画出来的字宽**算，不是按整块宽度算：
 * 居中/右对齐时文字左右都有留白，若从控件左边缘开始扫，进度条前半段在切空气，
 * 彩字要到半程才追上真正唱到的字（原来就是这么错的）。
 */
@Composable
private fun KaraokeSpan(
    text: String,
    progress: Double,
    baseColor: Color,
    fillColor: Color,
    textAlign: TextAlign,
    style: androidx.compose.ui.text.TextStyle,
    modifier: Modifier = Modifier,
) {
    var boxWidth by remember { mutableFloatStateOf(0f) }
    var lineWidth by remember { mutableFloatStateOf(0f) }

    Box(modifier = modifier.onSizeChanged { boxWidth = it.width.toFloat() }) {
        // 透明的「按内容测量」层：只用来量出这一行画出来的宽度（不 fillMaxWidth，所以量到的是字宽不是块宽）。
        // progress=0 时彩字层根本不画，这层也就没必要存在——歌词页上百行都在滚动
        if (progress > 0.0) {
            Text(
                text = text,
                style = style,
                color = Color.Transparent,
                maxLines = 3,
                overflow = TextOverflow.Ellipsis,
                modifier = Modifier
                    .wrapContentWidth()
                    .onSizeChanged { lineWidth = minOf(it.width.toFloat(), boxWidth) },
            )
        }
        Text(
            text = text,
            style = style,
            color = baseColor,
            textAlign = textAlign,
            maxLines = 3,
            overflow = TextOverflow.Ellipsis,
            modifier = Modifier.fillMaxWidth(),
        )
        if (progress > 0.0 && boxWidth > 0f) {
            // 量不到行宽就退回控件宽度（= 旧行为），不能让彩字层直接消失
            val measuredLine = if (lineWidth > 0f) lineWidth else boxWidth
            val (clipLeft, clipRight) = karaokeFillBounds(textAlign, boxWidth, measuredLine, progress.toFloat())
            Box(
                modifier = Modifier
                    .fillMaxWidth()
                    .drawWithContent {
                        clipRect(left = clipLeft, top = 0f, right = clipRight, bottom = size.height) {
                            this@drawWithContent.drawContent()
                        }
                    },
            ) {
                Text(
                    text = text,
                    style = style,
                    color = fillColor,
                    textAlign = textAlign,
                    maxLines = 3,
                    overflow = TextOverflow.Ellipsis,
                    modifier = Modifier.fillMaxWidth(),
                )
            }
        }
    }
}

/**
 * 彩字层的裁切区间：先按对齐算出这一行的左边界，再从行首扫 progress 个行宽。
 * 抽成纯函数是因为这一步只有算术可对证，像素效果在这台机器上没法截图。
 */
internal fun karaokeFillBounds(
    textAlign: TextAlign,
    boxWidthPx: Float,
    lineWidthPx: Float,
    progress: Float,
): Pair<Float, Float> {
    val p = progress.coerceIn(0f, 1f)
    val left = when (textAlign) {
        TextAlign.Center -> (boxWidthPx - lineWidthPx) / 2f
        TextAlign.End -> boxWidthPx - lineWidthPx
        else -> 0f
    }
    val right = left + lineWidthPx * p
    // 夹回控件范围内：Ellipsize 后行宽可能略大于实测宽度
    return left.coerceIn(0f, boxWidthPx) to right.coerceIn(0f, boxWidthPx)
}

private fun textAlignOf(alignment: Int): TextAlign = when (alignment) {
    0 -> TextAlign.Start
    2 -> TextAlign.End
    else -> TextAlign.Center
}
