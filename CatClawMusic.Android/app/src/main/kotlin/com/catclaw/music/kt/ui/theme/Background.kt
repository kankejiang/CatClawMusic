package com.catclaw.music.kt.ui.theme

import android.graphics.Bitmap
import android.graphics.BitmapFactory
import androidx.compose.runtime.Composable
import androidx.compose.runtime.compositionLocalOf
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
