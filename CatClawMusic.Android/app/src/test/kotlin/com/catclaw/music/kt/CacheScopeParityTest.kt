package com.catclaw.music.kt

import java.io.File
import org.junit.Assert.assertTrue
import org.junit.Test

/**
 * 源码闸门：设置页那个「已用多少」的数字，与「清除缓存」清掉的东西，必须是同一批。
 *
 * 为什么值得立这条：原来数字是「音乐缓存 + 下载目录」的和，漏掉了 media3 的播放缓存
 * （`cacheDir/player_cache`，真实占用能到 1GB+）—— 设置页显示「已用 0.0」，
 * 系统存储页却写着本应用缓存 1.07GB。这类不一致**不报错、不崩溃**，
 * 只是让用户以为设置坏了；而两处代码相隔很远，谁改谁都不会想起来另一边。
 */
class CacheScopeParityTest {

    private val viewModel by lazy {
        var dir: File? = File(".").absoluteFile
        while (dir != null && !File(dir, "app/src/main/kotlin").isDirectory) dir = dir.parentFile
        require(dir != null) { "找不到仓库根" }
        File(dir, "app/src/main/kotlin/com/catclaw/music/kt/ui/settings/SettingsViewModel.kt")
    }

    @Test
    fun `用量与清理覆盖同一批东西`() {
        val source = viewModel.readText()
        val usageLine = source.lineSequence()
            .firstOrNull { it.contains("cacheDirBytes() + downloader.directorySizeBytes()") }
        assertTrue(
            "找不到用量那行 —— 它应该同时算整个 cacheDir 与下载目录",
            usageLine != null &&
                usageLine.contains("cacheDirBytes()") &&
                usageLine.contains("downloader.directorySizeBytes()"),
        )
        val clearBody = source.substringAfter("fun clearCaches()", "").substringBefore("fun ")
        assertTrue(
            "清除缓存必须把播放缓存、其余缓存目录和下载目录都清掉，否则数字掉不下去",
            clearBody.contains("audioCache.clear()") &&
                clearBody.contains("RemoteAuthDataSources.clearCache()") &&
                clearBody.contains("clearOtherCacheDirs()") &&
                clearBody.contains("downloader.clearCache()"),
        )
        assertTrue(
            "清完必须重算那个数字",
            clearBody.contains("cacheDirBytes()") && clearBody.contains("downloader.directorySizeBytes()"),
        )
    }
}
