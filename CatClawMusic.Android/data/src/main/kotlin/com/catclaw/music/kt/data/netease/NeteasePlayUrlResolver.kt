package com.catclaw.music.kt.data.netease

import java.util.concurrent.ConcurrentHashMap
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
        val now = System.currentTimeMillis()
        for (quality in ladder(settings.quality)) {
            val key = "$songId:${quality.name}"
            cache[key]?.let { entry ->
                if (entry.expireAt > now) return entry.url
                cache.remove(key)
            }
            // 未登录时无损必然被风控：直接跳过这一档，省一次注定失败的请求
            if (quality == NeteasePlaylistClient.Quality.Lossless && !session.isLoggedIn) continue
            val url = runCatching { client.playUrl(songId, quality, session.cookie) }.getOrNull()
            if (!url.isNullOrBlank()) {
                cache[key] = Entry(url, now + CACHE_TTL_MS)
                return url
            }
        }
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
