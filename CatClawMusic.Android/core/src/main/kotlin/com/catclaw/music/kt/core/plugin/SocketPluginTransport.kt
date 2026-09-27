package com.catclaw.music.kt.core.plugin

import java.io.IOException
import java.io.InputStreamReader
import java.net.InetSocketAddress
import java.net.Socket
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.withContext

/**
 * [PluginTransport] 的 **socket 实现** —— 安卓设备上的插件承载走这条，不是 stdio。
 *
 * 为什么必须有第二条通道：Android 10 起的 W^X 策略禁止执行从 APK 里解包出来的文件，
 * 所以「解包一个 js/exe 再拿 stdin/stdout 喂它」在设备上不成立。设备上的形态是
 * **另一个 APK 暴露 Bound Service**，宿主绑定后由对方告知一个 loopback 端口，
 * 同一条换行分隔 JSON 协议跑在这个 socket 上。
 *
 * 与 [ProcessPluginTransport] 的分工：那条用 `BufferedReader.readLine()` 天然拿到整行，
 * 所以它不需要分帧；**这条会收到任意切片的字节流**（TCP 想怎么切就怎么切），
 * 因此正是 [FrameDecoder] 从一开始被留在协议层的那个消费者。
 *
 * 阻塞 IO 全部走 [Dispatchers.IO]，理由与进程版相同：读不到就挂住调用线程，在安卓上是 ANR。
 */
class SocketPluginTransport(
    private val port: Int,
    private val host: String = "127.0.0.1",
    private val timeoutMs: Int = PluginManifest.DEFAULT_TIMEOUT_MS,
    /** 只有测试会换掉它：真连接走 loopback，测试连一个本机 `ServerSocket` */
    private val connect: (String, Int, Int) -> Socket = ::connectLoopback,
) : PluginTransport {

    private val lock = Any()
    private var socket: Socket? = null
    private var reader: InputStreamReader? = null
    private val decoder = FrameDecoder()
    private val pending = ArrayDeque<String>()

    override suspend fun exchange(requestLine: String): String = withContext(Dispatchers.IO) {
        var lastFailure: Throwable? = null
        repeat(MAX_ATTEMPTS) {
            val outcome = runCatching {
                val sock = ensureConnected()
                sock.getOutputStream().write(FrameDecoder.encode(requestLine).toByteArray(UTF8))
                sock.getOutputStream().flush()
                readOneLine() ?: throw IOException("对端在应答前关闭了连接")
            }
            val line = outcome.getOrNull()
            if (line != null) return@withContext line
            lastFailure = outcome.exceptionOrNull()
            // 哑了或断了都别留着：下一次调用重连。与进程版"死了重开"同一条语义。
            dropConnection()
        }
        throw IOException("插件服务未能应答（${MAX_ATTEMPTS} 次）：${lastFailure?.message}")
    }

    /** 连上对端并报出端口 —— 端口由 Bound Service 的 Binder 告诉宿主，所以这里只做连接。 */
    private fun ensureConnected(): Socket {
        synchronized(lock) {
            socket?.takeIf { it.isConnected && !it.isClosed }?.let { return it }
        }
        val sock = connect(host, port, timeoutMs)
        sock.tcpNoDelay = true // 一问一答的小报文，攒 Nagle 只会白加延迟
        sock.soTimeout = timeoutMs
        synchronized(lock) {
            socket = sock
            reader = InputStreamReader(sock.getInputStream(), UTF8)
            decoder.reset()
            pending.clear()
        }
        return sock
    }

    private fun readOneLine(): String? {
        while (true) {
            if (pending.isNotEmpty()) return pending.removeFirst()
            val input = reader ?: return null
            val chunk = CharArray(READ_CHUNK_CHARS)
            val n = input.read(chunk)
            if (n < 0) return null
            if (n == 0) continue
            val frames = decoder.feed(String(chunk, 0, n))
            if (frames.isEmpty()) continue
            // 一次读进多条只说明对端在抢答（客户端已经按插件串行），留下次的先攒着
            if (frames.size > 1) pending.addAll(frames.drop(1))
            return frames.first()
        }
    }

    private fun dropConnection() = synchronized(lock) {
        runCatching { reader?.close() }
        runCatching { socket?.close() }
        reader = null
        socket = null
        decoder.reset()
        pending.clear()
    }

    override suspend fun close() = withContext(Dispatchers.IO) { dropConnection() }

    private companion object {
        const val MAX_ATTEMPTS = 2
        const val READ_CHUNK_CHARS = 4096
        val UTF8 = Charsets.UTF_8

        fun connectLoopback(host: String, port: Int, timeoutMs: Int): Socket {
            val sock = Socket()
            try {
                sock.connect(InetSocketAddress(host, port), timeoutMs)
            } catch (e: IOException) {
                runCatching { sock.close() }
                throw IOException("连不上插件服务 $host:$port —— ${e.message}", e)
            }
            return sock
        }
    }
}
