package com.catclaw.music.kt.data.diagnostic

import java.io.File
import java.text.SimpleDateFormat
import java.util.Date
import java.util.Locale
import java.util.zip.ZipEntry
import java.util.zip.ZipOutputStream

/**
 * 诊断包：`debug.log` + `device.txt` 打成一个 zip。
 *
 * 对应 C# `LogViewModel` 的导出——它也是"整份日志 + 一份设备信息"两个文件打包，
 * 因为只发日志过去，对方第一个问题一定是"你什么版本、报了几次"。
 *
 * 做成不吃 Context 的类，是为了能在 JVM 上用真文件测：zip 里到底有没有那两个条目、
 * 内容对不对，只有解开看一次才知道。
 */
class DiagnosticPackage(
    private val logFile: File,
    private val cacheDir: File,
) {

    /** 返回生成的 zip；日志文件还不存在时也会出一份只含 device.txt 的包（空日志同样是要交付的事实） */
    fun build(header: String, stamp: String = STAMP.format(Date())): File {
        cacheDir.mkdirs()
        val target = File(cacheDir, "catclaw_diag_$stamp.zip")
        ZipOutputStream(target.outputStream().buffered()).use { zip ->
            zip.putNextEntry(ZipEntry("device.txt"))
            zip.write(header.toByteArray())
            zip.closeEntry()
            if (logFile.isFile) {
                zip.putNextEntry(ZipEntry("debug.log"))
                zip.write(logFile.readBytes())
                zip.closeEntry()
            }
        }
        return target
    }

    companion object {
        /** 与 C# 同一时间戳形状：`catclaw_diag_20260929_085203.zip` */
        val STAMP = SimpleDateFormat("yyyyMMdd_HHmmss", Locale.US)
    }
}
