package com.catclaw.music.kt.core.diagnostic

import java.text.SimpleDateFormat
import java.util.Date
import java.util.Locale
import android.util.Log as SystemLog

/** 谁在收日志。`:data` 侧的实现负责落盘，`:core` 只管发。 */
interface DiagnosticSink {
    /** 开关由设置页控制，默认关 —— 所以它得可写 */
    var enabled: Boolean
    fun write(level: LogLevel, timestamp: String, tag: String, message: String)
}

/**
 * 全局日志门面 —— 对应 C# 的 `CatClawMusic.Core.Interfaces.Log`。
 *
 * 两处刻意的设计：
 * 1. **logcat 永远打**，文件是附加通道。logcat 是开发者不用授权就能看的通道，
 *    若改成"只在诊断开启时才有输出"会让日常调试变差；
 * 2. 没注册 sink 时只是一次空判断，不是异常 —— `:core` 不该因为 `:data` 还没起来就崩。
 */
object AppLog {

    @Volatile
    private var sink: DiagnosticSink? = null

    /** 由 DI 在应用启动时注册一次；传 null 用于测试里摘掉 */
    fun attach(target: DiagnosticSink?) {
        sink = target
    }

    fun d(tag: String, message: String) {
        SystemLog.d(tag, message)
        emit(LogLevel.Debug, tag, message)
    }

    fun i(tag: String, message: String) {
        SystemLog.i(tag, message)
        emit(LogLevel.Info, tag, message)
    }

    fun w(tag: String, message: String, error: Throwable? = null) {
        if (error == null) SystemLog.w(tag, message) else SystemLog.w(tag, message, error)
        emit(LogLevel.Warn, tag, withError(message, error))
    }

    fun e(tag: String, message: String, error: Throwable? = null) {
        if (error == null) SystemLog.e(tag, message) else SystemLog.e(tag, message, error)
        emit(LogLevel.Error, tag, withError(message, error))
    }

    private fun withError(message: String, error: Throwable?): String =
        if (error == null) message else "$message: ${error.javaClass.simpleName} ${error.message}"

    /**
     * 最近若干条日志的内存环形缓冲 —— 对应 C# 的「崩溃前日志轨迹」。
     *
     * **不受落盘开关影响**：落盘默认是关的，而崩溃恰恰最需要「崩溃前那几行」；
     * 只占内存、不写盘，所以可以一直开着。崩溃报告由 [CrashReportRules] 取它。
     */
    private val ring = ArrayDeque<String>()

    /** 缓冲上限。200 行足够覆盖一次崩溃前的上下文，又不至于让每次日志都复制一大段 */
    const val RING_CAPACITY = 200

    /** 取缓冲里的最近 [limit] 条（旧→新）。崩溃处理器可能在任意线程调用，所以这里加锁取快照 */
    fun recentLines(limit: Int = RING_CAPACITY): List<String> = synchronized(ring) {
        if (limit >= ring.size) ring.toList() else ring.toList().takeLast(limit)
    }

    private fun emit(level: LogLevel, tag: String, message: String) {
        // 时间戳在门面这里生成：一条日志的时间是"它被写出的那一刻"，
        // 落到哪个 sink、被谁格式化都不该改变它
        val stamp = runCatching { clock() }.getOrDefault("")
        val safe = runCatching { DiagnosticLogRules.redact(message) }.getOrDefault(message)
        runCatching { DiagnosticLogRules.format(stamp, level, tag, safe) }.getOrNull()?.let { line ->
            synchronized(ring) {
                ring.addLast(line)
                while (ring.size > RING_CAPACITY) ring.removeFirst()
            }
        }
        val target = sink ?: return
        if (!target.enabled) return
        runCatching { target.write(level, stamp, tag, safe) }
    }

    /** 测试里换掉它就不用依赖真实时钟 */
    var clock: () -> String = { SimpleDateFormat("HH:mm:ss.SSS", Locale.US).format(Date()) }
}
