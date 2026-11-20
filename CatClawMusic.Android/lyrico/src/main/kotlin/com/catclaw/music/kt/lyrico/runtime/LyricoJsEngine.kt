package com.catclaw.music.kt.lyrico.runtime

import app.cash.quickjs.QuickJs
import java.util.concurrent.Executors
import java.util.concurrent.TimeUnit
import java.util.concurrent.locks.ReentrantLock
import kotlin.concurrent.withLock

/**
 * Lyrico 源插件的 JS 执行环境（基于 app.cash.quickjs）。
 *
 * ## 为什么要「JSON 作为边界」
 *
 * cashapp 的 QuickJS 绑定只保证标量/String 往返可靠，JS 对象/数组到 Kotlin 没有稳定的
 * 结构化映射。而 Lyrico 插件两头都是结构化的：
 *   - 入参 `getLyrics(request)` 是对象
 *   - 出参是候选对象数组
 *   - 宿主 API 里 `http.get()` 返回 `{code, message, headers, body, bodyBase64}`
 *
 * 所以统一用 JSON 串作为跨界载体：宿主方法只收发 String/基本类型，桥接层负责
 * `JSON.parse` / `JSON.stringify`。这样既避开了类型映射的坑，也天然可单测。
 *
 * ## 线程模型（重要）
 *
 * QuickJS 实例非线程安全，且**必须在同一个线程上创建和执行**：实测在 A 线程
 * `QuickJs.create()`、在 B 线程 `evaluate()` 会直接抛 "stack overflow"（连 `1+1` 都失败）。
 * 因此这里不在构造期创建引擎，而是把创建也放进执行线程，首次调用时惰性初始化。
 */
class LyricoJsEngine private constructor() : AutoCloseable {

    private val lock = ReentrantLock()

    // QuickJS 解析/执行对原生栈要求较高，Android 默认 1MB 线程栈不够，给 8MB。
    private val executor = Executors.newSingleThreadExecutor { r ->
        Thread(null, r, "lyrico-js", JS_THREAD_STACK_BYTES).apply { isDaemon = true }
    }

    /** 只在 JS 线程内访问 */
    private var quickJs: QuickJs? = null

    @Volatile
    private var closed = false

    private fun engineOnJsThread(): QuickJs =
        quickJs ?: QuickJs.create().also { quickJs = it }

    /** 在 JS 线程上执行一段脚本，返回标量结果（String/Double/Boolean/null）。 */
    fun evaluate(script: String, timeoutMs: Long = DEFAULT_TIMEOUT_MS): Any? = submit(timeoutMs) {
        engineOnJsThread().evaluate(script)
    }

    /**
     * 把 Kotlin 接口实例暴露成 JS 全局变量。
     *
     * 注意：接口方法参数与返回值请只用 String/Int/Double/Boolean，复杂结构用 JSON 串。
     */
    fun <T : Any> bindGlobal(name: String, type: Class<T>, impl: T, timeoutMs: Long = DEFAULT_TIMEOUT_MS) {
        submit<Unit>(timeoutMs) { engineOnJsThread().set(name, type, impl) }
    }

    private fun <R> submit(timeoutMs: Long, block: () -> R): R {
        check(!closed) { "LyricoJsEngine 已关闭" }
        val future = executor.submit<R> {
            lock.withLock { block() }
        }
        return try {
            future.get(timeoutMs, TimeUnit.MILLISECONDS)
        } catch (e: java.util.concurrent.TimeoutException) {
            future.cancel(true)
            throw IllegalStateException("JS 执行超时（${timeoutMs}ms）", e)
        } catch (e: java.util.concurrent.ExecutionException) {
            throw (e.cause ?: e)
        }
    }

    override fun close() {
        if (closed) return
        closed = true
        // 关闭也要回到 JS 线程，避免跨线程释放
        runCatching {
            val f = executor.submit<Unit> { runCatching { quickJs?.close() }; Unit }
            f.get(3, TimeUnit.SECONDS)
        }
        runCatching { executor.shutdownNow() }
        quickJs = null
    }

    companion object {
        private const val DEFAULT_TIMEOUT_MS = 10_000L
        private const val JS_THREAD_STACK_BYTES = 8L * 1024 * 1024

        fun create(): LyricoJsEngine = LyricoJsEngine()
    }
}
