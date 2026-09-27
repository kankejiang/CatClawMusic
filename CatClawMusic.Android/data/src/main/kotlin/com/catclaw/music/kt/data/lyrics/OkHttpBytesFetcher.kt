package com.catclaw.music.kt.data.lyrics

import com.catclaw.music.kt.core.lyrics.RemoteBytesFetcher
import com.catclaw.music.kt.core.net.UrlCredentials
import java.util.concurrent.TimeUnit
import javax.inject.Inject
import javax.inject.Singleton
import kotlin.coroutines.cancellation.CancellationException
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.withContext
import okhttp3.Credentials
import okhttp3.OkHttpClient
import okhttp3.Request

/**
 * 远程侧车歌词的取字节实现。
 *
 * URL 里带 `user:pass@` 时**必须自己换成 Authorization 头** —— 这是本项目第三次踩同一处：
 * WebDAV 取流、断点续传下载、现在再加歌词。`java.net` 与 okhttp 都不会替你把 userinfo 变成凭据，
 * 表现是"歌词地址明明配好了却 401"。凭据解析复用 [UrlCredentials]，不再各写一遍。
 */
@Singleton
class OkHttpBytesFetcher @Inject constructor() : RemoteBytesFetcher {

    private val client by lazy {
        OkHttpClient.Builder()
            .connectTimeout(8, TimeUnit.SECONDS)
            .readTimeout(12, TimeUnit.SECONDS)
            .build()
    }

    override suspend fun fetch(url: String): ByteArray? = withContext(Dispatchers.IO) {
        val (clean, userInfo) = UrlCredentials.split(url)
        val request = Request.Builder().url(clean).apply {
            if (userInfo != null) {
                val parts = userInfo.split(':', limit = 2) // 密码里可以带冒号，只按第一个切
                val user = UrlCredentials.percentDecode(parts[0])
                val pass = UrlCredentials.percentDecode(parts.getOrNull(1).orEmpty())
                header("Authorization", Credentials.basic(user, pass, Charsets.UTF_8))
            }
        }.build()
        try {
            client.newCall(request).execute().use { response ->
                if (!response.isSuccessful) null else response.body?.bytes()
            }
        } catch (e: CancellationException) {
            throw e
        } catch (e: Exception) {
            null // 歌词是锦上添花：拿不到就回 null，让上层继续走内嵌，不打断播放
        }
    }
}
