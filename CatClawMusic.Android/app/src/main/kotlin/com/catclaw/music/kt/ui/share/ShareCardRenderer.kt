package com.catclaw.music.kt.ui.share

import android.graphics.Bitmap
import android.graphics.BlendMode
import android.graphics.Canvas
import android.graphics.LinearGradient
import android.graphics.Paint
import android.graphics.RectF
import android.graphics.Shader
import android.graphics.Typeface
import android.text.Layout
import com.catclaw.music.kt.core.model.Song

/**
 * 「分享高光歌词」的图片产物：1080 × 1240 JPEG 的画面部分。
 *
 * 暖棕渐变底 + 封面晕染 + 颗粒/暗角 + 封面头 + 米白大字号歌词 + 底部居中字标。
 */
object ShareCardRenderer {

    private const val PAD = 71.3f            // 6.6cqw
    private const val COVER = 126f
    private const val COVER_RADIUS = 18f
    private const val TITLE_SIZE = 46f
    private const val ARTIST_SIZE = 35f
    private const val LYRIC_MAX = 86f
    private const val LYRIC_MIN = 64f
    private const val LYRIC_GAP = 36.7f      // 3.4cqw
    private const val BRAND_SIZE = 46f

    fun render(
        song: Song,
        lines: List<String>,
        cover: Bitmap?,
        brandTypeface: Typeface? = null,
    ): Bitmap {
        val bitmap = Bitmap.createBitmap(CARD_W, CARD_H, Bitmap.Config.ARGB_8888)
        val canvas = Canvas(bitmap)
        val w = CARD_W.toFloat()
        val h = CARD_H.toFloat()
        val bounds = RectF(0f, 0f, w, h)

        // 1. 暖棕斜向渐变底
        val base = Paint().apply {
            shader = LinearGradient(
                0f, 0f, w * 0.65f, h,
                intArrayOf(BROWN_TOP, BROWN_MID, BROWN_BOTTOM),
                floatArrayOf(0f, 0.48f, 1f),
                Shader.TileMode.CLAMP,
            )
        }
        canvas.drawRect(bounds, base)

        // 2. 顶部一层封面模糊晕染（demo 的 .wash：soft-light + 30%，不要提亮/加饱和，
        //    否则叠在暖棕底上会整体发黄）
        if (cover != null) {
            val washW = (w * 1.2f).toInt()
            val washH = (h * 0.70f).toInt()
            val wash = centerCropBlur(cover, washW, washH, downscale = 10)
            canvas.drawBitmap(wash, -w * 0.10f, -h * 0.18f, Paint().apply {
                alpha = 77
                blendMode = BlendMode.SOFT_LIGHT
            })
            wash.recycle()
        }

        // 3. 颗粒 + 暗角
        drawGrain(canvas, bounds, alpha = 14)
        drawVignette(canvas, bounds, centerYFraction = 0.30f, inner = 0.40f, outerColor = 0x73481A0A)

        // 4. 头部：封面 + 曲名/歌手
        val coverRect = RectF(PAD, PAD, PAD + COVER, PAD + COVER)
        if (cover != null) {
            drawCenterCropRound(canvas, cover, coverRect, COVER_RADIUS)
        } else {
            drawCoverPlaceholder(canvas, coverRect, song.title)
        }

        val textX = coverRect.right + 28f
        val textWidth = (w - PAD - textX).toInt().coerceAtLeast(1)
        val title = layoutOf(
            song.title, TITLE_SIZE, 650, CREAM, textWidth,
            maxLines = 1, lineSpacingMult = 1.25f, letterSpacing = 0.015f,
            ellipsize = android.text.TextUtils.TruncateAt.END,
        )
        val artist = layoutOf(
            if (song.artist.isBlank()) "" else song.artist,
            ARTIST_SIZE, 400, CREAM_DIM, textWidth,
            maxLines = 1, lineSpacingMult = 1.25f, letterSpacing = 0.03f,
            ellipsize = android.text.TextUtils.TruncateAt.END,
        )
        val blockHeight = title.height + 8f + artist.height
        val blockTop = PAD + (COVER - blockHeight) / 2f
        canvas.save()
        canvas.translate(textX, blockTop)
        title.draw(canvas)
        canvas.translate(0f, title.height + 8f)
        artist.draw(canvas)
        canvas.restore()

        // 5. 高光歌词：去掉分隔线，给歌词更大的呼吸空间，整块在高光区垂直居中
        val lyricsTop = coverRect.bottom + 96f
        val footerTop = h - PAD - BRAND_SIZE * 1.6f
        val available = footerTop - lyricsTop - 24f
        var size = LYRIC_MAX
        var layouts = buildLyricLayouts(lines, size)
        while (size > LYRIC_MIN && totalHeight(layouts) > available) {
            size -= 3f
            layouts = buildLyricLayouts(lines, size)
        }
        val lyricsHeight = (totalHeight(layouts) - LYRIC_GAP).coerceAtLeast(0f)
        val startY = lyricsTop + ((footerTop - lyricsTop) - lyricsHeight) / 2f
        canvas.save()
        canvas.translate(PAD, startY)
        var y = 0f
        layouts.forEach { layout ->
            canvas.save()
            canvas.translate(0f, y)
            layout.draw(canvas)
            canvas.restore()
            y += layout.height + LYRIC_GAP
        }
        canvas.restore()

        // 6. 底部：只保留居中的「猫爪音乐」字标，不要图标和二维码
        val brand = layoutOf(
            "猫爪音乐", BRAND_SIZE, 550, withAlpha(CREAM, 158),
            (w - PAD * 2).toInt().coerceAtLeast(1),
            maxLines = 1, letterSpacing = 0.03f,
            align = Layout.Alignment.ALIGN_CENTER,
            typeface = brandTypeface,
        )
        canvas.save()
        canvas.translate(PAD, h - PAD - brand.height)
        brand.draw(canvas)
        canvas.restore()

        return bitmap
    }

    private fun buildLyricLayouts(lines: List<String>, size: Float) =
        lines.map {
            layoutOf(
                it, size, 800, CREAM, (CARD_W - PAD * 2).toInt(),
                lineSpacingMult = 1.44f, letterSpacing = 0.012f,
            )
        }

    private fun totalHeight(layouts: List<android.text.StaticLayout>): Float {
        if (layouts.isEmpty()) return 0f
        var height = 0f
        layouts.forEach { height += it.height + LYRIC_GAP }
        return height - LYRIC_GAP
    }

    private fun drawCoverPlaceholder(canvas: Canvas, rect: RectF, title: String) {
        canvas.drawRoundRect(rect, COVER_RADIUS, COVER_RADIUS, Paint(Paint.ANTI_ALIAS_FLAG).apply {
            color = withAlpha(CREAM, 40)
        })
        val initial = title.trim().take(1).ifEmpty { "♪" }
        val layout = layoutOf(
            initial, 48f, 700, CREAM, rect.width().toInt(),
            maxLines = 1, align = android.text.Layout.Alignment.ALIGN_CENTER,
        )
        canvas.save()
        canvas.translate(rect.left, rect.centerY() - layout.height / 2f)
        layout.draw(canvas)
        canvas.restore()
    }
}
