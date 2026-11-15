package com.catclaw.music.kt.data.lyrics

import com.catclaw.music.kt.core.lyrics.RemoteBytesFetcher
import com.catclaw.music.kt.core.net.RemoteHttp
import javax.inject.Inject
import javax.inject.Singleton
import kotlin.coroutines.cancellation.CancellationException
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.withContext

/**
 * 远程侧车歌词的取字节实现。
 *
 * 两处都必须走公共设施，各自抄一份就是 bug：
 * - **TLS**：走 [RemoteHttp.client] 才信任 NAS 的自签证书。此前这里自建 OkHttpClient、
 *   没带 trust-all，而 WebDAV 的 `.lrc` 全在自签 HTTPS 上 —— 于是「库里有 LyricsPath、
 *   NAS 上文件也在、歌词就是不显示」，异常还被吞成 null 只剩一行 `online fetched=-1`。
 * - **凭据**：URL 里带 `user:pass@` 时必须换成 Authorization 头（[RemoteHttp.request] 做）。
 *   `java.net` 与 okhttp 都不会替你把 userinfo 变成凭据，表现是"配好了却 401"。
 */
@Singleton
class OkHttpBytesFetcher @Inject constructor() : RemoteBytesFetcher {

    override suspend fun fetch(url: String): ByteArray? = withContext(Dispatchers.IO) {
        try {
            RemoteHttp.client.newCall(RemoteHttp.request(url)).execute().use { response ->
                if (!response.isSuccessful) null else response.body?.bytes()
            }
        } catch (e: CancellationException) {
            throw e
        } catch (e: Exception) {
            // 歌词是锦上添花：拿不到就回 null，让上层继续走内嵌，不打断播放。
            // 但要留痕 —— 上面那个「静默 401/SSL 失败」正是因为没有一行日志。
            com.catclaw.music.kt.core.diagnostic.AppLog.w(
                "CatClawLyrics",
                "sidecar fetch failed ${url.substringAfterLast('/')}: ${e.javaClass.simpleName} ${e.message}",
            )
            null
        }
    }
}
