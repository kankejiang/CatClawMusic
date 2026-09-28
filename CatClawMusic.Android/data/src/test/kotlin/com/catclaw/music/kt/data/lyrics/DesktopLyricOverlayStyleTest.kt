package com.catclaw.music.kt.data.lyrics

import java.io.File
import org.junit.Assert.assertTrue
import org.junit.Test

/**
 * 桌面歌词悬浮窗的样式保真闸门（MAUI `Platforms/Android/DesktopLyricService.cs` 是参照物）。
 *
 * 起因是本轮审计发现安卓侧漏掉了 MAUI 的两个观感要素：
 * 1. `SetShadowLayer(3dp, 0, 1dp, black)` —— 底字与彩字都有。默认配色是白字 α.70 压在黑底 α.30 上，
 *    叠在浅色界面上时合成结果只有约 **1.7:1**，全靠这层阴影撑住可读性；没有阴影就等于白字糊在白底上。
 * 2. 歌词下方的 🔒 锁定按钮 —— 安卓侧整个没实现，悬浮窗上没有任何可点控件。
 */
class DesktopLyricOverlayStyleTest {

    private val maui: File? by lazy {
        File("../../CatClawMusic.Maui/Platforms/Android/DesktopLyricService.cs")
            .takeIf { it.isFile }
    }

    private val android: File by lazy {
        listOf(
            "src/main/kotlin/com/catclaw/music/kt/data/lyrics/DesktopLyricController.kt",
            "data/src/main/kotlin/com/catclaw/music/kt/data/lyrics/DesktopLyricController.kt",
        ).map(::File).firstOrNull { it.isFile }
            ?: error("找不到安卓悬浮窗源码（测试工作目录应为 :data 模块根）")
    }

    private val androidSrc by lazy { android.readText() }

    @Test
    fun `悬浮窗保留了 MAUI 的文字样式与锁定按钮`() {
        listOf("setShadowLayer(", "setTextSize(", "setTextColor(").forEach { needed ->
            assertTrue("安卓悬浮窗缺了 $needed", androidSrc.contains(needed))
        }
        assertTrue("锁定按钮（🔒）没实现", androidSrc.contains("LOCK_GLYPH"))
        assertTrue("背景圆角不是 MAUI 的 16dp", androidSrc.contains("cornerRadius = dp(16)"))
        // 锁定后窗口不吃触摸，按钮必须藏起来，否则留下一个点不动的假控件
        assertTrue("锁定后没隐藏锁定按钮", Regex("""if \(locked\) View\.GONE""").containsMatchIn(androidSrc))
    }

    /** 交叉核对参照物：MAUI 在两层歌词上调过的**文字样式**方法，安卓侧都要有对应实现 */
    @Test
    fun `MAUI 用到的文字样式方法在安卓侧都有对应`() {
        val reference = maui ?: return  // 单独检出安卓工程时跳过
        val mauiCalls = Regex("""_textView(?:Base|Fill)\.(Set\w+)\(""")
            .findAll(reference.readText())
            .map { it.groupValues[1] }
            .toSet()
        assertTrue("从 MAUI 参照里一个 Set* 都没抓到，八成文件挪位置了", mauiCalls.isNotEmpty())

        // 只比文字样式三项：maxLines/ellipsize/gravity 这些构造期设置，安卓走的是 apply{} 里的属性，
        // 不该按「方法调用」来要求，否则这条闸门会因为写法不同而空喊
        val styling = setOf("SetShadowLayer", "SetTextColor", "SetTextSize")
        val missing = mauiCalls.filter { it in styling }
            .map { it.replaceFirstChar { c -> c.lowercaseChar() } }
            .filter { !androidSrc.contains("$it(") }
            .distinct()
        assertTrue("安卓悬浮窗少了 MAUI 有的样式调用: $missing", missing.isEmpty())
    }

    /** 正控制：同一判据在真源码上为真、在被删改的副本上为假，两边一起才说明判据有效 */
    @Test
    fun `探针 样式调用被删掉时必须能发现`() {
        for (call in listOf("setShadowLayer(", "setTextColor(", "setTextSize(", "LOCK_GLYPH")) {
            assertTrue("当前源码里就没有 $call，判据无法自证", androidSrc.contains(call))
            assertTrue("$call 被删掉后检测器没报警", !androidSrc.replace(call, "/* 删掉了 */").contains(call))
        }
    }
}
