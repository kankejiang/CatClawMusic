package com.catclaw.music.kt.ui.theme

import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.width
import androidx.compose.material3.Icon
import androidx.compose.material3.Switch
import androidx.compose.material3.SwitchDefaults
import androidx.compose.material3.Text
import androidx.compose.material3.TextField
import androidx.compose.material3.TextFieldDefaults
import androidx.compose.runtime.Composable
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.res.painterResource
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import com.catclaw.music.kt.R
import com.catclaw.music.kt.core.theme.CatClawPalette
import kotlin.math.pow

/**
 * 全 app 共用的小零件。
 *
 * 玻璃卡的那串 modifier 之前每个页面各抄一遍，抄出过 22/24/26 三种圆角和 α.08/.10 两种底色，
 * 这里收成一个入口——参数就是视觉语言的定义，改一处即全 app 生效。
 *
 * 卡面配方的出处是 MAUI `Styles.xaml` 的 GlassCardStyle：
 * 填充 `CardBackgroundColor #1AFFFFFF`（= 前景墨 α.10）、圆角 26、描边 `GlassStrokeColor #28FFFFFF`（= α.16）。
 * 之前有一版用 primaryLight 35% 当填充，在深色底上会糊成一片浅紫，已经否定。
 */
/**
 * 玻璃卡。
 *
 * ⚠ **不要再把它接到 [liquidGlass] 上**（2026-10-18 真机踩过）：`backdropSource` 录的是**整页内容**，
 * 而卡片就在这层里面 —— 卡片去采样这一层等于**采样到自己**（递归反馈），真机直接
 * `Fatal signal 11 (SIGSEGV) in RenderThread`，应用起不来。
 * 库的注释里写着消费者「必须是宿主的兄弟，不能是祖先」，而卡片天然是内容的一部分。
 *
 * 所以卡片保持「着色 + 描边」的实心玻璃；真玻璃只给**与录制层同级**的表面（迷你条 / 底栏 / 覆盖层）。
 * 真要让卡片也透，得先把录制层改成"只录背景"（见 `CatClawRoot` 的结构注释），那是另一件事。
 */
@Composable
fun Modifier.glassCard(palette: CatClawPalette = LocalCatClawPalette.current, corner: Int = 26, selected: Boolean = false): Modifier {
    val shape = RoundedCornerShape(corner.dp)
    val ink = Color(palette.textPrimary)
    return this
        .clip(shape)
        .background(
            if (selected) Color(palette.primary).copy(alpha = 0.16f) else ink.copy(alpha = 0.10f),
        )
        .border(
            width = 1.dp,
            color = if (selected) Color(palette.primary).copy(alpha = 0.45f) else ink.copy(alpha = 0.16f),
            shape = shape,
        )
}

/** 页面底色偏亮就算浅色主题：玻璃着色与描边按这一档切换 */
@androidx.compose.runtime.Stable
fun isLightPalette(p: CatClawPalette): Boolean = relativeLuminance(p.backgroundStops.first().argb) > 0.5

/** 「用主色标一下当前项」的取色：主色在这块合成底上不足 3:1 就退回正文色，不放粉压粉 */
@androidx.compose.runtime.Stable
fun highlightOn(p: CatClawPalette, surface: Long): Long =
    if (contrastRatio(p.primary, surface) >= 3.0) p.primary else p.textPrimary

/** src-over：带 alpha 的 fg 压在不透明底色上，得到屏幕上真正的那块颜色 */
@androidx.compose.runtime.Stable
fun blend(fg: Long, over: Long): Long {
    val a = ((fg shr 24) and 0xFF) / 255.0
    fun ch(shift: Int) = (((fg shr shift) and 0xFF) * a + ((over shr shift) and 0xFF) * (1 - a)).toLong().coerceIn(0, 255)
    return (0xFFL shl 24) or (ch(16) shl 16) or (ch(8) shl 8) or ch(0)
}

/** 玻璃卡的面 = textPrimary α.10 压在页底上（α.10 → 0x1A） */
@androidx.compose.runtime.Stable
fun glassCardSurface(p: CatClawPalette): Long =
    blend((0x1AL shl 24) or (p.textPrimary and 0xFFFFFFL), p.backgroundStops.last().argb)

/** 选中卡的面是 primary α.16 压在页面底色上，高亮文字要对着这块合成结果来挑 */
@androidx.compose.runtime.Stable
fun highlightOnSelectedCard(p: CatClawPalette): Long =
    highlightOn(p, blend((0x29L shl 24) or (p.primary and 0xFFFFFFL), p.backgroundStops.last().argb))

/** 普通玻璃卡上的高亮同样对着合成结果挑（浅色主题会退回正文色） */
@androidx.compose.runtime.Stable
fun highlightOnGlassCard(p: CatClawPalette): Long = highlightOn(p, glassCardSurface(p))

/**
 * 贴在玻璃卡上的小徽标（自身还压一层 ink α.08）：强调色撑不住 3.0 就退回正文色。
 * 浅色主题的薄荷绿计数徽标在真机上量到 **1.29:1**，这条就是补那个洞。
 */
@androidx.compose.runtime.Stable
fun badgeTintOn(p: CatClawPalette, tint: Long): Long {
    val badge = blend((0x14L shl 24) or (p.textPrimary and 0xFFFFFFL), glassCardSurface(p))
    return if (contrastRatio(tint, badge) >= 3.0) tint else p.textPrimary
}

/**
 * 选择用的胶囊 chip：全 app 五个页面各自实现过一份（圆角 26/50/999、字级 11/13/labelMedium、
 * 未选中底色 α.08/α.18/实心卡底都出现过），现在只留这一份定义。
 * 高度按 Material 3 FilterChip 的量（~36dp）而不是 [TouchMin]：chip 常成排出现，44dp 会把版面顶开。
 */
@Composable
fun GlassChip(
    label: String,
    selected: Boolean,
    onClick: () -> Unit,
    palette: CatClawPalette = LocalCatClawPalette.current,
    enabled: Boolean = true,
    modifier: Modifier = Modifier,
) {
    val shape = RoundedCornerShape(999.dp)
    val ink = Color(palette.textPrimary)
    Text(
        text = label,
        fontSize = 13.sp,
        fontWeight = if (selected) FontWeight.Bold else FontWeight.Medium,
        color = when {
            selected -> Color(inkOn(palette.primary))
            else -> Color(palette.textSecondary).copy(alpha = if (enabled) 1f else 0.5f)
        },
        maxLines = 1,
        overflow = androidx.compose.ui.text.style.TextOverflow.Ellipsis,
        modifier = modifier
            .clip(shape)
            .background(
                if (selected) Color(palette.primary).copy(alpha = if (enabled) 1f else 0.45f)
                else ink.copy(alpha = 0.08f),
            )
            .border(width = if (selected) 0.dp else 1.dp, color = ink.copy(alpha = 0.16f), shape)
            .clickable(enabled = enabled, onClick = onClick)
            .padding(horizontal = 14.dp, vertical = 9.dp),
    )
}

/**
 * 开关行：标题（可带副标题）在左，开关在右。
 *
 * 之前有两份：AI 设置页那一份给 Switch 配了 `palette.primary` 的滑块 + α.32 轨道，
 * 设置主页 `SwitchRow` 那一份不配色——**真机量下来两份都是粉的**（stock Switch 的选中轨道
 * 取的是主题 primary，而主题 primary 就是品牌粉），差别在**角色**：
 * 旧的那份是"实心粉轨道 + 白滑块"(245,111,160)/(237,232,234)，
 * 新这份是"半透明粉轨道 + 实心粉滑块"(240,193,210)/(245,113,161)。
 * 同一类控件两种长相就是这份要收掉的理由；别据此以为旧的是紫色。
 */
@Composable
fun GlassSwitch(
    label: String,
    checked: Boolean,
    onChange: (Boolean) -> Unit,
    subtitle: String? = null,
    modifier: Modifier = Modifier,
    palette: CatClawPalette = LocalCatClawPalette.current,
) {
    Row(
        modifier = modifier.fillMaxWidth().padding(vertical = 6.dp),
        verticalAlignment = Alignment.CenterVertically,
    ) {
        Column(modifier = Modifier.weight(1f)) {
            Text(
                text = label,
                fontSize = 15.sp,
                color = Color(palette.textPrimary),
                maxLines = 2,
                overflow = androidx.compose.ui.text.style.TextOverflow.Ellipsis,
            )
            if (!subtitle.isNullOrEmpty()) {
                Text(subtitle, fontSize = 12.sp, color = Color(palette.textSecondary))
            }
        }
        Spacer(Modifier.width(12.dp))
        Switch(
            checked = checked,
            onCheckedChange = onChange,
            colors = SwitchDefaults.colors(
                checkedThumbColor = Color(palette.primary),
                checkedTrackColor = Color(palette.primary).copy(alpha = 0.32f),
                uncheckedThumbColor = Color(palette.textSecondary),
                uncheckedTrackColor = Color(palette.textPrimary).copy(alpha = 0.18f),
            ),
        )
    }
}

/**
 * 搜索框。全 app 只这一份配方：墨色 α.10 面 / α.08 未聚焦面、圆角 22、去掉 M3 的下划线。
 *
 * 输入框是页面里最后还带着 M3 默认样式的那类控件（它自带一套 primary 配色），
 * 各页自己写就会写出不同的底色与圆角，所以收成一个入口。
 */
@Composable
fun GlassSearchField(
    query: String,
    onChange: (String) -> Unit,
    placeholder: String,
    modifier: Modifier = Modifier,
    palette: CatClawPalette = LocalCatClawPalette.current,
) {
    val ink = Color(palette.textPrimary)
    TextField(
        value = query,
        onValueChange = onChange,
        placeholder = { Text(placeholder, color = Color(palette.textSecondary), fontSize = 14.sp) },
        singleLine = true,
        colors = TextFieldDefaults.colors(
            focusedContainerColor = ink.copy(alpha = 0.10f),
            unfocusedContainerColor = ink.copy(alpha = 0.08f),
            focusedIndicatorColor = Color.Transparent,
            unfocusedIndicatorColor = Color.Transparent,
            focusedTextColor = ink,
            unfocusedTextColor = ink,
            cursorColor = Color(palette.primary),
        ),
        shape = RoundedCornerShape(22.dp),
        modifier = modifier.fillMaxWidth(),
    )
}

/**
 * 实心面（primary / primaryLight）上的前景色：按 WCAG 相对亮度挑对比度更高的那一支。
 *
 * 原来一律用 `primaryDark` 当字色，但这个调色板里 primary(#FF8FB8) 与 primaryDark(#F56FA0)
 * 的对比度实测只有 **1.29:1**——粉压粉，两种主题下胶囊文字都是糊的（PaletteContrastTest 量出来的）。
 * 墨/白候选在同一位置上分别能到 9.08/7.05 与 2.13/2.74，所以让算数来定，不再靠肉眼挑。
 */
@androidx.compose.runtime.Stable
fun inkOn(fill: Long): Long = inkOnGradient(listOf(fill))

/**
 * 渐变面上的前景：逐停靠点量，留下「最差那一段也更好」的一支。
 * 只在中间调上判会漏——封面字压到 primaryLight(#FFD9E7) 这种亮端时白字只剩 1.29:1。
 */
@androidx.compose.runtime.Stable
fun inkOnGradient(stops: List<Long>): Long {
    fun worst(fg: Long) = stops.minOf { contrastRatio(fg, it) }
    return if (worst(0xFF0B0E14L) >= worst(0xFFFFFFFFL)) 0xFF0B0E14L else 0xFFFFFFFFL
}

/**
 * Hero 渐变卡的垫层。字直接压在品牌粉上只有 2.1–2.7:1，而这两张卡的渐变是板块身份，
 * 不能为了对比度改成墨色，所以在文字一侧压一层由浅到深的黑：
 * 带顶（α.40）是白字的最坏情况，实测 5.37:1 起；左上角标签自压 α.40 后同样 5.37:1 起。
 */
@androidx.compose.runtime.Stable
val HeroScrimBandArgb = 0x66000000L

@androidx.compose.runtime.Stable
val HeroScrimDeepArgb = 0xB8000000L

@androidx.compose.runtime.Stable
val HeroTagScrimArgb = 0x66000000L

private fun contrastRatio(a: Long, b: Long): Double {    val l1 = relativeLuminance(a)
    val l2 = relativeLuminance(b)
    return (maxOf(l1, l2) + 0.05) / (minOf(l1, l2) + 0.05)
}

private fun relativeLuminance(argb: Long): Double {
    fun lin(shift: Int): Double {
        val c = ((argb shr shift) and 0xFF) / 255.0
        return if (c <= 0.03928) c / 12.92 else ((c + 0.055) / 1.055).pow(2.4)
    }
    return 0.2126 * lin(16) + 0.7152 * lin(8) + 0.0722 * lin(0)
}

/**
 * 可点零件的最小触达边长。
 * Android 手册给 48dp，本 app 版面密（标题行里并排 2-3 个零件），取 44dp：
 * 再大就开始把 22sp 标题行顶开。改这一处 = 全 app 的胶囊与返回方块一起变。
 */
@androidx.compose.runtime.Stable
val TouchMin = 44.dp

/** 胶囊的上下内边距：13sp 文字行高约 17.6dp，2×13 + 17.6 ≈ 44dp，正好等于 [TouchMin] */
@androidx.compose.runtime.Stable
private val TouchPadV = 13.dp

/** 胶囊按钮：filled = primary 面 + 自动对比墨色；描边款只给边框 */
@Composable
fun PillLabel(
    text: String,
    onClick: () -> Unit,
    palette: CatClawPalette = LocalCatClawPalette.current,
    filled: Boolean = true,
    enabled: Boolean = true,
) {
    val shape = RoundedCornerShape(999.dp)
    val tint = if (filled) Color(inkOn(palette.primary)) else Color(palette.textPrimary)
    Text(
        text = text,
        fontSize = 13.sp,
        fontWeight = FontWeight.SemiBold,
        color = tint.copy(alpha = if (enabled) 1f else 0.45f),
        modifier = Modifier
            .clip(shape)
            .background(
                if (filled) Color(palette.primary).copy(alpha = if (enabled) 1f else 0.18f) else Color.Transparent,
            )
            .border(width = if (filled) 0.dp else 1.dp, color = Color(palette.textPrimary).copy(alpha = 0.16f), shape)
            .clickable(enabled = enabled, onClick = onClick)
            // 13sp 文字行高约 17.6dp，上下各 13dp → 触达 ≈ 44dp（用 padding 而不是 heightIn：
            // heightIn 会把文字顶到大框的上沿，胶囊里的字就不居中了）
            .padding(horizontal = 18.dp, vertical = TouchPadV),
    )
}

/**
 * 空状态：列表页的「还没有内容」统一成一张玻璃卡 + 图标 + 一句话。
 * 之前每页各写一行 13sp 裸文字，发现页搜不到结果时干脆什么都不渲染——像死屏。
 */
@Composable
fun EmptyState(
    hint: String,
    iconRes: Int,
    palette: CatClawPalette = LocalCatClawPalette.current,
    modifier: Modifier = Modifier,
) {
    val ink = Color(palette.textPrimary)
    val tileShape = RoundedCornerShape(15.dp)
    Column(
        modifier = modifier
            .fillMaxWidth()
            .glassCard(palette = palette, corner = 22)
            .padding(horizontal = 18.dp, vertical = 22.dp),
        horizontalAlignment = Alignment.CenterHorizontally,
    ) {
        Box(
            modifier = Modifier
                .size(44.dp)
                .clip(tileShape)
                .background(ink.copy(alpha = 0.10f))
                .border(1.dp, ink.copy(alpha = 0.16f), tileShape),
            contentAlignment = Alignment.Center,
        ) {
            Icon(
                painter = painterResource(iconRes),
                contentDescription = null,
                tint = Color(palette.textSecondary),
                modifier = Modifier.size(22.dp),
            )
        }
        Text(
            text = hint,
            fontSize = 13.sp,
            color = Color(palette.textSecondary),
            textAlign = TextAlign.Center,
            modifier = Modifier.padding(top = 12.dp),
        )
    }
}

/** 子页返回方块：玻璃面 + 19dp 箭头，触达边长走 [TouchMin]，各页头部同款 */
@Composable
fun BackTile(onBack: () -> Unit, description: String? = null) {
    val palette = LocalCatClawPalette.current
    val shape = RoundedCornerShape(14.dp)
    androidx.compose.foundation.layout.Box(
        modifier = Modifier
            .size(TouchMin)
            .clip(shape)
            .background(Color(palette.textPrimary).copy(alpha = 0.10f))
            .border(1.dp, Color(palette.textPrimary).copy(alpha = 0.16f), shape)
            .clickable(onClickLabel = description) { onBack() },
        contentAlignment = Alignment.Center,
    ) {
        Icon(
            painter = painterResource(R.drawable.ic_arrow_back),
            contentDescription = description,
            tint = Color(palette.textPrimary),
            modifier = Modifier.size(24.dp),
        )
    }
}
