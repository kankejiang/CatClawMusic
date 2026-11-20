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
        val plugins = LyricoPluginLoader.loadFromAssets(appContext.assets, errors = errors)
        errors.forEach { logSink("warn", "LyricoPlugins", it) }
        plugins.forEach { pluginsById[it.id] = it }
        loadedBundled = true
        logSink("info", "LyricoPlugins", "内置源 ${plugins.size} 个：${plugins.map { it.id }}")
        return plugins
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
     * @return 候选列表；插件不支持/调用失败/超时都返回空（不抛，避免一个源拖垮整次搜索）
     */
    suspend fun searchLyrics(pluginId: String, target: LyricsTarget): List<LyricsCandidate> =
        withContext(Dispatchers.IO) {
            val plugin = pluginsById[pluginId] ?: return@withContext emptyList()
            val host = hostFor(pluginId) ?: return@withContext emptyList()
            if (!plugin.manifest.hasGetLyrics) return@withContext emptyList()

            val request = host.getLyricsRequest(
                title = target.title,
                artist = target.artist,
                album = target.album,
                durationMs = target.durationMs,
                songId = pluginId,
                sourceId = pluginId,
                pluginId = pluginId,
            )
            val result = host.invoke("getLyrics", request)
            if (!result.ok) {
                logSink("warn", "LyricoPlugins", "$pluginId 取词失败：${result.error}")
                return@withContext emptyList()
            }
            val parsed = LyricoLyricsParser.parseCandidates(result.valueJson)
            if (parsed.isEmpty()) {
                logSink("debug", "LyricoPlugins", "$pluginId 返回 0 个候选")
                return@withContext emptyList()
            }
            parsed.mapIndexed { index, lyrics ->
                LyricsCandidate(
                    sourceId = pluginId,
                    sourceName = plugin.displayName,
                    trackTitle = lyrics.tags["ti"] ?: target.title,
                    artist = lyrics.tags["ar"] ?: target.artist,
                    album = lyrics.tags["al"] ?: target.album,
                    // 多候选时插件通常按匹配度排序，靠前的更可信
                    durationMs = target.durationMs + index * 100L,
                    lyrics = lyrics,
                )
            }
        }

    /** 释放所有引擎（页面销毁/应用退出时调用）。 */
    fun release() {
        hosts.values.forEach { runCatching { it.close() } }
        hosts.clear()
    }
}
