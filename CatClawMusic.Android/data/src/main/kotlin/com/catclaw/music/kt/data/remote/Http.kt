package com.catclaw.music.kt.data.remote

import com.catclaw.music.kt.core.net.UrlCredentials
import java.io.IOException
import java.net.URL
import java.util.concurrent.TimeUnit
import okhttp3.MediaType.Companion.toMediaTypeOrNull
import okhttp3.OkHttpClient
import okhttp3.Request
import okhttp3.RequestBody.Companion.toRequestBody

/** 一次 HTTP 往返的最小结果；WebDAV 要自己处理重定向，所以状态码与 Location 都得露出来 */
data class HttpResult(val status: Int, val location: String?, val body: String)

/**
 * 组一个 GET：URL 上带 `user:pass@` 时摘掉换成 `Authorization: Basic`，可选加 Range。
 *
 * 单点在这而不是各调用点各写一遍：`java.net` 与 okhttp 都不会替你把 userinfo 变成凭据，
 * 而这一处此前写错过一次（把已经拼好的头当用户名又编码了一层），表现是每次 401 且不报错。
 */
internal fun authorizedRequest(url: String, rangeBytes: Int? = null): Request {
    val (clean, authorization) = UrlCredentials.split(url)
    return Request.Builder().url(clean).apply {
        if (authorization != null) header("Authorization", authorization)
        if (rangeBytes != null && rangeBytes > 0) header("Range", "bytes=0-${rangeBytes - 1}")
    }.build()
}

/**
 * 远程音源共用的取数口子：单测塞假响应，真机走 [UrlHttpFetcher]。
 * 保留 method / headers / body 三样，是因为 WebDAV 需要 PROPFIND + Depth，OpenList 需要 POST JSON。
 */
interface HttpFetcher {
    fun call(
        method: String,
        url: String,
        headers: Map<String, String> = emptyMap(),
        body: String? = null,
    ): HttpResult
}

/** GET 的便捷口：非 2xx 抛 IOException；重定向按 C# `GetWithRedirectAsync` 的做法手动跟到最多 5 跳 */
fun HttpFetcher.get(url: String): String {
    var current = url
    repeat(MAX_REDIRECT) {
        val result = call("GET", current)
        if (result.status in REDIRECTS) {
            current = result.location?.let { resolve(current, it) } ?: throw IOException("重定向缺 Location")
            return@repeat
        }
        if (result.status !in 200..299) throw IOException("HTTP ${result.status} for ${url.substringBefore('?')}")
        return result.body
    }
    throw IOException("重定向次数过多")
}

internal const val MAX_REDIRECT = 5
internal val REDIRECTS = setOf(301, 302, 303, 307, 308)

/** Location 可能是相对地址，按当前 URL 解析（与 C# `new Uri(base, location)` 同规则） */
internal fun resolve(base: String, location: String): String =
    runCatching { URL(URL(base), location).toString() }.getOrDefault(location)

/**
 * 零额外依赖的 JDK 实现**做不到** PROPFIND：`HttpURLConnection.setRequestMethod` 只认
 * GET/POST/HEAD/PUT/DELETE/OPTIONS/TRACE，WebDAV 一发方法就被 ProtocolException 挡在客户端
 * （真机上验证过：请求根本没到服务器）。所以这里用 okhttp —— 它允许任意方法名。
 */
object UrlHttpFetcher : HttpFetcher {

    private val client = OkHttpClient.Builder()
        .connectTimeout(10, TimeUnit.SECONDS)
        .readTimeout(30, TimeUnit.SECONDS)
        // 重定向交给上层按方法语义重放（PROPFIND 被降级成 GET 是很多 NAS 的常见坑）
        .followRedirects(false)
        .followSslRedirects(false)
        .build()

    override fun call(
        method: String,
        url: String,
        headers: Map<String, String>,
        body: String?,
    ): HttpResult {
        val contentType = headers.entries.firstOrNull { it.key.equals("Content-Type", true) }?.value
        val requestBody = body?.toRequestBody(contentType?.toMediaTypeOrNull())
        val request = Request.Builder()
            .url(url)
            .method(method, requestBody)
            .apply { headers.forEach { (k, v) -> header(k, v) } }
            .build()
        client.newCall(request).execute().use { response ->
            val text = response.body?.string().orEmpty()
            return HttpResult(response.code, response.header("Location"), text)
        }
    }
}

/** 远程协议下的一个可播条目（Subsonic track / WebDAV file 的公共子集） */
data class RemoteTrack(
    val remoteId: String,
    val title: String,
    val artist: String,
    val album: String,
    val durationSeconds: Int,
    val streamUrl: String,
    val coverArtUrl: String? = null,
    val sizeBytes: Long = 0,
    val bitrateKbps: Int = 0,
)

/** WebDAV / OpenList 列出来的一个远端文件（目录在列曲库时被过滤掉） */
data class RemoteFile(
    val name: String,
    val path: String,
    val isDirectory: Boolean,
    val size: Long = 0,
    val lastModifiedSeconds: Long = 0,
)
