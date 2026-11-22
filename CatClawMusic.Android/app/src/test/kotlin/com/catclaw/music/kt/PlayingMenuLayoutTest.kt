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

    /**
     * 2026-11-22：播放页「更多」从**列表**改成了**图标网格**（[PlayingMoreSheet]）。
     *
     * 这条断言随之改到网格文件上，检查的仍是同一件事：那四颗图标必须还在「更多」里。
     * 保留这条防护的理由没变 —— 它们放回进度条右上角不会报错，只是角上多四颗图标，
     * 而「开关状态看不出来」这个当初收进菜单的原因在代码里也看不出来。
     */
    @Test
    fun `四颗图标的入口都在更多面板里`() {
        val sheet = source("app/src/main/kotlin/com/catclaw/music/kt/ui/screen/PlayingMoreSheet.kt")
        val playing = source("app/src/main/kotlin/com/catclaw/music/kt/ui/screen/PlayingScreen.kt")
        // 面板被播放页真正调用（不是写完就闲置）
        assertTrue("播放页没有调用 PlayingMoreSheet", playing.contains("PlayingMoreSheet("))
        listOf(
            "R.string.sleep_timer_title" to "定时停止播放",
            "R.string.soundfx_title" to "音效",
            "R.string.PageTitleDesktopLyric" to "桌面歌词",
            "R.string.playing_rotate" to "横竖屏切换",
        ).forEach { (key, name) ->
            assertTrue("更多面板里少了「$name」", playing.contains(key) || sheet.contains(key))
        }
        // 面板必须是图标网格，不能退回一列平铺
        assertTrue("更多面板没有使用图标网格", sheet.contains("MenuTileGrid("))
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
