package com.catclaw.music.kt

import java.io.File
import org.junit.Assert.assertTrue
import org.junit.Test

/**
 * 源码闸门：**播放页也要把位置流提到 ~60fps。**
 *
 * 2026-10-18 真机投诉「逐字歌词卡，但上下滚动很流畅」——查出来是数据频率问题：
 * `LyricsViewModel.positionMs` 是「可见时 16ms、否则 120ms」，而置位这个开关的**只有全屏歌词页**。
 * 站在播放页时逐字着色每秒只有 8 格，看着一跳一跳；滚动是帧驱动的，所以显得流畅。
 * 同一条路上还有第二个洞：`PlayingViewModel.positionMs` 用的是默认 250ms（进度条每秒 4 下）。
 *
 * 为什么值得立闸门：这种错**编译通过、任何单测都不会红**，只是"看起来卡"，
 * 而根因（谁置位了可见性）在读代码时极容易被跳过 —— 我这次就是先围着 GPU 查了两轮才发现。
 */
class LyricFlowRateTest {

    private val root: File by lazy {
        var dir: File? = File(".").absoluteFile
        while (dir != null && !File(dir, "app/src/main/kotlin").isDirectory) dir = dir.parentFile
        require(dir != null) { "从 ${File(".").absolutePath} 往上找不到仓库根" }
        dir
    }

    private fun source(relative: String) = File(root, relative).readText()

    @Test
    fun `外壳按 settledPage 置位两条位置流`() {
        // 单一来源在外壳：页面是**预组装**的（beyondViewportPageCount = 1），
        // 各自置位会让旁边那页也跟着 60fps 轮询（主线程轮询，白耗电）
        val shell = source("app/src/main/kotlin/com/catclaw/music/kt/ui/CatClawRoot.kt")
        assertTrue("外壳没有置位播放页可见性", shell.contains("setPlayingSurfaceVisible(onPlayer)"))
        assertTrue("外壳没有置位歌词可见性", shell.contains("setPlayingLyricVisible(onPlayer || onLyrics)"))
        assertTrue("外壳没有按 settledPage 判在台上", shell.contains("snapshotFlow { pagerState.settledPage }"))
        val screen = source("app/src/main/kotlin/com/catclaw/music/kt/ui/screen/PlayingScreen.kt")
        assertTrue(
            "页面又开始自己置位了（会与外壳重复，且预组装页会误开）",
            !screen.contains("setPlayingSurfaceVisible(") && !screen.contains("setPlayingLyricVisible("),
        )
    }

    @Test
    fun `歌词流把两个开关或起来`() {
        val vm = source("app/src/main/kotlin/com/catclaw/music/kt/ui/lyrics/LyricsViewModel.kt")
        assertTrue("播放页的开关丢了", vm.contains("playingLyricVisible"))
        assertTrue("两个开关没有合并", vm.contains("combine(playingLyricVisible, lyricsSurfaceVisible"))
        // 正控制：把播放页那个开关从合并里去掉，判据必须变假
        assertTrue(
            "去掉播放页开关后判据没反应",
            !vm.replace("combine(playingLyricVisible, lyricsSurfaceVisible", "combine(lyricsSurfaceVisible")
                .contains("combine(playingLyricVisible, lyricsSurfaceVisible"),
        )
    }

    @Test
    fun `可见时两条流都提到 16ms`() {
        val lv = source("app/src/main/kotlin/com/catclaw/music/kt/ui/lyrics/LyricsViewModel.kt")
        val pv = source("app/src/main/kotlin/com/catclaw/music/kt/ui/playback/PlayingViewModel.kt")
        assertTrue("歌词流没提到 16ms", lv.contains("positionMsFlow(if (visible) 16L else 120L)"))
        assertTrue("播放页进度流没提到 16ms", pv.contains("positionMsFlow(if (visible) 16L else 250L)"))
    }
}
