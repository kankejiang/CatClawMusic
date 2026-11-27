package com.catclaw.music.kt.ui.share

import android.content.Context
import android.graphics.Bitmap
import android.graphics.BitmapShader
import android.graphics.Canvas
import android.graphics.ColorMatrix
import android.graphics.ColorMatrixColorFilter
import android.graphics.Matrix
import android.graphics.Paint
import android.graphics.RadialGradient
import android.graphics.Rect
import android.graphics.RectF
import android.graphics.Shader
import android.graphics.Typeface
import android.text.Layout
import android.text.StaticLayout
import android.text.TextPaint
import android.text.TextUtils
import androidx.core.content.res.ResourcesCompat
import com.catclaw.music.kt.R
import com.catclaw.music.kt.core.lyrics.LyricLine
import com.catclaw.music.kt.core.lyrics.Lyrics
import com.catclaw.music.kt.core.lyrics.isLyricsMetadataLine
import java.util.Random
import kotlin.math.max

/**
 * 分享产物（图片 / 视频）共用的绘制件。
 *
 * 所有尺寸都按 demo 的 cqw 直接换算：demo 画布宽 = 1080，1cqw = 10.8px。
 * 这里的常量就是换算后的像素值，两个渲染器不再各自重复推导。
 */

internal const val CARD_W = 1080
internal const val CARD_H = 1240
internal const val VIDEO_W = 1080
internal const val VIDEO_H = 1920

/** 米白主色（与 demo `--cream` 一致） */
internal const val CREAM = 0xFFF7E3C8.toInt()
/** 米白 55%（次要文字） */
internal const val CREAM_DIM = 0x8CF7E3C8.toInt()
/** 米白 34%（视频里非当前行歌词） */
internal const val CREAM_FAINT = 0x57F7E3C8.toInt()
/** 米白 22%（虚线分隔） */
internal const val CREAM_LINE = 0x38F7E3C8.toInt()

/** 暖棕底三段（demo 的 158deg 渐变） */
internal const val BROWN_TOP = 0xFF9C6A3C.toInt()
internal const val BROWN_MID = 0xFF81522C.toInt()
internal const val BROWN_BOTTOM = 0xFF5D3A1F.toInt()

/** 分享产物里「猫爪音乐」字标用的圆润艺术字体（站酷快乐体，OFL）。 */
internal fun loadBrandTypeface(context: Context): Typeface? =
    runCatching { ResourcesCompat.getFont(context, R.font.zcool_kuaile) }.getOrNull()

internal fun withAlpha(color: Int, alpha: Int): Int =
    (color and 0x00FFFFFF) or ((alpha.coerceIn(0, 255)) shl 24)

/** 从封面里裁出与目标同比例的一块，再缩到很小的尺寸 —— 拉回目标尺寸就是柔和模糊 */
internal fun centerCropBlur(
    cover: Bitmap,
    width: Int,
    height: Int,
    downscale: Int = 16,
    brightness: Float = 1f,
    saturation: Float = 1f,
): Bitmap {
    val smallW = max(1, width / downscale)
    val smallH = max(1, height / downscale)
    val small = Bitmap.createBitmap(smallW, smallH, Bitmap.Config.ARGB_8888)
    val paint = Paint(Paint.FILTER_BITMAP_FLAG or Paint.ANTI_ALIAS_FLAG)
    if (brightness != 1f || saturation != 1f) {
        paint.colorFilter = ColorMatrixColorFilter(
            ColorMatrix().apply {
                setSaturation(saturation)
                postConcat(ColorMatrix().apply { setScale(brightness, brightness, brightness, 1f) })
            },
        )
    }
    Canvas(small).drawBitmap(
        cover,
        centerCropRect(cover, width, height),
        Rect(0, 0, smallW, smallH),
        paint,
    )
    val out = Bitmap.createBitmap(width, height, Bitmap.Config.ARGB_8888)
    // 直接拉伸低分辨率会看到一块块方形色块；先在小图上抹两遍，边缘就顺了
    blurSmallInPlace(small, when {
        downscale >= 10 -> 3
        downscale >= 4 -> 2
        else -> 1
    })
    Canvas(out).drawBitmap(small, null, Rect(0, 0, width, height), Paint(Paint.FILTER_BITMAP_FLAG))
    small.recycle()
    return out
}

/** 小图上的两遍盒式模糊：只用于抹平缩略图拉伸的方块感，代价很低 */
private fun blurSmallInPlace(bitmap: Bitmap, radius: Int) {
    if (radius <= 0) return
    val w = bitmap.width
    val h = bitmap.height
    if (w <= 0 || h <= 0) return
    val buffer = IntArray(w * h)
    bitmap.getPixels(buffer, 0, w, 0, 0, w, h)
    val temp = IntArray(buffer.size)
    boxBlur(buffer, temp, w, h, radius)
    boxBlur(buffer, temp, w, h, radius)
    bitmap.setPixels(buffer, 0, w, 0, 0, w, h)
}

/** 可分离盒式模糊：input → (横向) output → (纵向) input */
private fun boxBlur(input: IntArray, output: IntArray, w: Int, h: Int, radius: Int) {
    val span = radius * 2 + 1
    for (y in 0 until h) {
        val row = y * w
        var a = 0
        var r = 0
        var g = 0
        var b = 0
        for (i in -radius..radius) {
            val c = input[row + i.coerceIn(0, w - 1)]
            a += c ushr 24
            r += (c shr 16) and 0xFF
            g += (c shr 8) and 0xFF
            b += c and 0xFF
        }
        for (x in 0 until w) {
            output[row + x] = ((a / span) shl 24) or
                ((r / span) shl 16) or ((g / span) shl 8) or (b / span)
            val add = input[row + (x + radius + 1).coerceAtMost(w - 1)]
            val rem = input[row + (x - radius).coerceAtLeast(0)]
            a += (add ushr 24) - (rem ushr 24)
            r += ((add shr 16) and 0xFF) - ((rem shr 16) and 0xFF)
            g += ((add shr 8) and 0xFF) - ((rem shr 8) and 0xFF)
            b += (add and 0xFF) - (rem and 0xFF)
        }
    }
    for (x in 0 until w) {
        var a = 0
        var r = 0
        var g = 0
        var b = 0
        for (i in -radius..radius) {
            val c = output[i.coerceIn(0, h - 1) * w + x]
            a += c ushr 24
            r += (c shr 16) and 0xFF
            g += (c shr 8) and 0xFF
            b += c and 0xFF
        }
        for (y in 0 until h) {
            input[y * w + x] = ((a / span) shl 24) or
                ((r / span) shl 16) or ((g / span) shl 8) or (b / span)
            val add = output[(y + radius + 1).coerceAtMost(h - 1) * w + x]
            val rem = output[(y - radius).coerceAtLeast(0) * w + x]
            a += (add ushr 24) - (rem ushr 24)
            r += ((add shr 16) and 0xFF) - ((rem shr 16) and 0xFF)
            g += ((add shr 8) and 0xFF) - ((rem shr 8) and 0xFF)
            b += (add and 0xFF) - (rem and 0xFF)
        }
    }
}

private fun centerCropRect(bitmap: Bitmap, targetW: Int, targetH: Int): Rect {
    val srcW = bitmap.width
    val srcH = bitmap.height
    if (srcW <= 0 || srcH <= 0) return Rect(0, 0, srcW, srcH)
    val targetRatio = targetW.toFloat() / targetH
    val srcRatio = srcW.toFloat() / srcH
    return if (srcRatio > targetRatio) {
        val w = (srcH * targetRatio).toInt().coerceIn(1, srcW)
        val left = (srcW - w) / 2
        Rect(left, 0, left + w, srcH)
    } else {
        val h = (srcW / targetRatio).toInt().coerceIn(1, srcH)
        val top = (srcH - h) / 2
        Rect(0, top, srcW, top + h)
    }
}

// 颗粒纹理只生成一次：确定性噪声，避免每次导出都跑一遍随机
private const val GRAIN_TILE = 160
private val grainTile: Bitmap by lazy {
    val rnd = Random(20261126L)
    val pixels = IntArray(GRAIN_TILE * GRAIN_TILE)
    for (i in pixels.indices) {
        val v = 128 + rnd.nextInt(128)
        val a = rnd.nextInt(160)
        pixels[i] = (a shl 24) or (v shl 16) or (v shl 8) or v
    }
    Bitmap.createBitmap(pixels, GRAIN_TILE, GRAIN_TILE, Bitmap.Config.ARGB_8888)
}

internal fun drawGrain(canvas: Canvas, bounds: RectF, alpha: Int = 14) {
    val paint = Paint().apply {
        shader = BitmapShader(grainTile, Shader.TileMode.REPEAT, Shader.TileMode.REPEAT)
        this.alpha = alpha
    }
    canvas.drawRect(bounds, paint)
}

internal fun drawVignette(
    canvas: Canvas,
    bounds: RectF,
    centerYFraction: Float,
    inner: Float,
    outerColor: Int,
) {
    val cx = bounds.centerX()
    val cy = bounds.top + bounds.height() * centerYFraction
    val radius = max(bounds.width(), bounds.height()) * 0.85f
    val paint = Paint().apply {
        shader = RadialGradient(
            cx, cy, radius,
            intArrayOf(0x00000000, outerColor),
            floatArrayOf(inner, 1f),
            Shader.TileMode.CLAMP,
        )
    }
    canvas.drawRect(bounds, paint)
}

/** 与 demo 同一套算法的「像二维码」占位矩阵（21×21 + 三个定位角） */
internal fun drawQrPlaceholder(canvas: Canvas, bounds: RectF, color: Int) {
    val n = 21
    val grid = Array(n) { IntArray(n) }
    var seed = 20261126
    fun rnd(): Int {
        seed = (seed * 1103515245 + 12345) and 0x7fffffff
        return seed
    }
    fun finder(x: Int, y: Int) {
        for (i in 0 until 7) for (j in 0 until 7) {
            val ring = i == 0 || i == 6 || j == 0 || j == 6
            val core = i in 2..4 && j in 2..4
            grid[y + i][x + j] = if (ring || core) 1 else 0
        }
    }
    finder(0, 0)
    finder(n - 7, 0)
    finder(0, n - 7)
    fun inFinder(x: Int, y: Int): Boolean =
        (x < 8 && y < 8) || (x >= n - 8 && y < 8) || (x < 8 && y >= n - 8)
    for (y in 0 until n) for (x in 0 until n) {
        if (!inFinder(x, y)) grid[y][x] = if (rnd() % 100 > 52) 1 else 0
    }
    val cell = bounds.width() / n
    val paint = Paint().apply { this.color = color }
    for (y in 0 until n) for (x in 0 until n) {
        if (grid[y][x] == 1) {
            canvas.drawRect(
                bounds.left + x * cell,
                bounds.top + y * cell,
                bounds.left + (x + 1) * cell,
                bounds.top + (y + 1) * cell,
                paint,
            )
        }
    }
}

/** 爪印：主掌 + 四趾，与 demo 的图标语言一致 */
internal fun drawPaw(canvas: Canvas, bounds: RectF, color: Int) {
    val paint = Paint(Paint.ANTI_ALIAS_FLAG).apply { this.color = color }
    val w = bounds.width()
    val h = bounds.height()
    canvas.drawOval(
        RectF(bounds.left + w * 0.13f, bounds.top + h * 0.45f, bounds.right - w * 0.13f, bounds.bottom),
        paint,
    )
    val toe = w * 0.115f
    canvas.drawCircle(bounds.left + w * 0.18f, bounds.top + h * 0.34f, toe, paint)
    canvas.drawCircle(bounds.left + w * 0.385f, bounds.top + h * 0.19f, toe * 1.05f, paint)
    canvas.drawCircle(bounds.left + w * 0.615f, bounds.top + h * 0.19f, toe * 1.05f, paint)
    canvas.drawCircle(bounds.left + w * 0.82f, bounds.top + h * 0.34f, toe, paint)
}

internal fun layoutOf(
    text: String,
    sizePx: Float,
    weight: Int,
    color: Int,
    widthPx: Int,
    maxLines: Int = Int.MAX_VALUE,
    lineSpacingMult: Float = 1f,
    letterSpacing: Float = 0f,
    align: Layout.Alignment = Layout.Alignment.ALIGN_NORMAL,
    ellipsize: TextUtils.TruncateAt? = null,
    typeface: Typeface? = null,
): StaticLayout {
    val paint = TextPaint(Paint.ANTI_ALIAS_FLAG).apply {
        textSize = sizePx
        this.typeface = typeface
            ?: Typeface.create(Typeface.create("sans-serif", Typeface.NORMAL), weight, false)
        this.color = color
        this.letterSpacing = letterSpacing
        isSubpixelText = true
    }
    val builder = StaticLayout.Builder.obtain(text, 0, text.length, paint, widthPx.coerceAtLeast(1))
        .setAlignment(align)
        .setLineSpacing(0f, lineSpacingMult)
        .setIncludePad(false)
    if (maxLines != Int.MAX_VALUE) builder.setMaxLines(maxLines)
    if (ellipsize != null) builder.setEllipsize(ellipsize)
    return builder.build()
}

internal fun drawCenterCropRound(canvas: Canvas, bitmap: Bitmap, bounds: RectF, radius: Float) {
    if (bounds.isEmpty) return
    val shader = BitmapShader(bitmap, Shader.TileMode.CLAMP, Shader.TileMode.CLAMP)
    val scale = max(bounds.width() / bitmap.width, bounds.height() / bitmap.height)
    val matrix = Matrix().apply {
        setScale(scale, scale)
        postTranslate(
            bounds.centerX() - bitmap.width * scale / 2f,
            bounds.centerY() - bitmap.height * scale / 2f,
        )
    }
    shader.setLocalMatrix(matrix)
    canvas.drawRoundRect(bounds, radius, radius, Paint(Paint.ANTI_ALIAS_FLAG).apply { this.shader = shader })
}

/** 去掉空行与署名行后的可展示歌词 */
internal fun validLyricLines(lyrics: Lyrics?): List<LyricLine> =
    lyrics?.lines.orEmpty().filter { it.text.isNotBlank() && !isLyricsMetadataLine(it.text) }

/** 当前位置落在哪一行；还没到第一行时给 0（视频 / 卡片都要从这里开始） */
internal fun lyricIndexAt(lines: List<LyricLine>, positionMs: Long): Int {
    if (lines.isEmpty()) return -1
    var index = 0
    for (i in lines.indices) {
        if (lines[i].startMs <= positionMs) index = i else break
    }
    return index
}
