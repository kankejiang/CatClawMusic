package com.catclaw.music.kt.core.diagnostic

/**
 * 崩溃报告的文本格式 —— 对应 C# `App.xaml.cs:60-71` 那份 `crash.log`。
 *
 * C# 只写了「时间 + 异常」两行。本端多带一段**崩溃前的日志轨迹**（`AppLog` 的内存环形缓冲），
 * 因为只给一个堆栈，用户报过来的东西仍然定位不了「他刚才在干什么」——
 * 而磁盘上的诊断日志默认是关的，崩溃前的那几行只有内存里还有。
 *
 * 抽成纯函数是为了可测：这份文本用户会直接贴进聊天窗口，格式坏掉（比如空日志时段落头还在）
 * 比崩溃本身更让人怀疑人生。
 */
object CrashReportRules {

    /** 报告里最多带多少行日志：一次崩溃的上下文不需要整份日志，太长了用户也不会看完 */
    const val MAX_LOG_LINES = 60

    const val LOG_SECTION_HEAD = "--- 崩溃前日志 ---"

    fun format(
        timestamp: String,
        throwableText: String,
        recentLines: List<String>,
        maxLines: Int = MAX_LOG_LINES,
    ): String = buildString {
        append('[').append(timestamp).append("] UnhandledException\n")
        append(throwableText.trimEnd()).append('\n')
        // 没有轨迹就整段不画：留一个空段头会让人以为日志丢了
        val tail = if (recentLines.size > maxLines) recentLines.takeLast(maxLines) else recentLines
        if (tail.isNotEmpty()) {
            append('\n').append(LOG_SECTION_HEAD).append('\n')
            tail.forEach { append(it.trimEnd()).append('\n') }
        }
    }
}
