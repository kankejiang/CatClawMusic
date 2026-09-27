package com.catclaw.music.kt.data.smb

import com.catclaw.music.kt.core.net.UrlCredentials
import com.catclaw.music.kt.data.remote.RemoteFile
import java.io.OutputStream
import java.net.InetAddress
import java.net.ServerSocket
import java.net.Socket
import kotlin.concurrent.thread

/** SMB 读取面的最小口子：代理只用到这三个方法，单测塞假实现即可覆盖取流逻辑 */
interface SmbReader {
    /** 文件字节数；不存在返回 -1 */
    fun fileSize(remotePath: String): Long

    /** 从 offset 读最多 count 字节，返回实际读到的字节数（0 = EOF） */
    fun read(remotePath: String, offset: Long, buffer: ByteArray, count: Int): Int

    fun list(remotePath: String): List<RemoteFile>

    fun close()
}

/**
 * 把 `smb://` 转成本地 HTTP 供 ExoPlayer 取流 —— Media3 没有 SMB 的 DataSource，
 * C# 侧也是同一套办法（`SmbStreamProxy.cs`）：`HttpListener` 监听 127.0.0.1:19876..19975，
 * 路径形如 `/smb/{percent-encoded 原始 smb url}`，支持 Range/206，512 KB 分块回写。
 *
 * 安卓没有 `HttpListener`，这里用 `ServerSocket` 手写同等语义的最小 HTTP/1.1 响应。
 */
class SmbStreamProxy(
    private val openReader: (SmbTarget) -> SmbReader,
) : AutoCloseable {

    @Volatile
    private var port = -1
    private var server: ServerSocket? = null
    private var acceptThread: Thread? = null
    private val readers = HashMap<String, SmbReader>()

    val boundPort: Int get() = port

    /** @return false 表示 19876..19975 全被占，起不起来 */
    @Synchronized
    fun start(): Boolean {
        if (port > 0) return true
        val loopback = InetAddress.getByName("127.0.0.1")
        for (candidate in PORT_START..PORT_END) {
            val bound = runCatching { ServerSocket(candidate, BACKLOG, loopback) }.getOrNull() ?: continue
            server = bound
            port = candidate
            acceptThread = thread(isDaemon = true, name = "catclaw-smb-proxy") { acceptLoop(bound) }
            return true
        }
        return false
    }

    /** 播放地址：把整条 smb url 百分号编码塞进路径，代理侧再解回来 */
    fun toHttpUrl(smbUrl: String): String? {
        if (!smbUrl.startsWith("smb://", true)) return null
        if (port <= 0 && !start()) return null
        return "http://127.0.0.1:$port/$SMB_PREFIX${percentEncode(smbUrl.trim())}"
    }

    private fun acceptLoop(socket: ServerSocket) {
        while (!socket.isClosed) {
            val client = runCatching { socket.accept() }.getOrNull() ?: return
            thread(isDaemon = true, name = "catclaw-smb-conn") { handle(client) }
        }
    }

    private fun handle(client: Socket) {
        client.use { socket ->
            runCatching {
                val input = socket.getInputStream().bufferedReader()
                val requestLine = input.readLine() ?: return
                val headers = HashMap<String, String>()
                while (true) {
                    val line = input.readLine() ?: break
                    if (line.isEmpty()) break
                    val idx = line.indexOf(':')
                    if (idx > 0) headers[line.substring(0, idx).trim().lowercase()] = line.substring(idx + 1).trim()
                }
                val parts = requestLine.split(' ')
                if (parts.size < 2 || !parts[0].equals("GET", true)) {
                    socket.getOutputStream().writeStatus(405, 0, "Method Not Allowed")
                    return
                }
                respond(parts[1], headers, socket.getOutputStream())
            }.onFailure {
                runCatching { socket.getOutputStream().writeStatus(500, 0, "Proxy Error") }
            }
        }
    }

    private fun respond(rawUrl: String, headers: Map<String, String>, out: OutputStream) {
        val path = rawUrl.substringBefore('?')
        if (!path.startsWith("/$SMB_PREFIX")) {
            out.writeStatus(404, 0, "Not Found")
            return
        }
        val smbUrl = UrlCredentials.percentDecode(path.removePrefix("/$SMB_PREFIX"))
        val target = parseSmbUrl(smbUrl)
        if (target == null) {
            out.writeStatus(400, 0, "Bad smb URL")
            return
        }

        val reader = readers.getOrPut(target.serviceKey) { openReader(target) }
        val fileSize = runCatching { reader.fileSize(target.remotePath) }.getOrDefault(-1L)
        if (fileSize < 0L) {
            out.writeStatus(404, 0, "Not Found")
            return
        }

        val range = parseRange(headers["range"], fileSize)
        val startAt = range?.first ?: 0L
        val endAt = range?.second ?: (fileSize - 1).coerceAtLeast(0L)
        val length = (endAt - startAt + 1).coerceAtLeast(0L)

        out.writeStatus(
            code = if (range != null) 206 else 200,
            contentLength = length,
            reason = "OK",
            extra = buildList {
                add("Accept-Ranges: bytes")
                add("Content-Type: application/octet-stream")
                if (range != null) add("Content-Range: bytes $startAt-$endAt/$fileSize")
            },
        )
        if (length == 0L) return

        val body = out
        val buffer = ByteArray(CHUNK)
        var offset = startAt
        var remaining = length
        while (remaining > 0) {
            val want = minOf(remaining, buffer.size.toLong()).toInt()
            val got = runCatching { reader.read(target.remotePath, offset, buffer, want) }.getOrDefault(0)
            if (got <= 0) break
            body.write(buffer, 0, got)
            offset += got
            remaining -= got
        }
        body.flush()
    }

    override fun close() {
        runCatching { server?.close() }
        server = null
        acceptThread = null
        port = -1
        readers.values.forEach { runCatching { it.close() } }
        readers.clear()
    }

    private companion object {
        const val PORT_START = 19876
        const val PORT_END = 19975
        const val BACKLOG = 16
        const val CHUNK = 512 * 1024
        const val SMB_PREFIX = "smb/"

        /** C# 只认 `bytes=start-end` 一种写法，且两端都可省略 */
        fun parseRange(header: String?, fileSize: Long): Pair<Long, Long>? {
            val value = header?.trim()?.takeIf { it.startsWith("bytes=", true) } ?: return null
            val spec = value.substring("bytes=".length)
            val dash = spec.indexOf('-')
            if (dash < 0) return null
            val startText = spec.substring(0, dash).trim()
            val endText = spec.substring(dash + 1).trim()
            if (fileSize <= 0) return null
            return if (startText.isEmpty()) {
                // 后缀范围 `bytes=-N` = 最后 N 字节
                val tail = endText.toLongOrNull() ?: return null
                if (tail <= 0) null else (fileSize - tail).coerceAtLeast(0L) to (fileSize - 1)
            } else {
                val from = startText.toLongOrNull() ?: return null
                if (from >= fileSize) return null
                val to = endText.toLongOrNull() ?: (fileSize - 1)
                from to to.coerceAtMost(fileSize - 1)
            }
        }


    }
}

private fun OutputStream.writeStatus(code: Int, contentLength: Long, reason: String, extra: List<String> = emptyList()) {
    val head = buildString {
        append("HTTP/1.1 ").append(code).append(' ').append(reason).append("\r\n")
        append("Content-Length: ").append(contentLength).append("\r\n")
        extra.forEach { append(it).append("\r\n") }
        append("Connection: close\r\n\r\n")
    }
    write(head.toByteArray(Charsets.US_ASCII))
    flush()
}
