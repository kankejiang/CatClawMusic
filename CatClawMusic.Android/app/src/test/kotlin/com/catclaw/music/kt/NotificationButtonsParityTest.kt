package com.catclaw.music.kt

import java.io.File
import org.junit.Assert.assertTrue
import org.junit.Test

/**
 * 通知栏那两颗自定义按钮（桌面歌词 / 收藏）的接线闸门。
 *
 * 这块没有任何行为测试抓得住 —— 要 fake 一整个 `MediaSessionService` 与 media3 的控制器。
 * 但它的外形极好判定，因为「按钮画出来」与「点了能进来」是**两处独立的代码**：
 * - `setCustomLayout(...)` 负责画按钮；
 * - `onConnect` 把这两条命令加进可用集合，`onCustomCommand` 才有机会被调到。
 * 少任何一处，症状都是用户在通知栏点了没反应 —— 而这一页永远不会有单元测试去点它。
 *
 * 另一条不变量：通知栏图标必须由 `:app` 注入（`:data` 一个 drawable 都没有）。
 * 谁哪天图省事在 `:data` 里复制一份同名 vector，就会踩上「同名资源跨模块互相覆盖」。
 */
class NotificationButtonsParityTest {

    /**
     * 仓库根：从工作目录往上找那个同时有 `data/` 与 `app/` 的目录。
     * 不去猜「工作目录是 :app 模块根还是仓库根」—— 猜错的表现是文件找不到，而报错信息会
     * 指向一个看着像对的相对路径（这里先前就写错过一次）。
     */
    private val repoRoot by lazy {
        var dir: File? = File(".").absoluteFile
        while (dir != null && !File(dir, "data/src/main/kotlin").isDirectory) dir = dir.parentFile
        require(dir != null) { "从 ${File(".").absolutePath} 往上找不到仓库根" }
        dir
    }

    private val service by lazy {
        File(repoRoot, "data/src/main/kotlin/com/catclaw/music/kt/data/service/MediaPlaybackService.kt")
            .also { require(it.isFile) { "找不到 MediaPlaybackService.kt：${it.path}" } }
            .readText()
    }

    private val playbackModule by lazy {
        File(repoRoot, "app/src/main/kotlin/com/catclaw/music/kt/di/PlaybackModule.kt")
            .also { require(it.isFile) { "找不到 PlaybackModule.kt：${it.path}" } }
            .readText()
    }

    @Test
    fun `两颗按钮都画出来并且都认得自己的命令`() {
        assertTrue(
            "服务里没有 setCustomLayout：通知栏上根本不会出现这两颗按钮",
            service.contains("setCustomLayout("),
        )
        assertTrue(
            "按钮只画了一颗（CommandButton.Builder 少于 2 次）",
            Regex("CommandButton\\.Builder\\s*\\(\\)").findAll(service).count() >= 2,
        )
        // 命令字符串只在 companion object 里出现一次，这里认的是那份契约本身
        assertTrue("少了桌面歌词那条命令", service.contains("\"catclaw.custom.LYRICS\""))
        assertTrue("少了收藏那条命令", service.contains("\"catclaw.custom.FAVORITE\""))
    }

    @Test
    fun `两条命令都必须列进 onConnect 的可用集合`() {
        val connect = service.substringAfter("override fun onConnect", missingDelimiterValue = "")
            .substringBefore("override fun onCustomCommand")
        assertTrue("onConnect 里没有可用命令集合，按钮点了会被拒", connect.contains("setAvailableSessionCommands"))
        listOf("ACTION_LYRICS", "ACTION_FAVORITE").forEach { action ->
            assertTrue("onConnect 没放行 $action：通知栏点了没反应", connect.contains(action))
        }
        val handling = service.substringAfter("override fun onCustomCommand")
        listOf("ACTION_LYRICS", "ACTION_FAVORITE").forEach { action ->
            assertTrue("onCustomCommand 没处理 $action", handling.contains(action))
        }
    }

    @Test
    fun `通知栏图标由 app 注入而不是在 data 里复制一份`() {
        assertTrue("服务没有用到注入的图标", service.contains("NotificationIcons"))
        assertTrue("app 的 DI 没有提供 NotificationIcons", playbackModule.contains("NotificationIcons("))
        assertTrue(
            "图标要指向 app 自己的 drawable",
            playbackModule.contains("R.drawable.ic_notif_lyric_on") &&
                playbackModule.contains("R.drawable.ic_notif_lyric_off") &&
                // 通知栏用自己的 24dp 心形，不借界面的大图标（重量要和 media3 细线按钮一致）
                playbackModule.contains("R.drawable.ic_notif_favorite") &&
                playbackModule.contains("R.drawable.ic_notif_favorite_border"),
        )
    }
}
