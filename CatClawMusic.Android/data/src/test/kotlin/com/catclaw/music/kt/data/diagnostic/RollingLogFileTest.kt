package com.catclaw.music.kt.data.diagnostic

import java.io.File
import org.junit.Assert.assertEquals
import org.junit.Assert.assertTrue
import org.junit.Test

/**
 * 滚动日志文件用真文件在 JVM 上跑。
 *
 * 钉的是那件"出事那天才需要、平时看不出来"的事：**裁剪必须留最新的尾巴**。
 * 留反了的话，用户来反馈问题时文件里只剩开机头几行，等于这套东西白做。
 */
class RollingLogFileTest {

    private fun temp(name: String) = File.createTempFile(name, ".log").apply { deleteOnExit() }

    @Test
    fun appendsInOrderAndReadsBack() {
        val log = RollingLogFile(temp("append"))
        log.append("11:11:11.111\t[I][A] 第一条")
        log.append("11:11:12.111\t[W][B] 第二条")
        assertEquals(
            listOf("11:11:11.111\t[I][A] 第一条", "11:11:12.111\t[W][B] 第二条"),
            log.readLines(),
        )
    }

    @Test
    fun emptyLinesAreNotWritten() {
        val log = RollingLogFile(temp("empty"))
        log.append("")
        assertEquals(emptyList<String>(), log.readLines())
    }

    @Test
    fun trimKeepsTheNewestTail() {
        // keep=3、阈值=5、每 1 条就检查：写到第 6 条时该把最旧的三条剪掉
        val log = RollingLogFile(temp("trim"), keepLines = 3, trimThreshold = 5, trimEvery = 1)
        (1..6).forEach { log.append("line$it") }
        assertEquals("留下的必须是最新的 3 条", listOf("line4", "line5", "line6"), log.readLines())
    }

    @Test
    fun trimCheckIsBatchedNotPerLine() {
        val log = RollingLogFile(temp("batch"), keepLines = 2, trimThreshold = 4, trimEvery = 100)
        (1..10).forEach { log.append("line$it") }
        assertEquals("没到检查点就不该动文件", 10, log.readLines().size)
    }

    @Test
    fun clearEmptiesTheFile() {
        val log = RollingLogFile(temp("clear"))
        log.append("x")
        assertTrue(log.readLines().isNotEmpty())
        log.clear()
        assertEquals(emptyList<String>(), log.readLines())
    }
}
