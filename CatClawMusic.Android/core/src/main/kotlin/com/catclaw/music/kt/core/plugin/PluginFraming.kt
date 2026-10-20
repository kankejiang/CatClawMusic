package com.catclaw.music.kt.core.plugin

import kotlinx.coroutines.sync.Mutex
import kotlinx.coroutines.sync.withLock
import kotlinx.coroutines.withTimeout
import kotlinx.serialization.json.JsonElement
import kotlinx.serialization.json.buildJsonObject

/**
 * 换行分隔 JSON 的**分帧**。跨语言协议真正容易出事的地方不是字段名，而是这一层：
 * 半包、粘包、单行失控、以及"对端崩在半行上"。
 *
 * 三条规则钉死，Kotlin 与 .NET 两侧必须逐字节一致：
 * ① 一条报文 = 一行 JSON，以 `LF` 结尾；报文内部不允许出现裸 LF
 *    （JSON 字符串里的换行本来就必须转义，所以这条不需要额外约定）；
 * ② 读侧按 LF 切，凑不满一条就留着等下一块 —— 所以 [feed] 可以任意次数、任意切分地调用；
 * ③ 单行超过 [maxLineChars] 就整条丢掉并继续等下一条，而不是让宿主内存跟着失控的行涨。
 *    这里按**字符数**封顶（UTF-8 下字节数只会更大），因为目的只是给内存设上界，不是做字节记账。
 */
class FrameDecoder(private val maxLineChars: Int = MAX_LINE_CHARS) {
    private val buffer = StringBuilder()
    private var discarding = false

    /** 喂进一块文本，返回这次能凑齐的完整报文（0 条到多条都可能） */
    fun feed(chunk: String): List<String> {
        val out = mutableListOf<String>()
        var from = 0
        while (from < chunk.length) {
            val nl = chunk.indexOf('\n', from)
            val segment = if (nl < 0) chunk.substring(from) else chunk.substring(from, nl)
            if (!discarding) {
                buffer.append(segment)
                if (buffer.length > maxLineChars) {
                    buffer.setLength(0)
                    discarding = true
                }
            }
            if (nl < 0) break
            from = nl + 1
            if (discarding) {
                discarding = false
                continue
            }
            val line = buffer.toString()
            buffer.setLength(0)
            line.trim().takeIf { it.isNotEmpty() }?.let { out += it }
        }
        return out
    }

    /** 连接关闭/进程重启时清掉残留的半条，避免下一次会话串台 */
    fun reset() {
        buffer.setLength(0)
        discarding = false
    }

    companion object {
        const val MAX_LINE_CHARS = 1_000_000

        /** 写侧：去掉尾随换行再补一个 LF，保证「一报文一行」 */
        fun encode(line: String): String = line.trimEnd('\n', '\r') + "\n"
    }
}

/**
 * 插件进程的对端抽象。真实实现是 `Process` 的 stdin/stdout；测试里用内存假件 ——
 * 所以协议层不必能起进程就能把往返测完。
 */
interface PluginTransport {
    /** 发一行请求文本，收回一行响应文本。超时或对端死亡直接抛，由 [PluginRpcClient] 收敛成 failure 报文 */
    suspend fun exchange(requestLine: String): String

    suspend fun close()
}

/**
 * 报文之上的请求/响应客户端：串行化、超时、错误码收敛。
 *
 * ⚠ 为什么强制串行：C# 侧今天靠 `InitParallelism=4` 并行初始化、`ProviderTimeout=8s` 并发取数，
 * 而单进程插件同时收两个请求就得自己排队 —— 那是把并发负担推给每个插件作者。
 * 宿主侧一把 Mutex 就让所有插件天然安全，代价只是同一插件内不并行（跨插件仍然并行）。
 *
 * 超时**必须在这里掐**：`Process` 不会自己回话，不掐就是 UI 永久等一个已经卡死的插件。
 */
class PluginRpcClient(
    private val transport: PluginTransport,
    private val timeoutMs: Int = PluginManifest.DEFAULT_TIMEOUT_MS,
) : PluginChannel {
    private val mutex = Mutex()
    private var nextId = 0

    suspend fun call(method: String, params: JsonElement = buildJsonObject { }): RpcResponse {
        val id = mutex.withLock {
            val next = ++nextId
            val line = runCatching {
                withTimeout(timeoutMs.toLong()) { transport.exchange(PluginProtocol.encodeRequest(next, method, params)) }
            }.getOrElse {
                return RpcResponse(next, null, PluginProtocol.ERR_PLUGIN, "插件无响应（${timeoutMs}ms 内）", retryable = true)
            }
            next to line
        }
        return PluginProtocol.decodeResponse(id.second)
            ?: RpcResponse(id.first, null, PluginProtocol.ERR_PARSE, "插件返回的不是合法报文", retryable = false)
    }

    /** 对 [PluginChannel] 的实现：接口收 `JsonElement`，内部走同一条 [call]（信封与超时只此一处） */
    override suspend fun request(method: String, params: JsonElement): RpcResponse = call(method, params)

    override suspend fun close() = transport.close()
}
