package com.catclaw.music.kt

import java.io.File
import org.junit.Assert.assertTrue
import org.junit.Test

/**
 * 源码闸门：设置页那个「已用多少」的数字，与「清除缓存」清掉的东西，必须是同一批。
 *
 * 为什么值得立这条：原来数字是「音频缓存 + 下载目录」的和，而清理只清音频缓存 ——
 * 用户看到「已用 300MB」点一下只掉几十 MB（下载目录占大头时几乎不动），看着像没生效。
 * 这类不一致**不报错、不崩溃**，只是让用户以为按钮坏了；而两个数字分处两段代码，
 * 谁改谁都不会想起来另一边。
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
        val usageLine = source.lineSequence().firstOrNull { it.contains("+= download") || it.contains("+ downloader.directorySizeBytes()") }
        assertTrue(
            "找不到用量那行 —— 它应该同时算 audioCache 与 downloader",
            usageLine != null && usageLine.contains("audioCache.totalBytes()") && usageLine.contains("downloader.directorySizeBytes()"),
        )
        val clearBody = source.substringAfter("fun clearCaches()", "").substringBefore("fun ")
        assertTrue(
            "清除缓存必须也清下载目录，否则数字掉不下去",
            clearBody.contains("audioCache.clear()") && clearBody.contains("downloader.clearCache()"),
        )
        assertTrue(
            "清完必须重算那个数字",
            clearBody.contains("audioCache.totalBytes()") && clearBody.contains("downloader.directorySizeBytes()"),
        )
    }
}
