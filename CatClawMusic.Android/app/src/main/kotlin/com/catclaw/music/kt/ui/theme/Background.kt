package com.catclaw.music.kt.ui.theme

import android.graphics.Bitmap
import android.graphics.BitmapFactory
import androidx.compose.animation.core.Animatable
import androidx.compose.animation.core.FastOutSlowInEasing
import androidx.compose.animation.core.LinearEasing
import androidx.compose.animation.core.tween
import androidx.compose.foundation.background
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.compositionLocalOf
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.drawBehind
import androidx.compose.ui.geometry.Offset
import androidx.compose.ui.geometry.Size
import androidx.compose.ui.graphics.Brush
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.asImageBitmap
import androidx.compose.ui.graphics.drawscope.DrawScope
import androidx.compose.ui.graphics.lerp
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

    return drawBehind {
        // 2026-11-06 用户要求：发现 / 歌单 / 音乐库上的「光斑」删掉。
        // 原来这里还会画调色板里的两层径向光晕（palette.glows），那两团柔光在动态背景之上仍然可见、
        // 又跟着主题色走，观感像「背景没洗干净」。现在只留三层线性渐变的纯净底。
        drawRect(
            brush = Brush.linearGradient(
                *stops,
                start = Offset.Zero,
                end = Offset(size.width, size.height),
            ),
        )
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
 * 发现 / 歌单 / 音乐库 三页的「动态封面背景」（2026-10-23 用户要求）。
 *
 * 做法：当前播放封面 AspectFill 铺满整屏 + 一层自上而下加深的主题色压暗。
 * **故意不解大图、也不挂 blur**：封面按 160px 解码后放大到整屏，天然就是柔的，
 * 既得到「封面虚化成背景」的观感，又省掉一次全屏 RenderEffect（这块 GPU 上很贵）。
 * 压暗必须用主题底色而不是纯黑：浅色主题下纯黑压暗会把整页压成脏灰。
 */
@Composable
fun Modifier.coverAuraBackground(cover: Bitmap?): Modifier {
    if (cover == null) return this
    val palette = LocalCatClawPalette.current
    val scrim = Color(palette.backgroundStops.last().argb)
    return drawBehind {
        val scale = maxOf(size.width / cover.width, size.height / cover.height)
        val w = cover.width * scale
        val h = cover.height * scale
        drawImage(
            image = cover.asImageBitmap(),
            dstOffset = IntOffset(((size.width - w) / 2f).roundToInt(), ((size.height - h) / 2f).roundToInt()),
            dstSize = androidx.compose.ui.unit.IntSize(w.toInt(), h.toInt()),
        )
        // 压暗：顶部留一点封面颜色，越往下越实，保证列表文字可读
        drawRect(
            brush = Brush.verticalGradient(
                0f to scrim.copy(alpha = 0.42f),
                0.45f to scrim.copy(alpha = 0.72f),
                1f to scrim.copy(alpha = 0.90f),
            ),
        )
    }
}

/**
 * 网易云「一起听」那套：封面原图**全宽、清晰**地占满上半屏（顶对齐 AspectFill），
 * 下缘一段雾化渐隐融进取自封面均色的底色，下半屏就是这块干净的纯色——没有方块、没有毛玻璃。
 * 取不到封面时调用方应退回 [artworkWash]。
 * 位图数学与 [catClawBackground] 的自定义背景同一套 AspectFill（2026-10-01 仿，第二版：不是小方块浮在模糊图上）。
 *
 * 切歌转场是「雾幕」而不是淡入淡出：上下两道雾相向推进合拢成全屏纯色 →
 * 在全屏不透明的这层雾里换图、同时把雾的颜色渐变到新封面的均色 → 雾再退开露出新封面。
 * 换图发生在什么都看不见的那一瞬，所以不会出现"半旧半新"的鬼影。
 */
private fun darken(c: Color, fraction: Float): Color =
    Color(c.red * (1 - fraction), c.green * (1 - fraction), c.blue * (1 - fraction))

/** 幕布转场三段：合拢 / 换色换图 / 散开 */
private const val CURTAIN_CLOSE_MS = 240
private const val CURTAIN_TINT_MS = 200
private const val CURTAIN_OPEN_MS = 300

@Composable
fun Modifier.artworkBackdrop(cover: Bitmap?): Modifier {
    if (cover == null) return this
    // 不能用 cover 直接画：LaunchedEffect 要等下一帧才启动，中间那一帧会把新图
    // 直接贴上去。shown 是"当前该画的那张"，只在幕布全遮住时才推进。
    var shown by remember { mutableStateOf<Bitmap?>(null) }
    /** 合拢时锁定的旧色调；非 null 表示正处在幕布转场中 */
    var fromTint by remember { mutableStateOf<Color?>(null) }
    /** 0 = 常态（只有上下两道雾），1 = 雾已合拢盖住整屏 */
    val curtain = remember { Animatable(0f) }
    /** 幕布色调：0 = 旧封面均色，1 = 新封面均色 */
    val tintPhase = remember { Animatable(1f) }
    val shownTint = rememberArtworkTint(shown)
    val incomingTint = rememberArtworkTint(cover)

    LaunchedEffect(cover) {
        if (shown === cover) return@LaunchedEffect
        if (shown == null) {
            // 首次进页面没有上一张可遮：直接落图
            shown = cover
            return@LaunchedEffect
        }
        // 此刻 shown 还是旧图，这个取色就是"上一张的色调"
        fromTint = shownTint
        tintPhase.snapTo(0f)
        // ① 上下两道雾相向推进，把画面整个盖住（这期间画的仍是旧封面）
        curtain.animateTo(1f, tween(CURTAIN_CLOSE_MS, easing = FastOutSlowInEasing))
        // ② 满屏不透明时换图，并让幕布色调过渡到新封面的均色
        shown = cover
        tintPhase.animateTo(1f, tween(CURTAIN_TINT_MS, easing = LinearEasing))
        // ③ 雾向上下退开，露出新封面
        curtain.animateTo(0f, tween(CURTAIN_OPEN_MS, easing = FastOutSlowInEasing))
        fromTint = null
    }

    // 幕布色：合拢阶段是旧色，换色阶段插值到新色，散开阶段已经是新色
    val curtainTint = fromTint?.let { lerp(it, incomingTint, tintPhase.value) } ?: incomingTint
    return drawBehind {
        val current = shown ?: return@drawBehind
        drawArtworkLayer(current, shownTint)
        if (curtain.value > 0f) drawArtworkCurtain(darken(curtainTint, 0.35f), curtain.value)
    }
}

/**
 * 幕布：上下两道雾相向推进。
 *
 * 只画两条而不画四条——封面是满宽 AspectFill，左右边缘就是图像边界，
 * 上下两条推到满高就已经遮满全屏；四边同时叠加反而会让中间区域提前变实，
 * 合拢后段像"啪"地关灯，失去雾气推进的层次。
 *
 * 色标里 70% 的实色段是必须的：纯线性渐变在 progress≈0.7 时中间那道缝
 * 只剩一半不透明度，两条叠完仍有可见的透光。
 */
private fun DrawScope.drawArtworkCurtain(color: Color, progress: Float) {
    val h = size.height * progress.coerceIn(0f, 1f)
    if (h <= 0f) return
    drawRect(
        brush = Brush.verticalGradient(
            colorStops = arrayOf(0f to color, 0.7f to color, 1f to Color.Transparent),
            startY = 0f, endY = h,
        ),
        size = Size(size.width, h),
    )
    drawRect(
        brush = Brush.verticalGradient(
            colorStops = arrayOf(0f to Color.Transparent, 0.3f to color, 1f to color),
            startY = size.height - h, endY = size.height,
        ),
        topLeft = Offset(0f, size.height - h),
        size = Size(size.width, h),
    )
}

/** 画封面底（AspectFill 图 + 上下雾带）。 */
private fun DrawScope.drawArtworkLayer(cover: Bitmap, tint: Color) {
    val base = darken(tint, 0.35f)
    // 封面全宽 AspectFill：上缘 18% 屏高、下缘 66%。上缘与雾带各自独立调——
    // 雾带高度固定，别跟着封面长，否则「下移封面」会被雾变厚吃掉（真机教训）
    val imgTop = size.height * 0.18f
    val imgBottom = size.height * 0.66f
    val scale = maxOf(size.width / cover.width, (imgBottom - imgTop) / cover.height)
    val w = cover.width * scale
    val h = cover.height * scale
    drawImage(
        image = cover.asImageBitmap(),
        dstOffset = IntOffset(((size.width - w) / 2f).roundToInt(), imgTop.roundToInt()),
        dstSize = androidx.compose.ui.unit.IntSize(w.toInt(), h.toInt()),
    )
    // 顶部雾化区：颜色与底部底色同源（base），上下两端把封面夹在同一种雾里。
    // 雾在封面上缘处必须完全不透明（100% 盖住图像边界），往下才渐隐——
    // 上一版雾只盖到 55%，图像顶边直接透出来，就是真机上那条「断层」
    val fadeBand = size.height * 0.12f
    drawRect(
        brush = Brush.verticalGradient(
            colorStops = arrayOf(
                0f to base,
                (imgTop / (imgTop + fadeBand)) to base,
                1f to Color.Transparent,
            ),
            startY = 0f, endY = imgTop + fadeBand,
        ),
        size = Size(size.width, imgTop + fadeBand),
    )
    // 下缘雾化→底色一条连续渐变到屏底。色标必须显式给：base 要恰好在封面下缘到位，
    // 均分三色的话到位点会掉到屏高中部，图像底边就会留一条缝（真机 v4 量到的）
    val fadeStart = imgBottom * 0.55f
    val midStop = (imgBottom - fadeStart) / (size.height - fadeStart)
    drawRect(
        brush = Brush.verticalGradient(
            colorStops = arrayOf(
                0f to Color.Transparent,
                midStop to base,
                1f to darken(base, 0.35f),
            ),
            startY = fadeStart, endY = size.height,
        ),
        topLeft = Offset(0f, fadeStart),
        size = Size(size.width, size.height - fadeStart),
    )
}

/**
 * 封面平均色：26×26 网格采样（≤676 个像素，一次算完，不逐帧扫图）。
 * 取不到封面就退回主色——染色不该因为一张图都没解出来就变成透明。
 */@Composable
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
