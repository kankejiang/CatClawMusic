package com.catclaw.music.kt.data.media

import java.io.InputStream
import java.net.InetAddress
import java.net.ServerSocket
import java.net.Socket
import java.util.Collections
import kotlin.concurrent.thread
import kotlinx.coroutines.runBlocking
import org.junit.Assert.assertEquals
import org.junit.Assert.assertNull
import org.junit.Test

/**
 * Range 取窗口对着一个真 HTTP 回环服务器测（okhttp 在 JVM 上就能跑，不需要设备）。
 *
 * 关键的一条是最后那个：**服务器不理 Range 时会从 0 开始把整首发过来**。
 * 头部请求这样退化是无害的（前若干字节本来就对），但尾部请求会把**文件的开头**
 * 当成结尾交给 `parseTail` —— 白找一趟 moov 还是轻的，重的是它看起来"成功"了。
 * 所以尾部必须是 206，否则判为取不到。
 */
class HttpAudioBytesReaderTest {

    private class FakeRangeServer(body: ByteArray, private val honorRange: Boolean) : AutoCloseable {
        private val backlog = ServerSocket(0, 16, InetAddress.getByName("127.0.0.1"))
        val seenRanges = Collections.synchronizedList(mutableListOf<String?>())
        private val content = body.copyOf()
        @Volatile
        private var stopped = false

        fun url(path: String): String = "http://127.0.0.1:${backlog.localPort}$path"

        init {
            thread(isDaemon = true) {
                while (!stopped) {
                    val client = runCatching { backlog.accept() }.getOrNull() ?: break
                    runCatching { handle(client) }
                }
            }
        }

        private fun handle(client: Socket) {
            client.use { socket ->
                val input = socket.getInputStream()
                var line = readLine(input)
                var range: String? = null
                while (line != null && line.isNotEmpty()) {
                    if (line.startsWith("Range:", ignoreCase = true)) range = line.substringAfter(':').trim()
                    line = readLine(input)
                }
                seenRanges += range
                val out = socket.getOutputStream()
                if (honorRange && range != null) {
                    val spec = range.removePrefix("bytes=").substringAfter("=")
                    val dash = spec.indexOf('-')
                    val from = spec.substring(0, dash).toLong()
                    val to = spec.substring(dash + 1).toLong().coerceAtMost(content.size - 1L)
                    val length = (to - from + 1).toInt()
                    out.write(
                        ("HTTP/1.1 206 Partial Content\r\nContent-Length: $length\r\n" +
                            "Content-Range: bytes $from-$to/${content.size}\r\nConnection: close\r\n\r\n")
                            .toByteArray(Charsets.US_ASCII),
                    )
                    out.write(content, from.toInt(), length)
                } else {
                    // 不理 Range：200 + 从 0 开始的整个文件
                    out.write(
                        ("HTTP/1.1 200 OK\r\nContent-Length: ${content.size}\r\nConnection: close\r\n\r\n")
                            .toByteArray(Charsets.US_ASCII),
                    )
                    out.write(content)
                }
                out.flush()
            }
        }

        override fun close() {
            stopped = true
            runCatching { backlog.close() }
        }

        /** 逐字节读一行：用 BufferedReader 会把 body 一起吞掉，后面的断言就没法看真实字节 */
        private fun readLine(input: InputStream): String? {
            val out = StringBuilder()
            while (true) {
                val b = input.read()
                if (b < 0) return if (out.isEmpty()) null else out.toString()
                if (b == '\n'.code) return out.toString().trimEnd('\r')
                out.append(b.toChar())
            }
        }
    }

    /** 每个字节的值就是它的位置（取 128 以内，避免 Byte 的符号把断言搅浑），
     *  这样"拿到的是哪一段"一眼可查 */
    private fun positional(size: Int): ByteArray = ByteArray(size) { (it % 128).toByte() }

    @Test
    fun headTakesOnlyTheAskedPrefix() {
        val server = FakeRangeServer(positional(1000), honorRange = true)
        server.use {
            val reader = HttpAudioBytesReader()
            val head = runBlocking { reader.head(it.url("/dav/a.mp3"), 64) }
            assertEquals(64, head?.size)
            assertEquals("bytes=0-63", server.seenRanges.single())
            assertEquals(0, head?.first()?.toInt())
            assertEquals(63, head?.last()?.toInt())
        }
    }

    @Test
    fun tailAsksForTheLastWindowAtTheRightOffset() {
        val server = FakeRangeServer(positional(1000), honorRange = true)
        server.use {
            val reader = HttpAudioBytesReader()
            // 文件 1000 字节，要最后 300 字节 ⇒ 偏移 700，闭区间到 999
            val tail = runBlocking { reader.tail(it.url("/dav/a.m4a"), bytes = 300, fileSize = 1000) }
            assertEquals(300, tail?.size)
            assertEquals("bytes=700-999", server.seenRanges.single())
            assertEquals(60, tail?.first()?.toInt()) // 700 % 128
            assertEquals(103, tail?.last()?.toInt()) // 999 % 128
        }
    }

    @Test
    fun serverIgnoringRangeMustNotPassHeadBytesOffAsTheTail() {
        val server = FakeRangeServer(positional(1000), honorRange = false)
        server.use {
            val reader = HttpAudioBytesReader()
            assertNull(runBlocking { reader.tail(it.url("/dav/a.m4a"), bytes = 300, fileSize = 1000) })
            // 正控制：同一条退化通道对头部请求是无害的，拿到的确实还是开头
            val head = runBlocking { reader.head(it.url("/dav/a.mp3"), 64) }
            assertEquals(64, head?.size)
            assertEquals(0, head?.first()?.toInt())
        }
    }

    @Test
    fun unknownFileSizeMeansNoTailRequestAtAll() {
        val server = FakeRangeServer(positional(100), honorRange = true)
        server.use {
            val reader = HttpAudioBytesReader()
            assertNull(runBlocking { reader.tail(it.url("/dav/a.m4a"), bytes = 300, fileSize = 0) })
            assertNull(runBlocking { reader.tail(it.url("/dav/b.m4a"), bytes = 0, fileSize = 1000) })
            assertEquals(0, server.seenRanges.size)
        }
    }
}
