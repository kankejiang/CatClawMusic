package com.catclaw.music.kt.data.ai

import com.catclaw.music.kt.core.ai.LlmHttpResult
import com.catclaw.music.kt.core.ai.LlmTransport
import java.util.concurrent.TimeUnit
import okhttp3.MediaType.Companion.toMediaTypeOrNull
import okhttp3.OkHttpClient
import okhttp3.Request
import okhttp3.RequestBody.Companion.toRequestBody

/**
 * LLM 传输的 okhttp 实现。
 *
 * 读超时放到 120 秒：推理模型在吐出第一个 token 前可能长时间静默，
 * 30 秒的普通读超时会把一次本来正常的长思考掐断。
 * 流式响应按行读并**原样累积**回 [LlmHttpResult.body]，
 * 这样 :core 在「服务端没按 SSE 返回」的异常形状下还能退回按普通 JSON 解一次。
 */
class OkHttpLlmTransport(
    connectTimeoutSeconds: Long = 15,
    readTimeoutSeconds: Long = 120,
) : LlmTransport {

    private val client = OkHttpClient.Builder()
        .connectTimeout(connectTimeoutSeconds, TimeUnit.SECONDS)
        .readTimeout(readTimeoutSeconds, TimeUnit.SECONDS)
        .writeTimeout(readTimeoutSeconds, TimeUnit.SECONDS)
        .build()

    override fun post(
        url: String,
        headers: Map<String, String>,
        body: String,
        forEachLine: (String) -> Unit,
    ): LlmHttpResult {
        val contentType = headers.entries
            .firstOrNull { it.key.equals("Content-Type", true) }
            ?.value
        val request = Request.Builder()
            .url(url)
            .post(body.toRequestBody(contentType?.toMediaTypeOrNull()))
            .apply { headers.forEach { (k, v) -> if (!k.equals("Content-Type", true)) header(k, v) } }
            .build()

        client.newCall(request).execute().use { response ->
            val source = response.body?.source() ?: return LlmHttpResult(response.code, "")
            if (response.code !in 200..299) {
                // 错误响应整体读回来给上层拼异常信息，别按行切
                return LlmHttpResult(response.code, source.readUtf8())
            }
            val replay = StringBuilder()
            while (true) {
                val line = source.readUtf8Line() ?: break
                replay.append(line).append('\n')
                forEachLine(line)
            }
            return LlmHttpResult(response.code, replay.toString())
        }
    }

    override fun get(url: String, headers: Map<String, String>): LlmHttpResult {
        val request = Request.Builder()
            .url(url)
            .get()
            .apply { headers.forEach { (k, v) -> header(k, v) } }
            .build()
        client.newCall(request).execute().use { response ->
            return LlmHttpResult(response.code, response.body?.string().orEmpty())
        }
    }
}
