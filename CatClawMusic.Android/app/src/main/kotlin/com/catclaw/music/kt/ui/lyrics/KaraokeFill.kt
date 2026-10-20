package com.catclaw.music.kt.ui.lyrics

import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.wrapContentWidth
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableFloatStateOf
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Modifier
import androidx.compose.ui.text.rememberTextMeasurer
import androidx.compose.ui.text.drawText
import androidx.compose.ui.text.AnnotatedString
import androidx.compose.ui.platform.LocalDensity
import androidx.compose.ui.graphics.SolidColor
import androidx.compose.ui.graphics.Brush
import androidx.compose.ui.geometry.Offset
import androidx.compose.runtime.mutableIntStateOf
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.Canvas
import androidx.compose.ui.draw.drawWithContent
import androidx.compose.ui.geometry.Rect
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.Path
import androidx.compose.ui.graphics.graphicsLayer
import androidx.compose.ui.graphics.drawscope.clipPath
import androidx.compose.ui.graphics.drawscope.clipRect
import androidx.compose.ui.graphics.drawscope.translate
import androidx.compose.ui.layout.onSizeChanged
import androidx.compose.ui.text.TextLayoutResult
import androidx.compose.ui.text.TextStyle
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.sp
import androidx.compose.ui.unit.dp

/**
 * 逐字填充的**共用底料**：几何算术 + 一个「底字 + 彩字覆盖」的文本原语。
 *
 * 为什么单独一个文件：播放页与全屏歌词页**各自有各自的排版**（前者一行、图标大小、靠左、不放大；
 * 后者多行滚动、可设字号/对齐/译文/罗马音、当前行放大 1.25）。
 * 2026-10-18 用户要求「播放页别完全复用全屏页那套」——于是两页的**组件**拆开了：
 * 播放页用 [PlayingLyricLine]，全屏页用 `LyricLineText`，两边只共用这里的东西（纯算术 + 这层文本原语）。
 *
 * 共用底料的边界很清楚：它不认识"页"，只认识"一行带进度的文本"。
 */

/** 排版后的一行：左右是**对齐之后**的真实边界（居中时左右都带留白） */
internal data class KaraokeRow(val left: Float, val top: Float, val right: Float, val bottom: Float)

/**
 * 逐行填充区间：把整块进度摊到每一行上，**前一行填满再填下一行**。
 *
 * 为什么不是一条竖带切到底：长句换行时竖带会把「第一行的前 60%」与「第二行的前 60%」一起染色，
 * 看着就是两行同时唱（2026-10-18 真机投诉）。真机上量不出像素，所以这一步做成纯算术可单测。
 *
 * 份额按**行宽加权**：CJK 等宽时约等于等分，长英文行会拿到更多份额 —— 比按行数等分更贴近"唱到哪儿"。
 * 单行时结果与 [karaokeFillBounds] 完全等价（整行按 progress 扫一遍）。
 */
internal fun karaokeFilledRows(rows: List<KaraokeRow>, progress: Float): List<KaraokeRow> {
    val p = progress.coerceIn(0f, 1f)
    if (rows.isEmpty() || p <= 0f) return emptyList()
    val widths = rows.map { (it.right - it.left).coerceAtLeast(0f) }
    val total = widths.sum()
    // 量到 0 宽（还没排版好）时退回等分，别让整块彩字消失
    val shares = if (total > 0f) widths.map { it / total } else rows.map { 1f / rows.size }
    val travelled = p
    var consumed = 0f
    val out = ArrayList<KaraokeRow>(rows.size)
    rows.forEachIndexed { i, row ->
        val share = shares[i]
        val local = if (share <= 0f) 0f else ((travelled - consumed) / share).coerceIn(0f, 1f)
        if (local > 0f) {
            out += row.copy(right = row.left + (row.right - row.left) * local)
        }
        consumed += share
    }
    return out
}

/**
 * 彩字层的裁切区间（**单行**用；多行见 [karaokeFilledRows]）：
 * 先按对齐算出这一行的左边界，再从行首扫 progress 个行宽。
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

/**
 * 一行带逐字填充的文本：底下画基础色，上面按进度裁出一层强调色。
 *
 * **进度必须是 lambda，不能是值**（2026-10-18 修卡顿）：传值时进度是在**组合期**读的，
 * 位置流每 16ms 一发 → 每帧重组整棵歌词子树 + 重建彩字层的 `Path`。真机实测（release、播放页）：
 * 20fps / 57% janky，`dumpsys gfxinfo` 指向 `Slow issue draw commands` —— 慢在 UI 线程，不是 GPU（GPU 才 8ms）。
 * 改成在 `drawWithContent` 里读进度之后，位置变化只让**绘制**失效：不重组、不重排版，也不分配。
 *
 * 逐行裁切用 `clipRect` 直接切（每行一条），不再拼 `Path`：
 * 一条 Path 每帧都要新建 + 若干个 Rect 对象，而这些在 60fps 下就是持续的 GC 压力。
 * 裁切区间按**这一行排版出来的真实边界**算（`TextLayoutResult` 给），不按整块宽度算 ——
 * 居中/右对齐时文字两边有留白，从控件左边缘扫会先切一段空气。
 */
@Composable
internal fun KaraokeText(
    text: String,
    progress: () -> Float,
    baseColor: Color,
    fillColor: Color,
    textAlign: TextAlign,
    style: TextStyle,
    modifier: Modifier = Modifier,
    /**
     * 单行模式：`maxLines = 1` + `softWrap = false` + `Clip`（不换行、不省略号），
     * 于是填充退化成**一条横切**，不需要逐行边界、也不需要在绘制期读排版结果。
     *
     * 这条路是照 Halcyon（`AppleMusicLyricLine.kt`）的 `singleLine` 抄的：
     * 成熟实现里"一句一行"是**前提**，长句靠缩字号而不是换行 —— 换行会逼出逐行裁切、
     * 每帧读排版、以及"两行一起着色"这类问题（我上一版全踩过）。
     */
    singleLine: Boolean = false,
) {
    // ── 单行模式：一次 drawText + 硬停渐变 ─────────────────────────────────
    //
    // 这是照 Halcyon（`AppleMusicKaraokeText.kt`）的做法：排版只测一次（`TextMeasurer`），
    // 之后每帧只发**一次** `drawText`，填充靠 brush 在进度处硬停 —— 没有第二遍绘制、
    // 没有裁切、没有逐行边界读取。不换行时这是每帧最省的画法。
    if (singleLine) {
        val measurer = rememberTextMeasurer()
        var boxWidth by remember { mutableIntStateOf(0) }
        // 只在「换句 / 字号变 / 宽度变」时量一次；量完这份排版结果被每帧复用
        val measured = remember(text, style, boxWidth) {
            measurer.measure(
                text = AnnotatedString(text),
                style = style,
                maxLines = 1,
                softWrap = false,
            )
        }
        val textHeight = with(LocalDensity.current) { measured.size.height.toDp() }
        Canvas(
            modifier = modifier
                .fillMaxWidth()
                .height(textHeight)
                // 独立图层：让这一小块的重录与页面其余部分解耦（draw 失效不会拖整屏）
                .graphicsLayer()
                .onSizeChanged { boxWidth = it.width },
        ) {
            val p = progress().coerceIn(0f, 1f)
            val w = measured.size.width.toFloat()
            // 进度落在字宽上（与旧的按字宽裁切一致）；左右留白由对齐方式决定，这里单行左对齐
            val brush = when {
                p <= 0f -> SolidColor(baseColor)
                p >= 1f -> SolidColor(fillColor)
                else -> Brush.horizontalGradient(
                    0f to fillColor,
                    p to fillColor,
                    p to baseColor,
                    1f to baseColor,
                    startX = 0f,
                    endX = w,
                )
            }
            // 安全区：父约束给的宽度 = ♡/⋮ 左边那块。超长句**必须裁掉**，否则会画到按钮上
            // （实测：英文长句在 10sp 仍抄出可视区，直接压在收藏心形上）
            val visible = size.width
            val tail = 16.dp.toPx()
            val offset = karaokeScrollOffset(w, visible, w * p, tail)
            clipRect(right = visible, bottom = size.height) {
                // 位移与逐字着色**同源**（都由 w*p 推出）：正在唱的那个字被推到右缘时整句跟着走，
                // 速度自然等于逐字速度，不需要另起一条动画
                translate(left = -offset) {
                    drawText(textLayoutResult = measured, brush = brush)
                }
            }
        }
        return
    }
    // 排版结果：逐行裁切要靠它拿到每行的真实左右边界（对齐后的位置也在里面）
    var layout by remember { mutableStateOf<TextLayoutResult?>(null) }

    // **独立图层**：Compose 默认把整棵树画进同一条 display list，于是逐字进度每 16ms 一变，
    // 整屏（封面 / 玻璃 / 底纹）都要跟着重录一遍 —— 真机上就是「逐字歌词卡」的根因
    // （实测帧时中位数 15ms，而 120Hz 只有 8.3ms 预算）。
    // 加一层 graphicsLayer 之后，这一小块的重录与页面其余部分解耦。
    Box(modifier = modifier.fillMaxWidth().graphicsLayer()) {
        Text(
            text = text,
            style = style,
            color = baseColor,
            textAlign = textAlign,
            maxLines = if (singleLine) 1 else Int.MAX_VALUE,
            softWrap = !singleLine,
            overflow = TextOverflow.Clip,
            onTextLayout = { layout = it },
            modifier = Modifier.fillMaxWidth(),
        )
        // 彩字层常驻（内容与底字相同，只是颜色不同）：进度为 0 时它被裁成空，等于不画。
        // 常驻而不是条件创建，是为了让下面这段 draw 逻辑成为唯一的按帧变化点
        Box(
            modifier = Modifier
                .fillMaxWidth()
                .drawWithContent {
                    val p = progress()
                    if (p <= 0f) return@drawWithContent
                    if (singleLine) {
                        // 一行模式：一条横切扫过去就对了（不换行 → 不存在"哪一行"的问题）
                        clipRect(left = 0f, top = 0f, right = size.width * p, bottom = size.height) {
                            this@drawWithContent.drawContent()
                        }
                        return@drawWithContent
                    }
                    val lr = layout ?: return@drawWithContent
                    val rows = (0 until lr.lineCount).map { line ->
                        KaraokeRow(
                            left = lr.getLineLeft(line),
                            top = lr.getLineTop(line),
                            right = lr.getLineRight(line),
                            bottom = lr.getLineBottom(line),
                        )
                    }
                    // 长句换行时**先填满第一行再填下一行**：一条竖带切到底会让两行同时着色（真机投诉过）
                    karaokeFilledRows(rows, p).forEach { row ->
                        clipRect(left = row.left, top = row.top, right = row.right, bottom = row.bottom) {
                            this@drawWithContent.drawContent()
                        }
                    }
                },
        ) {
            Text(
                text = text,
                style = style,
                color = fillColor,
                textAlign = textAlign,
                maxLines = if (singleLine) 1 else Int.MAX_VALUE,
                softWrap = !singleLine,
                overflow = TextOverflow.Clip,
                modifier = Modifier.fillMaxWidth(),
            )
        }
    }
}

/**
 * 长句横向偏移：让**正在唱的那个字**停在可视区右缘内（留 [tailPx] 余量）。
 *
 * 三条要点：
 * 1. 唱到右缘之前一律不位移（offset=0），所以短句、句子开头都不会跳；
 * 2. 越过之后 offset 是 `sungX` 的**线性**函数（斜率 1），于是整句左移的速度 == 逐字着色速度，
 *    正是「移到速度和逐字歌词保持一致」的要求；
 * 3. 上限 `textWidth - visibleWidth`：唱到句尾时最后一个字贴住右缘，不会把整句拖空。
 *
 * @param sungX 已唱到第几个像素（= 文本宽度 × 进度）
 */
internal fun karaokeScrollOffset(
    textWidth: Float,
    visibleWidth: Float,
    sungX: Float,
    tailPx: Float,
): Float {
    if (textWidth <= visibleWidth || visibleWidth <= 0f) return 0f
    val maxOffset = textWidth - visibleWidth
    return (sungX - (visibleWidth - tailPx)).coerceIn(0f, maxOffset)
}
