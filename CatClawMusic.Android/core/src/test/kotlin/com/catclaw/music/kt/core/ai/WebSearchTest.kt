package com.catclaw.music.kt.core.ai

import kotlinx.coroutines.test.runTest
import org.junit.Assert.assertEquals
import org.junit.Assert.assertTrue
import org.junit.Test

/** 联网搜索：三个引擎的 HTML 解析 + 多源聚合顺序。全部用固定页面，不打网络。 */
class WebSearchTest {

    private class FakeGateway(
        val pages: Map<String, String> = emptyMap(),
        val exaResponse: String? = null,
        val fail: Boolean = false,
    ) : WebFetchGateway {
        val htmlUrls = mutableListOf<String>()
        var exaCalls = 0

        override suspend fun getHtml(url: String): String? {
            if (fail) throw java.io.IOException("connection reset")
            htmlUrls += url
            return pages.entries.firstOrNull { url.startsWith(it.key) }?.value
        }

        override suspend fun postJson(url: String, payload: String): String? {
            if (fail) throw java.io.IOException("connection reset")
            exaCalls++
            return exaResponse
        }
    }

    private fun searcher(gateway: WebFetchGateway) = WebSearcher(gateway, perSourceTimeoutMs = 2_000)

    private val bingPage = """
        <ol><li class="b_algo">
          <h2><a href="//cn.bing.com/a/1">猫爪<em>音乐</em> 官网</a></h2>
          <p>一个 &quot;本地&quot; 播放器</p>
        </li>
        <li class="b_algo"><h2><a href="https://m.example/x">第二条</a></h2><p>摘要二</p></li>
        <li class="b_algo"><h2><a href="https://m.example/y">第三条</a></h2></li>
        </ol>
    """.trimIndent()

    private val baiduPage = """
        <h3 class="t"><a href="/s?wd=x&real=1">百度结果 &amp; 更多</a></h3>
        <h3 class="t"><a href="https://other.site/p">另一条</a></h3>
    """.trimIndent()

    private val ddgPage = """
        <a class="result__a" href="//duckduckgo.com/l/?uddg=https%3A%2F%2Freal.site%2Fpage&rut=abc">DDG 标题</a>
        <a class="result__snippet" href="#">摘要 <b>三</b></a>
        <a class="result__a" href="https://direct.site/">直链结果</a>
    """.trimIndent()

    @Test
    fun bingBlocksYieldTitleUrlAndSnippet() {
        val hits = WebSearcher.parseBing(bingPage)
        assertEquals(3, hits.size)
        assertEquals("猫爪 音乐 官网", hits[0].title)
        assertEquals("https://cn.bing.com/a/1", hits[0].url)
        assertEquals("一个 \"本地\" 播放器", hits[0].snippet)
        assertEquals("", hits[2].snippet)
    }

    @Test
    fun baiduRelativeLinksGetTheSitePrefix() {
        val hits = WebSearcher.parseBaidu(baiduPage)
        assertEquals(listOf("https://www.baidu.com/s?wd=x&real=1", "https://other.site/p"), hits.map { it.url })
        assertEquals("百度结果 & 更多", hits[0].title)
    }

    @Test
    fun ddgRedirectLinksAreUnwrappedAndSnippetsPairByOrder() {
        val hits = WebSearcher.parseDuckDuckGo(ddgPage)
        assertEquals(2, hits.size)
        assertEquals("https://real.site/page", hits[0].url)
        assertEquals("摘要 三", hits[0].snippet)
        assertEquals("https://direct.site/", hits[1].url)
        assertEquals("", hits[1].snippet)
    }

    @Test
    fun mcpTextHandlesBothBareJsonAndSse() {
        val bare = """{"result":{"content":[{"type":"text","text":"第一段"},{"type":"text","text":"第二段"}]}}"""
        assertEquals("第一段\n第二段", WebSearcher.parseMcpText(bare))
        val sse = "event: message\ndata: {\"result\":{\"content\":[{\"text\":\"SSE 正文\"}]}}\ndata: [DONE]\n"
        assertEquals("SSE 正文", WebSearcher.parseMcpText(sse))
        assertEquals(null, WebSearcher.parseMcpText("not json"))
        assertEquals(null, WebSearcher.parseMcpText("""{"result":{"content":[]}}"""))
    }

    @Test
    fun cleanTextStripsTagsEntitiesAndCollapsesWhitespace() {
        assertEquals("a b & c", WebSearcher.cleanText("  <em>a</em>&nbsp;b &amp; <b>c</b>  "))
    }

    @Test
    fun longExaResultShortCircuitsTheEngines() = runTest {
        val gateway = FakeGateway(exaResponse = """{"result":{"content":[{"text":"${"Exa 内容。".repeat(20)}"}]}}""")
        val outcome = searcher(gateway).search("猫爪音乐")
        assertTrue(outcome is WebOutcome.Exa)
        assertEquals(1, gateway.exaCalls)
        assertEquals("够用时不该再去抓三个搜索引擎", 0, gateway.htmlUrls.size)
    }

    @Test
    fun shortExaResultFallsThroughToTheEngines() = runTest {
        val gateway = FakeGateway(
            exaResponse = """{"result":{"content":[{"text":"太短"}]}}""",
            pages = mapOf("https://cn.bing.com/search" to bingPage),
        )
        val outcome = searcher(gateway).search("猫爪音乐")
        assertTrue("实际：$outcome", outcome is WebOutcome.Hits)
        assertEquals(3, (outcome as WebOutcome.Hits).results.size)
        assertTrue(gateway.htmlUrls.any { it.contains("baidu.com") })
        assertTrue(gateway.htmlUrls.any { it.contains("duckduckgo.com") })
        assertTrue(gateway.htmlUrls.single { it.contains("cn.bing.com") }.contains("q=%E7%8C%AB%E7%88%AA%E9%9F%B3%E4%B9%90"))
    }

    @Test
    fun resultsAreDedupedCaseInsensitivelyAcrossSources() = runTest {
        val bing = """<li class="b_algo"><h2><a href="https://Site.A/">A</a></h2></li>"""
        val baidu = """<h3><a href="https://site.a/">a 重复</a></h3><h3><a href="https://b.site/">B</a></h3>"""
        val gateway = FakeGateway(pages = mapOf("https://cn.bing.com/search" to bing, "https://www.baidu.com/s" to baidu))
        val outcome = searcher(gateway).search("x")
        assertEquals(listOf("https://Site.A/", "https://b.site/"), (outcome as WebOutcome.Hits).results.map { it.url })
    }

    @Test
    fun resultsAreCappedAtEight() = runTest {
        val many = (1..12).joinToString("") { """<li class="b_algo"><h2><a href="https://s/$it">r$it</a></h2></li>""" }
        val gateway = FakeGateway(pages = mapOf("https://cn.bing.com/search" to many))
        assertEquals(8, (searcher(gateway).search("x") as WebOutcome.Hits).results.size)
    }

    @Test
    fun allSourcesEmptyIsReportedAsNoneNotAnError() = runTest {
        val outcome = searcher(FakeGateway()).search("x")
        assertEquals(WebOutcome.None, outcome)
    }

    @Test
    fun unreachableSourcesSaySoInsteadOfImplyingNoNetwork() = runTest {
        val outcome = searcher(FakeGateway(fail = true)).search("x")
        assertTrue(outcome is WebOutcome.Failed)
        assertTrue((outcome as WebOutcome.Failed).reason.contains("所有搜索源都无法访问"))
    }

    @Test
    fun blankQueryNeverTouchesTheGateway() = runTest {
        val gateway = FakeGateway()
        assertEquals(WebOutcome.Failed("请提供搜索关键词"), searcher(gateway).search("   "))
        assertEquals(0, gateway.exaCalls + gateway.htmlUrls.size)
    }
}
