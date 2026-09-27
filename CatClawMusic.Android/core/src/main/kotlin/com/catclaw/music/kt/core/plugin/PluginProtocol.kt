package com.catclaw.music.kt.core.plugin

import kotlinx.serialization.json.Json
import kotlinx.serialization.json.JsonArray
import kotlinx.serialization.json.booleanOrNull
import kotlinx.serialization.json.JsonElement
import kotlinx.serialization.json.JsonObject
import kotlinx.serialization.json.JsonPrimitive
import kotlinx.serialization.json.buildJsonArray
import kotlinx.serialization.json.buildJsonObject
import kotlinx.serialization.json.intOrNull
import kotlinx.serialization.json.longOrNull

/**
 * 插件协议 v2 的**换行分隔 JSON** 报文层（stdio 或 loopback HTTP 都可承载，见 [PluginChannel]）。
 *
 * 形状刻意贴近 JSON-RPC 2.0：一行一个请求、一行一个响应，`id` 回显，错误走 `error.code`。
 * 这不是偷懒，而是"双端通用"的最低成本解 —— Kotlin 与 .NET 两边都有现成的 JSON 库，
 * 而任何自定义二进制头都会立刻变成两份需要逐字节对齐的实现。
 *
 * 与现状的对应关系（左：今天的 CLR 调用，右：v2 方法名）：
 *   `IOnlineMusicPlugin.SearchAsync`          → `search`
 *   `GetPlayUrlAsync(OnlineSong)`             → `get_play_url`
 *   `GetLyricsWithRomaAsync`                  → `get_lyrics`
 *   `GetPlaylistsAsync` / `GetPlaylistSongsAsync` → `get_playlists` / `get_playlist_songs`
 *   `InitializeAsync` / `ShutdownAsync`       → `initialize` / `shutdown`
 * 今天没接线的（音频增强每帧回调、贡献 MAUI 视图）**故意不进 v2**：
 * 前者跨进程必然掉帧，后者是 `Microsoft.Maui.Controls.Page`，Compose 造不出来。
 */
object PluginProtocol {
    const val VERSION = 2
    const val METHOD_INITIALIZE = "initialize"
    const val METHOD_SHUTDOWN = "shutdown"
    const val METHOD_SEARCH = "search"
    const val METHOD_GET_PLAY_URL = "get_play_url"
    const val METHOD_GET_LYRICS = "get_lyrics"
    const val METHOD_GET_PLAYLISTS = "get_playlists"
    const val METHOD_GET_PLAYLIST_SONGS = "get_playlist_songs"

    /** 与 JSON-RPC 同码，方便插件作者直接套现成库 */
    const val ERR_PARSE = -32700
    const val ERR_METHOD_NOT_FOUND = -32601
    const val ERR_INVALID_PARAMS = -32602
    const val ERR_PLUGIN = -32000

    /** 取流是**字符串接力**：宿主只认 `RemoteId`，回播放内核的仍是 URL（对齐 C# `Song.RemoteId = "{platform}:{onlineId}"`） */
    const val REMOTE_ID_SEPARATOR = ":"

    private val JSON = Json { ignoreUnknownKeys = true }

    fun encodeRequest(id: Int, method: String, params: JsonElement = buildJsonObject {}): String =
        JSON.encodeToString(
            JsonElement.serializer(),
            buildJsonObject {
                put("protocol", JsonPrimitive(VERSION))
                put("id", JsonPrimitive(id))
                put("method", JsonPrimitive(method))
                put("params", params)
            },
        )

    fun decodeRequest(line: String): RpcRequest? {
        val obj = runCatching { JSON.parseToJsonElement(line) as? JsonObject }.getOrNull() ?: return null
        val method = (obj["method"] as? JsonPrimitive)?.takeIf { it.isString }?.content ?: return null
        return RpcRequest(
            protocol = (obj["protocol"] as? JsonPrimitive)?.intOrNull ?: 0,
            id = (obj["id"] as? JsonPrimitive)?.intOrNull ?: 0,
            method = method,
            params = obj["params"] as? JsonObject ?: buildJsonObject {},
        )
    }

    fun encodeResult(id: Int, result: JsonElement): String = envelope(id) { put("result", result) }

    fun encodeError(id: Int, code: Int, message: String, retryable: Boolean = false): String = envelope(id) {
        put(
            "error",
            buildJsonObject {
                put("code", JsonPrimitive(code))
                put("message", JsonPrimitive(message))
                put("retryable", JsonPrimitive(retryable))
            },
        )
    }

    fun decodeResponse(line: String): RpcResponse? {
        val obj = runCatching { JSON.parseToJsonElement(line) as? JsonObject }.getOrNull() ?: return null
        val error = obj["error"] as? JsonObject
        if (error != null) {
            return RpcResponse(
                id = (obj["id"] as? JsonPrimitive)?.intOrNull ?: 0,
                result = null,
                errorCode = (error["code"] as? JsonPrimitive)?.intOrNull ?: ERR_PLUGIN,
                errorMessage = (error["message"] as? JsonPrimitive)?.content.orEmpty(),
                retryable = (error["retryable"] as? kotlinx.serialization.json.JsonPrimitive)?.booleanOrNullSafe() ?: false,
            )
        }
        if (!obj.containsKey("result")) return null
        return RpcResponse(id = (obj["id"] as? JsonPrimitive)?.intOrNull ?: 0, result = obj["result"], null, "", false)
    }

    /**
     * 协商：主版本必须相等。今天完全没有这一层（兼容靠反射适配器 + FullName 兜底），
     * 所以插件升级和宿主升级互相不可知 —— v2 把它变成一次显式问答。
     */
    fun negotiate(pluginProtocol: Int): Boolean = pluginProtocol == VERSION

    private inline fun envelope(id: Int, block: JsonObjectBuilder.() -> Unit): String {
        val builder = JsonObjectBuilder()
        builder.block()
        return JSON.encodeToString(
            JsonElement.serializer(),
            buildJsonObject {
                put("protocol", JsonPrimitive(VERSION))
                put("id", JsonPrimitive(id))
                builder.fields.forEach { (k, v) -> put(k, v) }
            },
        )
    }

    /** 只为让 `put("result", …)` / `put("error", …)` 两种分支共用同一个信封构造 */
    class JsonObjectBuilder {
        val fields = LinkedHashMap<String, JsonElement>()
        fun put(key: String, value: JsonElement) { fields[key] = value }
    }
}

data class RpcRequest(
    val protocol: Int,
    val id: Int,
    val method: String,
    val params: JsonObject,
)

data class RpcResponse(
    val id: Int,
    val result: JsonElement?,
    val errorCode: Int?,
    val errorMessage: String,
    val retryable: Boolean,
) {
    val isError: Boolean get() = errorCode != null
}

/** 跨边界的一首歌。字段是 C# `OnlineSong` 的**可序列化子集**，`Internal: Dictionary<string,object>` 被压成 `extra`。 */
data class RpcSong(
    val id: String,
    val platform: String,
    val title: String,
    val artist: String = "",
    val album: String = "",
    val durationMs: Long = 0,
    val coverUrl: String = "",
    val extra: Map<String, String> = emptyMap(),
) {
    val remoteId: String get() = "$platform${PluginProtocol.REMOTE_ID_SEPARATOR}$id"

    fun toJson(): JsonElement = buildJsonObject {
        put("id", JsonPrimitive(id))
        put("platform", JsonPrimitive(platform))
        put("title", JsonPrimitive(title))
        put("artist", JsonPrimitive(artist))
        put("album", JsonPrimitive(album))
        put("duration_ms", JsonPrimitive(durationMs))
        put("cover_url", JsonPrimitive(coverUrl))
        if (extra.isNotEmpty()) {
            put("extra", buildJsonObject { extra.forEach { (k, v) -> put(k, JsonPrimitive(v)) } })
        }
    }

    companion object {
        fun fromJson(element: JsonElement): RpcSong? {
            val obj = element as? JsonObject ?: return null
            fun s(key: String) = (obj[key] as? JsonPrimitive)?.takeIf { it.isString }?.content.orEmpty()
            val id = s("id")
            if (id.isBlank()) return null
            val extra = (obj["extra"] as? JsonObject)
                ?.mapNotNull { (k, v) -> (v as? JsonPrimitive)?.takeIf { p -> p.isString }?.let { k to it.content } }
                ?.toMap()
                ?: emptyMap()
            return RpcSong(
                id = id,
                platform = s("platform"),
                title = s("title"),
                artist = s("artist"),
                album = s("album"),
                durationMs = (obj["duration_ms"] as? JsonPrimitive)?.longOrNull ?: 0L,
                coverUrl = s("cover_url"),
                extra = extra,
            )
        }

        fun listFrom(element: JsonElement?): List<RpcSong> =
            (element as? JsonArray)?.mapNotNull { fromJson(it) } ?: emptyList()
    }
}

/** 一条方法调用的失败原因。`retryable` 让宿主能区分"换一首重试"和"这源废了"。 */
data class RpcFailure(val code: Int, val message: String, val retryable: Boolean)
private fun JsonPrimitive.booleanOrNullSafe(): Boolean? = if (isString) content.toBooleanStrictOrNull() else booleanOrNull

/**
 * 传输抽象。v2 只规定报文，不规定管子：
 * stdio（换行分隔）与 loopback HTTP（一次一请求）都能实现这一个接口。
 */
interface PluginChannel {
    suspend fun request(method: String, params: JsonElement = buildJsonObject {}): RpcResponse
    suspend fun close()
}
