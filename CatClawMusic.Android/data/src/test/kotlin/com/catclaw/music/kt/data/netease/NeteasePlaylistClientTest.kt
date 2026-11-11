package com.catclaw.music.kt.data.netease

import com.catclaw.music.kt.core.ai.SourceHttp
import kotlinx.coroutines.test.runTest
import org.junit.Assert.assertEquals
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Test

class NeteasePlaylistClientTest {

    private class FakeHttp(
        private val response: String?,
        private val status: Boolean = true,
    ) : SourceHttp {
        var lastUrl: String? = null
        var lastHeaders: Map<String, String> = emptyMap()

        override suspend fun get(url: String, headers: Map<String, String>, body: ByteArray): ByteArray? {
            lastUrl = url
            lastHeaders = headers
            return if (status) response?.toByteArray() else null
        }

        override suspend fun postForm(url: String, form: Map<String, String>, headers: Map<String, String>): ByteArray? = null
    }

    private class FakeEapi(
        private val detail: String?,
        private val songDetail: String? = null,
    ) : NeteaseEapiChannel {
        val calls = mutableListOf<String>()

        override suspend fun request(
            path: String,
            params: Map<String, Any>,
            rawCipherResponse: Boolean,
            cookie: String?,
        ): String? {
            calls += path
            return detail
        }

        override suspend fun songDetailRaw(songIds: List<Long>, cookie: String?): String? {
            calls += "songDetail:${songIds.size}"
            return songDetail
        }
    }

    private val eapiJson = """
        {
          "code": 200,
          "playlist": {
            "id": 3778678,
            "name": "晚上写代码",
            "coverImgUrl": "http://p1.music.126.net/cover.jpg",
            "trackCount": 3,
            "tracks": [
              {"id": 1, "name": "第一首", "dt": 216993, "ar": [{"name": "A"}], "al": {"name": "专辑一", "picUrl": "http://p2.music.126.net/a.jpg"}},
              {"id": 2, "name": "第二首", "dt": 245000, "ar": [{"name": "B"}], "al": {"name": "专辑二", "picUrl": null}}
            ],
            "trackIds": [{"id": 1}, {"id": 2}, {"id": 3}]
          }
        }
    """.trimIndent()

    private val songDetailJson = """
        {"code": 200, "songs": [
          {"id": 3, "name": "第三首", "dt": 180000, "ar": [{"name": "C"}, {"name": "D"}], "al": {"name": "专辑三", "picUrl": "http://p3.music.126.net/c.jpg"}}
        ]}
    """.trimIndent()

    private val webJson = """
        {
          "code": 200,
          "playlist": {
            "name": "网页版歌单",
            "coverImgUrl": "http://p1.music.126.net/web.jpg",
            "trackCount": 1,
            "tracks": [{"id": 9, "name": "网页曲", "dt": 100000, "ar": [{"name": "X"}], "al": {"name": "W", "picUrl": null}}]
          }
        }
    """.trimIndent()

    @Test
    fun `eapi 优先，缺失曲目按 trackIds 批量补齐并保持歌单顺序`() = runTest {
        val eapi = FakeEapi(eapiJson, songDetailJson)
        val client = NeteasePlaylistClient(FakeHttp(null, status = false), eapi)

        val playlist = client.fetch("3778678")

        assertEquals("晚上写代码", playlist?.name)
        assertEquals(listOf(1L, 2L, 3L), playlist?.tracks?.map { it.id })
        // 只补了缺的那 1 首
        assertTrue(eapi.calls.contains("songDetail:1"))

        val third = playlist!!.tracks[2]
        assertEquals("第三首", third.title)
        assertEquals("C, D", third.artist)
        assertEquals(180, third.durationSeconds)
        assertEquals("https://p3.music.126.net/c.jpg", third.coverUrl)
    }

    @Test
    fun `eapi 的 tracks 已全量时不再打批量详情`() = runTest {
        val full = """
            {"code":200,"playlist":{"name":"x","trackCount":1,
             "tracks":[{"id":1,"name":"a","dt":1000,"ar":[{"name":"A"}],"al":{"name":"B"}}],
             "trackIds":[{"id":1}]}}
        """.trimIndent()
        val eapi = FakeEapi(full)
        val playlist = NeteasePlaylistClient(FakeHttp(null, status = false), eapi).fetch("1")
        assertEquals(1, playlist?.tracks?.size)
        assertTrue(eapi.calls.none { it.startsWith("songDetail") })
    }

    @Test
    fun `eapi 失败时回落网页版`() = runTest {
        val http = FakeHttp(webJson)
        val client = NeteasePlaylistClient(http, FakeEapi(null))

        val playlist = client.fetch("3778678")

        assertEquals("网页版歌单", playlist?.name)
        assertEquals(1, playlist?.tracks?.size)
        assertTrue(http.lastUrl.orEmpty().contains("id=3778678"))
        assertTrue(http.lastHeaders.containsKey("Cookie"))
    }

    @Test
    fun `网页版接口报错时给 null`() = runTest {
        val http = FakeHttp("""{"code":404,"message":"接口未找到！"}""")
        assertNull(NeteasePlaylistClient(http, FakeEapi(null)).fetch("1"))
    }

    @Test
    fun `网络全失败时给 null`() = runTest {
        assertNull(NeteasePlaylistClient(FakeHttp(null, status = false), FakeEapi(null)).fetch("1"))
    }

    @Test
    fun `名字与曲目都空时不当成空歌单`() = runTest {
        val http = FakeHttp("""{"code":200,"playlist":{"id":1,"name":"","trackCount":0,"tracks":[]}}""")
        assertNull(NeteasePlaylistClient(http, FakeEapi(null)).fetch("1"))
    }

    @Test
    fun `缺 dt 或曲目名时不崩`() = runTest {
        val http = FakeHttp(
            """{"code":200,"playlist":{"name":"x","tracks":[{"id":9,"name":"ok"},{"id":10,"name":""},{"name":"无 id"}]}}""",
        )
        val playlist = NeteasePlaylistClient(http, FakeEapi(null)).fetch("1")
        assertEquals(1, playlist?.tracks?.size)
        assertEquals(0, playlist?.tracks?.get(0)?.durationSeconds)
    }

    @Test
    fun `取链解析出 data 里的 url，未登录时无损降到极高`() = runTest {
        val eapi = object : NeteaseEapiChannel {
            var lastParams: Map<String, Any> = emptyMap()
            override suspend fun request(
                path: String,
                params: Map<String, Any>,
                rawCipherResponse: Boolean,
                cookie: String?,
            ): String? {
                lastParams = params
                return """{"code":200,"data":[{"url":"https://m801.music.126.net/a.mp3","type":"mp3"}]}"""
            }

            override suspend fun songDetailRaw(songIds: List<Long>, cookie: String?): String? = null
        }
        val client = NeteasePlaylistClient(FakeHttp(null, status = false), eapi)

        assertEquals(
            "https://m801.music.126.net/a.mp3",
            client.playUrl(123, NeteasePlaylistClient.Quality.Lossless, cookie = null),
        )
        // 匿名请求无损会被风控，参数里应当是 exhigh
        assertEquals("exhigh", eapi.lastParams["level"])
        assertEquals("[123]", eapi.lastParams["ids"])
    }

    @Test
    fun `在线曲播放入口用 outer 跳转而不是 CDN 直链`() {
        assertEquals(
            "https://music.163.com/song/media/outer/url?id=3456047164.mp3",
            NeteasePlaylistClient.playUrlFallback(3456047164),
        )
        assertEquals("netease:3456047164", NeteasePlaylistClient.REMOTE_PREFIX + 3456047164)
    }
}
