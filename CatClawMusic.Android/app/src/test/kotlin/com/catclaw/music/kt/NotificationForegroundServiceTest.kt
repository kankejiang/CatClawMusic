package com.catclaw.music.kt

import java.io.File
import org.junit.Assert.assertTrue
import org.junit.Test

/**
 * 通知栏播放控件的两条外形判据。
 *
 * 2026-10-20 用户要求「移植通知栏播放控件」并指出「应该直接用系统媒体通知」。
 * 实查下来的真相是：会话、渠道、两颗自定义按钮全都在，**通知就是不出现**。
 * 两个坑各自都能让通知消失、且任何行为测试都抓不到：
 *
 * 1. 服务用 `startForegroundService` 启动 —— 但这一步发生在装载媒体**之前**，
 *    播放器还空着，media3 不会调 `startForeground`，5 秒后抛
 *    `ForegroundServiceDidNotStartInTimeException` 把整个进程打崩（真机崩溃栈实录）；
 * 2. 只靠 media3 自己发通知 —— 在「`startService` + in-process 播放器」这种用法下它不发，
 *    播放中 `dumpsys notification` 里没有本应用任何 NotificationRecord。
 *
 * 所以判据是：启动用普通 `startService`；通知由服务在**播放状态变化**时通过 media3 的公开钩子
 * `onUpdateNotification(...)` 自己挂（样式仍是系统媒体通知）。
 */
class NotificationForegroundServiceTest {

    private val repoRoot by lazy {
        var dir: File? = File(".").absoluteFile
        while (dir != null && !File(dir, "data/src/main/kotlin").isDirectory) dir = dir.parentFile
        require(dir != null) { "从 ${File(".").absolutePath} 往上找不到仓库根" }
        dir
    }

    private val starter by lazy {
        File(repoRoot, "data/src/main/kotlin/com/catclaw/music/kt/data/service/PlaybackSessionStarter.kt").readText()
    }

    private val service by lazy {
        File(repoRoot, "data/src/main/kotlin/com/catclaw/music/kt/data/service/MediaPlaybackService.kt").readText()
    }

    @Test
    fun `启动器不能用前台服务方式启动`() {
        // 只认**调用**（带括号）：注释里会写「不能用 startForegroundService」来解释原因，
        // 直接 contains 会把说明文字当成违规
        assertTrue(
            "启动器退回了 startForegroundService（空播放器时会在 5 秒后崩掉进程）",
            !Regex("startForegroundService\\s*\\(").containsMatchIn(starter),
        )
        assertTrue("启动器没有用 startService", starter.contains(".startService("))
    }

    @Test
    fun `通知由播放状态驱动 用 media3 的公开钩子挂出`() {
        assertTrue("没有调用 onUpdateNotification：系统媒体通知不会出现", service.contains(".onUpdateNotification("))
        assertTrue("provider 没有设置", service.contains("setMediaNotificationProvider(notificationProvider)"))
        // 空播放器时挂通知会把服务崩掉，必须先判媒体数
        val updater = service.substringAfter("private fun updateMediaNotification()").substringBefore("private fun launchPendingIntent")
        assertTrue("updateMediaNotification 没判 mediaItemCount，空队列时会崩", updater.contains("mediaItemCount == 0"))
        assertTrue("播放状态没有接到通知更新上", service.contains("controller.state.collect"))
    }

    @Test
    fun `服务声明了 mediaPlayback 前台类型`() {
        val manifest = File(repoRoot, "data/src/main/AndroidManifest.xml").readText()
        assertTrue("清单没声明 MediaPlaybackService", manifest.contains("MediaPlaybackService"))
        assertTrue("少了 foregroundServiceType=mediaPlayback", manifest.contains("foregroundServiceType=\"mediaPlayback\""))
    }
}
