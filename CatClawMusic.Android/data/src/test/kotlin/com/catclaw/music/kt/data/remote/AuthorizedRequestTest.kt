package com.catclaw.music.kt.data.remote

import java.util.Base64
import okhttp3.Credentials
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertNull
import org.junit.Test

/**
 * 远程取字节的凭据处理。
 *
 * 这里为什么单独立一条：`UrlCredentials.split` 回的是**拼好的 `Authorization` 头**，
 * 而它的一个调用点把第二个返回值当原始 `user:pass` 又拆了一次，于是把
 * `Basic <base64>` 当成用户名重新编码了一层 —— 每次请求都 401，而歌词是
 * 「拿不到就走内嵌」的静默降级，所以既不崩也不报错，只是远程侧车歌词一直不生效。
 *
 * 现在请求由 `authorizedRequest` 一处组装（远程歌词、远程头部字节都走它），
 * 所以断言也集中在这里。
 */
class AuthorizedRequestTest {

    @Test
    fun embeddedCredentialsBecomeOneBasicHeader() {
        val request = authorizedRequest("http://cat:p%40ss@192.0.2.1:5005/dav/%E5%A4%9C/a.lrc")
        val header = request.header("Authorization")
        // 独立算法算出期望值：用户是 cat、密码解出来是 p@ss
        assertEquals(Credentials.basic("cat", "p@ss", Charsets.UTF_8), header)
        // 正控制：旧写法把整头当用户名再编一层，解出来会以 "Basic " 开头
        val decoded = String(Base64.getDecoder().decode(header!!.removePrefix("Basic ").trim()), Charsets.UTF_8)
        assertEquals("cat:p@ss", decoded)
        assertFalse(decoded.startsWith("Basic "))
    }

    @Test
    fun userInfoIsStrippedFromTheRequestUrl() {
        val request = authorizedRequest("http://cat:secret@192.0.2.1:5005/dav/a.lrc")
        assertEquals("http://192.0.2.1:5005/dav/a.lrc", request.url.toString())
    }

    @Test
    fun anonymousUrlSendsNoAuthorization() {
        val request = authorizedRequest("https://openlist.example.com/d/Music/a.lrc?sign=AbCd")
        assertNull(request.header("Authorization"))
        assertEquals("https://openlist.example.com/d/Music/a.lrc?sign=AbCd", request.url.toString())
    }

    @Test
    fun rangeHeaderOnlyAppearsWhenAsked() {
        assertNull(authorizedRequest("http://host/a.mp3").header("Range"))
        // 头部字节按 0..size-1 取，闭区间写错就会多拿一个字节
        assertEquals("bytes=0-262143", authorizedRequest("http://host/a.mp3", rangeBytes = 256 * 1024).header("Range"))
        assertNull(authorizedRequest("http://host/a.mp3", rangeBytes = 0).header("Range"))
    }
}
