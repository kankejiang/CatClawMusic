package com.catclaw.music.kt.data.remote

import java.io.File
import org.junit.Assert.assertTrue
import org.junit.Test

/**
 * `Songs.CoverArtPath` 对远程曲目的**契约**：存的是可直接取的 URL，不是远端路径。
 *
 * 为什么值得单独立一道闸门：这个字段的消费者是 `CoverTile`，它只认「http(s) 直链」或「本地文件」。
 * WebDAV 导入器曾经往这里写 `file.path`（音频自己的远端路径），于是那一类曲目的封面**永远是首字母** ——
 * 不报错、不崩溃，只是安静地少一样东西；而另外两个导入器（Subsonic / 猫爪服务器）写的是 URL。
 * 三个导入器里有一个不一样，靠人眼 diff 很难发现，但外形是可判定的：赋值右边必须是造 URL 的调用。
 */
class CoverColumnContractTest {

    private val repoRoot by lazy {
        var dir: File? = File(".").absoluteFile
        while (dir != null && !File(dir, "data/src/main/kotlin").isDirectory) dir = dir.parentFile
        require(dir != null) { "从 ${File(".").absolutePath} 往上找不到仓库根" }
        dir
    }

    /** 三个导入器：文件 → 它给封面列造 URL 用的那个调用 */
    private val importers = linkedMapOf(
        "remote/WebDavImporter.kt" to "streamUrlFor",
        "remote/SubsonicImporter.kt" to "coverArtUrl",
        "remote/CatClawServerImporter.kt" to "assetUrl",
    )

    @Test
    fun `封面列在三个导入器里都存 URL`() {
        val bad = StringBuilder()
        importers.forEach { (rel, urlCall) ->
            val file = File(repoRoot, "data/src/main/kotlin/com/catclaw/music/kt/data/$rel")
            require(file.isFile) { "找不到 $rel" }
            // 赋值可能跨行（`coverArtPath = (...)` 换行后接 `?.let { streamUrlFor(it) }`），
            // 所以按「逻辑语句」取：从赋值那行起，一直拼到出现行尾逗号为止
            val lines = file.readLines()
            val assignments = mutableListOf<String>()
            var i = 0
            while (i < lines.size) {
                if (lines[i].trimStart().startsWith("coverArtPath =")) {
                    val sb = StringBuilder(lines[i])
                    var j = i
                    while (!lines[j].trimEnd().endsWith(",") && j + 1 < lines.size) {
                        j++
                        sb.append(' ').append(lines[j].trim())
                    }
                    assignments += sb.toString()
                    i = j
                }
                i++
            }
            if (assignments.isEmpty()) bad.append("$rel 里没有 coverArtPath 赋值，闸门失去意义\n")
            assignments.forEach { line ->
                if (!line.contains(urlCall)) {
                    bad.append("$rel: ${line.trim()} —— 右边不是 $urlCall，存路径的话封面不会显示\n")
                }
            }
        }
        assertTrue("远程曲目的封面列必须存 URL：\n$bad", bad.isEmpty())
    }
}
