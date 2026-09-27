package com.catclaw.music.kt.data.remote

import com.sun.net.httpserver.HttpServer
import java.net.InetSocketAddress
import java.net.URLDecoder
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Test

/**
 * 猫爪服务器客户端：端点、字段映射、转义与容错。
 * 假 fetcher 只证明「解析对不对」，所以另加一组打真 HTTP 服务器的用例，
 * 用服务端收到的 method + path + query 当客户端行为的直接证据。
 */
class CatClawServerClientTest {

    private class FakeFetcher(val responses: MutableMap<String, HttpResult> = mutableMapOf()) : HttpFetcher {
        val calls = mutableListOf<Pair<String, String>>()
        var throwOnCall: Throwable? = null

        override fun call(method: String, url: String, headers: Map<String, String>, body: String?): HttpResult {
            calls += method to url
            throwOnCall?.let { throw it }
            val key = responses.keys.firstOrNull { url.contains(it) }
            return responses[key] ?: HttpResult(404, null, "")
        }
    }

    private val statusOk = HttpResult(200, null, "{\"songs\":7,\"device\":\"box\"}")

    @Test
    fun statusParsesSnakeCaseAndMixedCasing() {
        val fetcher = FakeFetcher(
            mutableMapOf(
                "/api/status" to HttpResult(
                    200,
                    null,
                    "{\"Songs\":7,\"artists\":3,\"dht_peers\":12,\"rate_limit\":60,\"device\":\"nas-box\"}",
                ),
            ),
        )
        val status = CatClawServerClient("http://nas:8080", fetcher).status()!!
        assertEquals(7, status.songs)
        assertEquals(3, status.artists)
        assertEquals(12, status.dhtPeers)
        assertEquals(60, status.rateLimit)
        assertEquals("nas-box", status.device)
    }

    @Test
    fun statusHitsTheStatusEndpointAndTrimsTrailingSlashes() {
        val fetcher = FakeFetcher(mutableMapOf("/api/status" to statusOk))
        CatClawServerClient("http://nas:8080///", fetcher).status()
        assertEquals(listOf("GET" to "http://nas:8080/api/status"), fetcher.calls)
    }

    @Test
    fun testConnectionOnlyAcceptsTwoHundredAndSwallowsErrors() {
        assertTrue(CatClawServerClient("u", FakeFetcher(mutableMapOf("/api/status" to HttpResult(204, null, "")))).testConnection())
        assertFalse(CatClawServerClient("u", FakeFetcher(mutableMapOf("/api/status" to HttpResult(500, null, "boom")))).testConnection())
        val boom = FakeFetcher().apply { throwOnCall = java.net.ConnectException("refused") }
        assertFalse(CatClawServerClient("u", boom).testConnection())
    }

    @Test
    fun songsMapsEveryServerField() {
        val json = "[{\"id\":41,\"title\":\"夜曲\",\"artist\":\"周杰伦\",\"album\":\"十一月的萧邦\",\"duration\":253," +
            "\"file_path\":\"/music/jay/nocturne.flac\",\"file_size\":31000000,\"bitrate\":921," +
            "\"track_number\":3,\"year\":2005,\"genre\":\"Pop\",\"artist_id\":9,\"album_id\":12," +
            "\"cover_art_path\":\"/img/12.jpg\",\"date_modified\":1700000000,\"lyrics_path\":\"/lrc/nocturne.lrc\"}]"
        val fetcher = FakeFetcher(mutableMapOf("/api/songs" to HttpResult(200, null, json)))
        val songs = CatClawServerClient("http://nas:8080", fetcher).songs()
        assertEquals(1, songs.size)
        val ss = songs[0]
        assertEquals(41, ss.id)
        assertEquals("夜曲", ss.title)
        assertEquals("周杰伦", ss.artist)
        assertEquals(253, ss.durationSeconds)
        assertEquals("/music/jay/nocturne.flac", ss.filePath)
        assertEquals(31_000_000L, ss.fileSize)
        assertEquals(921, ss.bitrate)
        assertEquals(3, ss.trackNumber)
        assertEquals(2005, ss.year)
        assertEquals("Pop", ss.genre)
        assertEquals(9L, ss.artistId)
        assertEquals(12L, ss.albumId)
        assertEquals("/img/12.jpg", ss.coverArtPath)
        assertEquals(1_700_000_000L, ss.dateModified)
        assertEquals("/lrc/nocturne.lrc", ss.lyricsPath)
    }

    @Test
    fun songsToleratesSingleObjectAndMissingFields() {
        val fetcher = FakeFetcher(
            mutableMapOf("/api/songs" to HttpResult(200, null, "{\"id\":7,\"title\":\"Only\"}")),
        )
        val songs = CatClawServerClient("http://nas:8080", fetcher).songs()
        assertEquals(1, songs.size)
        assertEquals(7, songs[0].id)
        assertEquals("", songs[0].artist)
        assertEquals(0L, songs[0].fileSize)
        assertNull(songs[0].genre)
        assertNull(songs[0].lyricsPath)
    }

    @Test
    fun songsReturnsEmptyListInsteadOfThrowingOnHttpError() {
        val fetcher = FakeFetcher(mutableMapOf("/api/songs" to HttpResult(503, null, "")))
        assertTrue(CatClawServerClient("http://nas:8080", fetcher).songs().isEmpty())
    }

    @Test
    fun searchPercentEncodesSpaceAsTwentyNotPlus() {
        val fetcher = FakeFetcher(mutableMapOf("/api/search?" to HttpResult(200, null, "[]")))
        CatClawServerClient("http://nas:8080", fetcher).search("周杰伦 夜曲")
        val url = fetcher.calls.single().second
        assertTrue(url.startsWith("http://nas:8080/api/search?q="))
        assertEquals("%E5%91%A8%E6%9D%B0%E4%BC%A6%20%E5%A4%9C%E6%9B%B2", url.substringAfter("q="))
    }

    @Test
    fun streamAndAssetUrlsHangOffTheServerBase() {
        val client = CatClawServerClient("http://nas:8080/catclaw/", FakeFetcher())
        assertEquals("http://nas:8080/catclaw/api/stream/41", client.streamUrl(41))
        assertEquals("http://nas:8080/catclaw/img/a.jpg", client.assetUrl("/img/a.jpg"))
        assertEquals("http://cdn/x/a.jpg", client.assetUrl("http://cdn/x/a.jpg"))
        assertNull(client.assetUrl("  "))
        assertNull(client.assetUrl(null))
    }

    @Test
    fun unconfiguredServerIssuesNoRequestAtAll() {
        val fetcher = FakeFetcher()
        val client = CatClawServerClient("", fetcher)
        assertFalse(client.isConfigured())
        assertFalse(client.testConnection())
        assertNull(client.status())
        assertTrue(client.songs().isEmpty())
        assertTrue(client.search("x").isEmpty())
        assertTrue(fetcher.calls.isEmpty())
    }

    /** 起真 HTTP 服务器跑一遍；用完必须 stop —— 它的线程非守护，会挂住测试 JVM */
    private fun withServer(payload: String, body: (CatClawServerClient, MutableList<String>) -> Unit) {
        val seen = mutableListOf<String>()
        val server = HttpServer.create(InetSocketAddress("127.0.0.1", 0), 0)
        server.createContext("/") { exchange ->
            seen += exchange.requestMethod + " " + exchange.requestURI
            val bytes = payload.toByteArray(Charsets.UTF_8)
            exchange.responseHeaders.add("Content-Type", "application/json; charset=utf-8")
            exchange.sendResponseHeaders(200, bytes.size.toLong())
            exchange.responseBody.use { it.write(bytes) }
        }
        server.start()
        try {
            body(CatClawServerClient("http://127.0.0.1:" + server.address.port, UrlHttpFetcher), seen)
        } finally {
            server.stop(0)
        }
    }

    @Test
    fun realHttpServerReceivesTheExactPaths() {
        withServer("{\"songs\":2,\"device\":\"real-box\"}") { client, seen ->
            assertEquals(2, client.status()!!.songs)
            assertEquals("real-box", client.status()!!.device)
            // /api/status 的对象载荷对 songs() 来说是「一条缺字段的歌」，不能抛
            assertEquals(1, client.songs().size)
            assertEquals(
                listOf("GET /api/status", "GET /api/status", "GET /api/songs"),
                seen,
            )
        }
    }

    @Test
    fun realHttpServerReceivesUtf8PercentEncodedKeyword() {
        withServer("[]") { client, seen ->
            client.search("夜曲")
            val line = seen.single()
            assertTrue(line.startsWith("GET /api/search?q="))
            val encoded = line.substringAfter("q=").substringBefore(' ')
            assertEquals("夜曲", URLDecoder.decode(encoded, "UTF-8"))
            assertEquals("%E5%A4%9C%E6%9B%B2", encoded)
        }
    }
}
