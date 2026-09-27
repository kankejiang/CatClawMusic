package com.catclaw.music.kt.core.ai

import kotlinx.coroutines.async
import kotlinx.coroutines.coroutineScope
import kotlinx.coroutines.withTimeout
import kotlinx.serialization.json.Json
import kotlinx.serialization.json.JsonArray
import kotlinx.serialization.json.JsonElement
import kotlinx.serialization.json.JsonObject
import kotlinx.serialization.json.JsonPrimitive
import kotlinx.serialization.json.buildJsonObject
import kotlinx.serialization.json.contentOrNull
import kotlinx.serialization.json.jsonObject
import kotlinx.serialization.json.jsonPrimitive
import kotlinx.serialization.json.put
import java.net.URLDecoder

/** 一条搜索结果。字段名与 C# 匿名对象的 JSON 键一致（title / url / snippet）。 */
data class WebHit(val title: String, val url: String, val snippet: String = "")

/** 搜索的四种结局，分开建模是为了让工具层不必靠"空列表"猜原因。 */
sealed interface WebOutcome {
    data class Exa(val content: String) : WebOutcome
    data class Hits(val results: List<WebHit>) : WebOutcome
    data object None : WebOutcome
    data class Failed(val reason: String) : WebOutcome
}

/**
 * 抓 HTML / 发 JSON 的缝。真实实现是 OkHttp；测试里给固定页面 ——
 * 三个搜索引擎的 HTML 结构差异和跳转链接解包，用真网络测等于不可测。
 */
interface WebFetchGateway {
    suspend fun getHtml(url: String): String?
    suspend fun postJson(url: String, payload: String): String?
}

/**
 * 联网搜索，移植自 C# `AgentTools.WebSearch.cs`。策略照抄：
 * 首选 Exa 托管搜索（免 key、返回 LLM-ready 文本），不够再自抓 cn.bing / 百度 / DuckDuckGo。
 *
 * ⚠ 顺序不是随便排的：Bing 是主源，**够 3 条就直接返回，不等慢源**；
 * 不足才依次补百度、DDG。全并行发出去但按这个优先级收，是"延迟低"和"结果够"之间的取舍。
 */
class WebSearcher(
    private val gateway: WebFetchGateway,
    private val perSourceTimeoutMs: Long = 6_000,
    private val log: (String) -> Unit = {},
) {

    suspend fun search(rawQuery: String): WebOutcome {
        val query = rawQuery.trim()
        if (query.isEmpty()) return WebOutcome.Failed("请提供搜索关键词")

        val exa = runCatching { exaSearch(query) }.getOrNull()
        if (!exa.isNullOrBlank() && exa.length > MIN_EXA_CHARS) return WebOutcome.Exa(exa)

        return try {
            // 三路并行发出、按优先级收取（与 C# 同一形状）
            val hits = coroutineScope {
                val bing = async { withTimeout(perSourceTimeoutMs) { bingSearch(query) } }
                val baidu = async { withTimeout(perSourceTimeoutMs) { baiduSearch(query) } }
                val ddg = async { withTimeout(perSourceTimeoutMs) { ddgSearch(query) } }
                // 键用**小写 URL**：LinkedHashMap 保插入顺序，同时天然做大小写不敏感去重
                // （C# 那边是 results 列表 + 一个 OrdinalIgnoreCase 的 HashSet，两件事分开存）
                val results = LinkedHashMap<String, WebHit>()
                bing.await().forEach { add(results, it) }
                if (results.size < ENOUGH) {
                    baidu.await().forEach { add(results, it) }
                    if (results.size < ENOUGH) ddg.await().forEach { add(results, it) }
                }
                results.values.toList()
            }
            if (hits.isEmpty()) WebOutcome.None else WebOutcome.Hits(hits)
        } catch (error: Exception) {
            // 明确说"搜索源不可达"，不要报成"设备没网"——两者对用户的处置完全不同
            log("web_search 全部源失败: ${error.message}")
            WebOutcome.Failed("联网搜索暂不可用（所有搜索源都无法访问，请稍后重试）：${error.message.orEmpty()}")
        }
    }

    private suspend fun exaSearch(query: String): String? {
        val payload = buildJsonObject {
            put("jsonrpc", "2.0")
            put("id", 1)
            put("method", "tools/call")
            put(
                "params",
                buildJsonObject {
                    put("name", "web_search_exa")
                    put(
                        "arguments",
                        buildJsonObject {
                            put("query", query)
                            put("type", "auto")
                            put("numResults", 8)
                            put("livecrawl", "fallback")
                            put("contextMaxCharacters", 10_000)
                        },
                    )
                },
            )
        }.toString()
        val body = gateway.postJson("https://mcp.exa.ai/mcp", payload) ?: return null
        return parseMcpText(body)
    }

    private suspend fun bingSearch(query: String): List<WebHit> {
        val html = gateway.getHtml("https://cn.bing.com/search?q=${encode(query)}&mkt=zh-CN&setlang=zh-hans&count=10")
            ?: return emptyList()
        return parseBing(clip(html))
    }

    private suspend fun baiduSearch(query: String): List<WebHit> {
        val html = gateway.getHtml("https://www.baidu.com/s?wd=${encode(query)}&rn=10") ?: return emptyList()
        return parseBaidu(clip(html))
    }

    private suspend fun ddgSearch(query: String): List<WebHit> {
        val html = gateway.getHtml("https://html.duckduckgo.com/html/?q=${encode(query)}") ?: return emptyList()
        return parseDuckDuckGo(clip(html))
    }

    private fun add(into: LinkedHashMap<String, WebHit>, hit: WebHit) {
        if (hit.url.isBlank() || into.size >= MAX_RESULTS) return
        val key = hit.url.lowercase()
        if (!into.containsKey(key)) into[key] = hit
    }

    private fun clip(html: String) = if (html.length > MAX_HTML) html.substring(0, MAX_HTML) else html

    companion object {
        const val MAX_RESULTS = 8
        const val ENOUGH = 3
        const val MIN_EXA_CHARS = 50
        const val MAX_HTML = 2 * 1024 * 1024
        private const val BING_BASE = "https://www.bing.com"
        private const val BAIDU_BASE = "https://www.baidu.com"

        private val JSON = Json { ignoreUnknownKeys = true }

        fun encode(raw: String): String =
            java.net.URLEncoder.encode(raw, "UTF-8").replace("+", "%20")

        /** 标签剥掉 + 实体解开 + 空白压平：三个源共用 */
        fun cleanText(input: String): String =
            com.catclaw.music.kt.core.net.HtmlText.decodeEntities(input.replace(Regex("<[^>]+>"), " "))
                .replace(Regex("\\s+"), " ")
                .trim()

        fun parseBing(html: String): List<WebHit> =
            BING_BLOCK.findAll(html).mapNotNull { block ->
                val anchor = BING_ANCHOR.find(block.value) ?: return@mapNotNull null
                val title = cleanText(anchor.groupValues[2])
                if (title.isEmpty()) return@mapNotNull null
                WebHit(
                    title = title,
                    url = absolutize(anchor.groupValues[1], BING_BASE),
                    snippet = BING_SNIPPET.find(block.value)?.let { cleanText(it.groupValues[1]) }.orEmpty(),
                )
            }.take(MAX_RESULTS).toList()

        fun parseBaidu(html: String): List<WebHit> =
            BAIDU_BLOCK.findAll(html).mapNotNull { m ->
                val title = cleanText(m.groupValues[2])
                if (title.isEmpty()) return@mapNotNull null
                WebHit(title = title, url = absolutize(m.groupValues[1], BAIDU_BASE))
            }.take(MAX_RESULTS).toList()

        fun parseDuckDuckGo(html: String): List<WebHit> {
            val snippets = DDG_SNIPPET.findAll(html).map { cleanText(it.groupValues[1]) }.toList()
            var index = 0
            val out = mutableListOf<WebHit>()
            for (m in DDG_BLOCK.findAll(html)) {
                val title = cleanText(m.groupValues[2])
                if (title.isEmpty()) { index++; continue }
                out += WebHit(title = title, url = unwrapDdg(m.groupValues[1]), snippet = snippets.getOrElse(index) { "" })
                index++
                if (out.size >= MAX_RESULTS) break
            }
            return out
        }

        /** DDG 给的是跳转链接 `//duckduckgo.com/l/?uddg=<编码后的真地址>`，不解开模型会读到一条没用的跳转 */
        fun unwrapDdg(raw: String): String {
            if (!raw.contains("/l/?uddg=", ignoreCase = true)) return absolutize(raw, "https://duckduckgo.com")
            val target = raw.substringAfter("uddg=", "").substringBefore("&")
            return runCatching { URLDecoder.decode(target, "UTF-8") }.getOrDefault(raw)
        }

        fun absolutize(url: String, base: String): String = when {
            url.startsWith("//") -> "https:$url"
            url.startsWith("/") -> base + url
            else -> url
        }

        /**
         * Exa 走 MCP：响应可能是裸 JSON，也可能是 SSE（若干 `data: {...}` 行）。
         * 两种都从 `result.content[].text` 取文本 —— 与 C# `ParseMcpText` 同一取法。
         */
        fun parseMcpText(body: String): String? {
            val candidates = if (body.contains("data:")) {
                body.lines().filter { it.startsWith("data:") }.map { it.removePrefix("data:").trim() }
            } else {
                listOf(body)
            }
            for (raw in candidates) {
                if (raw.isBlank() || raw == "[DONE]") continue
                val obj = runCatching { JSON.parseToJsonElement(raw).jsonObject }
                    .getOrNull() ?: continue
                val content = ((obj["result"] as? JsonObject)?.get("content") as? JsonArray)
                    ?: (obj["content"] as? JsonArray)
                val text = content?.mapNotNull { (it as? JsonObject)?.get("text")?.jsonPrimitive?.contentOrNull }
                    ?.joinToString("\n")
                if (!text.isNullOrBlank()) return text
            }
            return null
        }

        private val BING_BLOCK = Regex("(?s)<li class=\"b_algo.*?</li>")
        private val BING_ANCHOR = Regex("(?is)<h2[^>]*>\\s*<a[^>]*href=\"([^\"]+)\"[^>]*>(.*?)</a>")
        private val BING_SNIPPET = Regex("(?is)<p[^>]*>(.*?)</p>")
        private val BAIDU_BLOCK = Regex("(?is)<h3[^>]*>.*?<a[^>]*href=\"([^\"]+)\"[^>]*>(.*?)</a>.*?</h3>")
        private val DDG_BLOCK = Regex("(?is)<a[^>]*class=\"result__a\"[^>]*href=\"([^\"]+)\"[^>]*>(.*?)</a>")
        private val DDG_SNIPPET = Regex("(?is)<a[^>]*class=\"result__snippet\"[^>]*>(.*?)</a>")
    }
}

/** 结果序列化成给模型看的 JSON（与 C# 匿名对象同一组键） */
fun WebHit.toJsonElement(): JsonElement = buildJsonObject {
    put("title", JsonPrimitive(title))
    put("url", JsonPrimitive(url))
    put("snippet", JsonPrimitive(snippet))
}
