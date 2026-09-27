package com.catclaw.music.kt.data.remote

import com.catclaw.music.kt.core.model.SongSource
import kotlinx.serialization.json.Json
import kotlinx.serialization.json.JsonArray
import kotlinx.serialization.json.JsonElement
import kotlinx.serialization.json.JsonNull
import kotlinx.serialization.json.JsonObject
import kotlinx.serialization.json.JsonPrimitive
import kotlinx.serialization.json.doubleOrNull
import kotlinx.serialization.json.int
import kotlinx.serialization.json.long
import kotlinx.serialization.json.longOrNull

/** 服务端 `/api/status` 载荷，对齐 C# `ServerStatus` */
data class ServerStatus(
    val songs: Int = 0,
    val artists: Int = 0,
    val albums: Int = 0,
    val dhtPeers: Int = 0,
    val rateLimit: Int = 0,
    val device: String = "",
)

/** 服务端歌曲 DTO，字段名按 C# `ServerSong` 的 `[JsonPropertyName]` 小写下划线 */
data class ServerSong(
    val id: Int,
    val title: String,
    val artist: String,
    val album: String,
    val durationSeconds: Int,
    val filePath: String,
    val fileSize: Long,
    val bitrate: Int,
    val trackNumber: Int,
    val year: Int,
    val genre: String?,
    val artistId: Long,
    val albumId: Long,
    val coverArtPath: String?,
    val dateModified: Long,
    val lyricsPath: String?,
)

/**
 * 猫爪服务器（NAS 端）HTTP 客户端，端点与语义逐条对齐 C# `CatClawServerClient`：
 * `/api/status`、`/api/songs`、`/api/search?q=`、`/api/stream/{id}`，无鉴权、无分页。
 * 属性名匹配按 C# 的 `PropertyNameCaseInsensitive = true` 做大小写不敏感。
 */
class CatClawServerClient(
    private val serverUrl: String,
    private val fetcher: HttpFetcher = UrlHttpFetcher,
) {
    private val base = serverUrl.trimEnd('/')

    fun isConfigured(): Boolean = serverUrl.isNotEmpty()

    /** C# 用 `IsSuccessStatusCode`，非 2xx 一律算连不上，不抛；未配服务器时连请求都不发（同 C# 的空判） */
    fun testConnection(): Boolean = isConfigured() &&
        runCatching { fetcher.call("GET", "$base/api/status").status in 200..299 }.getOrDefault(false)

    fun status(): ServerStatus? {
        if (!isConfigured()) return null
        val json = runCatching { fetcher.get("$base/api/status") }.getOrNull() ?: return null
        val obj = runCatching { Json.parseToJsonElement(json).asObjectOrNull() }.getOrNull() ?: return null
        return ServerStatus(
            songs = obj.intCI("songs"),
            artists = obj.intCI("artists"),
            albums = obj.intCI("albums"),
            dhtPeers = obj.intCI("dhtPeers", "dht_peers"),
            rateLimit = obj.intCI("rateLimit", "rate_limit"),
            device = obj.strCI("device"),
        )
    }

    fun songs(): List<ServerSong> = listAt("$base/api/songs")

    /** 关键词按 C# 的 `Uri.EscapeDataString` 转义（空格 → %20，不是 +） */
    fun search(keyword: String): List<ServerSong> =
        listAt("$base/api/search?q=${escapeDataString(keyword)}")

    fun streamUrl(songId: Int): String = "$base/api/stream/$songId"

    /** 服务端把封面/歌词给的是相对路径，取流地址同样挂在服务端下 */
    fun assetUrl(path: String?): String? {
        val p = path?.takeIf { it.isNotBlank() } ?: return null
        if (p.startsWith("http://") || p.startsWith("https://")) return p
        return "$base/${p.trimStart('/')}"
    }

    private fun listAt(url: String): List<ServerSong> {
        if (!isConfigured()) return emptyList()
        val json = runCatching { fetcher.get(url) }.getOrNull() ?: return emptyList()
        val elements = runCatching {
            when (val el = Json.parseToJsonElement(json)) {
                is JsonArray -> el.toList()
                // 与 Subsonic 侧同理：单元素时服务端可能直接回对象而不是数组
                is JsonObject -> listOf(el)
                else -> emptyList()
            }
        }.getOrDefault(emptyList())
        return elements.mapNotNull { el ->
            val obj = (el as? JsonObject)?.let { it } ?: return@mapNotNull null
            ServerSong(
                id = obj.intCI("id"),
                title = obj.strCI("title"),
                artist = obj.strCI("artist"),
                album = obj.strCI("album"),
                durationSeconds = obj.intCI("duration"),
                filePath = obj.strCI("filePath", "file_path"),
                fileSize = obj.longCI("fileSize", "file_size"),
                bitrate = obj.intCI("bitrate"),
                trackNumber = obj.intCI("trackNumber", "track_number"),
                year = obj.intCI("year"),
                genre = obj.strCI("genre").ifEmpty { null },
                artistId = obj.longCI("artistId", "artist_id"),
                albumId = obj.longCI("albumId", "album_id"),
                coverArtPath = obj.strCI("coverArtPath", "cover_art_path").ifEmpty { null },
                dateModified = obj.longCI("dateModified", "date_modified"),
                lyricsPath = obj.strCI("lyricsPath", "lyrics_path").ifEmpty { null },
            )
        }
    }

    private fun JsonElement.asObjectOrNull(): JsonObject? = this as? JsonObject

    private companion object {
        /** 只按 RFC 3986 的未保留字符转义，其余一律 %XX —— 与 .NET `Uri.EscapeDataString` 一致 */
        fun escapeDataString(value: String): String = buildString {
            for (byte in value.toByteArray(Charsets.UTF_8)) {
                val c = byte.toInt() and 0xFF
                if ((c in 'a'.code..'z'.code) || (c in 'A'.code..'Z'.code) || (c in '0'.code..'9'.code) ||
                    c == '-'.code || c == '_'.code || c == '.'.code || c == '~'.code
                ) {
                    append(c.toChar())
                } else {
                    append('%').append(c.toString(16).uppercase().padStart(2, '0'))
                }
            }
        }
    }
}

internal fun JsonObject.intCI(vararg names: String): Int = strKey(names)?.let { key ->
    (this[key] as? JsonPrimitive)?.let { it.int ?: it.doubleOrNull?.toInt() }
} ?: 0

internal fun JsonObject.longCI(vararg names: String): Long = strKey(names)?.let { key ->
    (this[key] as? JsonPrimitive)?.let { it.long ?: it.doubleOrNull?.toLong() }
} ?: 0L

internal fun JsonObject.strCI(vararg names: String): String = strKey(names)?.let { key ->
    val value = this[key]
    if (value is JsonNull || value !is JsonPrimitive) null else value.content
} ?: ""

/** 大小写不敏感找键名，对应 C# `PropertyNameCaseInsensitive = true` */
private fun JsonObject.strKey(names: Array<out String>): String? {
    for (name in names) {
        if (containsKey(name)) return name
        val match = keys.firstOrNull { it.equals(name, ignoreCase = true) } ?: continue
        return match
    }
    return null
}
