package com.catclaw.music.kt.ui.theme

import android.graphics.Bitmap
import android.graphics.BitmapFactory
import androidx.compose.foundation.background
import androidx.compose.runtime.Composable
import androidx.compose.runtime.compositionLocalOf
import androidx.compose.runtime.remember
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.drawBehind
import androidx.compose.ui.geometry.Offset
import androidx.compose.ui.graphics.Brush
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.asImageBitmap
import androidx.compose.ui.unit.IntOffset
import java.io.File
import kotlin.math.roundToInt

/**
 * 自定义背景图。非空时**替换**主题渐变 —— 与 C# `GlobalBackgroundService.CreateBackgroundDrawable`
 * 的优先级一致（custom > 主题 PNG），且同样是 AspectFill 铺满。
 * 注意：MAUI 侧 `custom_bg_opacity` 只存不渲染（没有任何读取点），所以这里也不应用它，保持行为一致。
 */
val LocalCustomBackground = compositionLocalOf<Bitmap?> { null }

/** 三层线性渐变 + 双层径向光晕的主题底，停靠点与光晕参数同 MAUI ThemeService 的两套 BgStop/BgGlow。 */
@Composable
fun Modifier.catClawBackground(): Modifier {
    val custom = LocalCustomBackground.current
    if (custom != null) {
        val bitmap = custom
        return drawBehind {
            // AspectFill：按大边等比放大后居中裁，跟 C# 的 CenterCropToScreen 同结果
            val scale = maxOf(size.width / bitmap.width, size.height / bitmap.height)
            val w = bitmap.width * scale
            val h = bitmap.height * scale
            drawImage(
                image = bitmap.asImageBitmap(),
                dstOffset = IntOffset(((size.width - w) / 2f).roundToInt(), ((size.height - h) / 2f).roundToInt()),
                dstSize = androidx.compose.ui.unit.IntSize(w.toInt(), h.toInt()),
            )
        }
    }
    // drawBehind 的 lambda 不是 Composable 上下文，调色板必须先取出来
    val stops = LocalCatClawPalette.current.backgroundStops
        .map { it.offset to Color(it.argb) }
        .toTypedArray()
    val glows = LocalCatClawPalette.current.glows
        .map { Triple(it.cx, it.cy, it.radius) to Color(it.centerArgb) }

    return drawBehind {
        drawRect(
            brush = Brush.linearGradient(
                *stops,
                start = Offset.Zero,
                end = Offset(size.width, size.height),
            ),
        )

        val scale = size.maxDimension
        glows.forEach { (geom, centerColor) ->
            val (cx, cy, radius) = geom
            val center = Offset(cx * size.width, cy * size.height)
            val r = radius * scale
            drawCircle(
                brush = Brush.radialGradient(
                    colors = listOf(centerColor, Color.Transparent),
                    center = center,
                    radius = r,
                ),
                radius = r,
                center = center,
            )
        }
    }
}

/**
 * **封面把环境染了**——Apple Music 的 Now Playing 不是「页面套一张通用渐变」，而是让当前唱的那张封面
 * 决定页面的色温：取封面平均色压成三段竖向渐变，盖在 [catClawBackground] 之上。
 * 播放页与全屏歌词页共用这一处定义（2026-09-29 随播放页重排一起提上来）。
 *
 * 三个停靠点的 alpha 都不满 1 是有意的：外壳那层底（三层渐变 + 光晕 + 用户自定义背景图）必须继续透得出来。
 * 早先版本底部压到 α.94，等于在播放页把自定义背景图盖掉——正是第 7 条背景闸门要防的那件事。
 */
@Composable
fun Modifier.artworkWash(cover: Bitmap?): Modifier {
    // 取色只算一次：连着调三次 rememberArtworkTint 会开出三个 remember 槽，各自重算一遍采样
    val tint = rememberArtworkTint(cover)
    return background(
        Brush.verticalGradient(
            listOf(
                tint.copy(alpha = 0.42f),
                tint.copy(alpha = 0.18f),
                tint.copy(alpha = 0.05f),
            ),
        ),
    )
}

/**
 * 封面平均色：26×26 网格采样（≤676 个像素，一次算完，不逐帧扫图）。
 * 取不到封面就退回主色——染色不该因为一张图都没解出来就变成透明。
 */
@Composable
fun rememberArtworkTint(cover: Bitmap?): Color {
    val palette = LocalCatClawPalette.current
    val sampled = remember(cover) {
        if (cover == null || cover.width == 0 || cover.height == 0) return@remember null
        var r = 0L
        var g = 0L
        var b = 0L
        var n = 0L
        val stepX = (cover.width / 26).coerceAtLeast(1)
        val stepY = (cover.height / 26).coerceAtLeast(1)
        var y = 0
        while (y < cover.height) {
            var x = 0
            while (x < cover.width) {
                // 必须用 getPixel：`androidx.compose.ui.graphics.getColor` 那个同名扩展会把结果包成 Color，
                // 后面按 Int 位移取通道就编译不过
                val pixel = cover.getPixel(x, y)
                r += (pixel shr 16) and 0xFF
                g += (pixel shr 8) and 0xFF
                b += pixel and 0xFF
                n++
                x += stepX
            }
            y += stepY
        }
        if (n == 0L) null else Color((r / n).toFloat() / 255f, (g / n).toFloat() / 255f, (b / n).toFloat() / 255f)
    }
    return sampled ?: Color(palette.primary)
}

/**
 * 按 MAUI 的做法降采样解码：目标边长 = max(屏宽, 屏高)，避免把原图整张读进内存。
 * 文件不存在或解不出来返回 null，调用方回落到主题渐变。
 */
fun loadCustomBackground(path: String?, maxEdgePx: Int): Bitmap? {
    val file = path?.let(::File) ?: return null
    if (!file.isFile) return null
    return runCatching {
        val bounds = BitmapFactory.Options().apply { inJustDecodeBounds = true }
        BitmapFactory.decodeFile(file.absolutePath, bounds)
        if (bounds.outWidth <= 0 || bounds.outHeight <= 0) return null
        var sample = 1
        while (maxOf(bounds.outWidth, bounds.outHeight) / (sample * 2) >= maxEdgePx) sample *= 2
        BitmapFactory.decodeFile(
            file.absolutePath,
            BitmapFactory.Options().apply { inSampleSize = sample },
        )
    }.getOrNull()
}
