package com.catclaw.music.kt.core.diagnostic

import org.junit.After
import org.junit.Assert.assertEquals
import org.junit.Assert.assertTrue
import org.junit.Test

/**
 * 日志门面的三条不变量：开关决定要不要落盘、脱敏在出门面前就做完、
 * 没有 sink 时调用不能炸（`:core` 会在 `:data` 起来之前被用到）。
 */
class AppLogTest {

    private class Recording : DiagnosticSink {
        override var enabled: Boolean = true
        val lines = mutableListOf<Triple<LogLevel, String, String>>()
        override fun write(level: LogLevel, timestamp: String, tag: String, message: String) {
            lines += Triple(level, tag, message)
        }
    }

    @After
    fun tearDown() {
        AppLog.attach(null)
    }

    @Test
    fun writesGoToTheSinkWithLevelAndTag() {
        val sink = Recording()
        AppLog.attach(sink)
        AppLog.clock = { "00:00:01.000" }
        AppLog.i("Backup", "开始")
        AppLog.e("Backup", "失败", IllegalStateException("zip 坏了"))
        assertEquals(listOf(LogLevel.Info to "Backup", LogLevel.Error to "Backup"), sink.lines.map { it.first to it.second })
        assertEquals("失败: IllegalStateException zip 坏了", sink.lines[1].third)
    }

    @Test
    fun disabledSinkReceivesNothing() {
        val sink = Recording().apply { enabled = false }
        AppLog.attach(sink)
        AppLog.w("Player", "缓冲")
        assertTrue("开关关着还落盘，就是把用户的听歌记录一直写在磁盘上", sink.lines.isEmpty())
    }

    @Test
    fun messageIsRedactedBeforeItLeavesTheFacade() {
        val sink = Recording()
        AppLog.attach(sink)
        AppLog.i("WebDAV", "GET https://admin:S3cr3t@dav.example.com/r")
        val written = sink.lines.single().third
        assertTrue("脱敏没做：$written", !written.contains("S3cr3t") && written.contains("://***@"))
    }

    @Test
    fun noSinkIsNotAnError() {
        AppLog.attach(null)
        AppLog.d("Any", "没人收也照样跑")
    }
}
