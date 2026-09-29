package com.catclaw.music.kt.core.download

/**
 * 把远端给的任意名字洗成一个**安全的单段文件名**。
 *
 * 这是下载目录唯一的路径守卫 —— 音乐源与 AI 工具回传的文件名完全不可信，所以规则要一次到位：
 * 1. 分隔符 `/` `\` 与 NUL、控制字符一律换成 `_`：留着就是目录穿越或非法文件名；
 * 2. **开头所有的点**都去掉：旧写法是 `removePrefix(".")`，只去一个 —— `".."` 会留下 `"."`，
 *    而 `File(dir, ".")` 就是下载目录本身、`File(dir, "..")` 直接跑到上一级；
 * 3. 单段名字截到 120 个字符：ext4 一段上限 255 **字节**，中文标题按 UTF-8 算三个字节一个，
 *    不截会在写文件时才炸。
 *
 * 注：`MusicDownloader.fileNameFor()` 走的是另一条规则（名字前面拼了 `song.id`），
 * 因为以数字开头，天然不会撞上"以点开头"这一类，不共用这个函数。
 */
object FileNames {
    const val FALLBACK = "download.bin"

    /** 留足余量：`.part` 这些后缀还要往同一行名字上加 */
    const val MAX_LENGTH = 120

    private val controlChars = Regex("[\\x00-\\x1F]")

    fun sanitize(raw: String?): String {
        val replaced = (raw ?: "")
            .replace('\\', '_')
            .replace('/', '_')
            .replace(controlChars, "_")
            .trimStart('.')
            .trim()
        if (replaced.isBlank() || replaced == "." || replaced == "..") return FALLBACK
        return replaced.take(MAX_LENGTH).ifBlank { FALLBACK }
    }
}
