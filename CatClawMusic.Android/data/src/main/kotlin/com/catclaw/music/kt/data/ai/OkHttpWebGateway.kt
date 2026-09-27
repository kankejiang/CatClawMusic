package com.catclaw.music.kt.data.ai

import com.catclaw.music.kt.core.ai.WebFetchGateway
import java.util.concurrent.TimeUnit
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.withContext
import okhttp3.MediaType.Companion.toMediaType
import okhttp3.OkHttpClient
import okhttp3.Request
import okhttp3.RequestBody.Companion.toRequestBody

/**
 * [WebFetchGateway] 的 OkHttp 实现。
 *
 * 三个搜索引擎都要求"看起来像浏览器"，否则会被挡（返回验证码页而不是结果）：
 * UA / Accept / Accept-Language 三条头是功能性的，不是装饰。
 * HTML 读取上限 400 KB —— 畸形页面能返回几十 MB，全读进来会先把内存吃掉再让正则回溯爆掉。
 */
class OkHttpWebGateway @javax.inject.Inject constructor() : WebFetchGateway {
    private val client = OkHttpClient.Builder()
        .connectTimeout(8, TimeUnit.SECONDS)
        .readTimeout(8, TimeUnit.SECONDS)
        .callTimeout(15, TimeUnit.SECONDS)
        .followRedirects(true)
        .build()

    private val htmlHeaders = mapOf(
        "User-Agent" to "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36",
        "Accept" to "text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8",
        "Accept-Language" to "zh-CN,zh;q=0.9,en;q=0.8",
    )

    override suspend fun getHtml(url: String): String? = withContext(Dispatchers.IO) {
        runCatching {
            val request = Request.Builder().url(url)
            htmlHeaders.forEach { (k, v) -> request.header(k, v) }
            client.newCall(request.build()).execute().use { response ->
                if (!response.isSuccessful) return@use null
                readLimited(response.body?.byteStream(), MAX_PAGE_BYTES)?.toString(Charsets.UTF_8)
            }
        }.getOrNull()
    }

    override suspend fun postJson(url: String, payload: String): String? = withContext(Dispatchers.IO) {
        runCatching {
            val request = Request.Builder().url(url)
                .header("User-Agent", htmlHeaders.getValue("User-Agent"))
                .header("Accept", "application/json, text/event-stream")
                .header("Content-Type", "application/json")
                .post(payload.toRequestBody("application/json".toMediaType()))
            client.newCall(request.build()).execute().use { response ->
                if (!response.isSuccessful) return@use null
                readLimited(response.body?.byteStream(), MAX_PAGE_BYTES)?.toString(Charsets.UTF_8)
            }
        }.getOrNull()
    }

    /** 只读前 [maxBytes]，多出来的直接丢弃 */
    private fun readLimited(source: java.io.InputStream?, maxBytes: Int): ByteArray? {
        if (source == null) return null
        val buffer = java.io.ByteArrayOutputStream()
        val chunk = ByteArray(8 * 1024)
        var total = 0
        source.use { input ->
            while (true) {
                val read = input.read(chunk)
                if (read < 0) break
                val room = maxBytes - total
                if (room <= 0) break
                buffer.write(chunk, 0, minOf(read, room))
                total += read
            }
        }
        return buffer.toByteArray()
    }

    private companion object {
        const val MAX_PAGE_BYTES = 400 * 1024
    }
}
