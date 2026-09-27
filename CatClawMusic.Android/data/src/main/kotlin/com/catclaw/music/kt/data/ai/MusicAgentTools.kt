package com.catclaw.music.kt.data.ai

import com.catclaw.music.kt.core.ai.Diagnosis
import com.catclaw.music.kt.core.ai.WebFetchGateway
import com.catclaw.music.kt.core.ai.WebOutcome
import com.catclaw.music.kt.core.ai.WebSearcher
import com.catclaw.music.kt.core.ai.MusicSourceRegistry
import com.catclaw.music.kt.core.ai.AgentTool
import com.catclaw.music.kt.core.ai.ToolParamSchema
import com.catclaw.music.kt.core.model.Song
import com.catclaw.music.kt.core.net.HtmlText
import com.catclaw.music.kt.core.playback.PlayMode
import com.catclaw.music.kt.core.playback.PlayerApi
import com.catclaw.music.kt.core.playback.SessionLauncher
import com.catclaw.music.kt.data.db.LibraryDao
import com.catclaw.music.kt.data.db.PlaylistDao
import com.catclaw.music.kt.data.db.SongListItem
import com.catclaw.music.kt.data.db.entity.PlaylistEntity
import com.catclaw.music.kt.data.db.toSong
import javax.inject.Inject
import javax.inject.Singleton
import kotlin.math.roundToInt
import kotlinx.coroutines.flow.first
import kotlinx.coroutines.async
import kotlinx.coroutines.awaitAll
import kotlinx.coroutines.coroutineScope
import kotlinx.serialization.json.Json
import kotlinx.serialization.json.JsonElement
import kotlinx.serialization.json.JsonObject
import kotlinx.serialization.json.JsonPrimitive
import kotlinx.serialization.json.buildJsonObject
import kotlinx.serialization.json.intOrNull
import kotlinx.serialization.json.jsonPrimitive

/** 工具返回给模型的结果 JSON（顺序无关，键名与 C# 侧一致：success / message / error / songs…） */
internal fun resultJson(vararg pairs: Pair<String, Any?>): String {
    val obj = JsonObject(pairs.associate { (k, v) ->
        k to when (v) {
            null -> JsonPrimitive(null as String?)
            is String -> JsonPrimitive(v)
            is Boolean -> JsonPrimitive(v)
            is Int -> JsonPrimitive(v)
            is Long -> JsonPrimitive(v)
            is JsonElement -> v
            is List<*> -> kotlinx.serialization.json.JsonArray(v.map { JsonPrimitive(it.toString()) })
            else -> JsonPrimitive(v.toString())
        }
    })
    return RESULT_JSON.encodeToString(JsonElement.serializer(), obj)
}

private val RESULT_JSON = Json { encodeDefaults = true }

internal fun argInt(arguments: String, vararg keys: String): Int {
    val obj = runCatching { RESULT_JSON.parseToJsonElement(arguments).let { it as? JsonObject } }.getOrNull()
        ?: return 0
    for (key in keys) {
        val value = obj[key] as? JsonPrimitive ?: continue
        value.intOrNull?.let { return it }
        value.content.toIntOrNull()?.let { return it }
    }
    return 0
}

/**
 * 与 [argInt] 的唯一区别：**参数缺失时返回 null 而不是 0**。
 *
 * C# `ArgHelper.ExtractIntArgFallback` 缺失回落到 0，而 `control_playback` 的 default 分支判的是
 * `volume >= 0 && volume <= 100` —— 于是模型只给了一个不认识 action、没带 volume 时，
 * C# 会静默把音量设成 0（表现就是"说着说着没声了"）。这里保留 C# 的分支形状，
 * 只把"缺失"和"真的是 0"分开，是有意的偏离，不是漏抄。
 */
internal fun argIntOrNull(arguments: String, key: String): Int? {
    val obj = runCatching { RESULT_JSON.parseToJsonElement(arguments).let { it as? JsonObject } }.getOrNull() ?: return null
    val value = obj[key] as? JsonPrimitive ?: return null
    return value.intOrNull ?: value.content.toIntOrNull()
}

internal fun argString(arguments: String, vararg keys: String): String {    val obj = runCatching { RESULT_JSON.parseToJsonElement(arguments).let { it as? JsonObject } }.getOrNull()
        ?: return ""
    for (key in keys) {
        ((obj[key] as? JsonPrimitive)?.contentOrNullSafe() ?: continue).let { if (it.isNotBlank()) return it }
    }
    return ""
}

private fun JsonPrimitive.contentOrNullSafe(): String? = if (isString) content else null

internal fun SongListItem.asSongRow(): String = "$id|$title|$artist|$album"

/**
 * 播放与曲库类工具，名称/描述/参数与 C# `AgentTools.Playback.cs`、`AgentTools.MusicLibrary.cs` 对齐。
 * 只读工具标 [isReadOnly] 以便 Agent 并行分派；会改状态的一律 false 走串行。
 */
@Singleton
class MusicAgentTools @Inject constructor(
    private val player: PlayerApi,
    private val dao: LibraryDao,
    private val session: SessionLauncher,
    private val playlists: PlaylistDao,
    private val fetcher: PageFetcher,
    /** 音乐源注册表。给默认值只为让既有单测不必构造整个注册表；DI 侧永远注入真实例 */
    private val sources: MusicSourceRegistry? = null,
    private val downloads: com.catclaw.music.kt.core.ai.DownloadEnqueuer? = null,
    private val web: com.catclaw.music.kt.core.ai.WebFetchGateway? = null,
    private val browser: com.catclaw.music.kt.core.ai.BrowserNavigator? = null,
) {

    val all: List<AgentTool> by lazy {
        listOf(
            SearchMusic(), PlaySong(), ControlPlayback(), GetCurrentSong(), GetPlayQueue(), ToggleFavorite(),
            GetFavoriteSongs(), GetRecentSongs(), ClearQueue(),
            CreatePlaylist(), AddSongToPlaylist(), RemoveSongFromPlaylist(), ListPlaylists(),
            GetPlaylistSongs(), DeletePlaylist(), AddToPlayQueue(), GetListeningStats(), FetchWebPage(),
            UpdateMusicSource(), KuwoDownload(), NeteaseDownload(), DownloadFile(), WebSearch(), BrowserOpen(),
        )
    }

    private suspend fun mergedSongs(): List<SongListItem> = dao.localSongRows()

    /** 本地指令用的搜索结果（没有模型时也要能给出可点的歌） */
    suspend fun searchCards(keyword: String): List<Song> = dao.searchSongs(keyword).map { it.toSong() }

    /** 0-100 的当前音量，本地"大声点/小声点"要在它基础上 ±10 */
    fun volumePercent(): Int = (player.volume * 100).roundToInt()

    private companion object {
        const val HONEST_UA = "CatClawMusic/1.8"
        const val MAX_CONTENT = 6_000
        const val PROBE_LIMIT = 6
    }

    inner class SearchMusic : AgentTool {
        override val name = "search_music"
        override val description = "在本地音乐库中搜索歌曲，返回带 ID 的候选列表"
        override val properties = mapOf("keyword" to ToolParamSchema("string", "歌名、歌手或专辑关键词"))
        override val required = listOf("keyword")
        override val isReadOnly = true

        override suspend fun execute(argumentsJson: String): String {
            val keyword = argString(argumentsJson, "keyword", "query")
            if (keyword.isBlank()) return resultJson("error" to "请提供搜索关键词")
            val rows = dao.searchSongs(keyword).take(20)
            if (rows.isEmpty()) return resultJson("success" to true, "message" to "没找到与「$keyword」相关的歌", "songs" to emptyList<String>())
            return resultJson(
                "success" to true,
                "message" to "找到 ${rows.size} 首",
                "songs" to rows.map { it.asSongRow() },
            )
        }
    }

    inner class PlaySong : AgentTool {
        override val name = "play_song"
        override val description = "播放指定歌曲"
        override val properties = mapOf("song_id" to ToolParamSchema("integer", "要播放的歌曲 ID"))
        override val required = listOf("song_id")
        override val isReadOnly = false

        override suspend fun execute(argumentsJson: String): String {
            val songId = argInt(argumentsJson, "song_id", "songId")
            if (songId <= 0) return resultJson("error" to "请提供有效的歌曲 ID")
            val songs = mergedSongs()
            val target = songs.firstOrNull { it.id == songId } ?: return resultJson("error" to "未找到该歌曲")
            session.ensure()
            player.playSongs(songs.map { it.toSong() }, startIndex = songs.indexOf(target))
            return resultJson("success" to true, "message" to "正在播放「${target.title}」- ${target.artist}")
        }
    }

    inner class ControlPlayback : AgentTool {
        override val name = "control_playback"
        override val description = "控制音乐播放，支持暂停、恢复、下一首、上一首、停止、调节音量、跳转到指定位置"
        override val properties = mapOf(
            "action" to ToolParamSchema("string", "操作类型", listOf("pause", "resume", "next", "previous", "stop")),
            "volume" to ToolParamSchema("integer", "音量 0-100，仅当 action 不指定时使用"),
            "seek_to" to ToolParamSchema("integer", "跳转到指定秒数"),
        )
        override val required = listOf("action")
        override val isReadOnly = false

        /**
         * 与 C# 同一结构：action 只列 5 个播放动作，音量/跳转落在 default 分支里按参数试，
         * 所以本地指令可以直接发 `{"volume":60}` 而不必编一个 C# 没有的 action。
         */
        override suspend fun execute(argumentsJson: String): String = when (val action = argString(argumentsJson, "action")) {
            "pause" -> { player.pause(); resultJson("success" to true, "message" to "已暂停播放") }
            "resume" -> { player.resume(); resultJson("success" to true, "message" to "已恢复播放") }
            "next" -> { player.next(); resultJson("success" to true, "message" to "已切到下一首") }
            "previous" -> { player.previous(); resultJson("success" to true, "message" to "已切到上一首") }
            "stop" -> { player.pause(); player.seekTo(0); resultJson("success" to true, "message" to "已停止播放") }
            else -> {
                val volume = argIntOrNull(argumentsJson, "volume")
                when {
                    volume != null && volume in 0..100 -> {
                        player.volume = volume / 100f
                        resultJson("success" to true, "message" to "音量已设置为 $volume")
                    }
                    else -> {
                        val seekTo = argIntOrNull(argumentsJson, "seek_to")
                        if (seekTo != null && seekTo >= 0) {
                            player.seekTo(seekTo * 1000L)
                            resultJson("success" to true, "message" to "已跳转到 $seekTo 秒")
                        } else {
                            resultJson("error" to "未知操作: $action，支持 pause/resume/next/previous/stop")
                        }
                    }
                }
            }
        }
    }

    inner class GetCurrentSong : AgentTool {
        override val name = "get_current_song"
        override val description = "获取当前正在播放（或队列中选中）的歌曲信息"
        override val properties = emptyMap<String, ToolParamSchema>()
        override val required = emptyList<String>()
        override val isReadOnly = true

        override suspend fun execute(argumentsJson: String): String {
            val state = player.state.value
            val song = state.currentSong ?: return resultJson("success" to true, "message" to "当前没有选中歌曲")
            return resultJson(
                "success" to true,
                "message" to "「${song.title}」- ${song.artist}",
                "song_id" to song.id,
                "playing" to state.isPlaying,
                "position_seconds" to (player.currentPositionMs() / 1000).toInt(),
                "duration_seconds" to song.durationSeconds,
            )
        }
    }

    inner class GetPlayQueue : AgentTool {
        override val name = "get_play_queue"
        override val description = "查看当前播放队列"
        override val properties = mapOf("limit" to ToolParamSchema("integer", "最多返回几条，默认 20"))
        override val required = emptyList<String>()
        override val isReadOnly = true

        override suspend fun execute(argumentsJson: String): String {
            val limit = argInt(argumentsJson, "limit").let { if (it <= 0) 20 else it }
            val songs = player.queueSongs().take(limit)
            return resultJson(
                "success" to true,
                "message" to "队列共 ${player.queueSongs().size} 首，模式 " + describeMode(player.state.value.mode),
                "songs" to songs.mapIndexed { index, song -> "${index + 1}|${song.id}|${song.title}|${song.artist}" },
            )
        }
    }

    inner class ToggleFavorite : AgentTool {
        override val name = "toggle_favorite"
        override val description = "收藏或取消收藏一首歌"
        override val properties = mapOf("song_id" to ToolParamSchema("integer", "歌曲 ID"))
        override val required = listOf("song_id")
        override val isReadOnly = false

        override suspend fun execute(argumentsJson: String): String {
            val songId = argInt(argumentsJson, "song_id", "songId")
            if (songId <= 0) return resultJson("error" to "请提供有效的歌曲 ID")
            val song = dao.songById(songId) ?: return resultJson("error" to "未找到该歌曲")
            val nowFavorite = !dao.isFavorite(songId)
            if (nowFavorite) {
                dao.upsertFavorite(
                    com.catclaw.music.kt.data.db.entity.FavoriteEntity(songId = songId, addedAt = System.currentTimeMillis()),
                )
            } else {
                dao.removeFavorite(songId)
            }
            return resultJson(
                "success" to true,
                "message" to if (nowFavorite) "已收藏「${song.title}」" else "已取消收藏「${song.title}」",
            )
        }
    }

    inner class GetFavoriteSongs : AgentTool {
        override val name = "get_favorite_songs"
        override val description = "列出收藏的歌曲"
        override val properties = mapOf("limit" to ToolParamSchema("integer", "最多返回几条，默认 20"))
        override val required = emptyList<String>()
        override val isReadOnly = true

        override suspend fun execute(argumentsJson: String): String {
            val limit = argInt(argumentsJson, "limit").let { if (it <= 0) 20 else it }
            val rows = dao.favoriteSongs(limit)
            return resultJson(
                "success" to true,
                "message" to "收藏了 ${rows.size} 首",
                "songs" to rows.map { it.asSongRow() },
            )
        }
    }

    inner class GetRecentSongs : AgentTool {
        override val name = "get_recent_songs"
        override val description = "列出最近播放过的歌曲"
        override val properties = mapOf("limit" to ToolParamSchema("integer", "最多返回几条，默认 20"))
        override val required = emptyList<String>()
        override val isReadOnly = true

        override suspend fun execute(argumentsJson: String): String {
            val limit = argInt(argumentsJson, "limit").let { if (it <= 0) 20 else it }
            val rows = dao.recentSongs(limit)
            return resultJson(
                "success" to true,
                "message" to "最近播过 ${rows.size} 首",
                "songs" to rows.map { it.asSongRow() },
            )
        }
    }

    inner class AddToPlayQueue : AgentTool {
        override val name = "add_to_play_queue"
        override val description = "将歌曲添加到播放队列，可以添加到下一首播放或添加到队列末尾"
        override val properties = mapOf(
            "song_id" to ToolParamSchema("integer", "要添加的歌曲 ID"),
            "position" to ToolParamSchema("string", "添加位置：next(下一首播放) 或 end(队列末尾)，默认 next", listOf("next", "end")),
        )
        override val required = listOf("song_id")
        override val isReadOnly = false

        override suspend fun execute(argumentsJson: String): String {
            val songId = argInt(argumentsJson, "song_id")
            if (songId <= 0) return resultJson("error" to "请提供有效的歌曲 ID")
            val position = argString(argumentsJson, "position").ifBlank { "next" }
            val target = mergedSongs().firstOrNull { it.id == songId } ?: return resultJson("error" to "未找到该歌曲")
            session.ensure()
            player.addToQueue(target.toSong(), nextUp = position != "end")
            return resultJson(
                "success" to true,
                "message" to if (position == "end") "已将「${target.title}」添加到播放队列末尾" else "已将「${target.title}」设为下一首播放",
            )
        }
    }

    inner class GetListeningStats : AgentTool {
        override val name = "get_listening_stats"
        override val description = "获取播放统计数据，包括播放次数最多的歌曲排行"
        override val properties = mapOf("top_n" to ToolParamSchema("integer", "排行榜前几名，默认 10"))
        override val required = emptyList<String>()
        override val isReadOnly = true

        override suspend fun execute(argumentsJson: String): String {
            val topN = argInt(argumentsJson, "top_n", "topN").let { if (it <= 0) 10 else it }
            val rows = dao.topPlayed(topN)
            return resultJson(
                "top_n" to topN,
                // 与 C# 同形：total_plays 只加**榜上这几首**，不是全库播放总数
                "total_plays" to rows.sumOf { it.playCount },
                "songs" to rows.map { "${it.id}|${it.title}|${it.artist}|${it.playCount}" },
            )
        }
    }

    inner class FetchWebPage : AgentTool {
        override val name = "fetch_web_page"
        override val description =
            "抓取指定网页的正文内容（输入 URL），返回页面标题与可读文本。" +
                "当搜索结果只给了链接和摘要、需要了解具体内容时使用此工具深入阅读页面。"
        override val properties = mapOf("url" to ToolParamSchema("string", "要抓取的网页完整 URL（http/https）"))
        override val required = listOf("url")
        override val isReadOnly = true

        /**
         * 分支形状照抄 C#：403 且带 `cf-mitigated: challenge` 才换诚实 UA 重试一次，
         * 其它 403 直接放弃（盲重试只会再吃一个 403 并白等一轮 RTT）。
         */
        override suspend fun execute(argumentsJson: String): String {
            val url = argString(argumentsJson, "url").trim()
            if (url.isEmpty()) return resultJson("error" to "请提供要抓取的 URL")
            if (!url.startsWith("http://", ignoreCase = true) && !url.startsWith("https://", ignoreCase = true)) {
                return resultJson("error" to "URL 必须以 http:// 或 https:// 开头")
            }

            var page = runCatching { fetcher.fetch(url) }.getOrElse {
                return resultJson("error" to "抓取失败: ${it.message.orEmpty()}")
            }
            if (page.status == 403) {
                if (!page.cfMitigated) return resultJson("error" to "抓取失败: HTTP 403")
                page = runCatching { fetcher.fetch(url, userAgent = HONEST_UA) }.getOrElse {
                    return resultJson("error" to "抓取失败: ${it.message.orEmpty()}")
                }
            }
            if (page.status !in 200..299) return resultJson("error" to "抓取失败: HTTP ${page.status}")

            val type = page.contentType
            if (!type.contains("html", true) && !type.contains("text/plain", true) && type.contains("json", true)) {
                // JSON / 其它格式：不解析，原样截断给模型
                return resultJson("success" to true, "url" to page.url, "title" to "", "content" to page.body.take(MAX_CONTENT))
            }
            return resultJson(
                "success" to true,
                "url" to page.url,
                "title" to HtmlText.title(page.body),
                "content" to HtmlText.text(page.body),
            )
        }
    }

    /**
     * 名称/描述/必填/错误文案逐项照 C# `AgentTools.Browser.cs`。
     * `isReadOnly = true` 同 C#：它只读网页，不改播放器状态，可以和别的只读工具并行分派。
     */
    inner class BrowserOpen : AgentTool {
        override val name = "browser_open"
        override val description =
            "打开内置浏览器加载指定网页，并读取页面渲染后的正文内容。" +
                "当 fetch_web_page 无法获取内容（动态渲染页面、需要执行 JavaScript 的站点、天气/实时数据等）时使用此工具。输入要打开的 URL。"
        override val properties = mapOf("url" to ToolParamSchema("string", "要打开的网页完整 URL（http/https）"))
        override val required = listOf("url")
        override val isReadOnly = true

        override suspend fun execute(argumentsJson: String): String {
            val url = argString(argumentsJson, "url").trim()
            if (url.isEmpty()) return resultJson("error" to "请提供要打开的 URL")
            if (!url.startsWith("http://", ignoreCase = true) && !url.startsWith("https://", ignoreCase = true)) {
                return resultJson("error" to "URL 必须以 http:// 或 https:// 开头")
            }
            val navigator = browser
                ?: return resultJson("error" to "浏览器功能未初始化（当前平台不支持）")

            val content = runCatching { navigator.openAndExtract(url) }.getOrElse {
                return resultJson("error" to "浏览器打开失败: ${it.message.orEmpty()}")
            }
            if (content.isNullOrBlank()) {
                return resultJson("error" to "页面加载完成但没有提取到正文（可能被阻止或需要交互）")
            }
            return resultJson("success" to true, "url" to url, "content" to content)
        }
    }

    inner class UpdateMusicSource : AgentTool {
        override val name = "update_music_source"
        override val description =
            "更新音乐源接口配置（酷我/网易云等），提交后自动验证可用性。当 kuwo_download / netease_download " +
                "返回'接口失效/配置无效'或下载失败时使用。省略 config_json 则只诊断现有配置。"
        override val properties = mapOf(
            "id" to ToolParamSchema("string", "源 ID：kuwo 或 netease"),
            "config_json" to ToolParamSchema("string", "可选：完整配置 JSON；省略则只诊断现有配置"),
        )
        override val required = listOf("id")
        override val isReadOnly = false

        /**
         * 与 C# 同一顺序：**先验证再落盘**。验证不过绝不保存 ——
         * 存一份坏配置会把原本还能用的内置源顶掉，用户从"偶尔失败"变成"永久失败"。
         */
        override suspend fun execute(argumentsJson: String): String {
            val registry = sources ?: return resultJson("error" to "音乐源注册表未接线")
            val id = argString(argumentsJson, "id").trim()
            if (id.isEmpty()) return resultJson("error" to "请提供源 ID（kuwo / netease）")
            val configJson = argString(argumentsJson, "config_json").trim()

            if (configJson.isEmpty()) {
                val existing = registry.get(id) ?: return resultJson("error" to "未找到已启用的源：$id")
                val diag = registry.validate(existing)
                return resultJson(
                    "success" to diag.ok,
                    "id" to id,
                    "message" to if (diag.ok) "「${existing.name}」现有配置可用" else "现有配置不可用：${diag.error}",
                    "diagnosis" to diagnosisJson(diag),
                )
            }

            val submitted = MusicSourceRegistry.decodeOne(configJson)
                ?: return resultJson(
                    "error" to "config_json 不是合法 JSON",
                    "hint" to "请确保 JSON 格式正确（注意转义正则中的反斜杠）",
                )
            if (submitted.template.isEmpty()) return resultJson("error" to "配置缺少 id 或 template 字段")
            val cfg = submitted.copy(id = id, name = submitted.name.ifBlank { id })

            val diagnosis = registry.validate(cfg)
            if (!diagnosis.ok) {
                return resultJson(
                    "success" to false,
                    "message" to "验证未通过，配置未保存。请根据以下诊断调整 config_json 后重试",
                    "id" to id,
                    "diagnosis" to diagnosisJson(diagnosis),
                )
            }
            registry.upsert(cfg.copy(enabled = true))
            return resultJson(
                "success" to true,
                "message" to "「${cfg.name}」源配置验证通过并已保存（${diagnosis.songs} 首搜索结果，直链: ${diagnosis.urlPreview}）",
                "id" to id,
                "diagnosis" to diagnosisJson(diagnosis),
            )
        }

        private fun diagnosisJson(diag: Diagnosis): JsonElement = buildJsonObject {
            put("ok", JsonPrimitive(diag.ok))
            put("search_ok", JsonPrimitive(diag.searchOk))
            put("songs", JsonPrimitive(diag.songs))
            put("first_result", JsonPrimitive(diag.firstResult))
            put("url_ok", JsonPrimitive(diag.urlOk))
            put("url_preview", JsonPrimitive(diag.urlPreview))
            put("http_ok", JsonPrimitive(diag.httpOk))
            put("error", JsonPrimitive(diag.error))
        }
    }

    /**
     * kuwo/netease 两个下载工具的共用流程，与 C# 同一顺序：
     * 搜索 → 有 artist 就按"歌手名包含"稳定排序 → 并行探前 6 条直链 → 取第一条可用的 → 入队。
     *
     * ⚠ 两个工具在 C# 里都标 `IsReadOnly => true`（虽然它们会入队下载）。这里照抄，
     * 因为它决定的是**能否与其他只读工具并行分派**，而"入队"对播放器状态没有副作用；
     * 改成 false 会让模型一次只能用一个下载工具，实测会更慢。
     */
    private suspend fun downloadFromSource(sourceId: String, keyword: String, artist: String): String {
        val registry = sources ?: return resultJson("error" to "音乐源注册表未接线")
        if (keyword.isEmpty()) return resultJson("error" to "请提供歌曲名称")
        val cfg = registry.get(sourceId)
            ?: return resultJson(
                "error" to "$sourceId 音乐源配置无效或已禁用",
                "hint" to "可让 Yuki 用 update_music_source 工具修复（搜最新音源源码 → 更新配置 → 自动验证）",
            )

        val found = registry.search(cfg, keyword)
        if (found.isEmpty()) {
            return resultJson("error" to "$sourceId 上未找到《$keyword》，可换另一个下载工具或改歌名")
        }
        val candidates = if (artist.isEmpty()) {
            found
        } else {
            found.sortedByDescending { it.artist.contains(artist, ignoreCase = true) }
        }

        val probes = coroutineScope {
            candidates.take(PROBE_LIMIT).map { song ->
                async {
                    val quality = song.bestQuality
                    Triple(song, quality, registry.url(cfg, song, quality))
                }
            }.awaitAll()
        }
        val usable = probes.filter { it.third != null }
        if (usable.isEmpty()) {
            return resultJson(
                "success" to false,
                "message" to "《$keyword》在 $sourceId 上未能获取到播放地址（接口可能已变更）",
                "hint" to "可用 update_music_source 工具检查并修复 $sourceId 源",
            )
        }

        val (song, quality, link) = usable.first()
        val directUrl = link.orEmpty()
        val filename = "${song.name}-${song.artist}.${if (quality == "flac") "flac" else "mp3"}"
        val note = downloads?.enqueue(directUrl, filename) ?: "下载器未接线，仅返回直链"
        return resultJson(
            "success" to true,
            "song" to song.name,
            "artist" to song.artist,
            "quality" to when (quality) {
                "flac" -> "FLAC无损"
                "320k" -> "320K"
                else -> "128K"
            },
            "version_note" to if (artist.isEmpty()) {
                ""
            } else if (song.artist.contains(artist, ignoreCase = true)) {
                "匹配到目标歌手版本"
            } else {
                "未找到 ${artist} 的版本，已选可下载的其他版本"
            },
            "message" to note,
            "url_preview" to directUrl.take(110),
        )
    }

    private inner class SourceDownloadTool(
        override val name: String,
        override val description: String,
        private val sourceId: String,
    ) : AgentTool {
        override val properties = mapOf(
            "keyword" to ToolParamSchema("string", "歌曲名称（必填）"),
            "artist" to ToolParamSchema("string", "歌手名（可选），用于优先匹配目标歌手版本"),
        )
        override val required = listOf("keyword")
        override val isReadOnly = true

        override suspend fun execute(argumentsJson: String): String =
            downloadFromSource(sourceId, argString(argumentsJson, "keyword").trim(), argString(argumentsJson, "artist").trim())
    }

    /** 酷我：免费直连且有真无损 FLAC，所以 C# 把它列为"下载某首歌"的首选 */
    inner class KuwoDownload : AgentTool {
        private val delegate = SourceDownloadTool(
            name = "kuwo_download",
            sourceId = "kuwo",
            description = "从酷我音乐搜索并下载歌曲（免费直连，支持无损 FLAC）。当用户要求'下载某首歌/某音乐/要无损音质'时优先使用本工具——" +
                "酷我有真无损直链且无需会员。输入歌名（可附歌手名）。",
        )

        override val name get() = delegate.name
        override val description get() = delegate.description
        override val properties get() = delegate.properties
        override val required get() = delegate.required
        override val isReadOnly get() = delegate.isReadOnly
        override suspend fun execute(argumentsJson: String) = delegate.execute(argumentsJson)
    }

    inner class NeteaseDownload : AgentTool {
        private val delegate = SourceDownloadTool(
            name = "netease_download",
            sourceId = "netease",
            description = "从网易云音乐搜索并下载歌曲（免费直连，无需会员）。当用户要求'下载某首歌/某音乐'时优先使用本工具——" +
                "网页下载站多为付费墙或网盘，本工具直接返回可用下载。输入歌名（可附歌手名）。",
        )

        override val name get() = delegate.name
        override val description get() = delegate.description
        override val properties get() = delegate.properties
        override val required get() = delegate.required
        override val isReadOnly get() = delegate.isReadOnly
        override suspend fun execute(argumentsJson: String) = delegate.execute(argumentsJson)
    }

    inner class DownloadFile : AgentTool {
        override val name = "download_file"
        override val description =
            "下载文件到本地（如音乐、图片、文档、安装包等），下载任务会出现在应用的下载管理里，支持查看进度。" +
                "当用户要求'下载 XX'或需要把某个文件的下载链接保存下来时使用此工具。" +
                "输入下载链接 URL（可从搜索结果或网页内容中获取）。"
        override val properties = mapOf(
            "url" to ToolParamSchema("string", "文件的下载链接（http/https）"),
            "filename" to ToolParamSchema("string", "可选：保存的文件名（含扩展名）；不填则取链接末段"),
        )
        override val required = listOf("url")
        override val isReadOnly = false

        override suspend fun execute(argumentsJson: String): String {
            val enqueuer = downloads ?: return resultJson("error" to "下载器未接线")
            val url = argString(argumentsJson, "url").trim()
            if (url.isEmpty()) return resultJson("error" to "请提供下载链接")
            if (!url.startsWith("http://", ignoreCase = true) && !url.startsWith("https://", ignoreCase = true)) {
                return resultJson("error" to "只支持 http/https 下载链接")
            }
            val filename = argString(argumentsJson, "filename").trim().ifBlank {
                url.substringAfterLast('/').substringBefore('?').substringBefore('#')
            }
            if (filename.isBlank()) return resultJson("error" to "无法从链接推断文件名，请显式提供 filename")
            val note = enqueuer.enqueue(url, filename) ?: "无法入队：$filename"
            return resultJson("success" to true, "filename" to filename, "message" to note)
        }
    }

    inner class WebSearch : AgentTool {
        override val name = "web_search"
        override val description =
            "在互联网上搜索信息，返回标题、链接与摘要，可以搜索新闻、知识、音乐资讯等内容。" +
                "当用户询问实时信息、最新资讯或你不确定的知识时使用此工具。" +
                "找到需要深入了解的页面时，请继续用 fetch_web_page 工具抓取正文。"
        override val properties = mapOf("query" to ToolParamSchema("string", "搜索关键词"))
        override val required = listOf("query")
        override val isReadOnly = true

        override suspend fun execute(argumentsJson: String): String {
            val query = argString(argumentsJson, "query", "keyword").trim()
            if (query.isEmpty()) return resultJson("error" to "请提供搜索关键词")
            val gateway = web ?: return resultJson("error" to "联网搜索网关未接线")
            val searcher = WebSearcher(gateway = gateway)
            return when (val outcome = searcher.search(query)) {
                is WebOutcome.Failed -> resultJson("error" to outcome.reason)
                is WebOutcome.Exa -> resultJson(
                    "success" to true, "query" to query, "content" to outcome.content,
                    "message" to "搜索完成（Exa AI 搜索）",
                )
                is WebOutcome.Hits -> resultJson(
                    "success" to true, "query" to query,
                    "results" to outcome.results.map { "${it.title}|${it.url}|${it.snippet}" },
                    "message" to "搜索完成，找到 ${outcome.results.size} 条相关结果",
                )
                WebOutcome.None -> resultJson(
                    "success" to false, "query" to query, "results" to emptyList<String>(),
                    "message" to "已搜索「$query」但未找到相关结果，建议换个关键词试试",
                )
            }
        }
    }

    inner class ClearQueue : AgentTool {        override val name = "clear_play_queue"
        override val description = "清空播放队列"
        override val properties = emptyMap<String, ToolParamSchema>()
        override val required = emptyList<String>()
        override val isReadOnly = false

        override suspend fun execute(argumentsJson: String): String {
            player.clearQueue()
            return resultJson("success" to true, "message" to "队列已清空")
        }
    }

    // ── 歌单类：名称/描述/必填参数与 C# `AgentTools.MusicLibrary.cs` 逐个对齐 ──────────

    inner class CreatePlaylist : AgentTool {
        override val name = "create_playlist"
        override val description = "创建新的播放列表（歌单）"
        override val properties = mapOf("name" to ToolParamSchema("string", "歌单名称"))
        override val required = listOf("name")
        override val isReadOnly = false

        override suspend fun execute(argumentsJson: String): String {
            val playlistName = argString(argumentsJson, "name")
            if (playlistName.isBlank()) return resultJson("error" to "请提供歌单名称")
            val now = System.currentTimeMillis()
            val id = playlists.upsert(PlaylistEntity(name = playlistName, createdAt = now, updatedAt = now)).toInt()
            return resultJson(
                "success" to true,
                "playlistId" to id,
                "playlistName" to playlistName,
                "message" to "歌单「$playlistName」已创建",
            )
        }
    }

    inner class AddSongToPlaylist : AgentTool {
        override val name = "add_song_to_playlist"
        override val description = "将歌曲添加到指定歌单中"
        override val properties = mapOf(
            "playlist_id" to ToolParamSchema("integer", "目标歌单 ID"),
            "song_id" to ToolParamSchema("integer", "要添加的歌曲 ID"),
        )
        override val required = listOf("playlist_id", "song_id")
        override val isReadOnly = false

        override suspend fun execute(argumentsJson: String): String {
            val playlistId = argInt(argumentsJson, "playlist_id")
            val songId = argInt(argumentsJson, "song_id")
            if (playlistId <= 0) return resultJson("error" to "请提供有效的歌单 ID")
            if (songId <= 0) return resultJson("error" to "请提供有效的歌曲 ID")
            // appendAll 顺手 touch 了 SongCount，不必再单独维护计数
            playlists.appendAll(playlistId, listOf(songId))
            return resultJson("success" to true, "message" to "歌曲已添加到歌单")
        }
    }

    inner class RemoveSongFromPlaylist : AgentTool {
        override val name = "remove_song_from_playlist"
        override val description = "从指定歌单中移除歌曲"
        override val properties = mapOf(
            "playlist_id" to ToolParamSchema("integer", "歌单 ID"),
            "song_id" to ToolParamSchema("integer", "要移除的歌曲 ID"),
        )
        override val required = listOf("playlist_id", "song_id")
        override val isReadOnly = false

        override suspend fun execute(argumentsJson: String): String {
            val playlistId = argInt(argumentsJson, "playlist_id")
            val songId = argInt(argumentsJson, "song_id")
            if (playlistId <= 0 || songId <= 0) return resultJson("error" to "请提供有效的歌单 ID 和歌曲 ID")
            playlists.removeItem(playlistId, songId)
            playlists.touch(playlistId, playlists.songIdsInOrder(playlistId).size, System.currentTimeMillis())
            return resultJson("success" to true, "message" to "歌曲已从歌单中移除")
        }
    }

    inner class ListPlaylists : AgentTool {
        override val name = "list_playlists"
        override val description = "获取用户所有播放列表（歌单）"
        override val properties = emptyMap<String, ToolParamSchema>()
        override val required = emptyList<String>()
        override val isReadOnly = true

        override suspend fun execute(argumentsJson: String): String {
            val rows = playlists.observePlaylists().first()
            return resultJson(
                "count" to rows.size,
                "playlists" to rows.map { "${it.id}|${it.name}|${it.songCount}" },
            )
        }
    }

    inner class GetPlaylistSongs : AgentTool {
        override val name = "get_playlist_songs"
        override val description = "获取指定歌单中的歌曲列表"
        override val properties = mapOf("playlist_id" to ToolParamSchema("integer", "歌单 ID"))
        override val required = listOf("playlist_id")
        override val isReadOnly = true

        override suspend fun execute(argumentsJson: String): String {
            val playlistId = argInt(argumentsJson, "playlist_id")
            if (playlistId <= 0) return resultJson("error" to "请提供有效的歌单 ID")
            val rows = playlists.observeItems(playlistId).first()
            return resultJson(
                "count" to rows.size,
                "songs" to rows.map { it.asSongRow() },
            )
        }
    }

    inner class DeletePlaylist : AgentTool {
        override val name = "delete_playlist"
        override val description = "删除指定的播放列表（歌单）"
        override val properties = mapOf("playlist_id" to ToolParamSchema("integer", "要删除的歌单 ID"))
        override val required = listOf("playlist_id")
        override val isReadOnly = false

        override suspend fun execute(argumentsJson: String): String {
            val playlistId = argInt(argumentsJson, "playlist_id")
            if (playlistId <= 0) return resultJson("error" to "请提供有效的歌单 ID")
            // delete 的 SQL 自带 `AND IsSystem = 0`：收藏/历史这类内置歌单删不掉，与 C# 同一保护
            playlists.delete(playlistId)
            playlists.clearItems(playlistId)
            return resultJson("success" to true, "message" to "歌单已删除")
        }
    }
}

/** 播放模式的文字说明，供「切换模式」类工具与状态展示复用 */
internal fun describeMode(mode: PlayMode): String = when (mode) {
    PlayMode.Sequential -> "顺序播放"
    PlayMode.Shuffle -> "随机播放"
    PlayMode.SingleRepeat -> "单曲循环"
    PlayMode.ListRepeat -> "列表循环"
}
