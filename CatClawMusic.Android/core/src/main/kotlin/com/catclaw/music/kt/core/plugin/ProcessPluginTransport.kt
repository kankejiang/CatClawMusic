package com.catclaw.music.kt.core.plugin

import java.io.IOException
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.withContext

/**
 * 一个插件进程的句柄。真实实现是 `ProcessBuilder` + 一条读线程，测试里给内存假件 ——
 * 分帧、重启、超时这些容易错的部分因此都能在 JVM 里跑完，不依赖某台机器上有没有可执行程序。
 */
interface PluginProcess {
    /** 写一行请求（实现内部负责补 LF 并 flush） */
    fun writeLine(line: String)

    /**
     * 返回**一整行**（不含 LF），读不到就返回 null。
     *
     * ⚠ 两点必须由实现兜住：① 时限 —— 无时限的阻塞读会让外层 deadline 形同虚设，
     * 插件不回话时线程永远挂住；② 行边界 —— 半行不许交上来，
     * 分帧是 [FrameDecoder] 的事，进程这条线用 `BufferedReader.readLine()` 天然满足。
     */
    fun readLine(budgetMs: Long): String?

    fun alive(): Boolean
    fun destroy()
}

/**
 * [PluginTransport] 的进程实现：一问一答，插件死了或哑了就重开一次。
 *
 * 为什么要有"重开"：C# 现状是插件与宿主同进程，禁用/卸载后程序集仍驻留，
 * 必须重启应用才生效（`CatClawMusic.Plugins/README.md:95`、`PluginRestartPrompt.cs` 就是为这个存在的）。
 * 进程外之后崩溃只影响这一个插件，重开一次就能继续 —— 这是换成交换格式最直接的用户可见收益。
 *
 * 阻塞 IO 全部走 [Dispatchers.IO]：读一行等不到就把调用线程挂住，在 Android 上是 ANR 而不是"慢一点"。
 */
class ProcessPluginTransport(
    private val spawn: () -> PluginProcess,
    private val timeoutMs: Int = PluginManifest.DEFAULT_TIMEOUT_MS,
) : PluginTransport {
    private var process: PluginProcess? = null
    private val lock = Any()

    override suspend fun exchange(requestLine: String): String = withContext(Dispatchers.IO) {
        var lastFailure: Throwable? = null
        repeat(MAX_ATTEMPTS) {
            val proc = synchronized(lock) { process?.takeIf { it.alive() } ?: spawn().also { process = it } }
            val outcome = runCatching { proc.writeLine(requestLine); readOne(proc) }
            val line = outcome.getOrNull()
            if (line != null) return@withContext line
            lastFailure = outcome.exceptionOrNull()
            // 走到这里说明这个进程要么哑了要么死了，留着只会继续卡：丢掉重开
            synchronized(lock) {
                runCatching { process?.destroy() }
                process = null
            }
        }
        throw IOException("插件未能应答（${MAX_ATTEMPTS} 次）：${lastFailure?.message}")
    }

    /** 一次调用只等一条完整报文；整体不超过 [timeoutMs] */
    private fun readOne(proc: PluginProcess): String {
        val remainingMs = timeoutMs.toLong()
        val raw = proc.readLine(remainingMs)
            ?: throw if (proc.alive()) IOException("插件超时未回话") else IOException("插件已退出")
        return raw
    }

    override suspend fun close() = withContext(Dispatchers.IO) {
        synchronized(lock) {
            runCatching { process?.destroy() }
            process = null
        }
    }

    private companion object {
        const val MAX_ATTEMPTS = 2
    }
}
