package com.catclaw.music.kt.data.diagnostic

import java.io.File

/**
 * 滚动日志文件：追加、读全量、超阈值后只留最后若干行。
 *
 * 单独成类（而不是写在 store 里）是因为它值得用真文件在 JVM 上测：
 * "裁剪裁错方向"这种 bug 只在日志已经很长、而且正好出事那天才看得见 ——
 * 留最新的尾巴是这套东西唯一还有用的前提。
 *
 * 裁剪不是每条都做：每条都读一遍全文件会让写日志比它要诊断的业务还重，
 * 所以每 [trimEvery] 条才检查一次，阈值给得比保留量高一倍，检查间隔内撑不爆。
 */
class RollingLogFile(
    private val file: File,
    private val keepLines: Int = DEFAULT_KEEP_LINES,
    private val trimThreshold: Int = DEFAULT_TRIM_THRESHOLD,
    private val trimEvery: Int = DEFAULT_TRIM_EVERY,
) {

    private var appends = 0

    fun append(line: String) {
        if (line.isEmpty()) return
        runCatching {
            file.parentFile?.mkdirs()
            file.appendText(line + "\n")
            appends += 1
            if (trimEvery > 0 && appends % trimEvery == 0) {
                val lines = readLines()
                if (lines.size > trimThreshold) {
                    file.writeText(lines.takeLast(keepLines).joinToString("\n") + "\n")
                }
            }
        }
    }

    fun readLines(): List<String> = runCatching {
        if (!file.isFile) return emptyList()
        file.readLines().filter { it.isNotBlank() }
    }.getOrDefault(emptyList())

    fun clear() {
        appends = 0
        runCatching { file.delete() }
    }

    fun path(): String = file.absolutePath

    companion object {
        const val DEFAULT_KEEP_LINES = 2_000
        const val DEFAULT_TRIM_THRESHOLD = 4_000
        const val DEFAULT_TRIM_EVERY = 200
    }
}
