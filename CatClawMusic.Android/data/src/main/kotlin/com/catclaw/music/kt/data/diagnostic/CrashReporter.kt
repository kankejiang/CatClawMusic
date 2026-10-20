package com.catclaw.music.kt.data.diagnostic

import android.content.Context
import android.util.Log
import com.catclaw.music.kt.core.diagnostic.AppLog
import com.catclaw.music.kt.core.diagnostic.CrashReportRules
import dagger.hilt.android.qualifiers.ApplicationContext
import java.io.File
import java.text.SimpleDateFormat
import java.util.Date
import java.util.Locale
import javax.inject.Inject
import javax.inject.Singleton

/**
 * 未捕获异常落盘 —— 对应 C# `App.xaml.cs:60-71` 的 `AppDomain.UnhandledException`。
 *
 * 为什么值得有：不挂调试器直接跑（桌面点图标 / 用户装包）时闪退是**没有任何信息**的，
 * 只留下一句「应用已停止」。写成文件之后 adb pull 得到，用户也能直接贴出来。
 *
 * 两处与 C# 不同，都是 Android 这边的现实：
 * - **覆盖写**而不是追加：与 C# 的 `File.WriteAllText` 一致，一次崩溃留最近一份；
 * - 写完**转交给上一个处理器**：C# 那边只写文件（它的处理器本来也拦不住进程退出），
 *   而 Android 上不转交就会吞掉系统那份崩溃日志与调试器的断点。
 */
@Singleton
class CrashReporter @Inject constructor(
    @ApplicationContext private val context: Context,
) {

    /** 崩溃文件的路径（`files/crash.log`，与 C# 同名同义，`adb pull` 得到） */
    fun path(): String = file().absolutePath

    /** 挂上处理器。重复调用只是把链条又套一层，所以由 Application 在一次启动里调一次 */
    fun install() {
        val previous = Thread.getDefaultUncaughtExceptionHandler()
        Thread.setDefaultUncaughtExceptionHandler { thread, error ->
            runCatching { write(thread, error) }
            previous?.uncaughtException(thread, error)
        }
    }

    private fun write(thread: Thread, error: Throwable) {
        val text = CrashReportRules.format(
            timestamp = SimpleDateFormat("yyyy-MM-dd HH:mm:ss", Locale.US).format(Date()),
            // 线程名 + 完整堆栈：C# 那边 `e.ExceptionObject.ToString()` 也是这么一大段
            throwableText = "thread=${thread.name}\n" + Log.getStackTraceString(error),
            recentLines = AppLog.recentLines(),
        )
        file().writeText(text)
    }

    private fun file(): File = File(context.filesDir, "crash.log")
}
