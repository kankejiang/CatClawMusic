package com.catclaw.music.kt

import java.io.File
import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Test

/**
 * 源码闸门：**播放页那一行歌词不再复用全屏歌词页的组件。**
 *
 * 2026-10-18 用户的要求。拆之前它是个薄包装：拿全屏页的 `LyricLineText`，
 * 再用一串 override（setFontSize / alignment / currentScale=1 / showExtras=false）把它压成页脚的一行注脚 ——
 * 每改一次播放页，都得先去读那个为"多行滚动页"写的参数表。
 *
 * 为什么值得立闸门：把两边重新接起来**不会有任何报错**，只会让播放页又被那套几何捆住，
 * 下次改（字号、位置、对齐）又要绕一遍。共用只该发生在 [KaraokeFill] 那种"不认识页"的地方。
 */
class PlayingLyricDecouplingTest {

    private val root: File by lazy {
        var dir: File? = File(".").absoluteFile
        while (dir != null && !File(dir, "app/src/main/kotlin").isDirectory) dir = dir.parentFile
        require(dir != null) { "从 ${File(".").absolutePath} 往上找不到仓库根" }
        dir
    }

    private fun source(relative: String) = File(root, relative).readText()

    private val playing = "app/src/main/kotlin/com/catclaw/music/kt/ui/lyrics/PlayingLyricLine.kt"
    private val full = "app/src/main/kotlin/com/catclaw/music/kt/ui/lyrics/LyricLineText.kt"

    @Test
    fun `播放页不引用全屏页那一行的组件`() {
        val src = source(playing)
        assertFalse("播放页又用上了 LyricLineText", src.contains("LyricLineText("))  // 只算真调用，注释里点名不算
        assertFalse("播放页不该带译文/罗马音那套副行参数", src.contains("showExtras"))
        assertFalse("播放页不该有放大档位", src.contains("currentScale"))
        assertTrue("播放页没走共用文本原语", src.contains("KaraokeText("))
    }

    @Test
    fun `全屏页也不知道播放页的存在`() {
        val src = source(full)
        // 盯的是**代码引用**（`名字(`），不是注释里提到的名字：
        // 两边注释互相点名是好事（后来者才知道对面那一页在哪），但一旦真调用就又捆上了
        assertFalse("全屏页反向依赖了播放页", src.contains("PlayingLyricLine("))
    }

    @Test
    fun `全屏页的几何常量是本页私有`() {
        val src = source("app/src/main/kotlin/com/catclaw/music/kt/ui/screen/FullLyricsScreen.kt")
        // 曾经为了"两页共用一档放大"放开成 internal，拆开后必须收回：
        // 开放出去就又会有人从外面依赖，两页重新被捆在一起
        listOf("LyricCurrentScale", "LyricGapExtra", "LyricScrollMs").forEach { name ->
            assertFalse("$name 又变成模块内可见了", src.contains("internal const val $name") || src.contains("internal val $name"))
        }
        assertTrue("private const val LyricCurrentScale 不见了", src.contains("private const val LyricCurrentScale"))
    }
}
