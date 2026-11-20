package com.catclaw.music.kt.lyrico.search

import android.content.Context
import com.catclaw.music.kt.lyrico.lyrics.LyricoLyricsParser
import com.catclaw.music.kt.lyrico.plugin.LyricoHost
import com.catclaw.music.kt.lyrico.plugin.LyricoPlugin
import com.catclaw.music.kt.lyrico.plugin.LyricoPluginLoader
import com.catclaw.music.kt.lyrico.plugin.LyricoScriptHost
import java.util.concurrent.ConcurrentHashMap
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.withContext
import okhttp3.OkHttpClient

/**
 * 内置 Lyrico 源插件的持有者。
 *
 * 每个插件一个 [LyricoScriptHost]（独立 QuickJS 实例），首次使用时创建并复用：
 * 装载一次约几十到几百毫秒，每次搜索都重建会很慢，而且插件内部常用
 * `Platform.cache` 做登录态/目录缓存，重建会把这些缓存丢掉。
 *
 * 线程模型：宿主调用本身是串行的（引擎单线程），但**不同插件之间**可以并行，
 * 所以这里用 ConcurrentHashMap 持有多个宿主。
 */
class LyricoPluginSources(
    context: Context,
    private val okHttpClient: OkHttpClient,
    private val locale: () -> String = { "zh-Hans" },
    private val logSink: (String, String, String) -> Unit = { _, _, _ -> },
) {

    private val appContext = context.applicationContext
    private val hosts = ConcurrentHashMap<String, LyricoScriptHost>()
    private val pluginsById = ConcurrentHashMap<String, LyricoPlugin>()

    @Volatile
    private var loadedBundled = false

    /** 装载内置源（幂等）。返回成功装载的插件列表。 */
    fun ensureBundledLoaded(): List<LyricoPlugin> {
        if (loadedBundled) return pluginsById.values.toList()
        val errors = mutableListOf<String>()
        val raw = LyricoPluginLoader.loadFromAssets(appContext.assets, errors = errors)
        errors.forEach { logSink("warn", "LyricoPlugins", it) }
        val localeTag = locale()
        raw.forEach { plugin ->
            // manifest 里的 name/description 可能写成 `@plugin.name` 这类 i18n key
            // （Lyrico 官方源就是这种写法）。不解析的话界面上会原样显示 `@plugin.name`。
            val strings = LyricoPluginLoader.resolveStrings(plugin, localeTag)
            val localized = plugin.copy(
                manifest = plugin.manifest.copy(
                    name = resolveText(plugin.manifest.name, strings),
                    description = resolveText(plugin.manifest.description, strings),
                ),
            )
            pluginsById[plugin.id] = localized
        }
        loadedBundled = true
        logSink(
            "info",
            "LyricoPlugins",
            "内置源 ${localizedCount()} 个：${pluginsById.values.map { "${it.id}(${it.displayName})" }}",
        )
        return pluginsById.values.toList()
    }

    private fun localizedCount(): Int = pluginsById.size

    /** `@key` → 文案；不是 `@` 开头就原样返回。 */
    private fun resolveText(value: String, strings: Map<String, String>): String {
        if (!value.startsWith("@")) return value
        return strings[value.removePrefix("@")] ?: value.removePrefix("@")
    }

    fun availablePlugins(): List<LyricoPlugin> {
        ensureBundledLoaded()
        return pluginsById.values.sortedBy { it.id }
    }

    /** 取（或创建）某插件的脚本宿主。 */
    fun hostFor(pluginId: String): LyricoScriptHost? {
        hosts[pluginId]?.let { return it }
        val plugin = pluginsById[pluginId] ?: return null
        val strings = LyricoPluginLoader.resolveStrings(plugin, locale())
        val host = LyricoHost(
            okHttpClient = okHttpClient,
            logSink = { level, tag, msg -> logSink(level, tag, msg) },
            strings = strings,
        )
        val scriptHost = LyricoScriptHost(plugin, host)
        return hosts.putIfAbsent(pluginId, scriptHost) ?: scriptHost
    }

    /**
     * 用某个插件搜歌词。
     *
     * **两步**（这是 Lyrico 的既定协议，跳不得）：
     *   1. `searchSongs(keyword)` 拿到该插件自己的歌曲记录（含它塞在 `internal` 里的真实 id）
     *   2. 用那条记录调 `getLyrics` —— 插件正是拿 `internal` 里的 id 去请求它自己的接口
     *
     * 早先的实现直接拿标题调 `getLyrics`，等于给插件一个假 id：LrcShare 会把插件 id
     * 当歌曲 id 拼进 URL（`/v1/song/com.lrcshare.source`）必然失败。
     *
     * 回退：`searchSongs` 不受支持或没匹配上时，仍用标题/艺人直接调 getLyrics，
     * 兼容「自己会搜索」的插件（网易云那类）。
     */
    suspend fun searchLyrics(pluginId: String, target: LyricsTarget): List<LyricsCandidate> =
        withContext(Dispatchers.IO) {
            val plugin = pluginsById[pluginId] ?: return@withContext emptyList()
            val host = hostFor(pluginId) ?: return@withContext emptyList()
            if (!plugin.manifest.hasGetLyrics) return@withContext emptyList()

            val keyword = listOf(target.title.trim(), target.artist.trim())
                .filter { it.isNotEmpty() }
                .joinToString(" ")

            // 1) searchSongs
            var hits: List<LyricoSongHit> = emptyList()
            if (plugin.manifest.hasSearchSongs && keyword.isNotEmpty()) {
                val res = host.invoke("searchSongs", host.searchSongsRequest(keyword))
                if (res.ok) {
                    hits = LyricoSongParser.parse(res.valueJson)
                    logSink("debug", "LyricoPlugins", "$pluginId searchSongs($keyword) => ${hits.size} 首")
                } else {
                    logSink("warn", "LyricoPlugins", "$pluginId searchSongs 失败：${res.error}")
                }
            }

            // 2) getLyrics：优先用匹配到的歌曲记录，其次回退到标题直调
            val best = LyricoSongMatcher.pickBest(target, hits)
            val attempts = buildList {
                if (best != null) add(host.getLyricsRequestForSong(best.json))
                if (isEmpty()) {
                    add(
                        host.getLyricsRequest(
                            title = target.title,
                            artist = target.artist,
                            album = target.album,
                            durationMs = target.durationMs,
                            songId = best?.id ?: "",
                            sourceId = pluginId,
                            pluginId = pluginId,
                        ),
                    )
                }
            }

            for (request in attempts) {
                val result = host.invoke("getLyrics", request)
                if (!result.ok) {
                    logSink("warn", "LyricoPlugins", "$pluginId 取词失败：${result.error}")
                    continue
                }
                val parsed = LyricoLyricsParser.parseCandidates(result.valueJson)
                if (parsed.isEmpty()) {
                    logSink("debug", "LyricoPlugins", "$pluginId 返回 0 个候选")
                    continue
                }
                return@withContext parsed.mapIndexed { index, lyrics ->
                    LyricsCandidate(
                        sourceId = pluginId,
                        sourceName = plugin.displayName,
                        trackTitle = lyrics.tags["ti"] ?: best?.title ?: target.title,
                        artist = lyrics.tags["ar"] ?: best?.artist ?: target.artist,
                        album = lyrics.tags["al"] ?: best?.album ?: target.album,
                        durationMs = best?.durationMs ?: target.durationMs,
                        lyrics = lyrics,
                    )
                }
            }
            emptyList()
        }

    /** 释放所有引擎（页面销毁/应用退出时调用）。 */
    fun release() {
        hosts.values.forEach { runCatching { it.close() } }
        hosts.clear()
    }
}
