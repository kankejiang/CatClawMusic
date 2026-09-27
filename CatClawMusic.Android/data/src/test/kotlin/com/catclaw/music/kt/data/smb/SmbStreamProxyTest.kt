package com.catclaw.music.kt.data.smb

import com.catclaw.music.kt.core.net.UrlCredentials
import com.catclaw.music.kt.data.remote.RemoteFile
import java.net.Socket
import org.junit.After
import org.junit.Assert.assertEquals
import org.junit.Assert.assertTrue
import org.junit.Test

/**
 * 打真 socket 的本地 HTTP 代理测试：假 SmbReader 只提供字节，
 * 断言的是「ExoPlayer 会依赖的那些响应头与切片行为」对不对。
 */
class SmbStreamProxyTest {

    private val payload = ByteArray(300_000) { (it % 251).toByte() }
    private var opened = 0
    private var proxy: SmbStreamProxy? = null

    private inner class FakeReader : SmbReader {
        override fun fileSize(remotePath: String): Long = if (remotePath.endsWith("missing.mp3")) -1 else payload.size.toLong()

        override fun read(remotePath: String, offset: Long, buffer: ByteArray, count: Int): Int {
            if (offset >= payload.size) return 0
            val n = minOf(count.toLong(), payload.size - offset).toInt()
            payload.copyInto(buffer, 0, offset.toInt(), offset.toInt() + n)
            return n
        }

        override fun list(remotePath: String): List<RemoteFile> =
            listOf(RemoteFile("song.mp3", "$remotePath\\song.mp3", false, payload.size.toLong()))

        override fun close() = Unit
    }

    private fun startProxy(): SmbStreamProxy {
        val p = SmbStreamProxy { target ->
            assertEquals("nas", target.host)
            opened++
            FakeReader()
        }
        assertTrue("19876..19975 全被占", p.start())
        proxy = p
        return p
    }

    @After
    fun tearDown() {
        proxy?.close()
        proxy = null
    }

    private data class Response(val status: Int, val headers: Map<String, String>, val body: ByteArray)

    private fun get(url: String, range: String? = null): Response {
        val u = url.removePrefix("http://")
        val hostPort = u.substringBefore('/')
        val path = "/" + u.substringAfter('/', "")
        Socket(hostPort.substringBefore(':'), hostPort.substringAfter(':').toInt()).use { s ->
            val req = buildString {
                append("GET ").append(path).append(" HTTP/1.1\r\n")
                append("Host: ").append(hostPort).append("\r\n")
                if (range != null) append("Range: ").append(range).append("\r\n")
                append("\r\n")
            }
            s.getOutputStream().write(req.toByteArray())
            s.getOutputStream().flush()
            val raw = s.getInputStream().readAllBytes()
            val split = indexOfDoubleCrLf(raw)
            val head = String(raw, 0, split, Charsets.US_ASCII)
            val lines = head.split("\r\n")
            val status = lines[0].split(' ')[1].toInt()
            val headers = lines.drop(1).mapNotNull { l ->
                val i = l.indexOf(':')
                if (i > 0) l.substring(0, i).trim().lowercase() to l.substring(i + 1).trim() else null
            }.toMap()
            return Response(status, headers, raw.copyOfRange(split + 4, raw.size))
        }
    }

    private fun indexOfDoubleCrLf(bytes: ByteArray): Int {
        for (i in 0..bytes.size - 4) {
            if (bytes[i] == 13.toByte() && bytes[i + 1] == 10.toByte() &&
                bytes[i + 2] == 13.toByte() && bytes[i + 3] == 10.toByte()
            ) return i
        }
        return -1
    }

    @Test
    fun fullGetReturnsWholeFile() {
        val p = startProxy()
        val url = p.toHttpUrl("smb://nas/Music/song.mp3")!!
        val r = get(url)
        assertEquals(200, r.status)
        assertEquals(payload.size.toString(), r.headers["content-length"])
        assertEquals("bytes", r.headers["accept-ranges"])
        assertEquals(null, r.headers["content-range"])
        assertTrue(payload.contentEquals(r.body))
        assertEquals(1, opened)
    }

    @Test
    fun rangeRequestReturnsTwentySixWithSlice() {
        val p = startProxy()
        val r = get(p.toHttpUrl("smb://nas/Music/song.mp3")!!, "bytes=1000-1999")
        assertEquals(206, r.status)
        assertEquals("bytes 1000-1999/${payload.size}", r.headers["content-range"])
        assertEquals("1000", r.headers["content-length"])
        assertEquals(1000, r.body.size)
        assertTrue(payload.copyOfRange(1000, 2000).contentEquals(r.body))
    }

    @Test
    fun openEndedRangeRunsToEndOfFile() {
        val p = startProxy()
        val r = get(p.toHttpUrl("smb://nas/Music/song.mp3")!!, "bytes=299000-")
        assertEquals(206, r.status)
        assertEquals("bytes 299000-${payload.size - 1}/${payload.size}", r.headers["content-range"])
        assertEquals(1000, r.body.size)
    }

    @Test
    fun suffixRangeTakesLastNBytes() {
        val p = startProxy()
        val r = get(p.toHttpUrl("smb://nas/Music/song.mp3")!!, "bytes=-500")
        assertEquals(206, r.status)
        assertEquals("bytes 299500-${payload.size - 1}/${payload.size}", r.headers["content-range"])
        assertEquals(500, r.body.size)
    }

    /** C# 只在 `bytes=` 且能解出区间时才给 206；越界或畸形都退回整文件 200 */
    @Test
    fun unparsableRangeFallsBackToFullBody() {
        val p = startProxy()
        val url = p.toHttpUrl("smb://nas/Music/song.mp3")!!
        assertEquals(200, get(url, "bytes=999999999-").status)
        assertEquals(200, get(url, "items=0-10").status)
    }

    @Test
    fun missingFileIsFourOFour() {
        val p = startProxy()
        val r = get(p.toHttpUrl("smb://nas/Music/missing.mp3")!!)
        assertEquals(404, r.status)
        assertEquals(0, r.body.size)
    }

    @Test
    fun wrongPrefixIsFourOFourAndBadSmbUrlIsFourHundred() {
        val p = startProxy()
        assertEquals(404, get("http://127.0.0.1:${p.boundPort}/other/x").status)
        val bad = "http://127.0.0.1:${p.boundPort}/smb/http%3A%2F%2Fnot%20smb"
        assertEquals(400, get(bad).status)
    }

    @Test
    fun encodedUrlRoundTripsTheOriginalSmbUrl() {
        val p = startProxy()
        val original = "smb://alice:p+q@nas/Music/中文/夜曲.mp3"
        val url = p.toHttpUrl(original)!!
        val encoded = url.substringAfter("/smb/")
        assertEquals(original, UrlCredentials.percentDecode(encoded))
        assertTrue(parseSmbUrl(UrlCredentials.percentDecode(encoded)) != null)
    }

    @Test
    fun nonSmbUrlHasNoHttpForm() {
        val p = startProxy()
        assertEquals(null, p.toHttpUrl("http://nas/a.mp3"))
    }

    @Test
    fun readersAreCachedPerServiceKey() {
        val p = startProxy()
        get(p.toHttpUrl("smb://nas/Music/a.mp3")!!)
        get(p.toHttpUrl("smb://nas/Music/b.mp3")!!)
        get(p.toHttpUrl("smb://nas/Other/c.mp3")!!)
        // 同 host|port|share|user 复用；换 share 才新建
        assertEquals(2, opened)
    }
}
