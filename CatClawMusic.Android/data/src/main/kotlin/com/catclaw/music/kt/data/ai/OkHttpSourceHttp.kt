package com.catclaw.music.kt.data.ai

import com.catclaw.music.kt.core.ai.SourceHttp
import java.util.concurrent.TimeUnit
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.withContext
import okhttp3.FormBody
import okhttp3.OkHttpClient
import okhttp3.Request

/**
 * [SourceHttp] 的 OkHttp 实现。
 *
 * 与 `fetch_web_page` 那条的区别只有一件事：**这里返回字节**，
 * 因为酷我 r.s 的响应是 GBK，字符集要留给注册表按 `spec.encoding` 决定。
 * 在缝里就转 String 的话，GBK 这条路径永远测不到，坏了也是"歌手名乱码"这种难查症状。
 */
class OkHttpSourceHttp @javax.inject.Inject constructor() : SourceHttp {
    private val client = OkHttpClient.Builder()
        .connectTimeout(10, TimeUnit.SECONDS)
        .readTimeout(15, TimeUnit.SECONDS)
        .callTimeout(20, TimeUnit.SECONDS)
        .followRedirects(true)
        .build()

    override suspend fun get(url: String, headers: Map<String, String>, body: ByteArray): ByteArray? =
        withContext(Dispatchers.IO) {
            runCatching {
                val request = Request.Builder().url(url)
                headers.forEach { (k, v) -> request.header(k, v) }
                client.newCall(request.build()).execute().use { response ->
                    if (!response.isSuccessful) null else response.body?.bytes()
                }
            }.getOrNull()
        }

    override suspend fun postForm(
        url: String,
        form: Map<String, String>,
        headers: Map<String, String>,
    ): ByteArray? = withContext(Dispatchers.IO) {
        runCatching {
            val builder = FormBody.Builder()
            form.forEach { (k, v) -> builder.add(k, v) }
            val request = Request.Builder().url(url).post(builder.build())
            headers.forEach { (k, v) -> request.header(k, v) }
            client.newCall(request.build()).execute().use { response ->
                if (!response.isSuccessful) null else response.body?.bytes()
            }
        }.getOrNull()
    }
}
