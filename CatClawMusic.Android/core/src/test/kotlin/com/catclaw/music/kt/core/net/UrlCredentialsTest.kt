package com.catclaw.music.kt.core.net

import org.junit.Assert.assertEquals
import org.junit.Assert.assertNull
import org.junit.Test

/** URL 内嵌凭据的拆解：真机上「下载 401」就是这一步没做对 */
class UrlCredentialsTest {

    @Test
    fun `拆出 Basic 头并把凭据从 URL 里去掉`() {
        val (url, header) = UrlCredentials.split("http://cat:p%40ss@192.0.2.1:5005/dav/Music/a.mp3")
        assertEquals("http://192.0.2.1:5005/dav/Music/a.mp3", url)
        // base64("cat:p@ss")
        assertEquals("Basic Y2F0OnBAc3M=", header)
    }

    @Test
    fun `没有 userinfo 时原样返回`() {
        val plain = "http://192.0.2.1:4535/rest/stream.view?id=tr-1&u=demo"
        val (url, header) = UrlCredentials.split(plain)
        assertEquals(plain, url)
        assertNull(header)
    }

    @Test
    fun `路径里的 at 号不算凭据`() {
        val url = "http://host:80/a@b/c.mp3"
        assertEquals(url to null, UrlCredentials.split(url))
    }

    @Test
    fun `百分号解码只认 %XX 且保留加号`() {
        assertEquals("p@ss", UrlCredentials.percentDecode("p%40ss"))
        assertEquals("a+b", UrlCredentials.percentDecode("a+b"))
        assertEquals("中文", UrlCredentials.percentDecode("%E4%B8%AD%E6%96%87"))
        assertEquals("bad%", UrlCredentials.percentDecode("bad%"))
        assertEquals("%2", UrlCredentials.percentDecode("%2"))
    }

    @Test
    fun `空密码也要拼出冒号`() {
        val (_, header) = UrlCredentials.split("http://onlyuser@host/x")
        assertEquals("Basic " + java.util.Base64.getEncoder().encodeToString("onlyuser:".toByteArray()), header)
    }
}
