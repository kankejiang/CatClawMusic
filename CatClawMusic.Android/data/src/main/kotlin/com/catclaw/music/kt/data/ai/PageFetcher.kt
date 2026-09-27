package com.catclaw.music.kt.data.ai

import java.util.concurrent.TimeUnit
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.withContext
import okhttp3.OkHttpClient
import okhttp3.Request

/** 一页抓回来的东西。`cfMitigated` 单独拎出来，是因为工具要根据它决定"换 UA 再试一次"还是直接放弃。 */
data class FetchedPage(
    val url: String,
    val status: Int,
    val contentType: String,
    val body: String,
    val cfMitigated: Boolean = false,
)

/**
 * 抓网页的缝。
 *
 * 为什么要有这个接口而不是在工具里直接发请求：`fetch_web_page` 的分支几乎全在**响应侧**
 * （403 且带 cf-mitigated / 403 不带 / 非 2xx / JSON 直返 / HTML 抽正文），
 * 用真网络测这些分支等于要一个稳定的外网 + 一个恰好会返回 403 的站点。
 * 有了缝，全部路径都能在 JVM 里几毫秒跑完。
 */
interface PageFetcher {
    suspend fun fetch(url: String, userAgent: String? = null): FetchedPage
}

/** Accept 头沿用 C# 的同一条策略：优先要纯文本/markdown，能省掉 HTML 解析（opencode webfetch 同款） */
class OkHttpPageFetcher @javax.inject.Inject constructor() : PageFetcher {
    private val client = OkHttpClient.Builder()
        .connectTimeout(10, TimeUnit.SECONDS)
        .readTimeout(15, TimeUnit.SECONDS)
        .callTimeout(25, TimeUnit.SECONDS)
        .followRedirects(true)
        .build()

    override suspend fun fetch(url: String, userAgent: String?): FetchedPage = withContext(Dispatchers.IO) {
        val builder = Request.Builder()
            .url(url)
            .header("Accept", "text/plain;q=1.0, text/markdown;q=0.9, text/html;q=0.8, */*;q=0.1")
        if (userAgent != null) builder.header("User-Agent", userAgent)
        client.newCall(builder.build()).execute().use { response ->
            FetchedPage(
                url = url,
                status = response.code,
                contentType = response.header("content-type").orEmpty(),
                body = response.body?.string().orEmpty(),
                cfMitigated = response.header("cf-mitigated").orEmpty()
                    .contains("challenge", ignoreCase = true),
            )
        }
    }
}
