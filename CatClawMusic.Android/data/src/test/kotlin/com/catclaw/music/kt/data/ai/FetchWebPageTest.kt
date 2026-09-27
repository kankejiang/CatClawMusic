package com.catclaw.music.kt.data.ai

import com.catclaw.music.kt.core.playback.SessionLauncher
import com.catclaw.music.kt.core.net.HtmlText
import java.lang.reflect.Proxy
import kotlinx.coroutines.test.runTest
import kotlinx.serialization.json.Json
import kotlinx.serialization.json.JsonElement
import kotlinx.serialization.json.jsonObject
import kotlinx.serialization.json.jsonPrimitive
import org.junit.Assert.assertEquals
import org.junit.Assert.assertTrue
import org.junit.Test

/** `fetch_web_page` 的分支。全程内存假抓取器，不打网络也不需要设备。 */
class FetchWebPageTest {

    private val fetcher = FakeFetcher(mutableListOf())

    private class Unused

    private val tools: MusicAgentTools = MusicAgentTools(
        player = Proxy.newProxyInstance(
            com.catclaw.music.kt.core.playback.PlayerApi::class.java.classLoader,
            arrayOf(com.catclaw.music.kt.core.playback.PlayerApi::class.java),
        ) { _, method, _ -> throw UnsupportedOperationException(method.name) } as com.catclaw.music.kt.core.playback.PlayerApi,
        dao = Proxy.newProxyInstance(
            com.catclaw.music.kt.data.db.LibraryDao::class.java.classLoader,
            arrayOf(com.catclaw.music.kt.data.db.LibraryDao::class.java),
        ) { _, method, _ -> throw UnsupportedOperationException(method.name) } as com.catclaw.music.kt.data.db.LibraryDao,
        session = SessionLauncher { },
        playlists = Proxy.newProxyInstance(
            com.catclaw.music.kt.data.db.PlaylistDao::class.java.classLoader,
            arrayOf(com.catclaw.music.kt.data.db.PlaylistDao::class.java),
        ) { _, method, _ -> throw UnsupportedOperationException(method.name) } as com.catclaw.music.kt.data.db.PlaylistDao,
        fetcher = fetcher,
    )

    private val tool get() = tools.all.first { it.name == "fetch_web_page" }

    private fun field(json: String, key: String): String =
        Json.parseToJsonElement(json).jsonObject[key]?.jsonPrimitive?.content.orEmpty()

    @Test
    fun contractMatchesCSharp() {
        assertEquals("fetch_web_page", tool.name)
        assertEquals(listOf("url"), tool.required)
        assertTrue("抓取是只读的，要能与别的只读工具并行", tool.isReadOnly)
        assertTrue(tool.description.contains("页面标题"))
    }

    @Test
    fun urlIsValidatedBeforeAnyNetworkCall() = runTest {
        assertTrue(tool.execute("{}").contains("请提供要抓取的 URL"))
        assertTrue(tool.execute("""{"url":"ftp://x/y"}""").contains("必须以 http:// 或 https:// 开头"))
        assertEquals("校验没过就不该发请求", 0, fetcher.calls.get())
    }

    @Test
    fun htmlBecomesTitlePlusReadableText() = runTest {
        fetcher.replies += page(
            type = "text/html; charset=utf-8",
            body = "<html><head><title> 猫爪 &amp; 音乐 </title><script>var x=1;</script></head>" +
                "<body><style>b{}</style><h1>歌词</h1><p>第一行<br>第二行</p><!--注释--><div>尾注</div></body></html>",
        )
        val result = tool.execute("""{"url":"https://example.com/a"}""")
        assertEquals("猫爪 & 音乐", field(result, "title"))
        val text = field(result, "content")
        assertTrue("脚本内容不该混进正文: $text", !text.contains("var x=1"))
        assertTrue("注释不该混进正文: $text", !text.contains("注释"))
        assertTrue("块级结束要成行: $text", text.contains("第一行\n第二行"))
        assertEquals("https://example.com/a", field(result, "url"))
    }

    @Test
    fun plainJsonIsReturnedVerbatimButTruncated() = runTest {
        fetcher.replies += page(type = "application/json", body = "x".repeat(9_000))
        val result = tool.execute("""{"url":"https://example.com/a"}""")
        assertEquals(6_000, field(result, "content").length)
        assertEquals("", field(result, "title"))
    }

    @Test
    fun forbiddenWithoutChallengeHeaderIsNotRetried() = runTest {
        fetcher.replies += page(status = 403, body = "")
        assertTrue(tool.execute("""{"url":"https://example.com/a"}""").contains("HTTP 403"))
        assertEquals("没有 cf-mitigated 就换 UA 是白等一次 RTT", 1, fetcher.calls.get())
    }

    @Test
    fun cloudflareChallengeIsRetriedOnceWithHonestUserAgent() = runTest {
        fetcher.replies += page(status = 403, cf = true, body = "")
        fetcher.replies += page(status = 200, body = "<title>ok</title><p>正文</p>")
        val result = tool.execute("""{"url":"https://example.com/a"}""")
        assertTrue(result.contains("正文"))
        assertEquals(2, fetcher.calls.get())
        assertEquals(listOf(null, "CatClawMusic/1.8"), fetcher.uas)
    }

    @Test
    fun nonSuccessStatusAfterRetryStillReportsTheCode() = runTest {
        fetcher.replies += page(status = 403, cf = true)
        fetcher.replies += page(status = 503, body = "")
        assertTrue(tool.execute("""{"url":"https://example.com/a"}""").contains("HTTP 503"))
    }

    /** 抓取自己抛异常（连接被重置这类）时，模型要拿到一句能读的错误，而不是让工具崩掉 */
    @Test
    fun transportFailureBecomesAnErrorMessageNotACrash() = runTest {
        val result = tool.execute("""{"url":"https://example.com/a"}""")
        assertTrue(result.contains("抓取失败"))
    }
}

/** HtmlText 的独立单测：这些细节用真网络反而测不出来 */
class HtmlTextTest {

    @Test
    fun doubleEscapedEntitiesAreNotDecodedTwice() {
        // `&amp;lt;` 的字面意思就是 `&lt;`，多趟替换会把它错解成 `<`
        assertEquals("&lt;", HtmlText.text("<p>&amp;lt;</p>"))
        assertEquals("<", HtmlText.text("<p>&lt;</p>"))
    }

    @Test
    fun numericEntitiesDecodeInBothBases() {
        assertEquals("中", HtmlText.text("<p>&#20013;</p>"))
        assertEquals("中", HtmlText.text("<p>&#x4E2D;</p>"))
        assertEquals("&notanentity;", HtmlText.text("<p>&notanentity;</p>"))
    }

    @Test
    fun titleComesOutCleaned() {
        // 行内标签按空格断开是刻意的：`<b>` 前后本来就是两个语素，粘成一词反而会误导模型
        assertEquals("猫爪 音乐", HtmlText.title("<title> 猫爪<b>音乐</b> </title>"))
        assertEquals("", HtmlText.title("<body>no title</body>"))
    }

    @Test
    fun longPagesAreCappedNotDropped() {
        val text = HtmlText.text("<p>" + "啊".repeat(7_000) + "</p>")
        assertEquals(6_000, text.length)
        assertEquals("短页原样", HtmlText.text("<p>短页原样</p>"))
    }
}
