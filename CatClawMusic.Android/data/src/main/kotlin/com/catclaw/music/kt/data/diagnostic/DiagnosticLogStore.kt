package com.catclaw.music.kt.data.diagnostic

import android.content.Context
import com.catclaw.music.kt.core.diagnostic.DiagnosticLogRules
import com.catclaw.music.kt.core.diagnostic.DiagnosticSink
import com.catclaw.music.kt.core.diagnostic.LogEntry
import com.catclaw.music.kt.core.diagnostic.LogLevel
import dagger.hilt.android.qualifiers.ApplicationContext
import java.io.File
import javax.inject.Inject
import javax.inject.Singleton
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.withContext

/**
 * 诊断日志的落盘端 —— 对应 C# 的 `LogService`（写 `debug.log`，由设置页开关控制，默认关）。
 *
 * 与 C# 的一处有意偏离：C# 写的是**共享存储根目录** `/sdcard/CatClawMusic/debug.log`，
 * 那在 Android 11+ 需要 `MANAGE_EXTERNAL_STORAGE`。本端写到应用专属的外部目录
 * （`Android/data/<包名>/files/diagnostic/debug.log`），系统文件管理器照样能看到，
 * 但不用向用户要一个"能读全部文件"的权限。
 */
@Singleton
class DiagnosticLogStore @Inject constructor(
    @ApplicationContext context: Context,
) : DiagnosticSink {

    private val prefs = context.getSharedPreferences(PREFS, Context.MODE_PRIVATE)
    private val log = RollingLogFile(
        File(context.getExternalFilesDir(null) ?: context.filesDir, "diagnostic/debug.log"),
    )

    override var enabled: Boolean = prefs.getBoolean(KEY_ENABLED, false)
        set(value) {
            field = value
            prefs.edit().putBoolean(KEY_ENABLED, value).apply()
        }

    /** 门面那边已经脱敏了，这里只负责格式化与写 */
    override fun write(level: LogLevel, timestamp: String, tag: String, message: String) {
        log.append(DiagnosticLogRules.format(timestamp, level, tag, message))
    }

    suspend fun entries(): List<LogEntry> = withContext(Dispatchers.IO) {
        log.readLines().mapNotNull { DiagnosticLogRules.parse(it) }
    }

    suspend fun clear() = withContext(Dispatchers.IO) { log.clear() }

    fun path(): String = log.path()

    private companion object {
        const val PREFS = "catclaw_state"
        const val KEY_ENABLED = "diagnostic_enabled"
    }
}
