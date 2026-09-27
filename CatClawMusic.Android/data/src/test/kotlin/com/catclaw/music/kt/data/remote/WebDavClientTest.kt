package com.catclaw.music.kt.data.remote

import com.catclaw.music.kt.core.library.MediaScanRules
import com.catclaw.music.kt.core.model.ConnectionProfile
import java.util.Base64
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Test

/**
 * WebDAV 移植保真测试：PROPFIND 报文形状、手动重放、multistatus 解析、
 * 前缀探测顺序、OpenList 回退与取流地址。全程假 fetcher，不碰网络。
 */
class WebDavClientTest {

    private class Call(val method: String, val url: String, val headers: Map<String, String>, val body: String?)

    private class Fake(private var responder: (Call) -> HttpResult) : HttpFetcher {
        val calls = mutableListOf<Call>()
        var handler: (Call) -> HttpResult = responder
        override fun call(method: String, url: String, headers: Map<String, String>, body: String?): HttpResult {
            val call = Call(method, url, headers, body)
            calls += call
            return handler(call)
        }
    }

    private val profile = ConnectionProfile(
        name = "nas",
        host = "192.0.2.10",
        port = 5005,
        userName = "cat",
        password = "p@ss",
    )

    private val okMultistatus = """
        <?xml version="1.0" encoding="utf-8"?>
        <D:multistatus xmlns:D="DAV:">
          <D:response>
            <D:href>/dav/Music/</D:href>
            <D:propstat><D:prop>
              <D:resourcetype><D:collection/></D:resourcetype>
              <D:displayname>Music</D:displayname>
            </D:prop><D:status>HTTP/1.1 200 OK</D:status></D:propstat>
          </D:response>
          <D:response>
            <D:href>http://192.0.2.10:5005/dav/Music/%E4%B8%AD%E6%96%87.mp3</D:href>
            <D:propstat><D:prop>
              <D:resourcetype/>
              <D:getcontentlength>4096</D:getcontentlength>
              <D:getlastmodified>Wed, 15 May 2024 08:00:00 GMT</D:getlastmodified>
            </D:prop><D:status>HTTP/1.1 200 OK</D:status></D:propstat>
          </D:response>
          <D:response>
            <D:href>/dav/Music/中文.lrc</D:href>
            <D:propstat><D:prop>
              <D:resourcetype/>
              <D:displayname>中文.lrc</D:displayname>
              <D:getcontentlength>120</D:getcontentlength>
            </D:prop><D:status>HTTP/1.1 200 OK</D:status></D:propstat>
          </D:response>
        </D:multistatus>
    """.trimIndent()

    @Test
    fun `PROPFIND 报文带 Depth 四个属性与 UTF8 Basic`() {
        val fake = Fake { HttpResult(207, null, okMultistatus) }
        WebDavClient(profile, fake).list("/Music")
        val call = fake.calls.single()
        assertEquals("PROPFIND", call.method)
        assertEquals("1", call.headers["Depth"])
        assertEquals("application/xml; charset=utf-8", call.headers["Content-Type"])
        for (prop in listOf("resourcetype", "getcontentlength", "getlastmodified", "displayname")) {
            assertTrue("缺属性 $prop", call.body!!.contains("D:$prop"))
        }
        val expected = "Basic " + Base64.getEncoder().encodeToString("cat:p@ss".toByteArray(Charsets.UTF_8))
        assertEquals(expected, call.headers["Authorization"])
        assertEquals("http://192.0.2.10:5005/Music/", call.url)
    }

    @Test
    fun `重定向后仍然用 PROPFIND 重放而不是被降级成 GET`() {
        val fake = Fake { call ->
            when {
                !call.url.contains("/dav/") -> HttpResult(307, "/dav/Music/", "")
                else -> HttpResult(207, null, okMultistatus)
            }
        }
        val files = WebDavClient(profile, fake).list("/Music")
        assertEquals(listOf("PROPFIND", "PROPFIND"), fake.calls.map { it.method })
        assertEquals("http://192.0.2.10:5005/dav/Music/", fake.calls[1].url)
        // 第二跳必须把原来的请求体也带上
        assertEquals(fake.calls[0].body, fake.calls[1].body)
        assertEquals(3, files.size)
    }

    @Test
    fun `multistatus 解析出目录标记 绝对 href 与百分号解码`() {
        val files = parseMultistatus(okMultistatus)
        assertEquals(3, files.size)
        val dir = files[0]
        assertTrue(dir.isDirectory)
        assertEquals("Music", dir.name)
        assertEquals("/dav/Music/", dir.path)   // 目录 href 保留尾部斜杠（与 C# 一致）

        val song = files[1]
        assertFalse(song.isDirectory)
        assertEquals("中文.mp3", song.name)           // href 末段解码
        // path 保留百分号转义：C# 用 Uri.AbsolutePath（也是转义态），
        // 它同时是 RemoteId 与拼 URL 用的路径，解码反而会打出带空格的坏 URL
        assertEquals("/dav/Music/%E4%B8%AD%E6%96%87.mp3", song.path)
        assertEquals(4096L, song.size)

        val lrc = files[2]
        assertEquals("中文.lrc", lrc.name)
        assertEquals(120L, lrc.size)
    }

    @Test
    fun `前缀写法差异与默认命名空间都能解析`() {
        // 真机上 Android 的 DOM 与 JVM 的 Xerces 对命名空间处理不一致，
        // 所以按本地名匹配：下面这份完全不带前缀，也要读出同样的结果
        val plain = """
            <?xml version="1.0" encoding="utf-8"?>
            <multistatus xmlns="DAV:">
              <response>
                <href>/dav/Music/x.flac</href>
                <propstat><prop>
                  <resourcetype/>
                  <getcontentlength>2048</getcontentlength>
                </D:prop></propstat>
              </response>
            </multistatus>
        """.trimIndent().replace("</D:prop>", "</prop>")
        val files = parseMultistatus(plain)
        assertEquals(1, files.size)
        assertEquals("x.flac", files[0].name)
        assertEquals("/dav/Music/x.flac", files[0].path)
        assertEquals(2048L, files[0].size)
        assertFalse(files[0].isDirectory)
    }

    @Test
    fun `XML 里带外部实体时直接拒解析而不是去取文件`() {
        val evil = """
            <?xml version="1.0"?>
            <!DOCTYPE foo [ <!ENTITY xxe SYSTEM "file:///etc/passwd"> ]>
            <D:multistatus xmlns:D="DAV:"><D:response><D:href>&xxe;</D:href></D:response></D:multistatus>
        """.trimIndent()
        assertTrue(parseMultistatus(evil).isEmpty())
    }

    @Test
    fun `播放地址把认证插进 scheme 之后并转义特殊字符`() {
        val client = WebDavClient(profile, Fake { HttpResult(207, null, okMultistatus) })
        assertEquals(
            "http://cat:p%40ss@192.0.2.10:5005/Music/a.mp3",
            client.buildStreamUrl("/Music/a.mp3"),
        )
    }

    @Test
    fun `无用户名的服务器不插认证`() {
        val client = WebDavClient(profile.copy(userName = "", password = ""), Fake { HttpResult(207, null, okMultistatus) })
        assertEquals("http://192.0.2.10:5005/Music/a.mp3", client.buildStreamUrl("/Music/a.mp3"))
    }

    @Test
    fun `探测顺序是 basePath 空前缀 dav webdav`() {
        val fake = Fake { call ->
            if (call.url == "http://192.0.2.10:5005/dav/") HttpResult(207, null, okMultistatus)
            else HttpResult(404, null, "")
        }
        val result = WebDavClient(profile.copy(basePath = "/base"), fake).test()
        assertTrue(result.ok)
        assertEquals("/dav", result.davPrefix)
        assertEquals(
            listOf(
                "http://192.0.2.10:5005/base/",
                "http://192.0.2.10:5005/",
                "http://192.0.2.10:5005/dav/",
            ),
            fake.calls.filter { it.method == "PROPFIND" }.map { it.url },
        )
        assertEquals("0", fake.calls.first().headers["Depth"])
    }

    @Test
    fun `depth 全库遇到 405 自动切 OpenList 并走 REST 列表`() {
        var logins = 0
        val fake = Fake { call ->
            when {
                call.method == "PROPFIND" -> HttpResult(405, null, "")
                call.url.endsWith("/api/login") -> {
                    logins++
                    HttpResult(200, null, """{"code":200,"message":"","data":{"token":"TK-1"}}""")
                }
                call.url.endsWith("/api/fs/list") -> {
                    assertEquals("TK-1", call.headers["Authorization"])
                    HttpResult(
                        200, null,
                        """{"code":200,"data":{"content":[{"name":"a.mp3","size":11,"dir":false},
                           {"name":"sub","size":0,"dir":true}]}}""",
                    )
                }
                else -> HttpResult(404, null, "{}")
            }
        }
        val client = WebDavClient(profile, fake)
        val files = client.listAll("/")
        assertEquals(1, logins)
        assertEquals(listOf("a.mp3", "sub"), files.map { it.name })
        assertEquals(false, files[0].isDirectory)
        assertEquals(11L, files[0].size)
        assertEquals("/sub", files[1].path)
        assertEquals(true, files[1].isDirectory)
        assertEquals(WebDavClient.ServerType.OpenList, client.serverType)
    }

    @Test
    fun `OpenList 取流优先 raw_url 否则退回 d 路径加 sign`() {
        fun clientReturning(dataJson: String): Pair<WebDavClient, Fake> {
            val fake = Fake { call ->
                when {
                    call.url.endsWith("/api/login") ->
                        HttpResult(200, null, """{"code":200,"data":{"token":"TK"}}""")
                    call.url.endsWith("/api/fs/get") ->
                        HttpResult(200, null, """{"code":200,"data":$dataJson}""")
                    else -> HttpResult(404, null, "{}")
                }
            }
            val client = WebDavClient(profile, fake)
            assertTrue(client.useOpenList())
            return client to fake
        }
        val (withRaw, _) = clientReturning("""{"raw_url":"https://cdn.example/x.mp3?sig=9"}""")
        assertEquals("https://cdn.example/x.mp3?sig=9", withRaw.streamUrlFor("/music/a.mp3"))

        val (signed, fakeSigned) = clientReturning("""{"sign":"S-9"}""")
        assertEquals("http://192.0.2.10:5005/d/music/a.mp3?sign=S-9", signed.streamUrlFor("/music/a.mp3"))
        assertTrue(fakeSigned.calls.last().url.endsWith("/api/fs/get"))
    }

    @Test
    fun `音频扩展名白名单与 C# 同步`() {
        assertTrue(MediaScanRules.isAudioFile("/dav/Music/a.FLAC"))
        assertTrue(MediaScanRules.isAudioFile("song.m4a"))
        assertFalse("歌词不是音频", MediaScanRules.isAudioFile("song.lrc"))
        assertFalse("无扩展名", MediaScanRules.isAudioFile("LICENSE"))
        assertEquals("中文", MediaScanRules.titleFromFileName("/dav/Music/中文.mp3"))
        assertEquals("/dav/Music", MediaScanRules.parentDir("/dav/Music/中文.mp3"))
    }
}
