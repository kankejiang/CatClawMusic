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
    /**
     * 「用户已启用的插件 id」提供者。
     *
     * 不注入 DAO 而注入这个函数：本类保持「不认识数据库」的纯逻辑层，
     * 由 DI 侧从 PluginRegistry 读库后传入。
     * 返回 null 表示「还说不好」（例如读库失败）—— 此时**不**过滤，
     * 宁可多搜也别把用户的源全关掉。
     */
    private val enabledIdsProvider: suspend () -> Set<String>? = { null },
) {

    private val appContext = context.applicationContext

    /** 用户安装的插件目录（与 PluginRegistry 用的同一处） */
    private val pluginsRoot = java.io.File(appContext.filesDir, "lyrico-plugins")

    private val hosts = ConcurrentHashMap<String, LyricoScriptHost>()

    /** 本次会话里已失效的插件（执行超时未中断）：后续搜索直接跳过，避免每次都等满超时 */
    private val brokenPlugins = ConcurrentHashMap.newKeySet<String>()
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
        // 再加载用户安装的插件（ZIP 导入 / 在线下载）。
        // 之前漏了这一步：插件页能看到它们、磁盘上也有文件，但搜索永远用不到 ——
        // 因为 pluginsById 里只有 assets 里的内置源。
        // 内置源优先：同 id 时保留内置版本，避免用户装的旧版盖掉随包发的版本。
        val installed = loadInstalledPlugins(localeTag)
        installed.forEach { plugin ->
            if (!pluginsById.containsKey(plugin.id)) {
                pluginsById[plugin.id] = plugin
            }
        }

        loadedBundled = true
        logSink(
            "info",
            "LyricoPlugins",
            "可用源 ${pluginsById.size} 个（内置 ${localizedCount() - installed.size} + " +
                "安装 ${installed.size}）：${pluginsById.values.map { "${it.id}(${it.displayName})" }}",
        )
        return pluginsById.values.toList()
    }

    /**
     * 读 `files/lyrico-plugins/` 下每个目录里的插件。
     *
     * 单个插件损坏（manifest 缺失/JSON 坏了）只跳过它并记日志，
     * 不能让一个坏插件把整批可用源拖下水。
     */
    private fun loadInstalledPlugins(localeTag: String): List<LyricoPlugin> {
        val dirs = pluginsRoot.listFiles()
            ?.filter { it.isDirectory && !it.name.startsWith(".tmp-") }
            .orEmpty()
        if (dirs.isEmpty()) return emptyList()
        val out = mutableListOf<LyricoPlugin>()
        dirs.forEach { dir ->
            val plugin = runCatching {
                com.catclaw.music.kt.lyrico.plugin.LyricoPluginLoader.loadFromDirectory(dir)
            }.getOrElse {
                logSink("warn", "LyricoPlugins", "${dir.name} 装载失败：${it.message}")
                null
            } ?: return@forEach
            // 与内置源一致地解析 i18n key（manifest 里写 `@plugin.name` 的情况）
            val strings = com.catclaw.music.kt.lyrico.plugin.LyricoPluginLoader
                .resolveStrings(plugin, localeTag)
            out += plugin.copy(
                manifest = plugin.manifest.copy(
                    name = resolveText(plugin.manifest.name, strings),
                    description = resolveText(plugin.manifest.description, strings),
                ),
            )
        }
        return out
    }

    private fun localizedCount(): Int = pluginsById.size

    /** `@key` → 文案；不是 `@` 开头就原样返回。 */
    private fun resolveText(value: String, strings: Map<String, String>): String {
        if (!value.startsWith("@")) return value
        return strings[value.removePrefix("@")] ?: value.removePrefix("@")
    }

    /**
     * 本次搜索可用的插件。
     *
     * 过滤三层：
     *  1. 用户停用的（数据库 enabled=0）
     *  2. 本次会话内损坏的（[brokenPlugins]）
     *
     * 第 1 层以前漏了 —— 用户关掉的源照样参与搜索，开关形同虚设。
     */
    fun availablePlugins(): List<LyricoPlugin> {
        ensureBundledLoaded()
        val allowed = enabledIdsSnapshot()
        return pluginsById.values
            .filter { it.id !in brokenPlugins }
            .filter { allowed == null || it.id in allowed }
            .sortedBy { it.id }
    }

    /**
     * 同步版本：内部持有最近一次从库里读到的启用集合。
     *
     * `availablePlugins()` 在搜索的同步路径里被调用，无法在这里 await，
     * 所以由 [refreshEnabledIds] 预先刷新这个快照。
     */
    @Volatile
    private var enabledIdsCache: Set<String>? = null

    private fun enabledIdsSnapshot(): Set<String>? = enabledIdsCache

    /** 搜索前调用：把库里「已启用」的集合读进快照。 */
    suspend fun refreshEnabledIds() {
        enabledIdsCache = runCatching { enabledIdsProvider() }
            .onFailure {
                com.catclaw.music.kt.core.diagnostic.AppLog.w(
                    "LyricoPlugins",
                    "读取插件启用状态失败，本次不过滤：${it.message}",
                )
            }
            .getOrNull()
        com.catclaw.music.kt.core.diagnostic.AppLog.d(
            "LyricoPlugins",
            "启用快照=${enabledIdsCache?.let { it.joinToString(",") } ?: "null(不过滤)"}" +
                " 已加载=${pluginsById.keys}",
        )
    }

    /** 把插件标记为本次会话不可用并释放它的引擎。 */
    private fun markBroken(pluginId: String, reason: String) {
        if (brokenPlugins.add(pluginId)) {
            logSink("warn", "LyricoPlugins", "$pluginId 已停用（本次会话）：$reason")
            hosts.remove(pluginId)?.let { runCatching { it.close() } }
        }
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
            val plugin = pluginsById[pluginId] ?: run {
                logSink("warn", "LyricoPlugins", "$pluginId 不在已加载列表")
                return@withContext emptyList()
            }
            val host = hostFor(pluginId) ?: run {
                logSink("warn", "LyricoPlugins", "$pluginId 脚本宿主创建失败")
                return@withContext emptyList()
            }
            if (!plugin.manifest.hasGetLyrics) {
                // 早点返回时一定要留痕：真机上「源启用了却一条都不出」全靠这行区分
                logSink(
                    "warn",
                    "LyricoPlugins",
                    "$pluginId 未声明 getLyrics，跳过（capabilities=${plugin.manifest.capabilities}）",
                )
                return@withContext emptyList()
            }

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
                    if (isFatalError(res.error)) markBroken(pluginId, res.error.orEmpty())
                }
            }

            // 「歌名 + 歌手」查不到时退回纯歌名再搜一次：不少源对多关键词做 AND 匹配，
            // 歌手名带 feat./多艺人时整条搜不到，纯歌名却能命中。只在首轮 0 结果时多发一次，
            // 正常路径不会多花时间。
            if (hits.isEmpty() && plugin.manifest.hasSearchSongs) {
                val titleOnly = target.title.trim()
                if (titleOnly.isNotEmpty() && titleOnly != keyword) {
                    val retry = host.invoke("searchSongs", host.searchSongsRequest(titleOnly))
                    if (retry.ok) {
                        hits = LyricoSongParser.parse(retry.valueJson)
                        logSink(
                            "debug",
                            "LyricoPlugins",
                            "$pluginId searchSongs($titleOnly) 回退 => ${hits.size} 首",
                        )
                    }
                }
            }

            // 2) getLyrics：最优命中 → 其余命中（前两首）→ 标题直调，依次尝试
            val best = LyricoSongMatcher.pickBest(target, hits)
            // 候选顺序：最优命中优先，其次再试两首 —— 只有前一次取词失败/为空才会走到下一首，
            // 所以正常路径仍然只打一次网页请求，不会变慢。
            val songOrder = buildList {
                best?.let { add(it) }
                hits.filter { it.id != best?.id }.take(2).forEach { add(it) }
            }
            // 每个请求带着它对应的歌曲记录：多首尝试时元信息要跟着变，
            // 否则第二条结果的标题/时长会被错填成最优命中那首的
            val attempts = buildList {
                songOrder.forEach { add(it to host.getLyricsRequestForSong(it.json)) }
                add(
                    null to host.getLyricsRequest(
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

            for ((song, request) in attempts) {
                val result = host.invoke("getLyrics", request)
                if (!result.ok) {
                    logSink("warn", "LyricoPlugins", "$pluginId 取词失败：${result.error}")
                    if (isFatalError(result.error)) markBroken(pluginId, result.error.orEmpty())
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
                        trackTitle = lyrics.tags["ti"] ?: song?.title ?: best?.title ?: target.title,
                        artist = lyrics.tags["ar"] ?: song?.artist ?: best?.artist ?: target.artist,
                        album = lyrics.tags["al"] ?: song?.album ?: best?.album ?: target.album,
                        durationMs = song?.durationMs ?: target.durationMs,
                        lyrics = lyrics,
                    )
                }
            }
            emptyList()
        }

    /**
     * 用某个插件搜封面。
     *
     * 走 `searchSongs` 而不是 `searchCovers`：前者返回的歌曲记录自带 `picUrl`，
     * 一次请求同时满足歌词与封面两种需求，也少一条要维护的调用路径。
     */
    suspend fun searchCovers(pluginId: String, target: LyricsTarget): List<CoverCandidate> =
        withContext(Dispatchers.IO) {
            val plugin = pluginsById[pluginId] ?: return@withContext emptyList()
            val host = hostFor(pluginId) ?: return@withContext emptyList()
            val keyword = listOf(target.title.trim(), target.artist.trim())
                .filter { it.isNotEmpty() }
                .joinToString(" ")
            if (keyword.isEmpty()) return@withContext emptyList()

            val res = host.invoke("searchSongs", host.searchSongsRequest(keyword))
            if (!res.ok) {
                logSink("warn", "LyricoPlugins", "$pluginId 封面搜索失败：${res.error}")
                return@withContext emptyList()
            }
            LyricoSongParser.parse(res.valueJson)
                .filter { it.picUrl.isNotBlank() }
                .map { hit ->
                    CoverCandidate(
                        sourceId = pluginId,
                        sourceName = plugin.displayName,
                        url = hit.picUrl,
                        title = hit.title,
                        artist = hit.artist,
                        album = hit.album,
                        score = LyricoSongMatcher.score(target, hit),
                    )
                }
                .sortedByDescending { it.score }
        }

    /** 只做 `searchSongs`，返回插件的歌曲记录（元数据匹配直接用这些字段）。 */
    suspend fun searchSongsHits(pluginId: String, target: LyricsTarget): List<LyricoSongHit> =
        withContext(Dispatchers.IO) {
            val host = hostFor(pluginId) ?: return@withContext emptyList()
            val keyword = listOf(target.title.trim(), target.artist.trim())
                .filter { it.isNotEmpty() }.joinToString(" ")
            if (keyword.isEmpty()) return@withContext emptyList()
            val res = host.invoke("searchSongs", host.searchSongsRequest(keyword))
            if (!res.ok) return@withContext emptyList()
            LyricoSongParser.parse(res.valueJson)
        }
    /**
     * 只有「引擎/脚本真的坏了」才把插件踢出本次会话。
     *
     * 超时**不算**：那是一次网络/接口慢，下一次可能就正常。之前把「超时」也算进来，
     * 结果是慢一点的源第一次被 12s 砍掉后就被 [markBroken] 永久移出
     * `availablePlugins()`，用户看到的现象正是「明明启用了多个源，只返回一个」——
     * 其余源早已在本次会话里被悄无声息地停用。
     */
    private fun isFatalError(error: String?): Boolean =
        error != null && "已失效" in error

    /** 释放所有引擎（页面销毁/应用退出时调用）。 */
    fun release() {
        hosts.values.forEach { runCatching { it.close() } }
        hosts.clear()
    }
}
