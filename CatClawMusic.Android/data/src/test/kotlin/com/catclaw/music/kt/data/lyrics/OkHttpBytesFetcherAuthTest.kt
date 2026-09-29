package com.catclaw.music.kt.data.lyrics

import java.util.Base64
import okhttp3.Credentials
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertNull
import org.junit.Test

/**
 * 远程侧车歌词的凭据处理。
 *
 * 这里为什么单独立一条：`UrlCredentials.split` 回的是**拼好的 `Authorization` 头**，
 * 而这一处的旧代码把第二个返回值当原始 `user:pass` 又拆了一次，于是把
 * `Basic <base64>` 当成用户名重新编码了一层 —— 每次请求都 401，而歌词是
 * 「拿不到就走内嵌」的静默降级，所以既不崩也不报错，只是远程侧车歌词一直不生效。
 */
class OkHttpBytesFetcherAuthTest {

    @Test
    fun embeddedCredentialsBecomeOneBasicHeader() {
        val request = OkHttpBytesFetcher.authorizedRequest("http://cat:p%40ss@192.0.2.1:5005/dav/%E5%A4%9C/a.lrc")
        val header = request.header("Authorization")
        // 独立算法算出期望值：用户是 cat、密码解出来是 p@ss
        assertEquals(Credentials.basic("cat", "p@ss", Charsets.UTF_8), header)
        // 正控制：旧写法会把 "Basic xxx" 再编码一层，解出来是 "Basic xxx:" 这种形状
        val decoded = String(Base64.getDecoder().decode(header!!.removePrefix("Basic ").trim()), Charsets.UTF_8)
        assertEquals("cat:p@ss", decoded)
        assertFalse(decoded.startsWith("Basic "))
    }

    @Test
    fun userInfoIsStrippedFromTheRequestUrl() {
        val request = OkHttpBytesFetcher.authorizedRequest("http://cat:secret@192.0.2.1:5005/dav/a.lrc")
        assertEquals("http://192.0.2.1:5005/dav/a.lrc", request.url.toString())
    }

    @Test
    fun anonymousUrlSendsNoAuthorization() {
        val request = OkHttpBytesFetcher.authorizedRequest("https://openlist.example.com/d/Music/a.lrc?sign=AbCd")
        assertNull(request.header("Authorization"))
        assertEquals("https://openlist.example.com/d/Music/a.lrc?sign=AbCd", request.url.toString())
    }
}
