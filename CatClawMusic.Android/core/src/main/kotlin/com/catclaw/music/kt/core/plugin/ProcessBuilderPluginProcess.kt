package com.catclaw.music.kt.core.plugin

import java.util.concurrent.LinkedBlockingQueue
import java.util.concurrent.TimeUnit
import kotlin.concurrent.thread

/**
 * [PluginProcess] 的真·子进程实现：起一个外部可执行，stdin 写请求、stdout 读应答。
 *
 * ⚠ **这条路只在"宿主可以自己起进程"的平台上成立（桌面 / JVM）**。
 * Android 10 起的 W^X 策略禁止执行从 APK 里解出来的文件，所以设备上的插件不能这么装 ——
 * 那一版要走「另一个 APK 暴露 Bound Service，同一条行协议跑在 socketpair 上」。
 * 把进程句柄抽成 [PluginProcess] 就是为了这两种承载能共用上面所有的协议逻辑。
 *
 * 两处不写就会死锁的细节：
 * ① stdout 必须**独立线程**读并排队，否则"先读后写"在插件也在等输入时会互相卡住；
 * ② stderr 也必须有消费者 —— 插件往 stderr 写日志写满管道缓冲区后，它会阻塞在写上，
 * 表现是"插件莫名超时"，查起来非常费劲。
 */
class ProcessBuilderPluginProcess(command: List<String>) : PluginProcess {
    private val process = ProcessBuilder(command).start()
    private val stdin = process.outputStream.bufferedWriter()
    private val queue = LinkedBlockingQueue<Any>()
    private val eofMarker = Any()

    init {
        thread(name = "catclaw-plugin-stdout", isDaemon = true) {
            runCatching { process.inputStream.bufferedReader().forEachLine { queue.put(it) } }
            queue.put(eofMarker)
        }
        thread(name = "catclaw-plugin-stderr", isDaemon = true) {
            runCatching { process.errorStream.bufferedReader().readText() }
        }
    }

    override fun writeLine(line: String) {
        stdin.write(line)
        stdin.write('\n'.code)
        stdin.flush()
    }

    override fun readLine(budgetMs: Long): String? {
        val taken = queue.poll(budgetMs, TimeUnit.MILLISECONDS) ?: return null
        if (taken === eofMarker) return null
        return taken as? String
    }

    override fun alive(): Boolean = process.isAlive

    override fun destroy() {
        runCatching { stdin.close() }
        runCatching { process.destroy() }
    }
}
