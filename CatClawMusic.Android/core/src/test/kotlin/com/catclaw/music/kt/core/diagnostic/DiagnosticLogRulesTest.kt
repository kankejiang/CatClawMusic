package com.catclaw.music.kt.core.diagnostic

import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertNull
import org.junit.Test

/**
 * 诊断日志的纯规则。
 *
 * 挑这几条来钉是因为它们**错了都不报错**：格式对不上，查看页就是一整页空白，
 * 看起来像功能坏了；脱敏漏一条，用户把日志发给别人时就把音源服务器的密码一起发了出去。
 */
class DiagnosticLogRulesTest {

    @Test
    fun formatAndParseRoundTrip() {
        val line = DiagnosticLogRules.format("12:34:56.789", LogLevel.Warn, "CatClawPlayback", "缓冲超时")
        assertEquals("12:34:56.789\t[W][CatClawPlayback] 缓冲超时", line)
        assertEquals(
            LogEntry("12:34:56.789", LogLevel.Warn, "CatClawPlayback", "缓冲超时"),
            DiagnosticLogRules.parse(line),
        )
    }

    @Test
    fun parseToleratesTabsFoldedIntoSpaces() {
        // 日志文件常被复制进聊天工具/邮件，制表符在那中间会被折成空格
        val entry = DiagnosticLogRules.parse("01:02:03.4  [I][Backup] 完成 12 首")
        assertEquals("01:02:03.4", entry?.timestamp)
        assertEquals(LogLevel.Info, entry?.level)
        assertEquals("Backup", entry?.tag)
        assertEquals("完成 12 首", entry?.message)
    }

    @Test
    fun parseRejectsJunkInsteadOfThrowing() {
        assertNull(DiagnosticLogRules.parse("完全不像日志的一行"))
        assertNull(DiagnosticLogRules.parse("12:34:56.789\t[Z][tag] 未知级别"))
    }

    @Test
    fun credentialsNeverSurviveRedaction() {
        val cases = listOf(
            "连接 subsonic://admin:S3cr3tPw@music.example.com:443/rest" to "subsonic://***@music.example.com",
            "请求头 Authorization: Bearer eyJhbGciOiJIUzI1NiJ9" to "Authorization: ***",
            """{"api_key":"sk-live-abcdef123456"}""" to "api_key=***",
            "llm token = topsecretvalue 已保存" to "token=***",
            "password: hunter2!" to "password=***",
        )
        cases.forEach { (input, _) ->
            val out = DiagnosticLogRules.redact(input)
            listOf("S3cr3tPw", "eyJhbGciOiJIUzI1NiJ9", "abcdef123456", "topsecretvalue", "hunter2")
                .forEach { secret ->
                    assertFalse("脱敏后不该还剩「$secret」：$out", out.contains(secret))
                }
        }
    }

    @Test
    fun redactionKeepsTheKeyNamesSoTheLogIsStillUseful() {
        assertEquals("Authorization:*** x", DiagnosticLogRules.redact("Authorization:Bearer abc123 x"))
        assertEquals("api_key=***", DiagnosticLogRules.redact("api_key=abcdef12345"))
        // 没有凭据形状的文字不该被动
        assertEquals("扫描完成 616 首", DiagnosticLogRules.redact("扫描完成 616 首"))
    }

    @Test
    fun filtersCombineAndAreCaseInsensitiveOnQuery() {
        val entries = listOf(
            LogEntry("01:00:00.000", LogLevel.Info, "Backup", "开始"),
            LogEntry("01:00:01.000", LogLevel.Error, "Backup", "ZIP 损坏"),
            LogEntry("01:00:02.000", LogLevel.Error, "Player", "解码失败"),
        )
        assertEquals(2, DiagnosticLogRules.filter(entries, level = LogLevel.Error).size)
        assertEquals(1, DiagnosticLogRules.filter(entries, tag = "Player").size)
        assertEquals(1, DiagnosticLogRules.filter(entries, query = "zip").size)
        assertEquals(
            "三个条件是且关系", 1,
            DiagnosticLogRules.filter(entries, level = LogLevel.Error, tag = "Backup", query = "损坏").size,
        )
    }

    @Test
    fun trimKeepsTheNewestTail() {
        val lines = (1..10).map { "l$it" }
        assertEquals(listOf("l9", "l10"), DiagnosticLogRules.trim(lines, 2))
        assertEquals(lines, DiagnosticLogRules.trim(lines, 10))
        assertEquals(lines, DiagnosticLogRules.trim(lines, 50))
        assertEquals("maxLines<=0 时不该把日志清空", lines, DiagnosticLogRules.trim(lines, 0))
    }

    @Test
    fun tagCandidatesAreOrderedByFrequencyThenByName() {
        val entries = listOf(
            LogEntry("t", LogLevel.Info, "Player", "a"),
            LogEntry("t", LogLevel.Info, "Player", "b"),
            LogEntry("t", LogLevel.Info, "Backup", "c"),
            LogEntry("t", LogLevel.Info, "Ai", "d"),
        )
        assertEquals(listOf("Player", "Ai", "Backup"), DiagnosticLogRules.tagsOf(entries))
    }
}
