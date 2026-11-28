package com.catclaw.music.kt.data.netease

import java.util.concurrent.ConcurrentHashMap
import kotlinx.coroutines.async
import kotlinx.coroutines.coroutineScope
import javax.inject.Inject
import javax.inject.Singleton

/**
 * 网易云播放直链解析：音质档位 + 20 分钟缓存 + 失败自动降档。
 *
 * 移植插件 `GetPlayUrlWithTypeAsync` 的骨架（eapi enhance → 老 web 接口 → outer 外链 +
 * 降档重试 + 会话续期），但**不做公共 API 实例兜底**：那几条第三方镜像时好时坏，
 * Android 端没有对应的失败重试预算，不如把 outer 外链作为最后一级 —— 它至少稳定可播（128k）。
 *
 * 直链带时效（几十分钟），所以缓存只在内存、不落库，且过期立刻失效；
 * 免登录外链是可以长期存库的，只在 `resolve` 的最后一级出现。
 *
 * **试听识别是这里的硬约束**：黑胶 VIP 却只听到 30 秒，几乎都是登录会话过期后
 * 服务端把请求当匿名处理（响应 code=200 但带 `freeTrialInfo`）。所以
 * ① 取链层丢掉了所有试听条目；② 全链拿不到完整链时先静默续期再重试一遍；
 * ③ 实在只能试听才返回 [Resolved.trial] = true，让 UI 明确告诉用户原因。
 */
@Singleton
class NeteasePlayUrlResolver @Inject constructor(
    private val client: NeteasePlaylistClient,
    private val session: NeteaseSession,
    private val settings: NeteaseQualitySettings,
    private val anon: NeteaseAnonClient,
    private val login: NeteaseLoginClient,
) {

    /**
     * @param url 可播放地址
     * @param trial 是否只是试听片段（登录态失效 / 无会员权限）。UI 据此提示用户，
     *   不能静默播 30 秒让人以为"改动没生效"。
     */
    data class Resolved(val url: String, val trial: Boolean = false)

    private data class Entry(val url: String, val expireAt: Long)
    private data class UrlHit(val url: String, val quality: NeteasePlaylistClient.Quality)

    private val cache = ConcurrentHashMap<String, Entry>()

    /** 当前偏好档位（UI 显示用） */
    val quality: NeteasePlaylistClient.Quality get() = settings.quality

    fun cycleQuality(): NeteasePlaylistClient.Quality = settings.cycle()

    /**
     * 解析一首歌的可播放地址。永远不会返回 null：最差也会给免登录外链。
     *
     * @param forceRefresh 跳过内存缓存强制重新取链。播放器报"源错误"时用：
     *   缓存里的直链可能已经过期（`expi=1200`，与 20 分钟 TTL 同量级），
     *   再拿同一份缓存重试等于没修。
     */
    suspend fun resolve(songId: Long, forceRefresh: Boolean = false): Resolved {
        val t0 = System.currentTimeMillis()
        var hitCache = false
        val preferred = settings.quality
        // 匿名请求先确保游客令牌（MUSIC_A）就位 —— 一次性注册，失败不阻塞播放
        if (!session.isLoggedIn) {
            runCatching { anon.ensureToken() }
                .onFailure { com.catclaw.music.kt.core.diagnostic.AppLog.w("NeteaseAnon", "ensureToken 异常: ${it.message}") }
        }
        // 任一档缓存命中直接回（强制刷新时整段跳过）
        if (!forceRefresh) {
            for (q in ladder(preferred)) {
                val key = cacheKey(songId, q)
                cache[key]?.let { entry ->
                    if (entry.expireAt > System.currentTimeMillis()) {
                        hitCache = true
                        com.catclaw.music.kt.core.diagnostic.AppLog.d("NeteaseTiming", "resolve cache-hit id=$songId ${System.currentTimeMillis() - t0}ms")
                        return Resolved(entry.url)
                    }
                    cache.remove(key)
                }
            }
        } else {
            ladder(preferred).forEach { cache.remove(cacheKey(songId, it)) }
        }
        // ── ① eapi level 取链 ────────────────────────────────────────────────
        resolveViaEapi(songId, preferred)?.let { hit ->
            cache[cacheKey(songId, hit.quality)] = Entry(hit.url, System.currentTimeMillis() + CACHE_TTL_MS)
            com.catclaw.music.kt.core.diagnostic.AppLog.d(
                "NeteaseTiming", "resolve eapi id=$songId q=${hit.quality} ${System.currentTimeMillis() - t0}ms cache=$hitCache",
            )
            return Resolved(hit.url)
        }
        // ── ② 老 web 接口带用户 Cookie ───────────────────────────────────────
        resolveViaWeb(songId, preferred)?.let { hit ->
            cache[cacheKey(songId, hit.quality)] = Entry(hit.url, System.currentTimeMillis() + CACHE_TTL_MS)
            com.catclaw.music.kt.core.diagnostic.AppLog.d(
                "NeteaseTiming", "resolve web id=$songId q=${hit.quality} ${System.currentTimeMillis() - t0}ms cache=$hitCache",
            )
            return Resolved(hit.url)
        }
        // ── ③ 会话续期后重试一轮 ─────────────────────────────────────────────
        // 登录很久后 MUSIC_U 会过期：本地 cookie 还在（isLoggedIn=true），但服务端
        // 已按匿名处理 → 会员歌只给 30 秒。MAUI 插件在遇到 301 时会静默续期，
        // Kotlin 侧此前从未调用过 refreshSession，这是"VIP 仍走试听"的头号原因。
        if (session.isLoggedIn && refreshSessionThrottled()) {
            resolveViaEapi(songId, preferred)?.let { hit ->
                cache[cacheKey(songId, hit.quality)] = Entry(hit.url, System.currentTimeMillis() + CACHE_TTL_MS)
                com.catclaw.music.kt.core.diagnostic.AppLog.d(
                    "NeteaseTiming", "resolve eapi-retry id=$songId q=${hit.quality} ${System.currentTimeMillis() - t0}ms",
                )
                return Resolved(hit.url)
            }
            resolveViaWeb(songId, preferred)?.let { hit ->
                cache[cacheKey(songId, hit.quality)] = Entry(hit.url, System.currentTimeMillis() + CACHE_TTL_MS)
                com.catclaw.music.kt.core.diagnostic.AppLog.d(
                    "NeteaseTiming", "resolve web-retry id=$songId q=${hit.quality} ${System.currentTimeMillis() - t0}ms",
                )
                return Resolved(hit.url)
            }
        }
        // ── ④ 免登录外链探测：2xx 才算可用 ───────────────────────────────────
        // 锁定歌的 outer 会 302 到 404 页；能播的也可能只是 30 秒试听片段，
        // 所以这一级一律标记 trial，UI 会提示用户而不是假装成功。
        val probed = runCatching { client.outerUrl(songId) }
            .onFailure { com.catclaw.music.kt.core.diagnostic.AppLog.w("NeteaseVip", "outerUrl 探测异常 id=$songId: ${it.message}") }
            .getOrNull()
        if (!probed.isNullOrBlank()) {
            com.catclaw.music.kt.core.diagnostic.AppLog.w(
                "NeteaseVip", "resolve 仅 outer 可用（试听兜底）id=$songId ${System.currentTimeMillis() - t0}ms",
            )
            return Resolved(probed, trial = true)
        }
        com.catclaw.music.kt.core.diagnostic.AppLog.w(
            "NeteaseVip", "resolve 全链失败 → outer 原始地址兜底 id=$songId ${System.currentTimeMillis() - t0}ms",
        )
        return Resolved(NeteasePlaylistClient.playUrlFallback(songId), trial = true)
    }

    /** eapi 各档取链；登录 + 无损偏好时无损/极高并行发，省一次网络往返 */
    private suspend fun resolveViaEapi(
        songId: Long,
        preferred: NeteasePlaylistClient.Quality,
    ): UrlHit? {
        val cookie = session.cookie
        if (preferred == NeteasePlaylistClient.Quality.Lossless && session.isLoggedIn) {
            val (lossless, exhigh) = coroutineScope {
                val l = async { runCatching { client.playUrl(songId, NeteasePlaylistClient.Quality.Lossless, cookie) }.getOrNull() }
                val e = async { runCatching { client.playUrl(songId, NeteasePlaylistClient.Quality.Exhigh, cookie) }.getOrNull() }
                (l.await() to e.await())
            }
            lossless?.takeIf { it.isNotBlank() }?.let { return UrlHit(it, NeteasePlaylistClient.Quality.Lossless) }
            exhigh?.takeIf { it.isNotBlank() }?.let { return UrlHit(it, NeteasePlaylistClient.Quality.Exhigh) }
            return null
        }
        for (q in ladder(preferred)) {
            if (q.ordinal >= NeteasePlaylistClient.Quality.Lossless.ordinal && !session.isLoggedIn) continue
            val url = runCatching { client.playUrl(songId, q, cookie) }
                .onFailure { com.catclaw.music.kt.core.diagnostic.AppLog.w("NeteaseVip", "playUrl 异常 id=$songId q=$q: ${it.message}") }
                .getOrNull()
            com.catclaw.music.kt.core.diagnostic.AppLog.d("NeteaseVip", "resolve try id=$songId q=$q got=${!url.isNullOrBlank()}")
            if (!url.isNullOrBlank()) return UrlHit(url, q)
        }
        return null
    }

    /** 老 web 接口兜底（带用户 Cookie；未登录用静态 cookie 防风控） */
    private suspend fun resolveViaWeb(
        songId: Long,
        preferred: NeteasePlaylistClient.Quality,
    ): UrlHit? {
        val cookie = session.cookie
        for (br in webBrLadder(preferred)) {
            val url = runCatching { client.playUrlViaWeb(songId, br, cookie) }
                .onFailure { com.catclaw.music.kt.core.diagnostic.AppLog.w("NeteaseVip", "playUrlViaWeb 异常 id=$songId br=$br: ${it.message}") }
                .getOrNull()
            if (!url.isNullOrBlank()) {
                val quality = qualityForBr(br, preferred)
                return UrlHit(url, quality)
            }
        }
        return null
    }

    /**
     * 静默续期一次（10 分钟内不重复尝试）：
     * 会话存在但服务端已不认时，`/eapi/login/token/refresh` 会轮换 MUSIC_U。
     * 失败不抛，让上层继续走 outer 兜底。
     */
    @Volatile
    private var lastRefreshAt = 0L

    private suspend fun refreshSessionThrottled(): Boolean {
        val now = System.currentTimeMillis()
        if (now - lastRefreshAt < REFRESH_COOLDOWN_MS) return false
        lastRefreshAt = now
        val ok = runCatching { login.refreshSession() }.getOrDefault(false)
        com.catclaw.music.kt.core.diagnostic.AppLog.i(
            "NeteaseVip", if (ok) "登录会话已续期，重试取链" else "登录会话续期失败（可能已彻底过期）",
        )
        return ok
    }

    /**
     * 丢弃某首歌的全部档位缓存。播放器确认这条直链不可用（403 / 源错误）时调用，
     * 之后再 `resolve` 就不会拿回同一条死链。
     */
    fun invalidate(songId: Long) {
        ladder(settings.quality).forEach { cache.remove(cacheKey(songId, it)) }
    }

    private fun cacheKey(songId: Long, quality: NeteasePlaylistClient.Quality): String = "$songId:${quality.name}"

    /** 音质档 → 老接口码率（插件 QualityToBr 同表：标准 128k，极高 320k，无损及以上封顶 999k） */
    private fun brForQuality(q: NeteasePlaylistClient.Quality): Int = when (q) {
        NeteasePlaylistClient.Quality.Standard -> 128_000
        NeteasePlaylistClient.Quality.Exhigh -> 320_000
        else -> 999_000
    }

    /** 老接口命中码率 → 缓存档位（999k 视为偏好档，320k/128k 各自成档） */
    private fun qualityForBr(br: Int, preferred: NeteasePlaylistClient.Quality): NeteasePlaylistClient.Quality =
        when (br) {
            128_000 -> NeteasePlaylistClient.Quality.Standard
            320_000 -> NeteasePlaylistClient.Quality.Exhigh
            else -> preferred
        }

    /**
     * 老接口降档顺序（插件方案 ②→④ 同款）：先按偏好档码率，≥320k 时再补 128k 一档。
     * 登录 Cookie 直接透传 —— VIP 完整度靠它，与 eapi 未登录才降档的规则独立。
     */
    private fun webBrLadder(preferred: NeteasePlaylistClient.Quality): List<Int> = buildList {
        add(brForQuality(preferred))
        if (preferred.ordinal >= NeteasePlaylistClient.Quality.Lossless.ordinal) add(320_000)
        if (128_000 !in this) add(128_000)
    }

    /** 降档顺序：臻音 → Hi-Res → 无损 → 320k → 128k；320k → 128k；128k 不再降 */
    private fun ladder(preferred: NeteasePlaylistClient.Quality): List<NeteasePlaylistClient.Quality> =
        NeteasePlaylistClient.Quality.entries
            .filter { it.ordinal <= preferred.ordinal }
            .sortedByDescending { it.ordinal }

    private companion object {
        /**
         * 直链缓存时长：15 分钟。
         *
         * 服务端签名 `expi=1200` 秒（20 分钟），但缓存到第 19 分钟才起播的曲目
         * 会在播到一半时失效（队列长时间挂着、暂停后继续都会踩到）。
         * 提前 5 分钟过期，与 `PlayerController.URL_TTL_MS` 对齐。
         */
        const val CACHE_TTL_MS = 15 * 60 * 1000L

        /** 续期节流：失败后 10 分钟内不再打续期接口，避免每首歌都多一次请求 */
        const val REFRESH_COOLDOWN_MS = 10 * 60 * 1000L
    }
}
