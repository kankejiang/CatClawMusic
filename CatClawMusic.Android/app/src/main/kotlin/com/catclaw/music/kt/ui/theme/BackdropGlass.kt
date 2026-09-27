package com.catclaw.music.kt.ui.theme

import androidx.compose.foundation.background
import androidx.compose.foundation.layout.Box
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
    val Tint: Color = Color(0x4D1C1C25)
    const val HighlightAlpha: Float = 0.7f
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
    content: @Composable () -> Unit,
) {
    val blurPx = with(LocalDensity.current) { GlassTokens.BlurRadius.toPx() }
    Box(
        modifier = modifier.drawBackdrop(
            backdrop = state.backdrop,
            shape = { RoundedCornerShape(cornerRadius) },
            effects = {
                vibrancy()
                blur(blurPx)
                // 折射：让背后的内容在边缘处弯折，这是「液态」观感的来源；AGSL 需 API 33+（真机 34）
                lens(28f.dp.toPx(), 28f.dp.toPx())
            },
            highlight = { Highlight(alpha = GlassTokens.HighlightAlpha) },
        ),
    ) {
        Spacer(modifier = Modifier.matchParentSize().background(GlassTokens.Tint))
        content()
    }
}
