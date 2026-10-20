package com.catclaw.music.kt

import java.io.File
import org.junit.Assert.assertTrue
import org.junit.Test

/**
 * 通知栏两颗自定义按钮的**形态**闸门。
 *
 * 2026-10-20 用户反馈「通知栏按钮图标没有变化，桌面歌词和收藏按钮只有一种形态」。
 * 两个原因叠在一起，都是「编译过、行为测试抓不到」的那种：
 *
 * 1. 桌面歌词图标**只注入了一张 on**（连 `ic_notif_lyric_off` 资源都不存在），
 *    按钮怎么切都是同一个样子 —— SystemUI 会给通知图标统一染色，形态差异只能靠形状表达；
 * 2. `setCustomLayout(...)` 只改会话侧的命令集合，系统通知是生成器产出的**快照**，
 *    不重建通知的话图标/文案永远停在上一次（所以「点了收藏心还是空心」）。
 */
class NotificationButtonVariantsTest {

    private val repoRoot by lazy {
        var dir: File? = File(".").absoluteFile
        while (dir != null && !File(dir, "data/src/main/kotlin").isDirectory) dir = dir.parentFile
        require(dir != null) { "从 ${File(".").absolutePath} 往上找不到仓库根" }
        dir
    }

    private val service by lazy {
        File(repoRoot, "data/src/main/kotlin/com/catclaw/music/kt/data/service/MediaPlaybackService.kt").readText()
    }

    private val playbackModule by lazy {
        File(repoRoot, "app/src/main/kotlin/com/catclaw/music/kt/di/PlaybackModule.kt").readText()
    }

    @Test
    fun `桌面歌词有开关两张图标且都注入了`() {
        assertTrue("缺少空心图标资源 ic_notif_lyric_off.xml", File(repoRoot, "app/src/main/res/drawable/ic_notif_lyric_off.xml").isFile)
        assertTrue("DI 没注入 desktopLyricOn", playbackModule.contains("desktopLyricOn = R.drawable.ic_notif_lyric_on"))
        assertTrue("DI 没注入 desktopLyricOff", playbackModule.contains("desktopLyricOff = R.drawable.ic_notif_lyric_off"))
        assertTrue("按钮没用状态选图标", service.contains("if (lyricsOn) icons.desktopLyricOn else icons.desktopLyricOff"))
    }

    @Test
    fun `按钮重摆之后必须重建通知`() {
        val refresh = service.substringAfter("private fun refreshNotificationButtons()").substringBefore("/**", missingDelimiterValue = service.substringAfter("private fun refreshNotificationButtons()"))
        assertTrue("refreshNotificationButtons 里没有 updateMediaNotification：图标不会变", refresh.contains("updateMediaNotification()"))
    }

    @Test
    fun `收藏变化会驱动按钮重摆`() {
        assertTrue("没有监听收藏变化", service.contains("observeFavorites()"))
        val favoriteToggle = service.substringAfter("private fun toggleFavoriteFromNotification()")
        assertTrue("通知栏点收藏后没有重摆按钮", favoriteToggle.contains("refreshNotificationButtons()"))
    }
}
