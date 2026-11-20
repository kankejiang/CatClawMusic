package com.catclaw.music.kt.lyrico.plugin

import com.catclaw.music.kt.lyrico.runtime.LyricoJsEngine
import kotlinx.serialization.json.Json
import kotlinx.serialization.json.JsonPrimitive
import kotlinx.serialization.json.jsonObject

/** 插件函数调用结果：成功时 [valueJson] 是返回值的 JSON，失败时 [error] 有原因。 */
data class LyricoInvokeResult(
    val ok: Boolean,
    val valueJson: String? = null,
    val error: String? = null,
) {
    fun asArrayOrNull(): String? = valueJson?.takeIf { it.startsWith("[") }
}

/**
 * 单个 Lyrico 源插件的执行宿主。
 *
 * 负责：加载引擎 → 绑定宿主 API → 注入 Platform → 拼接并执行插件脚本 → 调用其函数。
 *
 * 一个插件一个实例（QuickJS 的全局状态是各自的，不能共用）。
 * 引擎本身不是线程安全的，[LyricoJsEngine] 已用单线程串行化，这里不再加锁。
 */
class LyricoScriptHost(
    val plugin: LyricoPlugin,
    private val hostApi: LyricoHostApi,
) : AutoCloseable {

    private val json = Json { ignoreUnknownKeys = true; isLenient = true }
    private val engine = LyricoJsEngine.create()

    @Volatile
    private var loaded = false

    @Volatile
    private var loadError: String? = null

    val isLoaded: Boolean get() = loaded
    val lastError: String? get() = loadError

    /** 装载插件脚本。重复调用是幂等的。 */
    fun load(): Boolean {
        if (loaded) return true
        loadError?.let { /* 允许重试 */ }
        return try {
            engine.bindGlobal("__lyricoHost", LyricoHostApi::class.java, hostApi)
            engine.evaluate(LyricoBootstrap.SCRIPT)
            val script = plugin.buildCompositeScript()
                ?: run {
                    loadError = "插件缺少入口脚本 ${plugin.manifest.entry}"
                    return false
                }
            engine.evaluate(script)
            // 检查插件是否真的暴露了函数（很多插件只在支持的能力里声明）
            loaded = true
            loadError = null
            true
        } catch (t: Throwable) {
            loadError = "装载失败：${t.message}"
            loaded = false
            false
        }
    }

    /**
     * 调用插件函数。
     *
     * @param fnName `getLyrics` / `searchSongs` / `searchCovers`
     * @param requestJson 请求对象的 JSON
     */
    fun invoke(
        fnName: String,
        requestJson: String,
        timeoutMs: Long = DEFAULT_INVOKE_TIMEOUT_MS,
    ): LyricoInvokeResult {
        if (!load()) return LyricoInvokeResult(false, error = loadError ?: "未装载")
        return try {
            val expression = "(${LyricoBootstrap.INVOKE_SCRIPT})(${jsonString(fnName)}, ${jsonString(requestJson)})"
            val out = engine.evaluate(expression, timeoutMs) as? String
                ?: return LyricoInvokeResult(false, error = "插件返回非字符串")
            val obj = runCatching { json.parseToJsonElement(out).jsonObject }.getOrNull()
                ?: return LyricoInvokeResult(false, error = "无法解析调用结果")
            val ok = (obj["ok"] as? JsonPrimitive)?.content?.toBooleanStrictOrNull() ?: false
            if (ok) {
                LyricoInvokeResult(true, valueJson = obj["value"]?.toString())
            } else {
                LyricoInvokeResult(false, error = (obj["error"] as? JsonPrimitive)?.content)
            }
        } catch (t: Throwable) {
            LyricoInvokeResult(false, error = "${t.javaClass.simpleName}: ${t.message}")
        }
    }

    /** 搜索歌曲请求（Lyrico `searchSongs`）。 */
    fun searchSongsRequest(
        keyword: String,
        config: Map<String, String> = emptyMap(),
        page: Int = 1,
        pageSize: Int = 20,
        separator: String = "/",
    ): String = buildString {
        append("{\"keyword\":").append(jsonString(keyword))
        append(",\"page\":").append(page)
        append(",\"pageSize\":").append(pageSize)
        append(",\"separator\":").append(jsonString(separator))
        append(",\"config\":{")
        config.entries.forEachIndexed { index, (k, v) ->
            if (index > 0) append(',')
            append(jsonString(k)).append(':').append(jsonString(v))
        }
        append("}}")
    }

    /**
     * 用**插件自己返回的歌曲对象**构造 getLyrics 请求。
     *
     * 这是 Lyrico 的正确用法：`getLyrics` 需要的是 `searchSongs` 里那条记录的 `id`
     * 和 `internal`（各插件把自己的真实 id 藏在这里）。直接拿标题去调会让插件
     * 拿到一个假 id —— 实测 LrcShare 会把插件 id 当歌曲 id 拼进 URL（`/v1/song/com.lrcshare.source`），
     * 必然 404。所以这里把歌曲 JSON **原样内嵌**，不做字段挑拣。
     */
    fun getLyricsRequestForSong(
        songJson: String,
        config: Map<String, String> = emptyMap(),
        page: Int = 1,
        pageSize: Int = 20,
        separator: String = "/",
    ): String = buildString {
        append("{\"song\":").append(songJson)
        append(",\"page\":").append(page)
        append(",\"pageSize\":").append(pageSize)
        append(",\"separator\":").append(jsonString(separator))
        append(",\"config\":{")
        config.entries.forEachIndexed { index, (k, v) ->
            if (index > 0) append(',')
            append(jsonString(k)).append(':').append(jsonString(v))
        }
        append("}}")
    }

    /** 与插件约定的请求对象。字段名对齐 Lyrico 的 PluginGetLyricsRequest。 */
    fun getLyricsRequest(
        title: String,
        artist: String,
        album: String,
        durationMs: Long,
        /** 歌曲在本 App 侧的标识（插件部分能力要求必填，缺了会被判为非法请求） */
        songId: String = "",
        sourceId: String = "",
        pluginId: String = plugin.id,
        date: String = "",
        config: Map<String, String> = emptyMap(),
        page: Int = 1,
        pageSize: Int = 20,
        separator: String = "/",
    ): String = buildString {
        append("{\"song\":{")
        append("\"id\":").append(jsonString(songId)).append(',')
        append("\"title\":").append(jsonString(title)).append(',')
        append("\"artist\":").append(jsonString(artist)).append(',')
        append("\"album\":").append(jsonString(album)).append(',')
        append("\"date\":").append(jsonString(date)).append(',')
        append("\"duration\":").append(durationMs).append(',')
        append("\"sourceId\":").append(jsonString(sourceId)).append(',')
        append("\"pluginId\":").append(jsonString(pluginId))
        append("},\"page\":").append(page)
        append(",\"pageSize\":").append(pageSize)
        append(",\"separator\":").append(jsonString(separator))
        append(",\"config\":{")
        config.entries.forEachIndexed { index, (k, v) ->
            if (index > 0) append(',')
            append(jsonString(k)).append(':').append(jsonString(v))
        }
        append("}}")
    }

    private fun jsonString(value: String): String {
        val sb = StringBuilder(value.length + 16)
        sb.append('"')
        value.forEach { c ->
            when (c) {
                '"' -> sb.append("\\\"")
                '\\' -> sb.append("\\\\")
                '\n' -> sb.append("\\n")
                '\r' -> sb.append("\\r")
                '\t' -> sb.append("\\t")
                else -> if (c < ' ') sb.append("\\u%04x".format(c.code)) else sb.append(c)
            }
        }
        sb.append('"')
        return sb.toString()
    }

    override fun close() {
        runCatching { engine.close() }
        loaded = false
    }

    private companion object {
        /**
         * 单次插件调用预算。
         *
         * 插件内部往往要「搜索 → 逐首取详情 → 取词」多跳网络请求（LrcShare 实测 >10s），
         * 10s 会把正常但偏慢的源直接判死。30s 兼顾体验与成功率。
         */
        const val DEFAULT_INVOKE_TIMEOUT_MS = 30_000L
    }
}
