package com.catclaw.music.kt.core.diagnostic

/**
 * 诊断包里的设备信息头 —— 对应 C# `LogViewModel` 导出时附的 `device.txt`。
 *
 * 单独做成纯函数是因为这份文本是"别人帮你排障时读的第一段"：
 * 少了版本/条数/错误数，对方只能问"你这是什么版本、报了几次错"，一轮来回就过去了。
 */
object DiagnosticReport {

    /** 一条日志的统计口径：总数 + 按级别的数量 */
    fun summary(entries: List<LogEntry>): String {
        val errors = entries.count { it.level == LogLevel.Error }
        val warns = entries.count { it.level == LogLevel.Warn }
        return "日志条数: ${entries.size} (错误 $errors, 警告 $warns)"
    }

    fun header(
        appVersion: String,
        buildNumber: String,
        deviceModel: String,
        osVersion: String,
        firstInstall: String,
        entries: List<LogEntry>,
        exportedAt: String,
    ): String = buildString {
        appendLine("应用版本: $appVersion (build $buildNumber)")
        appendLine("设备型号: $deviceModel")
        appendLine("系统版本: $osVersion")
        appendLine("首次安装: $firstInstall")
        appendLine(summary(entries))
        appendLine("导出时间: $exportedAt")
    }.trimEnd()
}
