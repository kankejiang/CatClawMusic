package com.catclaw.music.kt

import java.io.File
import org.junit.Assert.assertTrue
import org.junit.Test

/**
 * 源码闸门：**逐字高亮用正文色（深色主题下即白色），不用主题强调色（粉）。**
 *
 * 2026-10-19 用户明确要求：「逐字歌词字体颜色改一下，不要用粉色的，高亮使用白色」。
 * 原来的涂法是「未唱 = 正文字色、已唱 = palette.primary（粉）」——靠**色相**表进度。
 * 改成「已唱 = 正文字色、未唱 = 同色降透明度」之后，进度靠**明度**表现，
 * 粉色就不会再和封面染色、封面流背景混在一起。
 *
 * 为什么要立闸门：这类配色改动编译通过、逻辑测试也不会红，只会在真机上「看起来不对」；
 * 而且 fillColor/baseColor 是两个不同的调用点（播放页 / 全屏歌词），改一处漏一处很常见。
 */
class KaraokeColorTest {

    private val root: File by lazy {
        var dir: File? = File(".").absoluteFile
        while (dir != null && !File(dir, "app/src/main/kotlin").isDirectory) dir = dir.parentFile
        require(dir != null) { "从 ${File(".").absolutePath} 往上找不到仓库根" }
        dir
    }

    private fun source(rel: String): String = File(root, rel).readText()

    @Test
    fun `播放页与全屏歌词的高亮都是正文色 且比未唱亮`() {
        val playing = source("app/src/main/kotlin/com/catclaw/music/kt/ui/lyrics/PlayingLyricLine.kt")
        val full = source("app/src/main/kotlin/com/catclaw/music/kt/ui/lyrics/LyricLineText.kt")

        // ① 高亮 = 正文色（深色主题下就是白色）
        assertTrue("播放页高亮不是正文色", playing.contains("fillColor = Color(palette.textPrimary)"))
        assertTrue("全屏歌词高亮不是正文色", full.contains("val fillColor = Color(palette.textPrimary)"))

        // ② 不许再用主题强调色当高亮
        assertTrue("播放页又用回粉/强调色高亮了", !playing.contains("fillColor = Color(palette.primary)"))
        assertTrue("全屏歌词又用回粉/强调色高亮了", !full.contains("val fillColor = Color(palette.primary)"))

        // ③ 未唱必须比高亮淡，否则逐字进度看不出来（高亮=白、未唱=白 → 白涂白，等于没涂）
        assertTrue("播放页未唱色没降透明度", playing.contains("baseColor = Color(palette.textPrimary).copy(alpha = 0.45f)"))
        assertTrue("全屏歌词未唱色没降透明度", full.contains("Color(palette.textPrimary).copy(alpha = 0.45f)"))
    }
}
