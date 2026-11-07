package com.catclaw.music.kt

import java.io.File
import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Test

/**
 * 源码闸门：**玻璃消费者必须是录制层的兄弟，不能是它的后代。**
 *
 * 2026-10-18 真机踩过（这次代价是应用起不来）：当时把 `glassCard` 接到 `liquidGlass` 上，
 * 想让全 app 卡片都透出背后内容 —— 结果卡片本身就在 `backdropSource` 录制的那棵子树里，
 * 采样等于**采样到自己**（递归反馈），真机直接 `Fatal signal 11 (SIGSEGV) in RenderThread`。
 *
 * 为什么值得立成闸门：这个错误**编译通过、单测全绿**，只有真机启动时才炸；
 * 而库的规则只写在注释里（"消费者必须与宿主同级"），代码上看不出来。
 */
class GlassSiblingRuleTest {

    private val root: File by lazy {
        var dir: File? = File(".").absoluteFile
        while (dir != null && !File(dir, "app/src/main/kotlin").isDirectory) dir = dir.parentFile
        require(dir != null) { "从 ${File(".").absolutePath} 往上找不到仓库根" }
        dir
    }

    private fun source(relative: String) = File(root, relative).readText()

    @Test
    fun `页面卡片不许采样录制层`() {
        val glassParts = source("app/src/main/kotlin/com/catclaw/music/kt/ui/theme/GlassParts.kt")
        val glassCard = glassParts.substringAfter("fun Modifier.glassCard(")
            .substringBefore("\n}")
        assertFalse(
            "glassCard 又接上了 liquidGlass —— 卡片在录制层内部，采样自己会 SIGSEGV（应用起不来）",
            glassCard.contains("liquidGlass("),
        )
        assertTrue(
            "卡片应保持「着色 + 描边」的实心玻璃",
            glassCard.contains("background(") && glassCard.contains("border("),
        )
    }

    /**
     * 录制层现在是**两份**，各有分工（2026-11-07 用户反馈底栏发黑）：
     * - 内容层：给页内玻璃卡采样（`LocalBackdropState`）；
     * - 背景层：给底栏 / 迷你播放器采样 —— 它们贴在屏幕最下方，那里没有内容，
     *   采内容层是黑的；背景层单独录一次（背景 8fps 才更新），两条玻璃采样它就有渐变色。
     *
     * 上限仍是 2：再多就说明有人又在别处乱开录制层。
     */
    @Test
    fun `录制层仍然存在且只有两处`() {
        val shell = source("app/src/main/kotlin/com/catclaw/music/kt/ui/CatClawRoot.kt")
        val hits = Regex("backdropSource\\(").findAll(shell).count()
        assertTrue("外壳里应当有且只有两份录制层（内容 + 背景），实际 $hits 份", hits == 2)
        assertTrue("内容层那处必须保留（LocalBackdropState 提供给它）", "backdropSource(backdrop)" in shell)
        assertTrue("背景层那处必须保留（底栏 / 迷你条采样它）", "backdropSource(backgroundBackdrop)" in shell)
    }
}
