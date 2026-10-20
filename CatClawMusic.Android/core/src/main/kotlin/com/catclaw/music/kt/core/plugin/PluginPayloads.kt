package com.catclaw.music.kt.core.plugin

import kotlinx.serialization.json.JsonArray
import kotlinx.serialization.json.JsonElement
import kotlinx.serialization.json.JsonObject
import kotlinx.serialization.json.JsonPrimitive
import kotlinx.serialization.json.buildJsonObject
import kotlinx.serialization.json.jsonPrimitive
import kotlinx.serialization.json.put

/**
 * 各方法的**参数与返回值形状** —— v2 报文只规定了信封（`PluginProtocol`），
 * 这一层规定信封里装什么，逐条对齐 C# `IOnlineMusicPlugin` 的签名
 * （`SearchAsync(kw, page=1, pageSize=8)`、`GetPlayUrlAsync(song, quality=0)`、
 * `GetLyricsWithRomaAsync` 的 `(Lrc, TLrc, RLrc)` 三流）。
 *
 * 为什么要有这一层：形状不定下来，插件作者与宿主就会各自猜一套 ——
 * 而「猜错」的症状是**空结果**，不是报错（宿主会说「没搜到」，插件作者会说「我明明返回了」）。
 * 这里顺手把几个容易错的点钉死：
 *
 * - **页码从 1 开始**（C# 就是这个约定，从 0 开始会让第一页永远空）；
 * - 取流结果的 URL 既可能是裸字符串也可能被包成 `{"url": …}`，两种都认；
 * - 歌词的三流是 `lrc` / `tlrc` / `rlrc`（lx-music 的叫法，也是 C# 元组那三个）；
 *   它们最终要落到 [com.catclaw.music.kt.core.lyrics.Lyrics] 的
 *   `translationLines` / `romaLines` 上（`LrcParser.mergeExtendedStreams` 负责并入主行）。
 */
object PluginPayloads {

    /** C# `SearchAsync` 的 `page = 1` */
    const val FIRST_PAGE = 1
    const val DEFAULT_SEARCH_PAGE_SIZE = 8
    const val DEFAULT_PLAYLIST_PAGE_SIZE = 50

    fun searchParams(
        keyword: String,
        page: Int = FIRST_PAGE,
        pageSize: Int = DEFAULT_SEARCH_PAGE_SIZE,
    ): JsonObject = buildJsonObject {
        put("keyword", JsonPrimitive(keyword))
        put("page", JsonPrimitive(page.coerceAtLeast(FIRST_PAGE)))
        put("page_size", JsonPrimitive(pageSize.coerceAtLeast(1)))
    }

    fun playUrlParams(song: RpcSong, quality: Int = 0): JsonObject = buildJsonObject {
        put("id", JsonPrimitive(song.id))
        put("platform", JsonPrimitive(song.platform))
        put("quality", JsonPrimitive(quality))
        // extra 里常放着平台侧的关键 id（如网易云的 songId 与 albumId 不同），丢了就取不到直链
        if (song.extra.isNotEmpty()) {
            put("extra", buildJsonObject { song.extra.forEach { (k, v) -> put(k, JsonPrimitive(v)) } })
        }
    }

    fun lyricsParams(song: RpcSong): JsonObject = buildJsonObject {
        put("id", JsonPrimitive(song.id))
        put("platform", JsonPrimitive(song.platform))
        put("title", JsonPrimitive(song.title))
        put("artist", JsonPrimitive(song.artist))
        put("duration_ms", JsonPrimitive(song.durationMs))
    }

    /**
     * 取直链的返回值：裸字符串，或 `{"url": …}`。
     * 空串、`null`、只有空白都算「没取到」—— 返回空串当 URL 会让播放器静默失败。
     */
    fun playUrlFrom(result: JsonElement?): String? {
        val raw = when (result) {
            is JsonPrimitive -> result.takeIf { it.isString }?.content
            is JsonObject -> (result["url"] as? JsonPrimitive)?.takeIf { it.isString }?.content
            else -> null
        }
        return raw?.trim()?.takeIf { it.isNotEmpty() }
    }

    /** 三流歌词。任一流缺失就是 null（`LrcParser` 按「没有这一流」处理） */
    data class LyricsStreams(val lrc: String?, val translation: String?, val roma: String?) {
        val isEmpty: Boolean get() = lrc.isNullOrBlank() && translation.isNullOrBlank() && roma.isNullOrBlank()
    }

    /**
     * 取歌词的返回值：`{"lrc": …, "tlrc": …, "rlrc": …}`，或整段就是歌词文本的裸字符串。
     * 三流都没有（或只有空白）返回 null。
     */
    fun lyricsFrom(result: JsonElement?): LyricsStreams? {
        val streams = when (result) {
            is JsonPrimitive -> if (result.isString) LyricsStreams(result.content.blankToNull(), null, null) else null
            is JsonObject -> LyricsStreams(
                lrc = stringOf(result, "lrc"),
                translation = stringOf(result, "tlrc") ?: stringOf(result, "translation"),
                roma = stringOf(result, "rlrc") ?: stringOf(result, "roma"),
            )
            else -> null
        } ?: return null
        return streams.takeUnless { it.isEmpty }
    }

    /** 歌单列表：`OnlinePlaylist` 的可序列化子集（id / name / cover_url / count） */
    data class RpcPlaylist(val id: String, val name: String, val coverUrl: String = "", val count: Int = 0) {
        fun toJson(): JsonElement = buildJsonObject {
            put("id", JsonPrimitive(id))
            put("name", JsonPrimitive(name))
            put("cover_url", JsonPrimitive(coverUrl))
            put("count", JsonPrimitive(count))
        }

        companion object {
            fun fromJson(element: JsonElement): RpcPlaylist? {
                val obj = element as? JsonObject ?: return null
                val id = stringOf(obj, "id") ?: return null
                return RpcPlaylist(
                    id = id,
                    name = stringOf(obj, "name").orEmpty(),
                    coverUrl = stringOf(obj, "cover_url").orEmpty(),
                    count = (obj["count"] as? JsonPrimitive)?.intOrNullSafe() ?: 0,
                )
            }

            fun listFrom(element: JsonElement?): List<RpcPlaylist> =
                (element as? JsonArray)?.mapNotNull { fromJson(it) } ?: emptyList()
        }
    }

    private fun stringOf(obj: JsonObject, key: String): String? =
        (obj[key] as? JsonPrimitive)?.takeIf { it.isString }?.content.blankToNull()

    private fun String?.blankToNull(): String? = this?.trim()?.takeIf { it.isNotEmpty() }

    private fun JsonPrimitive.intOrNullSafe(): Int? =
        if (isString) content.trim().toIntOrNull() else content.trim().toIntOrNull()
}
