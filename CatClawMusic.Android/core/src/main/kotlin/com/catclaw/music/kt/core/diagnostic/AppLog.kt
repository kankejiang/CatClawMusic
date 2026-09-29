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

    private fun emit(level: LogLevel, tag: String, message: String) {
        val target = sink ?: return
        if (!target.enabled) return
        // 时间戳在门面这里生成：一条日志的时间是"它被写出的那一刻"，
        // 落到哪个 sink、被谁格式化都不该改变它
        runCatching { target.write(level, clock(), tag, DiagnosticLogRules.redact(message)) }
    }

    /** 测试里换掉它就不用依赖真实时钟 */
    var clock: () -> String = { SimpleDateFormat("HH:mm:ss.SSS", Locale.US).format(Date()) }
}
