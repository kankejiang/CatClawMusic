package com.catclaw.music.kt.ui.theme

import androidx.compose.foundation.Canvas
import androidx.compose.runtime.Composable
import androidx.compose.ui.Modifier
import androidx.compose.ui.geometry.CornerRadius
import androidx.compose.ui.geometry.Offset
import androidx.compose.ui.geometry.Size
import androidx.compose.ui.graphics.Brush
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.drawscope.Stroke
import androidx.compose.ui.text.TextStyle
import androidx.compose.ui.text.drawText
import androidx.compose.ui.text.rememberTextMeasurer
import androidx.compose.ui.text.style.TextAlign

/**
 * 没封面歌曲的占位封面：**代码绘制的唱片碟**。
 *
 * 移植自 MAUI `Services/DefaultCoverService.cs` 里的 `VinylDrawable`（原实现把这张图渲染成 720px PNG
 * 缓存到 AppDataDirectory，再用图片管线加载）。Compose 这边不需要落盘：全是圆和两行字，
 * 直接画更省事、还能跟着主题实时换色。
 *
 * 为什么不用「灰底 + 歌名首字」：那看起来像图片加载失败（2026-10-19 用户反馈「没用封面的歌曲感觉好奇怪」）。
 * 唱片碟是个**有设计感的中性占位**：一眼能看出「这张没有封面」，但不像坏图。
 *
 * 比例全部取自 MAUI（`VinylDrawable.Draw`，同文件 :101-155）：
 * 圆角 0.12S、碟径 0.66S、纹路 0.86R/0.70R、碟心 0.62R、文字 0.052S/0.023S、中心孔 0.012S。
 */
@Composable
fun VinylCover(
    modifier: Modifier = Modifier,
    primary: Color,
    isDark: Boolean = LocalIsDarkTheme.current,
) {
    val measurer = rememberTextMeasurer()
    Canvas(modifier) {
        val s = size.minDimension
        val black = Color.Black
        val white = Color.White
        fun mix(a: Color, b: Color, t: Float) = Color(
            a.red + (b.red - a.red) * t,
            a.green + (b.green - a.green) * t,
            a.blue + (b.blue - a.blue) * t,
            1f,
        )

        // 圆角渐变底（MAUI :107-112）：主题色压得很深/提得很浅，保证碟体跳出来
        val bgTop = if (isDark) mix(primary, black, 0.80f) else mix(primary, white, 0.88f)
        val bgBottom = if (isDark) mix(primary, black, 0.90f) else mix(primary, white, 0.78f)
        drawRoundRect(
            brush = Brush.linearGradient(listOf(bgTop, bgBottom), start = Offset.Zero, end = Offset(s, s)),
            cornerRadius = CornerRadius(s * 0.12f),
            size = Size(s, s),
        )

        val cx = s / 2f
        val cy = s / 2f
        val discR = s * 0.33f

        // 碟体：径向渐变模拟碟面明暗（MAUI :117-122）
        val discCenter = if (isDark) mix(primary, black, 0.68f) else mix(primary, white, 0.52f)
        val discEdge = if (isDark) mix(primary, black, 0.84f) else mix(primary, white, 0.66f)
        drawCircle(
            brush = Brush.radialGradient(listOf(discCenter, discEdge), center = Offset(cx, cy), radius = discR),
            radius = discR,
            center = Offset(cx, cy),
        )

        // 唱片纹路：两道同心圆（MAUI :124-128）
        val grooveColor = if (isDark) white.copy(alpha = 0.10f) else black.copy(alpha = 0.08f)
        val grooveWidth = (s * 0.002f).coerceAtLeast(1f)
        for (k in floatArrayOf(0.86f, 0.70f)) {
            drawCircle(
                color = grooveColor,
                radius = discR * k,
                center = Offset(cx, cy),
                style = Stroke(width = grooveWidth),
            )
        }

        // 碟心标签：主题色渐变圆 + 白描边（MAUI :130-139）
        val labelR = discR * 0.62f
        val labelTop = if (isDark) mix(primary, white, 0.18f) else mix(primary, white, 0.28f)
        val labelBottom = if (isDark) mix(primary, black, 0.28f) else mix(primary, black, 0.08f)
        drawCircle(
            brush = Brush.verticalGradient(
                listOf(labelTop, labelBottom),
                startY = cy - labelR,
                endY = cy + labelR,
            ),
            radius = labelR,
            center = Offset(cx, cy),
        )
        drawCircle(
            color = white.copy(alpha = 0.25f),
            radius = labelR,
            center = Offset(cx, cy),
            style = Stroke(width = (s * 0.003f).coerceAtLeast(1f)),
        )

        // 标签文字（MAUI :141-147）：中文在上半、英文小字在下半
        drawText(
            textMeasurer = measurer,
            text = "猫爪音乐",
            topLeft = Offset(cx - labelR, cy - labelR * 0.78f),
            style = TextStyle(
                color = white.copy(alpha = 0.95f),
                fontSize = (s * 0.052f).toSp(),
                textAlign = TextAlign.Center,
            ),
            size = Size(labelR * 2, labelR * 0.62f),
        )
        drawText(
            textMeasurer = measurer,
            text = "CATCLAW MUSIC",
            topLeft = Offset(cx - labelR, cy + labelR * 0.16f),
            style = TextStyle(
                color = white.copy(alpha = 0.70f),
                fontSize = (s * 0.023f).toSp(),
                textAlign = TextAlign.Center,
            ),
            size = Size(labelR * 2, labelR * 0.62f),
        )

        // 中心孔（MAUI :149-155）
        val holeR = s * 0.012f
        drawCircle(color = bgBottom, radius = holeR, center = Offset(cx, cy))
        drawCircle(
            color = white.copy(alpha = 0.30f),
            radius = holeR,
            center = Offset(cx, cy),
            style = Stroke(width = (s * 0.002f).coerceAtLeast(1f)),
        )
    }
}
