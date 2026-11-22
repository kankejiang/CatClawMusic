package com.catclaw.music.kt.lyrico.search

import com.catclaw.music.kt.lyrico.lyrics.LyricoLyrics
import com.catclaw.music.kt.lyrico.lyrics.LyricoPayloadType
import kotlinx.serialization.json.Json
import kotlinx.serialization.json.JsonArray
import kotlinx.serialization.json.JsonObject
import kotlinx.serialization.json.JsonPrimitive
import okhttp3.HttpUrl.Companion.toHttpUrlOrNull
import okhttp3.OkHttpClient
import okhttp3.Request

/**
 * LRCLIB 客户端（https://lrclib.net/api）。
 *
 * LRCLIB 是免费开放歌词库：`/api/search` 按 歌名+艺人(+专辑) 模糊搜，
 * `/api/get` 按四元组精确取。这里两路都实现：
 *  - 搜索页用 search 拿候选列表
 *  - 自动匹配用 get 精确命中（命中时最省事、最准）
 *
 * syncedLyrics 是带行时间戳的 LRC，plainLyrics 是无时间戳纯文本 —— 前者优先。
 */
class LrclibClient(
    private val okHttpClient: OkHttpClient,
    private val baseUrl: String = "https://lrclib.net/api",
) {

    private val json = Json { ignoreUnknownKeys = true; isLenient = true }

    /** 模糊搜索，返回候选（可能为空）。网络失败也返回空，由调用方决定提示。 */
    fun search(target: LyricsTarget, limit: Int = 20): List<LyricsCandidate> {
        val url = "$baseUrl/search".toHttpUrlOrNull()?.newBuilder()
            ?.addQueryParameter("track_name", target.title)
            ?.addQueryParameter("artist_name", target.artist)
            ?.apply { if (target.album.isNotBlank()) addQueryParameter("album_name", target.album) }
            ?.build()
            ?: return emptyList()
        val body = get(url.toString()) ?: return emptyList()
        val arr = runCatching { json.parseToJsonElement(body) as? JsonArray }.getOrNull()
            ?: return emptyList()
        return arr.mapNotNull { (it as? JsonObject)?.let(::toCandidate) }
            .filter { it.lyrics.hasContent }
            .take(limit)
    }

    /** 精确命中：四元组完全匹配时返回单个候选。 */
    fun get(target: LyricsTarget): LyricsCandidate? {
        if (target.title.isBlank() || target.durationMs <= 0) return null
        val builder = "$baseUrl/get".toHttpUrlOrNull()?.newBuilder()
            ?.addQueryParameter("track_name", target.title)
            ?.addQueryParameter("artist_name", target.artist)
            ?.addQueryParameter("duration", (target.durationMs / 1000).toString())
        if (target.album.isNotBlank()) builder?.addQueryParameter("album_name", target.album)
        val url = builder?.build()?.toString() ?: return null
        val body = get(url) ?: return null
        val obj = runCatching { json.parseToJsonElement(body) as? JsonObject }.getOrNull() ?: return null
        return toCandidate(obj).takeIf { it.lyrics.hasContent }
    }

    /**
     * 发一次 GET。
     *
     * **不要把异常吞干净**：原来这里是 `runCatching { ... }.getOrNull()`，于是
     * 「DNS 不通」「TLS 失败」「HTTP 4xx」全都表现为「返回 0 条候选」，
     * 界面上看起来就是「这个源没有歌词」，实际是请求根本没成功。
     * 现在把失败原因写进日志（含耗时与异常类型），便于判断。
     */
    private fun get(url: String): String? {
        val started = System.currentTimeMillis()
        return try {
            val request = Request.Builder()
                .url(url)
                .header("User-Agent", USER_AGENT)
                .build()
            okHttpClient.newCall(request).execute().use { resp ->
                val ms = System.currentTimeMillis() - started
                if (!resp.isSuccessful) {
                    com.catclaw.music.kt.core.diagnostic.AppLog.w(
                        "LyricoSearch",
                        "LRCLIB HTTP ${resp.code}（${ms}ms）$url",
                    )
                    null
                } else {
                    com.catclaw.music.kt.core.diagnostic.AppLog.d(
                        "LyricoSearch",
                        "LRCLIB 请求成功（${ms}ms）",
                    )
                    resp.body?.string()
                }
            }
        } catch (t: Throwable) {
            com.catclaw.music.kt.core.diagnostic.AppLog.w(
                "LyricoSearch",
                "LRCLIB 请求失败（${System.currentTimeMillis() - started}ms）：" +
                    "${t.javaClass.simpleName}: ${t.message}",
            )
            null
        }
    }

    private fun toCandidate(obj: JsonObject): LyricsCandidate {
        val synced = obj.string("syncedLyrics").orEmpty()
        val plain = obj.string("plainLyrics").orEmpty()
        val instrumental = (obj["instrumental"] as? JsonPrimitive)?.content == "true"
        val lyrics = when {
            instrumental -> LyricoLyrics(payloadType = LyricoPayloadType.RAW_PLAIN_LRC)
            synced.isNotBlank() -> LyricoLyrics(
                payloadType = LyricoPayloadType.RAW_PLAIN_LRC,
                rawPlainLrc = synced,
                tags = buildMap {
                    obj.string("trackName")?.let { put("ti", it) }
                    obj.string("artistName")?.let { put("ar", it) }
                    obj.string("albumName")?.let { put("al", it) }
                },
            )
            plain.isNotBlank() -> LyricoLyrics(
                // 纯文本没有时间戳，用 raw 标记为纯文本，展示层按 5s 一行推进
                payloadType = LyricoPayloadType.RAW_PLAIN_LRC,
                rawPlainLrc = plain,
                tags = buildMap {
                    obj.string("trackName")?.let { put("ti", it) }
                    obj.string("artistName")?.let { put("ar", it) }
                },
            )
            else -> LyricoLyrics(payloadType = LyricoPayloadType.RAW_PLAIN_LRC)
        }
        return LyricsCandidate(
            sourceId = SOURCE_ID,
            sourceName = SOURCE_NAME,
            trackTitle = obj.string("trackName").orEmpty(),
            artist = obj.string("artistName").orEmpty(),
            album = obj.string("albumName").orEmpty(),
            durationMs = ((obj["duration"] as? JsonPrimitive)?.content?.toDoubleOrNull() ?: 0.0)
                .times(1000).toLong(),
            lyrics = lyrics,
        )
    }

    private fun JsonObject.string(key: String): String? =
        (this[key] as? JsonPrimitive)?.takeIf { it.isString }?.content

    companion object {
        const val SOURCE_ID = "lrclib"
        const val SOURCE_NAME = "LRCLIB"
        private const val USER_AGENT = "CatClawMusic/1.0 (https://github.com/kankejiang/CatClawMusic)"
    }
}
