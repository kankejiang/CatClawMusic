package com.catclaw.music.kt.data.diagnostic

import java.io.File
import java.util.zip.ZipFile
import org.junit.Assert.assertEquals
import org.junit.Assert.assertTrue
import org.junit.Test

/**
 * 诊断包用真文件 + 真 zip 解开来看。
 *
 * 这份产物是"发给别人帮忙排障"的东西：里面少了设备信息，对方第一个问题就是"你什么版本"；
 * 少了日志，包就完全没有意义。这两条只有解开 zip 才知道。
 */
class DiagnosticPackageTest {

    private fun dir(name: String) = File(System.getProperty("java.io.tmpdir"), "diag-$name-${System.nanoTime()}")

    private fun entriesOf(zip: File): Map<String, String> = ZipFile(zip).use { file ->
        file.entries().toList().associate { entry ->
            entry.name to file.getInputStream(entry).bufferedReader().readText()
        }
    }

    @Test
    fun packageCarriesBothFiles() {
        val logDir = dir("log").apply { mkdirs() }
        val logFile = File(logDir, "debug.log").apply { writeText("08:00:00.000\t[I][A] 第一条\n") }
        val zip = DiagnosticPackage(logFile = logFile, cacheDir = dir("cache"))
            .build(header = "应用版本: 0.1.0 (build 1)", stamp = "20260929_080000")

        assertTrue("zip 该落在缓存目录里", zip.name == "catclaw_diag_20260929_080000.zip")
        val parts = entriesOf(zip)
        assertEquals(setOf("device.txt", "debug.log"), parts.keys)
        assertEquals("应用版本: 0.1.0 (build 1)", parts["device.txt"])
        assertTrue(parts.getValue("debug.log").startsWith("08:00:00.000"))
    }

    @Test
    fun emptyLogStillProducesAUsablePackage() {
        // 日志文件还没生成（用户刚开开关就导出）时不该抛，也不该塞一个空的 debug.log 进去
        val zip = DiagnosticPackage(
            logFile = File(dir("missing"), "debug.log"),
            cacheDir = dir("cache2"),
        ).build(header = "x", stamp = "s")
        assertEquals(setOf("device.txt"), entriesOf(zip).keys)
    }
}
