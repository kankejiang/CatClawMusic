package com.catclaw.music.kt.ui.theme

import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.runtime.Composable
import androidx.compose.runtime.remember
import androidx.compose.runtime.Stable
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.Color
import com.catclaw.music.kt.core.theme.CatClawPalette
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
    /**
     * 2026-10-21 照抄 Halcyon（`GlassEffect.kt:17 GlassEffect` 的默认档）：
     * blur **6dp**、折射 24dp、高光 α **0.75**、外加 40dp 的采样 padding。
     * 我们原来是 20dp —— 重了 3 倍多，观感是「磨砂玻璃」而不是「液态玻璃」。
     */
    val BlurRadius: Dp = 12.dp
    val CornerRadius: Dp = 18.dp
    /** α≈.30：着色只用来压一档亮度，糊与增艳交给 blur+vibrancy —— α .55 实测会把底栏压成黑条 */
    val TintArgb: Long = 0x4D1C1C25L
    val Tint: Color = Color(TintArgb)
    const val HighlightAlpha: Float = 0.92f

    /** 折射的采样余量：效果要在面外多取像素，边缘才折得动。折射调大必须跟着调大 */
    val RefractionPadding: Dp = 64.dp

    /**
     * 折射强度：Halcyon 默认 24dp，用户 2026-10-21 反馈「液态力度不够」，提到 46dp。
     * 再往上（>52dp）边缘会撕裂成两层。
     */
    val RefractionSize: Dp = 46.dp

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

/**
 * 宿主那层与**它属于哪个窗口**。
 *
 * 记 window 的用处：Dialog / Popup 是独立窗口，它们采不到主窗口录的那层，
 * 硬采会拿到空区域（表现是「有面、没有糊」，深色底上甚至发黑）。
 * 有了这个字段，材质自己能判断「我是不是还在宿主那个窗口里」，不必让每处弹窗各自声明。
 */
@Stable
class BackdropState internal constructor(
    internal val backdrop: LayerBackdrop,
    internal val hostView: android.view.View? = null,
)

/**
 * 当前组合树里那层可采样的宿主（由外壳 `CatClawRoot` 提供）。
 *
 * 用 CompositionLocal 而不是层层传参：全 app 的卡片/面板都要用它，
 * 而调用方（`glassCard` 的使用者）根本不该知道「玻璃从哪儿采样」这件事。
 *
 * 弹窗（Dialog / Popup）拿到的仍是这个值，但它们**是别的窗口**，采样会拿到空区域。
 * 这一点由 [BackdropState.hostView] 自动判定：不在宿主窗口里就退回实心玻璃，
 * 于是各处弹窗不必自己声明（逐个声明迟早会漏）。
 */
val LocalBackdropState = androidx.compose.runtime.compositionLocalOf<BackdropState?> { null }

@Composable
fun rememberBackdropState(): BackdropState {
    val backdrop = rememberLayerBackdrop()
    // 宿主 View：用来判断「当前这块玻璃是不是还在宿主那个窗口里」（弹窗是别的窗口）
    val view = androidx.compose.ui.platform.LocalView.current
    return remember(backdrop, view) { BackdropState(backdrop, view) }
}

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
                // padding → **blur** → vibrancy → lens。
                // blur 必须排在所有 shader 之前：它作用于采到的图层本身，排在 vibrancy()/lens() 之后
                // 就没有图层可糊了 —— 真机表现就是「透明、有折射、但没有模糊」（2026-10-21 用户两次反馈）。
                padding = GlassTokens.RefractionPadding.toPx()
                blur(blurPx)
                // 折射：让背后的内容在边缘处弯折，这是「液态」观感的来源；AGSL 需 API 33+（真机 34）
                // 后两个布尔是 kyant 2.0.1 默认关着的开关（用 javap 反查出来的），开了边缘才有「折」
                lens(GlassTokens.RefractionSize.toPx(), GlassTokens.RefractionSize.toPx(), true, true)
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
 * **液玻材质**：卡片 / 面板 / 菜单 / 弹层的统一入口。
 *
 * 在有宿主（[LocalBackdropState] 非空）时走真玻璃：`drawBackdrop` 采样背后内容 → 模糊 → 增艳 →
 * **边缘折射（lens）** → 高光边 —— 「液态」的观感就来自折射与高光这两项，
 * 只加模糊的话是磨砂玻璃，不是液态玻璃。
 *
 * 没有宿主时（弹窗、或宿主未就绪）退回「着色 + 描边」的实心玻璃：
 * 观感略平，但对比度与层级都对，不会出现「有面、没有糊」的空采样。
 *
 * 性能边界（写在参数旁边，免得下次有人把它套到列表行上）：
 * 每块玻璃都要采一次宿主那层并跑一遍 AGSL。整页十来块卡片没问题；
 * 但**列表行/小 chip 不要用**（一屏 30 行就是 30 次采样），它们本来也不需要「透出背后」——
 * 那层内容就是页面底色，糊与不糊看不出区别，代价却是实打实的。
 */
@Composable
fun Modifier.liquidGlass(
    palette: CatClawPalette = LocalCatClawPalette.current,
    corner: Int = 26,
    selected: Boolean = false,
): Modifier {
    // 只在「宿主那个窗口」里采样：弹窗/弹出菜单是别的窗口，采到的是空区域，
    // 所以它们自动退回实心玻璃（hostView 为 null 时不做限制，方便预览与测试）
    val state = LocalBackdropState.current?.takeIf {
        it.hostView == null || it.hostView === androidx.compose.ui.platform.LocalView.current
    }
    val shape = RoundedCornerShape(corner.dp)
    val ink = Color(palette.textPrimary)
    // 选中项：主色 16% 底 + 45% 描边；普通项：墨色 10% 底 + 16% 描边（沿用原 glassCard 的两档）
    val fill = if (selected) Color(palette.primary).copy(alpha = 0.16f) else ink.copy(alpha = 0.10f)
    val line = if (selected) Color(palette.primary).copy(alpha = 0.45f) else ink.copy(alpha = 0.16f)

    if (state == null) {
        return this
            .clip(shape)
            .background(fill)
            .border(1.dp, line, shape)
    }

    val blurPx = with(LocalDensity.current) { GlassTokens.BlurRadius.toPx() }
    val lensPx = with(LocalDensity.current) { 28.dp.toPx() }
    val tint = if (isLightPalette(palette)) GlassTokens.TintLight else GlassTokens.Tint
    return this
        .clip(shape)
        .drawBackdrop(
            backdrop = state.backdrop,
            shape = { shape },
            effects = {
                // 同上：padding → blur → lens（vibrancy 是 shader，会盖掉 blur，已去掉）
                padding = GlassTokens.RefractionPadding.toPx()
                blur(blurPx)
                lens(GlassTokens.RefractionSize.toPx(), GlassTokens.RefractionSize.toPx(), true, true)
            },
            highlight = { Highlight(alpha = GlassTokens.HighlightAlpha) },
        )
        .background(tint)
        // 选中态再加一层主色：着色层已经压过一档，这里只叠一点点色相
        .then(if (selected) Modifier.background(fill) else Modifier)
        .border(1.dp, line, shape)
}

/**
 * 悬浮栏专用表面 —— MiniPlayer 与 TabBar 都走这一个，尺寸/圆角/描边/模糊全部锁死在同一组 token 上，
 * 免得两边各自传参又漂开（2026-09-28 用户要求两栏效果完全一致）。
 */
@Composable
fun BarGlass(
    state: BackdropState,
    modifier: Modifier = Modifier,
    /**
     * 动画进行中传 false：这一帧**不录背景、也不采样**，直接画实心档。
     *
     * 为什么要有这一档（2026-10-18 实测）：120Hz 每帧只有 8.3ms，而「录一层背景 + 模糊采样」
     * 本身就要 8–12ms（gfxinfo：GPU p50 10ms、p50 帧时 11ms，只有 30fps）。
     * 也就是说**真玻璃与 120fps 在这块 GPU 上只能二选一**。取巧的地方在于：
     * 甩动/滚动时人眼根本看不清模糊，停下来那一瞬间才需要它 —— 于是动时切实心、停时恢复玻璃。
     */
    glassOn: Boolean = true,
    content: @Composable () -> Unit,
) {
    val palette = LocalCatClawPalette.current
    val light = isLightPalette(palette)
    val stroke = if (light) GlassTokens.BarStrokeLight else GlassTokens.BarStroke
    if (glassOn) {
        BackdropGlass(
            state = state,
            modifier = modifier.height(GlassTokens.BarHeight),
            cornerRadius = GlassTokens.BarCornerRadius,
            stroke = stroke,
            content = content,
        )
        return
    }
    // 实心档：底色 = 页面底色（不透明）压一档 + 原来的着色与描边，观感与玻璃档同族、只是不糊背后
    val shape = RoundedCornerShape(GlassTokens.BarCornerRadius)
    val base = Color(palette.backgroundStops.last().argb)
    Box(
        modifier = modifier
            .height(GlassTokens.BarHeight)
            .clip(shape)
            .background(base.copy(alpha = 0.88f))
            .background(if (light) GlassTokens.TintLight else GlassTokens.Tint)
            .border(GlassTokens.BarStrokeWidth, stroke, shape),
    ) { content() }
}
