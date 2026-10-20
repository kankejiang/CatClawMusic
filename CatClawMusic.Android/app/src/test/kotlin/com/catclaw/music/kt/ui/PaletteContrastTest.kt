package com.catclaw.music.kt.ui

import com.catclaw.music.kt.core.theme.CatClawPalette
import com.catclaw.music.kt.core.theme.CatClawTheme
import com.catclaw.music.kt.ui.theme.GlassTokens
import com.catclaw.music.kt.ui.theme.badgeTintOn
import com.catclaw.music.kt.ui.theme.blend
import com.catclaw.music.kt.ui.theme.HeroScrimBandArgb
import com.catclaw.music.kt.ui.theme.HeroScrimDeepArgb
import com.catclaw.music.kt.ui.theme.HeroTagScrimArgb
import com.catclaw.music.kt.ui.theme.highlightOn
import com.catclaw.music.kt.ui.theme.inkOn
import com.catclaw.music.kt.ui.theme.inkOnGradient
import com.catclaw.music.kt.ui.theme.isLightPalette
import java.io.File
import org.junit.Assert.assertTrue
import org.junit.Test
import kotlin.math.pow

/**
 * 版面配色的**算数**体检：把美化实际叠出来的层算成最终色，再量 WCAG 对比度。
 *
 * 为什么用测试而不是截图：真机只有一份，深浅两套主题、玻璃叠加后的实际底色，
 * 靠肉眼看一张深色截图是看不出来的——本轮就是这么发现「实心胶囊 = primary 面 + primaryDark 字」
 * 在这个调色板里只有 1.3:1（粉压粉），两种主题都糊。
 *
 * 阈值取 WCAG：正文 ≥4.5，大字/图标控件 ≥3.0，纯装饰不计。
 */
class PaletteContrastTest {

    private val report = mutableListOf<String>()
    private val violations = mutableListOf<String>()

    private fun luminance(argb: Long): Double {
        fun lin(shift: Int): Double {
            val c = ((argb shr shift) and 0xFF) / 255.0
            return if (c <= 0.03928) c / 12.92 else ((c + 0.055) / 1.055).pow(2.4)
        }
        return 0.2126 * lin(16) + 0.7152 * lin(8) + 0.0722 * lin(0)
    }

    private fun ratio(fg: Long, bg: Long): Double {
        val l1 = luminance(fg)
        val l2 = luminance(bg)
        return (maxOf(l1, l2) + 0.05) / (minOf(l1, l2) + 0.05)
    }

    private fun CatClawPalette.bgTop() = backgroundStops.first().argb
    private fun CatClawPalette.bgBottom() = backgroundStops.last().argb

    /** 前景直接压在已知最终色上（不像 measure 那样再叠一层 alpha），逐停靠点量用这个 */
    private fun check(name: String, fg: Long, bg: Long, floor: Double) {
        val r = ratio(fg, bg)
        report += "%-24s %-16s %5.2f  floor %.1f  %s"
            .format(name, "%06X".format(bg and 0xFFFFFF), r, floor, if (r >= floor) "OK" else "LOW")
        if (r < floor) violations += "$name on %06X = %.2f:1（要求 ≥%.1f）".format(bg and 0xFFFFFF, r, floor)
    }

    /** 这张表量的是页面上真在渲染的渐变，所以停靠点要从发现页源码里抽，不能手抄一份 */
    private fun discoverSource(): String {
        val rel = "src/main/kotlin/com/catclaw/music/kt/ui/screen/SearchScreen.kt"
        val f = File(rel).takeIf { it.isFile } ?: File("app/$rel")
        assertTrue("找不到发现页源码（测试工作目录应为 :app 模块根）: ${f.path}", f.isFile)
        return f.readText()
    }

    private fun paletteName(p: CatClawPalette, argb: Long): String = when (argb) {
        p.primary -> "primary"
        p.primaryLight -> "primaryLight"
        p.primaryDark -> "primaryDark"
        else -> error("调色板外的停靠点 %06X，这张表要重写".format(argb and 0xFFFFFF))
    }

    /**
     * 抽 `HeroCard(... brush = Brush.linearGradient(listOf(Color(0xAARRGGBB), ...))` 的停靠点。
     * 十六进制后面不跟 `)`：最后一个停靠点的右括号被 listOf 的收尾吃掉，跟了就永远少抽一个色。
     */
    private fun multiGlassStops(src: String): List<List<Long>> =
        Regex("""stops = listOf\(0x([0-9A-Fa-f]+)L, 0x([0-9A-Fa-f]+)L\)""")
            .findAll(src)
            .map { m ->
                listOf(m.groupValues[1].toLong(16), m.groupValues[2].toLong(16))
            }
            .toList()

    /** ink α 落在 页面底/玻璃卡/玻璃栏 三种真实底上 */
    private fun measure(
        name: String,
        ink: Long,
        alpha: Double,
        floor: Double,
        surface: String,
        bg: Long,
        gate: Boolean = true,
    ) {
        val fg = if (alpha >= 1.0) ink else blend((alpha * 255).toLong() shl 24 or (ink and 0xFFFFFFL), bg)
        val r = ratio(fg, bg)
        report += "%-26s %-16s %5.2f  floor %.1f  %s".format(name, surface, r, floor, if (r >= floor) "OK" else "LOW")
        if (r < floor && gate) violations += "$name @ $surface = %.2f:1（要求 ≥%.1f）".format(r, floor)
    }

    /** 候选墨色：只看数，不进闸门——用来定「实心面该配哪支字色」 */
    private fun inkCandidates(p: CatClawPalette, tag: String) {
        for ((name, fill) in listOf("primary" to p.primary, "primaryLight" to p.primaryLight)) {
            measure("$tag 墨字候选", 0xFF0B0E14L, 1.0, 3.0, "${name}面", fill, gate = false)
            measure("$tag 白字候选", 0xFFFFFFFFL, 1.0, 3.0, "${name}面", fill, gate = false)
            measure("$tag primaryDark现用", p.primaryDark, 1.0, 3.0, "${name}面", fill, gate = false)
        }
    }

    private fun themePair(p: CatClawPalette, tag: String) {
        val bgTop = p.bgTop()
        val bgBottom = p.bgBottom()
        val ink = p.textPrimary
        val inkDim = p.textSecondary

        // 玻璃卡：textPrimary α.10 面 + α.16 边，正文/次要文字落在合成后的面上
        val cardTop = blend(0x1A000000L or (ink and 0xFFFFFFL), bgTop)
        val cardBottom = blend(0x1A000000L or (ink and 0xFFFFFFL), bgBottom)
        val tintArgb = if (isLightPalette(p)) GlassTokens.TintLightArgb else GlassTokens.TintArgb
        val glassTint = blend(tintArgb, bgTop) // BarGlass 着色层：浅色压白雾、深色压墨蓝

        measure("$tag 正文/页面底", ink, 1.0, 4.5, "页顶底", bgTop)
        measure("$tag 正文/页面底(末段)", ink, 1.0, 4.5, "页末底", bgBottom)
        measure("$tag 次要/页面底", blend(inkDim, bgTop), 1.0, 4.5, "页顶底", bgTop)
        measure("$tag 正文/玻璃卡", ink, 1.0, 4.5, "卡面α.10", cardBottom)
        // 次要色自带 alpha（深色 #CCFFFFFF）时必须先合成再比，否则等于把 80% 白当 100% 白量
        measure("$tag 次要/玻璃卡", blend(inkDim, cardBottom), 1.0, 3.0, "卡面α.10", cardBottom)
        // 玻璃卡上再压 α.08 的小徽标：强调色撑不住就退回正文色（浅色主题薄荷绿真机实测 1.29:1）
        val badge = blend(0x14000000L or (ink and 0xFFFFFFL), cardBottom)
        measure("$tag 徽标/玻璃卡", badgeTintOn(p, p.accent), 1.0, 4.5, "inkα.08徽标", badge)
        measure("$tag 正文/玻璃栏", ink, 1.0, 4.5, "Tint玻璃", glassTint)
        measure("$tag 次要/玻璃栏", blend(inkDim, glassTint), 1.0, 4.5, "Tint玻璃", glassTint)

        // 选中卡：primary α.16 面，正文与「当前项」高亮文字
        val selCard = blend(0x29000000L or (p.primary and 0xFFFFFFL), bgBottom)
        measure("$tag 正文/选中卡", ink, 1.0, 4.5, "primaryα.16", selCard)
        measure("$tag 高亮/选中卡", highlightOn(p, selCard), 1.0, 4.5, "primaryα.16", selCard)

        // 实心面前景由 inkOn() 现算：primary 面配墨（9.08/7.05），不再拿 primaryDark 粉压粉（1.29）
        measure("$tag 胶囊字/primary面", inkOn(p.primary), 1.0, 4.5, "primary实心", p.primary)
        measure("$tag 播放键字/primaryLight面", inkOn(p.primaryLight), 1.0, 4.5, "primaryLight实心", p.primaryLight)

        // 图标控件：次级文字色的可点方块
        measure("$tag 图标/textPrimaryα.10块", ink, 1.0, 3.0, "小图标块", cardTop)

        // 只门禁「页面上真有的搭配」：面 α → (用在哪儿, 上面是否压次要小字)
        // 玻璃卡(ink α.10) 与选中卡(primary α.16) 在上面已经按 Hero 之外的真实底色量过，这里不重复
        val inkSurfaces = listOf(0.28 to ("TabCell 选中胶囊" to false))
        for ((a, meta) in inkSurfaces) {
            val (name, hasDim) = meta
            val surface = blend((a * 255).toLong() shl 24 or (ink and 0xFFFFFFL), bgBottom)
            measure("$tag 正文/$name", ink, 1.0, 4.5, "inkα$a", surface)
            if (hasDim) measure("$tag 次要/$name", inkDim, 1.0, 3.0, "inkα$a", surface)
        }
        val primarySurfaces = listOf(0.22 to "序号块", 0.30 to "资料库图标块")
        for ((a, name) in primarySurfaces) {
            val surface = blend((a * 255).toLong() shl 24 or (p.primary and 0xFFFFFFL), bgBottom)
            measure("$tag 正文/$name", ink, 1.0, 4.5, "primaryα$a", surface)
        }
    }

    /**
     * 半透明 Hero 渐变上的文字。这层要单独量：它不是落在页面底色上，而是「渐变压底色」再被文字压一次。
     *
     * 两轮实测得到的规则：
     * 1. 渐变上的次要文字也要用**正文墨色**（层级靠字号/字重），用次要色只有 2.73–3.21:1；
     * 2. 给文字垫的面板必须**往墨色的反方向走**（浅色主题压白、深色主题压黑）——
     *    反过来叠一层同色只会把中亮度的渐变推得更难读：深色主题在 Hero 上叠 ink α.10 实测 3.83:1，
     *    改成叠黑 α.30 后回到 6 以上。这和玻璃 Tint/TintLight 是同一个道理。
     */
    private fun heroSurfaces(p: CatClawPalette, tag: String) {
        val bgTop = p.bgTop()
        val ink = p.textPrimary
        val settingsStart = blend(0x7A55D6FFL, bgTop)
        val settingsEnd = blend(0x4D7ED8C3L, bgTop)
        val libraryHero = blend(0x40FFD9A8L, blend(0x66000000L or (p.primary and 0xFFFFFFL), bgTop))
        val lightTheme = isLightPalette(p)
        val panel = if (lightTheme) blend(0x6BFFFFFFL, libraryHero) else blend(0x4D000000L, libraryHero)

        for ((name, surface) in listOf(
            "设置Hero起站" to settingsStart,
            "设置Hero末站" to settingsEnd,
            "音乐库Hero" to libraryHero,
            "Hero上的面板" to panel,
        )) {
            measure("$tag 正文/$name", ink, 1.0, 4.5, name, surface)
            // 旧写法一：次要色压渐变；旧写法二：同色 α.10 叠面板 —— 都只作对照，不进门禁
            measure("$tag 次要色候选", blend(p.textSecondary, surface), 1.0, 4.5, name, surface, gate = false)
            val sameTonePanel = blend(0x1A000000L or (ink and 0xFFFFFFL), libraryHero)
            measure("$tag 同色面板候选", ink, 1.0, 4.5, "同色α.10", sameTonePanel, gate = false)
        }
    }

    @Test
    fun `深浅两套主题的实际叠色必须达到对比度下限`() {
        themePair(CatClawTheme.dark, "深色")
        themePair(CatClawTheme.light, "浅色")
        inkCandidates(CatClawTheme.dark, "深色")
        inkCandidates(CatClawTheme.light, "浅色")
        heroSurfaces(CatClawTheme.dark, "深色")
        heroSurfaces(CatClawTheme.light, "浅色")
        // 数字留档：通过与否都要能看到每一项量到了多少
        File("build").mkdirs()
        File("build/contrast-report.txt").writeText(report.joinToString("\n"))
        // 表塞进断言消息：Gradle 的 stdout 不落进测试 XML，只有失败消息读得到
        assertTrue("\n" + report.joinToString("\n") + "\n—— 违规: " + violations.joinToString("; "), violations.isEmpty())
    }

    @Test
    fun `实心面前景由 inkOn 现算，不取 primaryDark`() {
        for (p in listOf(CatClawTheme.dark, CatClawTheme.light)) {
            val gap = ratio(p.primaryDark, p.primary)
            assertTrue("primary↔primaryDark 现在有 %.2f:1，inkOn 的必要性要重新评估".format(gap), gap < 3.0)
            assertTrue("inkOn 挑出来的前景在实心面上仍不足 4.5", ratio(inkOn(p.primary), p.primary) >= 4.5)
            assertTrue("inkOn 选中了 primaryDark 那支粉", inkOn(p.primary) != p.primaryDark)
        }
    }
}
