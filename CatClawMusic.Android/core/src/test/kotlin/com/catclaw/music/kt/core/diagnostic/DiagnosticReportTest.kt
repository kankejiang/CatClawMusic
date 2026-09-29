package com.catclaw.music.kt.core.diagnostic

import org.junit.Assert.assertEquals
import org.junit.Assert.assertTrue
import org.junit.Test

/** 导出包里的设备信息头。它决定对方要不要再回来问你一轮"什么版本、几次错"。 */
class DiagnosticReportTest {

    private fun entry(level: LogLevel) = LogEntry("08:00:00.000", level, "Player", "x")

    @Test
    fun summaryCountsErrorsAndWarnsSeparately() {
        val entries = listOf(
            entry(LogLevel.Error), entry(LogLevel.Error), entry(LogLevel.Warn), entry(LogLevel.Info), entry(LogLevel.Debug),
        )
        assertEquals("日志条数: 5 (错误 2, 警告 1)", DiagnosticReport.summary(entries))
        assertEquals("日志条数: 0 (错误 0, 警告 0)", DiagnosticReport.summary(emptyList()))
    }

    @Test
    fun headerCarriesEveryFactAndNoTrailingBlank() {
        val text = DiagnosticReport.header(
            appVersion = "0.1.0",
            buildNumber = "1",
            deviceModel = "Xiaomi 2211133C",
            osVersion = "Android 15 (API 35)",
            firstInstall = "2026-09-27 01:57:03",
            entries = listOf(entry(LogLevel.Info), entry(LogLevel.Error)),
            exportedAt = "2026-09-29 08:52:03",
        )
        listOf(
            "应用版本: 0.1.0 (build 1)",
            "设备型号: Xiaomi 2211133C",
            "系统版本: Android 15 (API 35)",
            "首次安装: 2026-09-27 01:57:03",
            "日志条数: 2 (错误 1, 警告 0)",
            "导出时间: 2026-09-29 08:52:03",
        ).forEach { line ->
            assertTrue("少了这行对方就要再问一次：$line", text.lineSequence().any { it == line })
        }
        assertEquals("末尾不该留空行", text, text.trimEnd())
    }
}
