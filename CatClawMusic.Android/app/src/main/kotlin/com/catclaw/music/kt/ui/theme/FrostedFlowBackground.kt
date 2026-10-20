package com.catclaw.music.kt.ui.theme

import android.graphics.Bitmap
import androidx.compose.foundation.Image
import androidx.compose.foundation.background
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.BoxWithConstraints
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableIntStateOf
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.runtime.withFrameNanos
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.Brush
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.ImageBitmap
import androidx.compose.ui.graphics.asImageBitmap
import androidx.compose.ui.graphics.graphicsLayer
import androidx.compose.ui.layout.ContentScale
import androidx.compose.ui.platform.LocalDensity
import com.catclaw.music.kt.core.theme.FROSTED_FLOW_BASE_ARGB
import com.catclaw.music.kt.core.theme.FrostedFlowAnimator
import com.catclaw.music.kt.core.theme.FrostedFlowPreset
import com.catclaw.music.kt.core.theme.CoverFlowProcessor
import com.catclaw.music.kt.core.theme.FrostedFlowProcessor
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.delay
import kotlinx.coroutines.withContext
import kotlin.math.roundToInt

/**
 * 流光喷发动态背景（Halcyon / Apple Music 风格）—— MAUI `FrostedBackground` 的 Compose 移植
 * （2026-10-07，渲染架构对齐 Android 端 `FrostedBackgroundHandler`）：
 *
 * - 内核 = `FrostedFlowProcessor`（4 光点圆周漂移 + Perlin 噪声扰动 + 反色降饱和 + 提亮 + 颗粒），
 *   在 **视口 1/16（高 dpi 1/24）的低分辨率缓冲**上由后台线程逐帧渲染，主线程只把帧位图放大铺满——
 *   与 MAUI 同一套「低分辨率 CPU 渲染 + 放大」的省电做法；
 * - 节拍 ~8fps（MAUI ValueAnimator 125ms），`isActive=false`（暂停）或 `isScrolling=true`（列表拖动）
 *   时停拍冻结最后一帧（MAUI ShouldAnimate 同款 gate）；
 * - 有封面均色（tint）时：封面色竖向渐变作不透明底（明度阶梯 .60/.34/.14），
 *   流光以 α0.55 盖在其上；无 tint 时流光直接不透明铺底（MAUI OnDraw hasTint 分支）；
 * - 最上叠黑色暗化层（dimAmount，本页 0.12）。
 *
 * MAUI 还有「封面流」模式（CoverFlowProcessor：过饱和封面三层旋转平铺+盒式模糊），本端未移植，
 * 以封面均色染底代替（见交接遗留）。
 */
@Composable
fun FrostedFlowBackground(
    isActive: Boolean,
    isScrolling: Boolean,
    tint: Color,
    modifier: Modifier = Modifier,
    isDark: Boolean = true,
    tintOpacity: Float = 0.60f,
    dimAmount: Float = 0.12f,
    /**
     * 封面流源（当前封面，≤256 长边的 ARGB）。非空时切**封面流模式** —— MAUI
     * `FrostedBackgroundHandler.CoverFlowMode => !_coverSrc.IsEmpty`（同文件 :378/:441）。
     *
     * 两种模式的观感差别就是要补的缺口：流光模式是「取封面均色染底 + 四点流光」，
     * 封面流模式**封面本身就是背景**（过饱和 + 旋转平铺 + 盒式模糊），所以鲜亮、贴合封面。
     */
    coverSource: CoverFlowProcessor.CoverSource? = null,
    /** 封面强调色（0xRRGGBB）：封面流的统一底色 middle 由它推出（MAUI TintColor 同源） */
    accentArgb: Int = 0,
) {
    val coverFlow = coverSource?.takeIf { !it.isEmpty }
    val preset = remember(isDark) { FrostedFlowPreset.choose(isDark) }
    val animator = remember(isDark) { FrostedFlowAnimator(preset.colorInterpPeriod) }
    var frame by remember { mutableStateOf<ImageBitmap?>(null) }
    var bufferW by remember { mutableIntStateOf(0) }
    var bufferH by remember { mutableIntStateOf(0) }

    BoxWithConstraints(modifier.fillMaxSize()) {
        val density = LocalDensity.current
        // 低分辨率缓冲：视口/16（高 dpi≥420 时 /24），尺寸变化才重算（MAUI EnsureFlowBuffer）；
        // Density 只带 density 档位，dpi = density×160（Android mdpi 基准）
        LaunchedEffect(maxWidth, maxHeight, density) {
            val dpi = (density.density * 160f).roundToInt()
            val (w, h) = FrostedFlowProcessor.suggestBufferSize(
                with(density) { maxWidth.toPx() }.roundToInt(),
                with(density) { maxHeight.toPx() }.roundToInt(),
                dpi,
            )
            bufferW = w
            bufferH = h
        }

        // 暂停/拖动时停拍但保留最后一帧（MAUI：非播放不动画但保留最后一帧）
        val shouldAnimate = isActive && !isScrolling
        LaunchedEffect(bufferW, bufferH, preset, shouldAnimate, coverFlow, accentArgb) {
            if (bufferW <= 0 || bufferH <= 0 || !shouldAnimate) return@LaunchedEffect
            val colors = FloatArray(16)
            var lastNanos = withFrameNanos { it }
            while (true) {
                val now = withFrameNanos { it }
                val dt = ((now - lastNanos) / 1e9f).coerceIn(0f, 0.25f)
                lastNanos = now
                animator.advance(dt, playing = true)
                val bitmap = withContext(Dispatchers.Default) {
                    // 快照当前阶段/时钟，纯计算放后台（MAUI TryDispatchBackgroundRender 的线程池拆分）
                    val stage = animator.colorStage
                    val time = animator.animTime
                    val argb = IntArray(bufferW * bufferH)
                    if (coverFlow != null) {
                        // 封面流：同一张低分辨率画布上做「过饱和封面三层旋转平铺 + 两趟盒式模糊」
                        val f = CoverFlowProcessor.render(
                            src = coverFlow,
                            outW = bufferW,
                            outH = bufferH,
                            timeMs = (animator.animTime * 1000f).toLong(),
                            blur = 42f,
                            accentArgb = accentArgb,
                            isDark = isDark,
                        )
                        if (f.pixels.isNotEmpty()) f.pixels.copyInto(argb) else argb.fill(0xFF000000.toInt())
                    } else {
                        FrostedFlowProcessor.interpolateColors(preset, stage, colors)
                        FrostedFlowProcessor.render(argb, bufferW, bufferH, preset, colors, time, 0f, 0f, 1f, 1f, FROSTED_FLOW_BASE_ARGB)
                    }
                    Bitmap.createBitmap(bufferW, bufferH, Bitmap.Config.ARGB_8888).also {
                        it.setPixels(argb, 0, bufferW, 0, 0, bufferW, bufferH)
                    }
                }
                frame = bitmap.asImageBitmap()
                delay(125) // ~8fps，雾面背景低帧率省电（MAUI SetDuration(125)）
            }
        }

        if (coverFlow != null) {
            CoverFlowLayers(frame = frame, isDark = isDark)
        } else {
            FlowLayers(frame, hasTint = tintOpacity > 0f && tint.alpha > 0f, tint = tint, dimAmount = dimAmount)
        }
    }
}

/**
 * 封面流模式的分层 —— MAUI `OnDraw` 的 CoverFlowMode 分支（FrostedBackgroundHandler.cs:441-461）：
 * **帧本身即背景**（不叠色调渐变、不做全屏暗化），最上面只盖一层「上轻 / 中透明 / 下轻」的镜面纱，
 * 保证歌词文字可读、又不遮挡中间的封面流。
 *
 * 首帧未就绪时用 middle 附近的深底铺一层（同文件 :451-459 的做法），避免露黑场。
 */
@Composable
private fun CoverFlowLayers(frame: ImageBitmap?, isDark: Boolean) {
    Box(modifier = Modifier.fillMaxSize().background(if (isDark) Color(0xFF0B0D20) else Color(0xFFEDEFF5))) {
        if (frame != null) {
            Image(
                bitmap = frame,
                contentDescription = null,
                contentScale = ContentScale.FillBounds,
                modifier = Modifier.fillMaxSize(),
            )
        }
        // Halcyon 原版 alpha（不随 DimAmount 缩放）：深色黑纱 0.18/0.30、浅色白纱 0.14/0.22
        val scrim = if (isDark) Color.Black else Color.White
        Spacer(
            Modifier
                .fillMaxSize()
                .background(
                    Brush.verticalGradient(
                        colorStops = arrayOf(
                            0f to scrim.copy(alpha = if (isDark) 0.18f else 0.14f),
                            0.5f to Color.Transparent,
                            1f to scrim.copy(alpha = if (isDark) 0.30f else 0.22f),
                        ),
                    ),
                ),
        )
    }
}

/** 三层叠加：封面色渐变底 → 流光帧（有 tint 时 α0.55 半透明保留动效）→ 黑色暗化（MAUI OnDraw） */
@Composable
private fun FlowLayers(frame: ImageBitmap?, hasTint: Boolean, tint: Color, dimAmount: Float) {
    Box(modifier = Modifier.fillMaxSize()) {
        if (hasTint) {
            Spacer(
                Modifier
                    .fillMaxSize()
                    // 封面渐变作为不透明底色（Halcyon：top/mid/bottom 由明度阶梯求得）——
                    // 这才是「背景看起来像封面」的关键
                    .background(
                        Brush.verticalGradient(
                            colorStops = arrayOf(
                                0f to scaled(tint, 0.60f),
                                0.5f to scaled(tint, 0.34f),
                                1f to scaled(tint, 0.14f),
                            ),
                        ),
                    ),
            )
        }
        if (frame != null) {
            // 低分辨率帧放大铺满（MAUI DrawBitmap 双线性 FilterBitmap 同效）
            Image(
                bitmap = frame,
                contentDescription = null,
                contentScale = ContentScale.FillBounds,
                modifier = Modifier
                    .fillMaxSize()
                    .graphicsLayer { alpha = if (hasTint) 0.55f else 1f },
            )
        }
        if (dimAmount > 0f) {
            Spacer(Modifier.fillMaxSize().background(Color.Black.copy(alpha = dimAmount)))
        }
    }
}

/** MAUI Argb(c, factor)：按明度系数缩放为不透明颜色（Halcyon darken 阶梯用） */
private fun scaled(c: Color, factor: Float): Color =
    Color(c.red * factor, c.green * factor, c.blue * factor, 1f)
