package com.catclaw.music.kt

import java.io.File
import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Test

/**
 * 源码闸门：播放页那四颗图标（定时停止 / 音效 / 桌面歌词 / 横竖屏）必须待在「更多」菜单里，
 * 而不是又爬回进度条右上角。
 *
 * 为什么值得立这条：这个位置**改回去不会有任何报错**，只是角上多四颗图标 ——
 * 而当初把它们收进菜单的理由（角上只能靠「点亮」表达状态、用户看不出开关是开是关）
 * 在代码里看不出来。真机反馈驱动的布局决定，就得用源码闸门钉住。
 */
class PlayingMenuLayoutTest {

    private val root: File by lazy {
        var dir: File? = File(".").absoluteFile
        while (dir != null && !File(dir, "app/src/main/kotlin").isDirectory) dir = dir.parentFile
        require(dir != null) { "从 ${File(".").absolutePath} 往上找不到仓库根" }
        dir
    }

    private fun source(relative: String) = File(root, relative).readText()

    @Test
    fun `播放页不再有那一排可点亮的图标`() {
        val playing = source("app/src/main/kotlin/com/catclaw/music/kt/ui/screen/PlayingScreen.kt")
        assertFalse(
            "ActionIcon 又被加回来了 —— 那四颗图标该待在更多菜单里",
            playing.contains("private fun ActionIcon("),
        )
    }

    @Test
    fun `四颗图标的入口都在菜单的播放一节里`() {
        val playing = source("app/src/main/kotlin/com/catclaw/music/kt/ui/screen/PlayingScreen.kt")
        val sections = playing.substringAfter("sections = listOf(", "")
        assertTrue("菜单里找不到「播放」这一节", sections.isNotEmpty())
        val firstLabel = sections.substringBefore("SongMenuItem(")
        assertTrue("这一节得有小标题", firstLabel.contains("song_menu_section_play"))
        listOf(
            "R.string.sleep_timer_title" to "定时停止播放",
            "R.string.soundfx_title" to "音效",
            "R.string.PageTitleDesktopLyric" to "桌面歌词",
            "R.string.playing_rotate" to "横竖屏切换",
        ).forEach { (key, name) ->
            assertTrue("菜单的播放一节里少了「$name」", sections.contains(key))
        }
    }

    @Test
    fun `菜单是分节的，且开关类项带状态`() {
        val menu = source("app/src/main/kotlin/com/catclaw/music/kt/ui/song/SongActionMenu.kt")
        listOf("song_menu_section_song", "song_menu_section_about").forEach {
            assertTrue("菜单少了分组标题 $it", menu.contains(it))
        }
        // 2026-10-20 重做后，「收藏」从列表行提到顶部胶囊：状态不再是「开/关」两个字，
        // 而是整颗胶囊转成**实心主题色**。判据跟着挪到这里。
        assertTrue("快捷区的收藏胶囊没有按收藏态切换", menu.contains("on = isFavorite"))
        assertTrue("收藏态胶囊没有实心样式", menu.contains("if (on) Color(palette.primary)"))
        // 列表里的开关类项（桌面歌词/定时停止）仍必须有 value：菜单里没有「点亮」这种表达
        assertTrue("带 value 的行没落到 UI 上", menu.contains("item.value?.let"))
    }
}
