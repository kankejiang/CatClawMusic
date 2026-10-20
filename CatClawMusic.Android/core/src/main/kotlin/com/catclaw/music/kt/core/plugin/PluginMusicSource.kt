package com.catclaw.music.kt.core.plugin

import kotlinx.serialization.json.JsonElement
import kotlinx.serialization.json.JsonObject
import kotlinx.serialization.json.JsonPrimitive
import kotlinx.serialization.json.buildJsonObject
import kotlinx.serialization.json.intOrNull
import kotlinx.serialization.json.put

/**
 * 一个插件 = 一个音乐来源：初始化协商 → 搜索 → 取流 → 歌词（→ 歌单）。
 *
 * 这一层是把 [PluginRpcClient]（报文）接到应用音乐来源上的那一段，对应 C# `IOnlineMusicPlugin`
 * 被 `OnlineMusicAggregator` 消费的那几件事（见 [PluginProtocol] 顶部的对应表）。
 * 在此之前，协议、传输、注册表、诊断全都写好了并有测试，**但没有任何主源码调用方** ——
 * 也就是说插件从来没有真的被调用过。
 *
 * 三条在这里定下的规矩：
 *
 * 1. **失败收敛成空值，不抛异常**。搜索失败返回空列表、取流失败返回 null。
 *    理由与 `OnlineMusicAggregator` 一致：一个源坏了不该让整次搜索失败，
 *    上层要能对多个源逐个取结果（C# 那边也是「聚合，按平台合并展示」）；
 * 2. **能力以清单为准，但清单不写就不限制**。老插件不必为了兼容去补 capabilities 字段；
 *    写了就按写的来（未知能力已被 [PluginManifest.validate] 剔掉，不会因此变哑巴）；
 * 3. **初始化必须协商协议版本**（[PluginProtocol.negotiate]）。版本不匹配就当这个插件不可用，
 *    而不是带着未知语义硬调 —— 那正是 C# 靠反射适配器兜着的那类麻烦。
 */
class PluginMusicSource(
    val manifest: PluginManifest,
    private val channel: PluginChannel,
    private val hostPlatform: String,
    private val hostVersion: String,
) {

    /** 插件自称的平台名（搜索结果里的 `platform` 字段是它） */
    val platform: String get() = manifest.id.substringAfterLast('/')

    private val declared = manifest.capabilities.toSet()

    /** 清单没写能力 = 不限制；写了就按写的来 */
    fun supports(capability: String): Boolean = declared.isEmpty() || capability in declared

    /** 初始化并协商协议版本；false = 这个插件用不了（调用方据此跳过它，而不是让整轮搜索失败） */
    suspend fun initialize(): Boolean {
        val response = channel.request(
            PluginProtocol.METHOD_INITIALIZE,
            buildJsonObject {
                put("protocol", JsonPrimitive(PluginProtocol.VERSION))
                put("platform", JsonPrimitive(hostPlatform))
                put("host_version", JsonPrimitive(hostVersion))
            },
        )
        if (response.isError) return false
        // 回 `{"protocol": 2}` 或直接回 2，两种都认（同 PluginPayloads 对取流结果的态度）
        val reported = when (val r = response.result) {
            is JsonPrimitive -> r.intOrNull
            is JsonObject -> (r["protocol"] as? JsonPrimitive)?.intOrNull
            else -> null
        } ?: return false
        return PluginProtocol.negotiate(reported)
    }

    suspend fun search(
        keyword: String,
        page: Int = PluginPayloads.FIRST_PAGE,
        pageSize: Int = PluginPayloads.DEFAULT_SEARCH_PAGE_SIZE,
    ): List<RpcSong> {
        if (!supports(CAP_SEARCH)) return emptyList()
        val response = channel.request(
            PluginProtocol.METHOD_SEARCH,
            PluginPayloads.searchParams(keyword, page, pageSize),
        )
        if (response.isError) return emptyList()
        return RpcSong.listFrom(response.result)
    }

    suspend fun playUrl(song: RpcSong, quality: Int = 0): String? {
        if (!supports(CAP_PLAY)) return null
        val response = channel.request(
            PluginProtocol.METHOD_GET_PLAY_URL,
            PluginPayloads.playUrlParams(song, quality),
        )
        if (response.isError) return null
        return PluginPayloads.playUrlFrom(response.result)
    }

    suspend fun lyrics(song: RpcSong): PluginPayloads.LyricsStreams? {
        if (!supports(CAP_LYRICS)) return null
        val response = channel.request(
            PluginProtocol.METHOD_GET_LYRICS,
            PluginPayloads.lyricsParams(song),
        )
        if (response.isError) return null
        return PluginPayloads.lyricsFrom(response.result)
    }

    suspend fun playlists(category: String? = null): List<PluginPayloads.RpcPlaylist> {
        if (!supports(CAP_PLAYLIST)) return emptyList()
        val params: JsonElement = buildJsonObject {
            if (!category.isNullOrBlank()) put("category", JsonPrimitive(category))
        }
        val response = channel.request(PluginProtocol.METHOD_GET_PLAYLISTS, params)
        if (response.isError) return emptyList()
        return PluginPayloads.RpcPlaylist.listFrom(response.result)
    }

    suspend fun playlistSongs(
        playlistId: String,
        page: Int = PluginPayloads.FIRST_PAGE,
        pageSize: Int = PluginPayloads.DEFAULT_PLAYLIST_PAGE_SIZE,
    ): List<RpcSong> {
        if (!supports(CAP_PLAYLIST)) return emptyList()
        val params = buildJsonObject {
            put("playlist_id", JsonPrimitive(playlistId))
            put("page", JsonPrimitive(page.coerceAtLeast(PluginPayloads.FIRST_PAGE)))
            put("page_size", JsonPrimitive(pageSize.coerceAtLeast(1)))
        }
        val response = channel.request(PluginProtocol.METHOD_GET_PLAYLIST_SONGS, params)
        if (response.isError) return emptyList()
        return RpcSong.listFrom(response.result)
    }

    suspend fun close() = channel.close()

    private companion object {
        const val CAP_SEARCH = "search"
        const val CAP_PLAY = "play"
        const val CAP_LYRICS = "lyrics"
        const val CAP_PLAYLIST = "playlist"
    }
}
