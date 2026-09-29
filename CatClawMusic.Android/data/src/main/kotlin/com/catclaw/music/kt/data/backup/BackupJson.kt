package com.catclaw.music.kt.data.backup

import kotlinx.serialization.json.Json
import kotlinx.serialization.json.JsonArray
import kotlinx.serialization.json.JsonObject
import kotlinx.serialization.json.JsonPrimitive
import kotlinx.serialization.json.buildJsonArray
import kotlinx.serialization.json.buildJsonObject
import kotlinx.serialization.json.jsonArray
import kotlinx.serialization.json.JsonElement
import kotlinx.serialization.json.jsonObject
import kotlinx.serialization.json.put

/**
 * 备份文件的 JSON 读写。手写而不上 `@Serializable`，两个理由：
 * 1. `:data` 没挂 serialization 插件，而 [com.catclaw.music.kt.data.ai.LlmConfigStore] 早就是手写 JsonElement，
 *    同一模块里两种风格更糟；
 * 2. 这个文件是**跨端交换格式**，键名要逐条钉住 C# 的 camelCase 输出，写死比让插件猜更安全。
 * 读侧对多余键忽略、对缺失键取默认值——C# 那边以后加字段时旧安卓包不该崩。
 */
object BackupJson {

    private val JSON = Json { prettyPrint = true; encodeDefaults = false }

    fun encode(data: BackupData, configEncoder: (com.catclaw.music.kt.core.ai.LlmConfig) -> JsonObject): String {
        val root = buildJsonObject {
            put("version", data.version)
            put("createdAt", data.createdAt)
            put("playlists", buildJsonArray { data.playlists.forEach { add(it.toJson()) } })
            put("playlistSongs", buildJsonArray { data.playlistSongs.forEach { add(it.toJson()) } })
            put("playHistory", buildJsonArray { data.playHistory.forEach { add(it.toJson()) } })
            put("favorites", buildJsonArray { data.favorites.forEach { add(it.toJson()) } })
            put("artists", buildJsonArray { data.artists.forEach { add(it.toJson()) } })
            put("artistCovers", buildJsonArray { data.artistCovers.forEach { add(it.toJson()) } })
            put("llmConfigs", buildJsonArray { data.llmConfigs.forEach { add(configEncoder(it)) } })
            data.currentConfigName?.let { put("currentConfigName", it) }
            data.currentAgentId?.let { put("currentAgentId", it) }
            put("chatMessages", buildJsonArray { data.chatMessages.forEach { add(it.toJson()) } })
            data.aiMemoryContent?.let { put("aiMemoryContent", it) }
        }
        return JSON.encodeToString(JsonElement.serializer(), root)
    }

    fun decode(text: String, configDecoder: (JsonObject) -> com.catclaw.music.kt.core.ai.LlmConfig): BackupData {
        val root = JSON.parseToJsonElement(text).jsonObject
        fun str(k: String) = (root[k] as? JsonPrimitive)?.contentOrNullSafe()
        fun intAt(o: JsonObject, k: String) = (o[k] as? JsonPrimitive)?.content?.toIntOrNull() ?: 0
        fun longAt(o: JsonObject, k: String) = (o[k] as? JsonPrimitive)?.content?.toLongOrNull() ?: 0L
        fun boolAt(o: JsonObject, k: String) = (o[k] as? JsonPrimitive)?.content?.toBooleanStrictOrNull() ?: false
        fun strAt(o: JsonObject, k: String) = (o[k] as? JsonPrimitive)?.takeIf { it.content != "null" }?.content
        fun rows(k: String): List<JsonObject> = (root[k] as? JsonArray)?.mapNotNull { it as? JsonObject } ?: emptyList()

        return BackupData(
            version = str("version") ?: "1.0",
            createdAt = str("createdAt").orEmpty(),
            playlists = rows("playlists").map {
                PlaylistBackupEntry(intAt(it, "id"), strAt(it, "name").orEmpty(), longAt(it, "createdAt"), longAt(it, "updatedAt"), intAt(it, "songCount"), boolAt(it, "isSystem"), intAt(it, "sortOrder"))
            },
            playlistSongs = rows("playlistSongs").map {
                PlaylistSongBackupEntry(intAt(it, "playlistId"), intAt(it, "songId"), strAt(it, "songTitle"), strAt(it, "songArtist"), intAt(it, "position"))
            },
            playHistory = rows("playHistory").map {
                PlayHistoryBackupEntry(intAt(it, "songId"), strAt(it, "songTitle"), strAt(it, "songArtist"), intAt(it, "playCount"), longAt(it, "playedAt"))
            },
            favorites = rows("favorites").map {
                FavoriteBackupEntry(intAt(it, "songId"), strAt(it, "songTitle"), strAt(it, "songArtist"), longAt(it, "addedAt"))
            },
            artists = rows("artists").map {
                ArtistBackupEntry(strAt(it, "name").orEmpty(), strAt(it, "gender"), strAt(it, "birthday"), strAt(it, "region"), strAt(it, "description"))
            },
            artistCovers = rows("artistCovers").map {
                ArtistCoverBackupEntry(strAt(it, "artistName").orEmpty(), strAt(it, "fileName").orEmpty())
            },
            llmConfigs = rows("llmConfigs").map(configDecoder),
            currentConfigName = str("currentConfigName"),
            currentAgentId = str("currentAgentId"),
            chatMessages = rows("chatMessages").map {
                ChatMessageBackupEntry(intAt(it, "id"), strAt(it, "role").orEmpty(), strAt(it, "content").orEmpty(), longAt(it, "timestamp"))
            },
            aiMemoryContent = str("aiMemoryContent"),
        )
    }

    /** C# 的 `JsonNamingPolicy.CamelCase` 会把 null 写成 `"x": null`（默认不写），两种都要吃得下 */
    private fun JsonPrimitive.contentOrNullSafe(): String? = if (isString) content else content.takeIf { it != "null" }

    private fun PlaylistBackupEntry.toJson() = buildJsonObject {
        put("id", id); put("name", name); put("createdAt", createdAt); put("updatedAt", updatedAt)
        put("songCount", songCount); put("isSystem", isSystem); put("sortOrder", sortOrder)
    }

    private fun PlaylistSongBackupEntry.toJson() = buildJsonObject {
        put("playlistId", playlistId); put("songId", songId)
        songTitle?.let { put("songTitle", it) }; songArtist?.let { put("songArtist", it) }; put("position", position)
    }

    private fun PlayHistoryBackupEntry.toJson() = buildJsonObject {
        put("songId", songId); songTitle?.let { put("songTitle", it) }; songArtist?.let { put("songArtist", it) }
        put("playCount", playCount); put("playedAt", playedAt)
    }

    private fun FavoriteBackupEntry.toJson() = buildJsonObject {
        put("songId", songId); songTitle?.let { put("songTitle", it) }; songArtist?.let { put("songArtist", it) }; put("addedAt", addedAt)
    }

    private fun ArtistBackupEntry.toJson() = buildJsonObject {
        put("name", name); gender?.let { put("gender", it) }; birthday?.let { put("birthday", it) }
        region?.let { put("region", it) }; description?.let { put("description", it) }
    }

    private fun ArtistCoverBackupEntry.toJson() = buildJsonObject { put("artistName", artistName); put("fileName", fileName) }

    private fun ChatMessageBackupEntry.toJson() = buildJsonObject {
        put("id", id); put("role", role); put("content", content); put("timestamp", timestamp)
    }
}
