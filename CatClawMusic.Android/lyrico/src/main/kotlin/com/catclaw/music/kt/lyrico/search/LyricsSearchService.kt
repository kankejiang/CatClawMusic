package com.catclaw.music.kt.lyrico.search

import com.catclaw.music.kt.core.diagnostic.AppLog
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.async
import kotlinx.coroutines.coroutineScope
import kotlinx.coroutines.flow.Flow
import kotlinx.coroutines.flow.channelFlow
import kotlinx.coroutines.launch
import kotlinx.coroutines.withContext
import kotlinx.coroutines.withTimeoutOrNull

/**
 * 歌词搜索编排：LRCLIB + 全部内置源并行，**先到先返回**。
 *
 * ## 为什么不是 awaitAll
 *
 * 最初用 `awaitAll()`：要等**所有**源都结束才返回。问题是源之间速度差极大 ——
 * 网易云通常 10~15s 出结果，而 LrcShare 的接口不可达时要等满 30s 的 JS 超时。
 * 用户等了 30 秒，其实 15 秒时结果就已经够了。
 *
 * 现在「谁先回来先给谁」：第一个源出结果就 emit，其余源回来一个补一个。
 *
 * ## 用 channelFlow 而不是 flow + 手写 Channel
 *
 * 第一版用 `flow { }` + 一个 `Channel<Unit>` 做信号，但**忘了 close()** ——
 * `for (signal in channel)` 会永远挂着等下一个信号，于是第一次 emit 之后
 * 后续源的结果永远发不出来（真机表现：只看到一个来源）。
 *
 * `channelFlow` 的语义正好是「子协程结束时自动关闭 channel」，
 * 不必自己管理生命周期，也就不会漏。
 */
class LyricsSearchService(
    private val lrclib: LrclibClient,
    private val pluginSources: LyricoPluginSources? = null,
    /**
     * 单个源的超时预算。
     *
     * 12s：插件内部是「搜索→逐条详情→取词」多跳串行请求，正常源 10s 内能完成；
     * 超过 12s 的基本是卡住了，继续等只会拖累用户。
     */
    private val perSourceTimeoutMs: Long = 40_000L,
) {

    /** 一次搜索结果：候选 + 各源的问题（界面可以展示「某源失败」） */
    data class Result(
        val candidates: List<LyricsCandidate>,
        val errors: List<String> = emptyList(),
    )

    /**
     * 搜索结果流：每有一个源返回就 emit 一次累积结果。
     *
     * 调用方收到第一次 emit 就能展示候选，后续 emit 用于补充。
     * 全部源结束（或超时）后流自动关闭。
     */
    fun searchStream(target: LyricsTarget): Flow<Result> = channelFlow {
        val errors = java.util.Collections.synchronizedList(mutableListOf<String>())
        val collected = java.util.Collections.synchronizedList(mutableListOf<LyricsCandidate>())

        // 先刷新「已启用插件」快照：availablePlugins() 是同步调用，读不了库
        pluginSources?.refreshEnabledIds()

        val sourceNames = buildList {
            add(LRCLIB)
            pluginSources?.availablePlugins()?.forEach { add(it.id) }
        }
        AppLog.d("LyricoSearch", "开始搜索「${target.title}」源=$sourceNames")

        sourceNames.forEach { sourceName ->
            // **必须显式切到 IO**：channelFlow 的 launch 继承调用者上下文，
            // 而 ViewModel 里是在主线程收集这个流 —— 不切线程的话，
            // LrclibClient 的同步 OkHttp 调用会抛 NetworkOnMainThreadException，
            // 3ms 就失败、看起来像「这个源没有歌词」（真机日志确认过）。
            launch(Dispatchers.IO) {
                val items = fetchFrom(sourceName, target, errors)
                // 记录「原始几条 → 打分后剩几条」：ranking 会按硬规则淘汰候选，
                // 只看界面会误以为「某个源没返回」，其实是被过滤掉了。
                val rankedItems = LyricsRanking.rank(target, items)
                AppLog.d(
                    "LyricoSearch",
                    "$sourceName 返回 ${items.size} 条，经打分保留 ${rankedItems.size} 条",
                )
                if (rankedItems.isNotEmpty()) {
                    synchronized(collected) { collected.addAll(rankedItems) }
                    // channelFlow 里 send 是安全的：子协程结束会关闭 channel
                    send(
                        Result(
                            candidates = LyricsRanking.rank(target, dedupe(collected.toList())),
                            errors = synchronized(errors) { errors.toList() },
                        ),
                    )
                }
            }
        }
        // channelFlow 会等所有子协程结束才关闭 —— 不需要手动 close，
        // 这正是换掉手写 Channel 的原因（那一版漏了 close，流永远挂着）。
    }

    /** 单个源：带超时、吞异常（一个源失败不该影响其它源）。 */
    private suspend fun fetchFrom(
        sourceName: String,
        target: LyricsTarget,
        errors: MutableList<String>,
    ): List<LyricsCandidate> = try {
        withTimeoutOrNull(perSourceTimeoutMs) {
            if (sourceName == LRCLIB) {
                lrclib.search(target)
            } else {
                pluginSources!!.searchLyrics(sourceName, target)
            }
        } ?: run {
            AppLog.w("LyricoSearch", "$sourceName 超时（${perSourceTimeoutMs / 1000}s）")
            errors.add("$sourceName 超时")
            emptyList()
        }
    } catch (t: Throwable) {
        AppLog.w("LyricoSearch", "$sourceName 失败：${t.message}", t)
        errors.add("$sourceName 失败：${t.message ?: t.javaClass.simpleName}")
        emptyList()
    }

    /** 兼容旧调用：等到所有源结束再返回（批量任务用，那边不追求首屏速度）。 */
    suspend fun search(target: LyricsTarget): Result = withContext(Dispatchers.IO) {
        val errors = java.util.Collections.synchronizedList(mutableListOf<String>())
        val sourceNames = buildList {
            add(LRCLIB)
            pluginSources?.availablePlugins()?.forEach { add(it.id) }
        }
        val all = coroutineScope {
            sourceNames.map { name ->
                async { fetchFrom(name, target, errors) }
            }.map { it.await() }.flatten()
        }
        Result(
            candidates = LyricsRanking.rank(target, dedupe(all)),
            errors = synchronized(errors) { errors.toList() },
        )
    }

    /** 自动匹配结果：带上各源的问题，界面/批量任务能说清「为什么没找到」。 */
    data class AutoMatch(
        val candidate: LyricsCandidate?,
        val errors: List<String> = emptyList(),
    )

    /** 精确取词（自动匹配用）：先试 LRCLIB /get，没有再退回搜索取第一条。 */
    suspend fun autoMatch(target: LyricsTarget): LyricsCandidate? = autoMatchDetailed(target).candidate

    suspend fun autoMatchDetailed(target: LyricsTarget): AutoMatch = withContext(Dispatchers.IO) {
        val direct = runCatching { lrclib.get(target) }
        if (direct.isSuccess) {
            val hit = direct.getOrNull()
            if (hit != null && hit.lyrics.hasContent) {
                return@withContext AutoMatch(hit)
            }
        }
        val result = search(target)
        AutoMatch(result.candidates.firstOrNull(), result.errors)
    }

    /**
     * 去重：同来源 + 同首行文本视为重复（插件偶尔一次返回同一份歌词的多个变体）。
     * 保留先出现的那条（插件已按自身置信度排序）。
     */
    private fun dedupe(candidates: List<LyricsCandidate>): List<LyricsCandidate> {
        val seen = HashSet<String>()
        val out = ArrayList<LyricsCandidate>(candidates.size)
        candidates.forEach { c ->
            val firstLine = c.lyrics.original.firstOrNull()?.text
                ?: c.lyrics.rawPlainLrc.lineSequence().firstOrNull { it.isNotBlank() }
                ?: ""
            val key = "${c.sourceId}|${firstLine.take(40)}"
            if (seen.add(key)) out += c
        }
        return out
    }

    private companion object {
        const val LRCLIB = "LRCLIB"
    }
}
