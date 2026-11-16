package com.catclaw.music.kt.data.remote

import com.catclaw.music.kt.core.model.ConnectionProfile
import java.io.IOException
import java.security.MessageDigest
import java.util.UUID
import kotlinx.serialization.json.Json
import kotlinx.serialization.json.JsonArray
import kotlinx.serialization.json.JsonObject
import kotlinx.serialization.json.contentOrNull
import kotlinx.serialization.json.intOrNull
import kotlinx.serialization.json.jsonArray
import kotlinx.serialization.json.jsonObject
import kotlinx.serialization.json.jsonPrimitive
import kotlinx.serialization.json.longOrNull

/**
 * Subsonic / OpenSubsonic（Navidrome）客户端，行为逐条对齐 C# `CatClawMusic.Data/SubsonicService.cs`：
 * salt = GUID("N") 前 12 位、token = md5(password + salt) 小写、认证参数走 query、
 * 响应里数组/单对象两种写法都要能吃（服务端只有一张专辑时会退化成对象）。
 *
 * 全部方法是阻塞的：调用方负责放到 IO 线程上。
 */
class SubsonicClient(
    private val profile: ConnectionProfile,
    private val fetcher: HttpFetcher = UrlHttpFetcher,
    private val saltSupplier: () -> String = { UUID.randomUUID().toString().replace("-", "").take(12) },
) {

    data class AlbumRef(val id: String, val title: String, val artist: String)

    data class Ping(val ok: Boolean, val message: String, val version: String = "")

    private val json = Json { ignoreUnknownKeys = true; isLenient = true }

    fun authParams(): String {
        val salt = saltSupplier()
        val token = md5Hex(profile.password + salt)
        return "u=${urlEncode(profile.userName)}&t=$token&s=$salt" +
            "&v=${urlEncode(profile.apiVersion)}&c=${urlEncode(profile.clientName)}&f=json"
    }

    fun apiUrl(endpointWithQuery: String): String {
        val sep = if (endpointWithQuery.contains('?')) "&" else "?"
        return "${profile.baseUrl()}/rest/$endpointWithQuery$sep${authParams()}"
    }

    /** 播放地址直接带认证参数，ExoPlayer 才能自己去取流 */
    fun streamUrl(trackId: String): String = apiUrl("stream.view?id=${urlEncode(trackId)}")

    fun coverArtUrl(coverArtId: String?): String? =
        coverArtId?.takeIf { it.isNotBlank() }
            ?.let { apiUrl("getCoverArt.view?id=${urlEncode(it)}") }

    fun ping(): Ping = try {
        // `request` 在 status != ok 时抛 IOException，所以走到这里就说明服务器答了 ok。
        val root = request("ping.view")
        // `openSubsonic` 是**布尔**（Navidrome 0.61 实测返回 `"openSubsonic":true`），不是对象。
        // 早先这里写 `root["openSubsonic"]?.jsonObject`，遇到布尔字面量抛
        // `IllegalArgumentException: JsonLiteral is not a JsonObject`；而当时只 catch IOException，
        // 于是「测试连接」必然失败 —— 症状是 Navidrome 永远连不上，其实认证与列曲目都正常。
        val kind = root.str("type").ifEmpty { "subsonic" }
        val serverVersion = root.str("serverVersion")
        Ping(ok = true, message = "ok", version = listOf(kind, serverVersion).filter { it.isNotEmpty() }.joinToString(" "))
    } catch (e: IOException) {
        Ping(ok = false, message = e.message ?: "unreachable")
    }

    fun search(query: String): List<RemoteTrack> {
        val root = request("search3.view?query=${urlEncode(query)}")
        val result = root.obj("searchResult3") ?: return emptyList()
        return result.objects("song").map { track(it) }
    }

    /** type 例如 alphabeticalByArtist / newest / random / frequent */
    fun albumList(type: String, size: Int = 200, offset: Int = 0): List<AlbumRef> {
        val root = request("getAlbumList2.view?type=$type&size=$size&offset=$offset")
        return root.obj("albumList2")?.objects("album").orEmpty().map {
            AlbumRef(
                id = it.str("id"),
                title = it.str("name"),
                artist = it.str("artist"),
            )
        }
    }

    /** 与 C# 一致：按艺术家字母序翻页，最多 50 页就收，避免无限翻 */
    fun allAlbums(maxPages: Int = 50, pageSize: Int = 200): List<AlbumRef> {
        val acc = ArrayList<AlbumRef>()
        for (page in 0 until maxPages) {
            val batch = albumList("alphabeticalByArtist", pageSize, page * pageSize)
            if (batch.isEmpty()) break
            acc += batch
            if (batch.size < pageSize) break
        }
        return acc
    }

    fun albumSongs(albumId: String): List<RemoteTrack> =
        request("getAlbum.view?id=${urlEncode(albumId)}").obj("album")
            ?.objects("song").orEmpty().map { track(it) }

    fun song(trackId: String): RemoteTrack? =
        request("getSong.view?id=${urlEncode(trackId)}").obj("song")?.let { track(it) }

    /** 歌词有三种返回形态（structuredLyrics / lyrics / lyricsList），与 C# 的兜底顺序一致 */
    fun lyrics(trackId: String): String? = try {
        val root = request("getLyricsBySongId.view?id=${urlEncode(trackId)}")
        val list = root.obj("lyricsList")
        val structured = list?.objects("structuredLyrics")?.firstOrNull()
        val lines = structured?.objects("line").orEmpty()
        when {
            // 毫秒时间戳转成 LRC，交给 :core 的 LrcParser 读，不再自造格式
            lines.isNotEmpty() -> lines.joinToString("\n") {
                "[${lrcTimestamp(it.long("start"))}]${it.str("value")}"
            }
            else -> list?.objects("lyrics")?.firstOrNull()?.str("value")
                ?: root.obj("lyrics")?.str("value")?.takeIf { it.isNotEmpty() }
        }
    } catch (e: IOException) {
        null
    }

    private fun request(endpointWithQuery: String): JsonObject {
        val text = fetcher.get(apiUrl(endpointWithQuery))
        val root = json.parseToJsonElement(text).jsonObject
            .get("subsonic-response")?.jsonObject
            ?: throw IOException("不是 Subsonic 响应")
        val status = root.str("status")
        if (status != "ok") {
            val error = root.obj("error")
            throw IOException("subsonic ${error?.str("type").orEmpty()} ${error?.str("message").orEmpty()}")
        }
        return root
    }

    private fun track(o: JsonObject) = RemoteTrack(
        remoteId = o.str("id"),
        title = o.str("title"),
        artist = o.str("artist"),
        album = o.str("album"),
        durationSeconds = o.int("duration"),
        streamUrl = streamUrl(o.str("id")),
        coverArtUrl = coverArtUrl(o["coverArt"]?.jsonPrimitive?.contentOrNull),
        sizeBytes = o.long("size"),
        bitrateKbps = o.int("bitRate"),
    )

    /** 数组 / 单对象两种写法都归一成 List —— Subsonic 的 JSON 只有一项时会退化成对象 */
    private fun JsonObject.objects(key: String): List<JsonObject> = when (val el = this[key]) {
        null -> emptyList()
        is JsonArray -> el.mapNotNull { it as? JsonObject }
        is JsonObject -> listOf(el)
        else -> emptyList()
    }

    /**
     * 安全取对象：字段缺失、类型不是对象（布尔/字符串/数组）都回 null。
     *
     * 不能直接 `.jsonObject` —— 那个在类型不符时抛 `IllegalArgumentException`，
     * 而本类的方法只 catch `IOException`，异常会一路冒到「测试连接」变成一句
     * 莫名其妙的失败。`openSubsonic: true` 就是这个坑（见 [ping]）。
     */
    private fun JsonObject.obj(key: String): JsonObject? = this[key] as? JsonObject

    private fun JsonObject.str(key: String): String = this[key]?.jsonPrimitive?.contentOrNull.orEmpty()

    private fun JsonObject.int(key: String): Int = this[key]?.jsonPrimitive?.intOrNull ?: 0

    private fun JsonObject.long(key: String): Long = this[key]?.jsonPrimitive?.longOrNull ?: 0L
}

internal fun md5Hex(text: String): String =
    MessageDigest.getInstance("MD5").digest(text.toByteArray(Charsets.UTF_8))
        .joinToString("") { "%02x".format(it) }

/** 毫秒 → LRC 的 mm:ss.SSS（LrcParser 按三位小数当毫秒处理） */
private fun lrcTimestamp(ms: Long): String {
    val total = ms.coerceAtLeast(0L)
    return "%02d:%02d.%03d".format(total / 60_000, (total / 1_000) % 60, total % 1_000)
}

private fun urlEncode(value: String): String =
    java.net.URLEncoder.encode(value, "UTF-8")
