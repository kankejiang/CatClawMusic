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

    @Test
    fun `录制层仍然存在且只有一处`() {
        val shell = source("app/src/main/kotlin/com/catclaw/music/kt/ui/CatClawRoot.kt")
        val hits = Regex("backdropSource\\(").findAll(shell).count()
        assertTrue("外壳里应当有唯一的录制层（被摘掉过，见那里的注释）", hits == 1)
    }
}
