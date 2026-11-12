package com.catclaw.music.kt.data.netease

import java.util.concurrent.ConcurrentHashMap
import kotlinx.coroutines.async
import kotlinx.coroutines.coroutineScope
import javax.inject.Inject
import javax.inject.Singleton

/**
 * 网易云播放直链解析：音质档位 + 20 分钟缓存 + 失败自动降档。
 *
 * 移植插件 `GetPlayUrlWithTypeAsync` 的骨架（eapi enhance → 降档重试 → 免登录外链兜底），
 * 但**不做公共 API 实例兜底**：那几条第三方镜像时好时坏，Android 端没有对应的失败重试预算，
 * 不如把 outer 外链作为最后一级 —— 它至少稳定可播（128k）。
 *
 * 直链带时效（几十分钟），所以缓存只在内存、不落库，且过期立刻失效；
 * 免登录外链是可以长期存库的，只在 `resolve` 的最后一级出现。
 */
@Singleton
class NeteasePlayUrlResolver @Inject constructor(
    private val client: NeteasePlaylistClient,
    private val session: NeteaseSession,
    private val settings: NeteaseQualitySettings,
) {

    private data class Entry(val url: String, val expireAt: Long)

    private val cache = ConcurrentHashMap<String, Entry>()

    /** 当前偏好档位（UI 显示用） */
    val quality: NeteasePlaylistClient.Quality get() = settings.quality

    fun cycleQuality(): NeteasePlaylistClient.Quality = settings.cycle()

    /**
     * 解析一首歌的可播放地址。永远不会返回 null：最差也会给免登录外链。
     */
    suspend fun resolve(songId: Long): String {
        val t0 = System.currentTimeMillis()
        var hitCache = false
        val preferred = settings.quality
        // 任一档缓存命中直接回
        for (quality in ladder(preferred)) {
            val key = "$songId:${quality.name}"
            cache[key]?.let { entry ->
                if (entry.expireAt > System.currentTimeMillis()) {
                    hitCache = true
                    com.catclaw.music.kt.core.diagnostic.AppLog.d("NeteaseTiming", "resolve cache-hit id=$songId ${System.currentTimeMillis() - t0}ms")
                    return entry.url
                }
                cache.remove(key)
            }
        }
        if (preferred == NeteasePlaylistClient.Quality.Lossless && session.isLoggedIn) {
            // 无损与极高**并行**发：旧实现串行降档，最坏要吃 3 次网络往返才出声
            val (lossless, exhigh) = coroutineScope {
                val l = async { runCatching { client.playUrl(songId, NeteasePlaylistClient.Quality.Lossless, session.cookie) }.getOrNull() }
                val e = async { runCatching { client.playUrl(songId, NeteasePlaylistClient.Quality.Exhigh, session.cookie) }.getOrNull() }
                (l.await() to e.await())
            }
            val pick = lossless?.takeIf { it.isNotBlank() } ?: exhigh?.takeIf { it.isNotBlank() }
            if (!pick.isNullOrBlank()) {
                val hitQuality = if (lossless?.takeIf { it.isNotBlank() } != null) {
                    NeteasePlaylistClient.Quality.Lossless
                } else {
                    NeteasePlaylistClient.Quality.Exhigh
                }
                cache["$songId:${hitQuality.name}"] = Entry(pick, System.currentTimeMillis() + CACHE_TTL_MS)
                com.catclaw.music.kt.core.diagnostic.AppLog.d(
                    "NeteaseTiming", "resolve id=$songId q=$hitQuality ${System.currentTimeMillis() - t0}ms cache=$hitCache",
                )
                return pick
            }
        } else {
            for (quality in ladder(preferred)) {
                if (quality == NeteasePlaylistClient.Quality.Lossless && !session.isLoggedIn) continue
                val url = runCatching { client.playUrl(songId, quality, session.cookie) }.getOrNull()
                if (!url.isNullOrBlank()) {
                    cache["$songId:${quality.name}"] = Entry(url, System.currentTimeMillis() + CACHE_TTL_MS)
                    com.catclaw.music.kt.core.diagnostic.AppLog.d(
                        "NeteaseTiming", "resolve id=$songId q=$quality ${System.currentTimeMillis() - t0}ms cache=$hitCache",
                    )
                    return url
                }
            }
        }
        com.catclaw.music.kt.core.diagnostic.AppLog.d(
            "NeteaseTiming", "resolve id=$songId fallback ${System.currentTimeMillis() - t0}ms",
        )
        return NeteasePlaylistClient.playUrlFallback(songId)
    }

    /** 降档顺序：无损 → 320k → 128k；320k → 128k；128k 不再降 */
    private fun ladder(preferred: NeteasePlaylistClient.Quality): List<NeteasePlaylistClient.Quality> =
        when (preferred) {
            NeteasePlaylistClient.Quality.Lossless -> listOf(
                NeteasePlaylistClient.Quality.Lossless,
                NeteasePlaylistClient.Quality.Exhigh,
                NeteasePlaylistClient.Quality.Standard,
            )
            NeteasePlaylistClient.Quality.Exhigh -> listOf(
                NeteasePlaylistClient.Quality.Exhigh,
                NeteasePlaylistClient.Quality.Standard,
            )
            NeteasePlaylistClient.Quality.Standard -> listOf(NeteasePlaylistClient.Quality.Standard)
        }

    private companion object {
        /** 与插件一致的直链缓存时长：20 分钟 */
        const val CACHE_TTL_MS = 20 * 60 * 1000L
    }
}
