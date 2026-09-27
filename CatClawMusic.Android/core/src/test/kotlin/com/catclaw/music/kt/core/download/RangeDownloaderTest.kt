package com.catclaw.music.kt.core.download

import com.sun.net.httpserver.HttpServer
import java.io.File
import java.net.InetSocketAddress
import java.util.concurrent.atomic.AtomicInteger
import java.util.concurrent.atomic.AtomicReference
import kotlinx.coroutines.test.runTest
import org.junit.After
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Before
import org.junit.Test

/**
 * 断点续传的行为闸门：跑一个**真 HTTP 服务器**（JDK 自带 HttpServer，只支持 GET 就够），
 * 因为续传的语义只在真实的状态码 / Content-Range / 连接中断下才成立，
 * 拿假 InputStream 自证是测不出「服务器不认 Range 时会不会拼出坏文件」这类问题的。
 */
class RangeDownloaderTest {

    private lateinit var dir: File
    private lateinit var server: HttpServer
    private var base: String = ""

    private val totalBytes = 300_000
    private val payload: ByteArray = ByteArray(totalBytes) { (it % 251).toByte() }

    /** null = 支持 Range；非 null = 无视 Range 直接 200 全量（模拟不支持的服务器） */
    @Volatile private var rangeCapable = true

    /** >0 表示发送这么多字节后硬断连接，模拟网络掉线 */
    @Volatile private var dropAfter = 0

    private val rangeHeaderSeen = AtomicReference<String?>(null)
    private val requests = AtomicInteger(0)

    @Before
    fun startServer() {
        dir = java.nio.file.Files.createTempDirectory("catclaw-dl").toFile()
        server = HttpServer.create(InetSocketAddress("127.0.0.1", 0), 0)
        server.createContext("/file") { exchange ->
            requests.incrementAndGet()
            rangeHeaderSeen.set(exchange.requestHeaders.getFirst("Range"))
            val from = if (rangeCapable) parseStart(rangeHeaderSeen.get()) else 0L
            val start = from.toInt()
            val length = payload.size - start
            if (!rangeCapable) {
                exchange.responseHeaders.add("Content-Length", payload.size.toString())
                exchange.sendResponseHeaders(200, payload.size.toLong())
                exchange.responseBody.use { it.write(payload) }
                return@createContext
            }
            if (start > 0) {
                exchange.responseHeaders.add("Content-Range", "bytes $start-${payload.size - 1}/${payload.size}")
            }
            if (dropAfter in 1 until length) {
                // 声明总长后只发一半再收尾：客户端看到的是「干净的提前 EOF」，
                // 只有按总长校验才发现文件被截断（这正是本用例要守的行为）
                exchange.responseHeaders.add("Content-Range", "bytes $start-${payload.size - 1}/${payload.size}")
                exchange.sendResponseHeaders(206, 0)
                exchange.responseBody.use { it.write(payload, start, dropAfter) }
                exchange.close()
                return@createContext
            }
            exchange.sendResponseHeaders(if (start > 0) 206 else 200, length.toLong())
            exchange.responseBody.use { it.write(payload, start, length) }
        }
        server.start()
        base = "http://127.0.0.1:${server.address.port}/file"
    }

    @After
    fun stopServer() {
        server.stop(0)
        dir.deleteRecursively()
    }

    private fun parseStart(header: String?): Long =
        header?.removePrefix("bytes=")?.substringBefore('-')?.toLongOrNull() ?: 0L

    @Test
    fun `断线后临时文件保留，第二次从断点续传并落盘`() = runTest {
        val downloader = RangeDownloader(dir)
        dropAfter = 100_000

        val first = runCatching { downloader.download("song.bin", base) }
        assertTrue("第一次应当失败", first.isFailure)
        val part = downloader.partFile("song.bin")
        assertTrue("应留下 .part", part.exists())
        val partial = part.length()
        assertTrue("断点字节数不合理: $partial", partial in 1..100_000L)
        assertFalse("失败时不该有成品文件", downloader.targetFile("song.bin").exists())

        dropAfter = 0
        val second = downloader.download("song.bin", base)
        assertEquals("第二次必须带 Range 续传", "bytes=$partial-", rangeHeaderSeen.get())
        assertEquals(partial, second.resumedFrom)
        assertEquals(totalBytes.toLong(), second.file.length())
        assertTrue(payload.contentEquals(second.file.readBytes()))
        assertFalse("完成后 .part 应当被改名走掉", part.exists())
        assertEquals(2, requests.get())
    }

    @Test
    fun `服务器不认 Range 时清掉临时文件从头下，成品不掺假`() = runTest {
        val downloader = RangeDownloader(dir)
        // 先造一段脏的 .part
        downloader.partFile("song.bin").writeBytes(ByteArray(50_000) { 0x7F })

        rangeCapable = false
        val outcome = downloader.download("song.bin", base)

        assertEquals(0L, outcome.resumedFrom)
        assertEquals(totalBytes.toLong(), outcome.file.length())
        assertTrue("不能把脏字节拼进成品", payload.contentEquals(outcome.file.readBytes()))
        assertFalse(downloader.partFile("song.bin").exists())
    }

    @Test
    fun `首次就成功时不发送 Range 头`() = runTest {
        val downloader = RangeDownloader(dir)
        val outcome = downloader.download("song.bin", base)
        assertEquals(null, rangeHeaderSeen.get())
        assertEquals(0L, outcome.resumedFrom)
        assertTrue(payload.contentEquals(outcome.file.readBytes()))
    }

    @Test
    fun `进度按已下载与总长回调，收尾给满值`() = runTest {
        val downloader = RangeDownloader(dir)
        val seen = mutableListOf<Pair<Long, Long>>()
        downloader.download("song.bin", base) { downloaded, total -> seen += downloaded to total }
        assertTrue("至少要有一次进度", seen.isNotEmpty())
        assertEquals(totalBytes.toLong(), seen.last().first)
        assertEquals(totalBytes.toLong(), seen.last().second)
    }

    @Test
    fun `批量下载受并发上限约束且单个失败不影响其余`() = runTest {
        val downloader = RangeDownloader(dir, concurrentLimit = 1)
        val bad = "http://127.0.0.1:${server.address.port}/nope"
        val results = downloader.downloadAll(listOf("a.bin" to base, "b.bin" to bad))
        assertEquals(2, results.size)
        assertTrue(results[0].isSuccess)
        assertTrue(results[1].isFailure)
        assertTrue(downloader.targetFile("a.bin").exists())
    }

    @Test
    fun `并发数越界直接拒`() {
        listOf(0, 6, -1).forEach { bad ->
            val e = runCatching { RangeDownloader(dir, concurrentLimit = bad) }.exceptionOrNull()
            assertTrue("并发 $bad 应当被拒", e is IllegalArgumentException)
        }
    }

    @Test
    fun `HTTP 非 2xx 抛错且不产出成品文件`() = runTest {
        val downloader = RangeDownloader(dir)
        val missing = "http://127.0.0.1:${server.address.port}/missing"
        assertTrue(runCatching { downloader.download("x.bin", missing) }.isFailure)
        assertFalse(downloader.targetFile("x.bin").exists())
    }

    @Test
    fun `URL 带内嵌凭据时改发 Basic 头而不是原样送出`() = runTest {
        // 真机上「下载失败」的回归用例：HttpURLConnection 不会把 userinfo 变成认证头
        val requireAuth = HttpServer.create(InetSocketAddress("127.0.0.1", 0), 0)
        val seenAuth = AtomicReference<String?>(null)
        requireAuth.createContext("/secure") { exchange ->
            seenAuth.set(exchange.requestHeaders.getFirst("Authorization"))
            if (seenAuth.get() != "Basic Y2F0OnBAc3M=") {
                exchange.sendResponseHeaders(401, -1)
            } else {
                exchange.sendResponseHeaders(200, payload.size.toLong())
                exchange.responseBody.use { it.write(payload) }
            }
            exchange.close()
        }
        requireAuth.start()
        try {
            val downloader = RangeDownloader(dir)
            val host = "127.0.0.1:${requireAuth.address.port}"

            assertTrue(
                "不带凭据应当被 401 拒",
                runCatching { downloader.download("noauth.bin", "http://$host/secure/a.mp3") }.isFailure,
            )
            assertNull(seenAuth.get())

            val outcome = downloader.download("auth.bin", "http://cat:p%40ss@$host/secure/a.mp3")
            assertEquals("Basic Y2F0OnBAc3M=", seenAuth.get())
            assertEquals(totalBytes.toLong(), outcome.file.length())
            assertTrue(payload.contentEquals(outcome.file.readBytes()))
        } finally {
            requireAuth.stop(0)
        }
    }

    @Test
    fun `discardPart 清掉遗留的临时文件`() = runTest {
        val downloader = RangeDownloader(dir)
        downloader.partFile("song.bin").writeBytes(byteArrayOf(1, 2, 3))
        downloader.discardPart("song.bin")
        assertFalse(downloader.partFile("song.bin").exists())
    }
}
