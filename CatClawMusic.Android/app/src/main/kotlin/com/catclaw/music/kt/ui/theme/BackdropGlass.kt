package com.catclaw.music.kt.ui.theme

import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.runtime.Composable
import androidx.compose.runtime.Stable
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.platform.LocalDensity
import androidx.compose.ui.unit.Dp
import androidx.compose.ui.unit.dp
import com.kyant.backdrop.backdrops.LayerBackdrop
import com.kyant.backdrop.backdrops.layerBackdrop
import com.kyant.backdrop.backdrops.rememberLayerBackdrop
import com.kyant.backdrop.drawBackdrop
import com.kyant.backdrop.effects.blur
import com.kyant.backdrop.effects.lens
import com.kyant.backdrop.effects.vibrancy
import com.kyant.backdrop.highlight.Highlight

/**
 * 定档值（HTML 原型「推荐」档，2026-09-27 用户拍板）：
 * 模糊 20dp、增艳 vibrancy、着色 `#6B1C1C25`（α≈.42 的冷墨蓝，来自 MAUI `BlurConsumerView.TintColor=#8C1C1C25`）、
 * 边缘高光 α .7、圆角 18dp。
 *
 * 玻璃本体不再自研：宿主 `layerBackdrop` 录制内容，消费者 `drawBackdrop` 采样并挂效果 ——
 * 帧同步/双缓冲是 `io.github.kyant0:backdrop` 负责的，我这边只留一组可调参数。
 */
object GlassTokens {
    val BlurRadius: Dp = 20.dp
    val CornerRadius: Dp = 18.dp
    /** α≈.30：着色只用来压一档亮度，糊与增艳交给 blur+vibrancy —— α .55 实测会把底栏压成黑条 */
    val TintArgb: Long = 0x4D1C1C25L
    val Tint: Color = Color(TintArgb)
    const val HighlightAlpha: Float = 0.7f

    /**
     * 悬浮栏（MiniPlayer 与 TabBar）共用同一档玻璃：着色走 [Tint]、圆角走 [BarCornerRadius]。
     * MAUI 原值是 `BlurConsumerView.TintColor Dark=#8C1C1C25`（α .55），2026-09-28 用户要求两栏观感一致，
     * 所以播放条不再单独压深 —— 保留 token 位置，想回到 MAUI 那档只改这一处。
     */
    val BarCornerRadius: Dp = 16.dp

    /** 两栏共用同一档尺寸：迷你播放条不再比底栏高（2026-09-28 用户要求大小完全一致） */
    val BarHeight: Dp = 64.dp

    /** MAUI `BottomBarStrokeColor` + `Border StrokeThickness="1.5"`：描边是「有面的胶囊」和「透明」的分界 */
    val BarStroke: Color = Color(0x40FFFFFF)
    val BarStrokeWidth: Dp = 1.5.dp

    /**
     * 浅色主题的着色层：压白雾而不是墨蓝。
     * 依据是 MAUI `BlurConsumerView` 自己的注释（"For a lighter glass on light themes, try #B2FFFFFF"）；
     * 实测墨蓝 α.30 落在浅底上，次要文字只剩 2.35:1，白雾下能回到 4.5 以上（PaletteContrastTest）。
     */
    val TintLightArgb: Long = 0xB2FFFFFFL
    val TintLight: Color = Color(TintLightArgb)

    /** 白雾上要换深色描边，否则 α.25 的白边等于没有 */
    val BarStrokeLight: Color = Color(0x330B0E14L)
}

@Stable
class BackdropState internal constructor(internal val backdrop: LayerBackdrop)

@Composable
fun rememberBackdropState(): BackdropState = BackdropState(rememberLayerBackdrop())

/**
 * 宿主：把要被透出的内容录进 LayerBackdrop（必须是消费者的兄弟，不能是祖先）。
 * 注意要 `this.then(...)` —— 直接返回 layerBackdrop 会把调用方的 modifier 链（fillMaxSize 等）整条丢掉，
 * 那样宿主录到的 layer 尺寸不对，消费者采样到空区域，表现就是「玻璃有面、没有糊」。
 */
fun Modifier.backdropSource(state: BackdropState): Modifier = this.then(layerBackdrop(state.backdrop))

/** 消费者：采样宿主的 layer，先模糊再增艳，上面压一层着色，最后画自己的内容 */
@Composable
fun BackdropGlass(
    state: BackdropState,
    modifier: Modifier = Modifier,
    cornerRadius: Dp = GlassTokens.CornerRadius,
    tint: Color? = null,
    stroke: Color? = null,
    strokeWidth: Dp = GlassTokens.BarStrokeWidth,
    content: @Composable () -> Unit,
) {
    val blurPx = with(LocalDensity.current) { GlassTokens.BlurRadius.toPx() }
    val shape = RoundedCornerShape(cornerRadius)
    // 默认着色跟着页面底色走：浅底压白雾、深底压墨蓝（GlassTokens 里两套值）
    val effectiveTint = tint ?: if (isLightPalette(LocalCatClawPalette.current)) GlassTokens.TintLight else GlassTokens.Tint
    Box(
        modifier = modifier.drawBackdrop(
            backdrop = state.backdrop,
            shape = { shape },
            effects = {
                vibrancy()
                blur(blurPx)
                // 折射：让背后的内容在边缘处弯折，这是「液态」观感的来源；AGSL 需 API 33+（真机 34）
                lens(28f.dp.toPx(), 28f.dp.toPx())
            },
            highlight = { Highlight(alpha = GlassTokens.HighlightAlpha) },
        ),
    ) {
        Spacer(modifier = Modifier.matchParentSize().background(effectiveTint))
        // 描边画在内容**之下**：它是面的一圈边，内容（如迷你播放条底部那条进度线）要能盖住它
        if (stroke != null) {
            Spacer(
                modifier = Modifier
                    .matchParentSize()
                    .border(strokeWidth, stroke, shape),
            )
        }
        content()
    }
}

/**
 * 悬浮栏专用表面 —— MiniPlayer 与 TabBar 都走这一个，尺寸/圆角/描边/模糊全部锁死在同一组 token 上，
 * 免得两边各自传参又漂开（2026-09-28 用户要求两栏效果完全一致）。
 */
@Composable
fun BarGlass(
    state: BackdropState,
    modifier: Modifier = Modifier,
    content: @Composable () -> Unit,
) {
    BackdropGlass(
        state = state,
        modifier = modifier.height(GlassTokens.BarHeight),
        cornerRadius = GlassTokens.BarCornerRadius,
        stroke = if (isLightPalette(LocalCatClawPalette.current)) GlassTokens.BarStrokeLight else GlassTokens.BarStroke,
        content = content,
    )
}
