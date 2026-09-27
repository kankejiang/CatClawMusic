package com.catclaw.music.kt.data.remote

import com.catclaw.music.kt.core.model.ConnectionProfile
import java.io.IOException
import org.junit.Assert.assertEquals
import org.junit.Assert.assertNull
import org.junit.Assert.assertThrows
import org.junit.Assert.assertTrue
import org.junit.Test

/**
 * Subsonic 客户端保真测试：认证参数、URL 形状、数组/单对象容错、错误响应、歌词转 LRC。
 * MD5 期望值由 python 独立算出（hashlib.md5(b'hunter2'+b'abcdef012345').hexdigest()），不是拿实现自证。
 */
class SubsonicClientTest {

    private class Recorder(val body: String) : HttpFetcher {
        var url: String = ""
        override fun call(
            method: String,
            url: String,
            headers: Map<String, String>,
            body: String?,
        ): HttpResult {
            this.url = url
            return HttpResult(200, null, this.body)
        }
    }

    private val profile = ConnectionProfile(
        name = "navi",
        host = "music.example.com",
        port = 4535,
        userName = "tester",
        password = "hunter2",
    )

    private fun client(body: String, basePath: String = ""): Pair<SubsonicClient, Recorder> {
        val recorder = Recorder(body)
        val client = SubsonicClient(
            profile.copy(basePath = basePath),
            fetcher = recorder,
            saltSupplier = { "abcdef012345" },
        )
        return client to recorder
    }

    @Test
    fun `认证参数是 md5 密码拼 salt 的小写十六进制`() {
        val (client, _) = client("{}")
        val params = client.authParams()
        assertTrue(params, params.contains("t=1cc621900651b5c473617990ba8d7192"))
        assertTrue(params, params.contains("&s=abcdef012345"))
        assertTrue(params, params.startsWith("u=tester&"))
        assertTrue(params, params.contains("&v=1.16.1&c=CatClawMusic&f=json"))
    }

    @Test
    fun `URL 形状是 base 加 rest 端点，BasePath 参与拼接`() {
        val (plain, _) = client("{}")
        assertTrue(plain.apiUrl("ping.view").startsWith("http://music.example.com:4535/rest/ping.view?u="))
        val (withPath, _) = client("{}", basePath = "/sub/")
        assertTrue(withPath.apiUrl("ping.view").startsWith("http://music.example.com:4535/sub/rest/ping.view?u="))
    }

    @Test
    fun `ping 取到服务端版本`() {
        val (client, recorder) = client(
            """{"subsonic-response":{"status":"ok","version":"1.16.1","openSubsonic":{"version":"0.55.7"}}}""",
        )
        val ping = client.ping()
        assertTrue(ping.message, ping.ok)
        assertEquals("0.55.7", ping.version)
        assertTrue(recorder.url, recorder.url.contains("ping.view"))
    }

    @Test
    fun `ping 网络不通时不抛而是报 unreachable`() {
        val client = SubsonicClient(
            profile,
            fetcher = object : HttpFetcher {
                override fun call(
                    method: String,
                    url: String,
                    headers: Map<String, String>,
                    body: String?,
                ): HttpResult = throw IOException("connect failed")
            },
            saltSupplier = { "abcdef012345" },
        )
        val ping = client.ping()
        assertEquals(false, ping.ok)
        assertEquals("connect failed", ping.message)
    }

    @Test
    fun `status failed 抛带错误类型的 IOException`() {
        val (client, _) = client(
            """{"subsonic-response":{"status":"failed","error":{"code":40,"type":"wrongPassword","message":"bad"}}}""",
        )
        val e = assertThrows(IOException::class.java) { client.albumList("newest") }
        assertTrue(e.message!!, e.message!!.contains("wrongPassword"))
    }

    @Test
    fun `专辑列表只有一张时退化成对象也要能吃`() {
        val (client, _) = client(
            """{"subsonic-response":{"status":"ok","albumList2":{"album":{"id":"al-1","name":"Rumours","artist":"Fleetwood Mac"}}}}""",
        )
        val list = client.albumList("alphabeticalByArtist")
        assertEquals(1, list.size)
        assertEquals("al-1", list[0].id)
        assertEquals("Fleetwood Mac", list[0].artist)
    }

    @Test
    fun `专辑曲目解析出时长大小码率与带认证的播放地址`() {
        val (client, _) = client(
            """{"subsonic-response":{"status":"ok","album":{"song":[
              {"id":"tr-9","title":"The Chain","artist":"Fleetwood Mac","album":"Rumours",
               "duration":264,"size":10485760,"bitRate":320,"coverArt":"cv-3"},
              {"id":"tr-10","title":"Second Hand News","duration":162}]}}}""",
        )
        val songs = client.albumSongs("al-1")
        assertEquals(2, songs.size)
        val first = songs[0]
        assertEquals("The Chain", first.title)
        assertEquals(264, first.durationSeconds)
        assertEquals(10_485_760L, first.sizeBytes)
        assertEquals(320, first.bitrateKbps)
        assertTrue(first.streamUrl, first.streamUrl.startsWith("http://music.example.com:4535/rest/stream.view?id=tr-9&u=tester"))
        assertTrue(first.streamUrl, first.streamUrl.contains("t=1cc621900651b5c473617990ba8d7192"))
        assertTrue(first.coverArtUrl!!, first.coverArtUrl!!.contains("getCoverArt.view?id=cv-3"))
        // 缺字段的第二首按零值兜住，不炸
        assertEquals("Second Hand News", songs[1].title)
        assertEquals(0, songs[1].sizeBytes)
        assertNull(songs[1].coverArtUrl)
    }

    @Test
    fun `search3 命中曲目标题`() {
        val (client, recorder) = client(
            """{"subsonic-response":{"status":"ok","searchResult3":{"song":{"id":"tr-1","title":"Dreams","artist":"Fleetwood Mac"}}}}""",
        )
        val hits = client.search("dreams")
        assertEquals(1, hits.size)
        assertEquals("Dreams", hits[0].title)
        assertTrue(recorder.url, recorder.url.contains("search3.view?query=dreams"))
    }

    @Test
    fun `structuredLyrics 转成 LRC 时间戳`() {
        val (client, _) = client(
            """{"subsonic-response":{"status":"ok","lyricsList":{"structuredLyrics":{"lang":"en",
               "line":[{"start":0,"value":"First line"},{"start":61234,"value":"Second line"}]}}}}""",
        )
        assertEquals("[00:00.000]First line\n[01:01.234]Second line", client.lyrics("tr-1"))
    }

    @Test
    fun `歌词三种形态都没有时返回 null`() {
        val (client, _) = client("""{"subsonic-response":{"status":"ok"}}""")
        assertNull(client.lyrics("tr-1"))
    }

    @Test
    fun `md5 已知向量`() {
        assertEquals("a4d49a3476cad06df36ecdafc7fc6248", md5Hex("pw0123456789ab"))
        assertEquals("d41d8cd98f00b204e9800998ecf8427e", md5Hex(""))
    }
}
