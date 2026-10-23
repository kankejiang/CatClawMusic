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
import androidx.compose.ui.draw.drawBehind
import androidx.compose.ui.draw.drawWithContent
import androidx.compose.ui.geometry.Offset
import androidx.compose.ui.graphics.BlurEffect
import androidx.compose.ui.graphics.TileMode
import androidx.compose.ui.graphics.drawscope.scale
import androidx.compose.ui.graphics.drawscope.translate
import androidx.compose.ui.graphics.layer.GraphicsLayer
import androidx.compose.ui.graphics.rememberGraphicsLayer
import androidx.compose.ui.graphics.layer.drawLayer
import androidx.compose.ui.layout.onGloballyPositioned
import top.yukonga.miuix.kmp.blur.LayerBackdrop as MiuixLayerBackdrop
import top.yukonga.miuix.kmp.blur.blur as miuixBlur
import top.yukonga.miuix.kmp.blur.drawBackdrop as miuixDrawBackdrop
import top.yukonga.miuix.kmp.blur.highlight.Highlight as MiuixHighlight
import top.yukonga.miuix.kmp.blur.layerBackdrop as miuixLayerBackdrop
import top.yukonga.miuix.kmp.blur.rememberLayerBackdrop as rememberMiuixLayerBackdrop
import com.catclaw.music.kt.ui.theme.liquid.lens as halcyonLens
import com.catclaw.music.kt.ui.theme.liquid.vibrancy as halcyonVibrancy
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.Color
import com.catclaw.music.kt.core.theme.CatClawPalette
import androidx.compose.ui.platform.LocalDensity
import androidx.compose.ui.unit.Dp
import androidx.compose.ui.unit.dp
import com.kyant.backdrop.backdrops.LayerBackdrop
import androidx.compose.foundation.layout.Box as ComposeBox
import dev.chrisbanes.haze.HazeState
import dev.chrisbanes.haze.HazeStyle
import dev.chrisbanes.haze.HazeTint
import dev.chrisbanes.haze.hazeEffect
import dev.chrisbanes.haze.hazeSource
import dev.chrisbanes.haze.rememberHazeState
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
 *
 * 关于模糊：这块 GPU 的模糊半径上限约 25px（实测 20dp 与 80dp 观感完全一致），
 * 所以内容层按 [DOWNSCALE] 降采样后再糊，放大回屏幕时等效半径 ×6。
 */

/**
 * 内容层录制时的缩小倍数。
 *
 * 这块 GPU 对上屏的模糊半径有上限（实测 20dp 与 80dp 观感完全一致，约 25px），
 * 直接加大半径没有意义。把内容缩到 1/6 录制、在这张小图上用低于上限的半径糊，
 * 再放大 6 倍画出来 —— 屏幕上的**等效半径约 138px**，字才会真的化开。
 */
private const val DOWNSCALE = 6f

object GlassTokens {
    /**
     * 2026-10-21 照抄 Halcyon（`GlassEffect.kt:17 GlassEffect` 的默认档）：
     * blur **6dp**、折射 24dp、高光 α **0.75**、外加 40dp 的采样 padding。
     * 我们原来是 20dp —— 重了 3 倍多，观感是「磨砂玻璃」而不是「液态玻璃」。
     */
    /**
     * 悬浮两栏底下那层高斯模糊的半径（dp）。
     * 2026-10-22 真机量化：12dp 时栏内边绿强度从背景的 11.3 降到 6.0/7.8 —— 确实在糊，但还看得出字，
     * 加大到 28dp（≈80px）才够「雾面」。
     */
    /** 模糊半径（dp）。实际渲染时会把内容缩到 1/6 再糊，等效半径 ≈ ×6.25（绕开 GPU 的模糊半径上限） */
    val BlurRadius: Dp = 40.dp
    val CornerRadius: Dp = 18.dp
    /**
     * 着色层不透明度。2026-10-22：库的 backdrop blur 在本项目里不落地（去掉 lens、半径 12→20dp
     * 都实测完全不糊；这版 Compose 又没有 rememberGraphicsLayer 可供手工高斯模糊），
     * 2026-10-22 后半段：真高斯模糊接上之后，这层重着色就多余了 —— 它当初是为了「没模糊时遮丑」。
     * 降到 α .35：糊过的内容能透出来，玻璃才通透（α .55 时糊了也看不出来，只剩一块深色板）。
     */
    val TintArgb: Long = 0x731C1C25L
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
    /**
     * 两栏的大圆角（2026-10-23 用户要求「加上大圆角」）：16dp 太方正，提到 30dp ——
     * 栏高 64dp，30dp 已经接近胶囊，但还留一点「方」的感觉，不是纯药丸。
     */
    val BarCornerRadius: Dp = 30.dp

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
    /**
     * 内容层的录制副本 —— 玻璃用它自己做**真**的高斯模糊。
     *
     * 为什么不用库的 blur()：2026-10-22 A/B 实测（去掉 lens / 半径 12→20dp / blur 提到最前 /
     * 去掉 vibrancy）背后文字**始终完全锐利** —— 这个库的模糊效果在本项目的组合结构里没落到屏幕上。
     * RenderEffect 是平台自带（API 31+，与本应用 minSdk 一致），一定会出糊。
     */
    internal val contentLayer: GraphicsLayer,
    /** Haze（现成方案）的状态：背板模糊交给它，自研 RenderEffect 那套在真机上不生效 */
    internal val haze: HazeState,
    /** miuix 的录制层：两栏用 Halcyon 那套 drawBackdrop + liquid 效果（真机上唯一真的糊得动的方案） */
    internal val miuixBackdrop: MiuixLayerBackdrop,
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
    // 图层必须由 rememberGraphicsLayer 创建：GraphicsLayer 的公开构造要 GraphicsLayerImpl，那是内部实现
    val contentLayer = rememberGraphicsLayer()
    val haze = rememberHazeState()
    // miuix 的录制层：两栏走 Halcyon 那套（drawBackdrop + liquid 效果），这是真机唯一糊得动的路径
    val miuixBackdrop = rememberMiuixLayerBackdrop()
    // 宿主 View：用来判断「当前这块玻璃是不是还在宿主那个窗口里」（弹窗是别的窗口）
    val view = androidx.compose.ui.platform.LocalView.current
    return remember(backdrop, contentLayer, haze, miuixBackdrop, view) {
        BackdropState(backdrop, contentLayer, haze, miuixBackdrop, view)
    }
}

/**
 * 宿主：把要被透出的内容录进 LayerBackdrop（必须是消费者的兄弟，不能是祖先）。
 * 注意要 `this.then(...)` —— 直接返回 layerBackdrop 会把调用方的 modifier 链（fillMaxSize 等）整条丢掉，
 * 那样宿主录到的 layer 尺寸不对，消费者采样到空区域，表现就是「玻璃有面、没有糊」。
 */
fun Modifier.backdropSource(state: BackdropState): Modifier = this
    .then(layerBackdrop(state.backdrop))
    // miuix 的录制（两栏用）：与 kyant 那份并行，互不影响
    .then(miuixLayerBackdrop(state.miuixBackdrop))
    // 同一块内容也登记成 Haze 的来源：两栏的模糊由 Haze 负责（自研的 RenderEffect 方案真机不生效）
    .hazeSource(state.haze)
    // 同一帧把内容录进 contentLayer（专供模糊），页面本身仍用 drawContent() 画锐利版。
    // 注意**不能**在这里 drawLayer：图层挂着 BlurEffect，那样整页都会被糊。
    .drawWithContent {
        // **降采样录制**：模糊要在小图上做（见 DOWNSCALE 注释），页面本身仍用 drawContent() 画锐利版。
        val w = (size.width / DOWNSCALE).toInt().coerceAtLeast(1)
        val h = (size.height / DOWNSCALE).toInt().coerceAtLeast(1)
        state.contentLayer.record(
            density = this,
            layoutDirection = layoutDirection,
            size = androidx.compose.ui.unit.IntSize(w, h),
        ) {
            scale(1f / DOWNSCALE, pivot = Offset.Zero) { this@drawWithContent.drawContent() }
        }
        drawContent()
    }

/** 消费者：采样宿主的 layer，先模糊再增艳，上面压一层着色，最后画自己的内容 */
@Composable
fun BackdropGlass(
    state: BackdropState,
    modifier: Modifier = Modifier,
    cornerRadius: Dp = GlassTokens.CornerRadius,
    tint: Color? = null,
    stroke: Color? = null,
    strokeWidth: Dp = GlassTokens.BarStrokeWidth,
    /**
     * 是否在玻璃底下铺一层**真**的高斯模糊（平台 RenderEffect）。
     * 只有悬浮两栏开：每开一块就要多画一次全屏图层，卡片/菜单这类小面不值得。
     */
    blurBehind: Boolean = false,
    content: @Composable () -> Unit,
) {
    val blurPx = with(LocalDensity.current) { GlassTokens.BlurRadius.toPx() }
    val shape = RoundedCornerShape(cornerRadius)
    // 自己在窗口里的位置：drawLayer 画的是全屏图层，必须抵消掉自身偏移才对得上背后的内容
    val posInRoot = remember { androidx.compose.runtime.mutableStateOf(Offset.Zero) }
    // 默认着色跟着页面底色走：浅底压白雾、深底压墨蓝（GlassTokens 里两套值）
    val effectiveTint = tint ?: if (isLightPalette(LocalCatClawPalette.current)) GlassTokens.TintLight else GlassTokens.Tint
    Box(
        modifier = modifier
            .then(
                if (blurBehind) {
                    Modifier.onGloballyPositioned { posInRoot.value = it.localToRoot(Offset.Zero) }
                } else {
                    Modifier
                },
            )
            // 先裁成玻璃形状：后面画的全屏模糊稿也会被裁进来
            .clip(shape)
            .drawBackdrop(
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
        )
        // **雾面**必须画在 drawBackdrop 之后：那一步已经把一层锐利的背景画上去了，
        // 画在它之前会被整块盖掉（2026-10-22 实测：换成 28dp 后两次量化结果一模一样 = 模糊根本没上屏）。
        // 这里用 drawWithContent 把模糊稿画在背景之上、栏内内容之下。
        .drawWithContent {
            if (blurBehind) {
                // 图层自带 BlurEffect（见 rememberBackdropState），这里只负责对齐位置后画出来
                translate(-posInRoot.value.x, -posInRoot.value.y) { drawLayer(state.contentLayer) }
            }
            drawContent()
        },
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
    val shape = RoundedCornerShape(GlassTokens.BarCornerRadius)
    if (glassOn) {
        // Halcyon `FloatingBottomBar.kt:424-500` 的挂法：miuix 的 drawBackdrop + 它自己那套 liquid 效果
        // （vibrancy / blur / 带色散的 lens），参数照抄它的默认档。
        //
        // **2026-10-23 的关键修正**：上次接这套渲染成「黑板 + 一块绿」，原因是**没传 onDrawSurface** ——
        // 那样 backdrop 什么都不画，只剩底部内容。这次按 Halcyon 的做法传 `onDrawSurface = { drawRect(着色) }`。
        val container = if (light) GlassTokens.TintLight else GlassTokens.Tint
        Box(
            modifier = modifier
                .height(GlassTokens.BarHeight)
                .clip(shape)
                .miuixDrawBackdrop(
                    backdrop = state.miuixBackdrop,
                    shape = { shape },
                    effects = {
                        padding = GlassTokens.RefractionPadding.toPx()
                        // vibrancy 是「提亮 + 增艳」：Halcyon 用在小药丸上没事，我们用整条宽栏会大面积泛白
                        miuixBlur(4.dp.toPx(), 4.dp.toPx())
                        halcyonLens(
                            refractionHeight = 24.dp.toPx(),
                            refractionAmount = 24.dp.toPx(),
                            depthEffect = true,
                            chromaticAberration = 0.2f,
                        )
                    },
                    // 高光同样按「宽栏」下调：Halcyon 0.75 是给窄药丸的，整条栏上会糊成一片白
                    highlight = { MiuixHighlight(alpha = 0.35f) },
                    // 没有它 = 黑板（上次的坑）。着色由它画，别再额外 background()
                    onDrawSurface = { drawRect(container) },
                )
                .border(GlassTokens.BarStrokeWidth, stroke, shape),
        ) { content() }
        return
    }
    // 实心档：底色 = 页面底色（不透明）压一档 + 原来的着色与描边，观感与玻璃档同族、只是不糊背后
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
