package com.catclaw.music.kt.lyrico.search

import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.async
import kotlinx.coroutines.awaitAll
import kotlinx.coroutines.coroutineScope
import kotlinx.coroutines.withContext
import kotlinx.coroutines.withTimeoutOrNull

/**
 * 歌词搜索编排：LRCLIB + 全部内置源并行，合并去重后按匹配度排序。
 *
 * 设计要点：
 *  - **一个源失败不影响其它源**：每个源各自包 catch，慢源各自超时
 *  - 总耗时约等于最慢的那个源，而不是所有源之和
 *  - 去重按「来源内不重复 + 同来源首行内容相同视为同一份」
 */
class LyricsSearchService(
    private val lrclib: LrclibClient,
    private val pluginSources: LyricoPluginSources? = null,
    /** 单个源的超时预算；插件内部可能要多次网络请求 */
    private val perSourceTimeoutMs: Long = 35_000L,
) {

    /** 一次搜索结果：候选 + 各源的问题（界面可以展示「某源失败」） */
    data class Result(
        val candidates: List<LyricsCandidate>,
        val errors: List<String> = emptyList(),
    )

    suspend fun search(target: LyricsTarget): Result = withContext(Dispatchers.IO) {
        val errors = java.util.Collections.synchronizedList(mutableListOf<String>())
        val pluginIds = pluginSources?.availablePlugins()?.map { it.id }.orEmpty()

        val collected = coroutineScope {
            val jobs = mutableListOf<kotlinx.coroutines.Deferred<List<LyricsCandidate>>>()

            // LRCLIB
            jobs += async {
                runCatching {
                    withTimeoutOrNull(perSourceTimeoutMs) { lrclib.search(target) }
                        ?: run {
                            errors += "LRCLIB 超时"
                            emptyList()
                        }
                }.getOrElse {
                    errors += "LRCLIB 失败：${it.message}"
                    emptyList()
                }
            }

            // 各内置源
            pluginIds.forEach { pluginId ->
                jobs += async {
                    runCatching {
                        withTimeoutOrNull(perSourceTimeoutMs) {
                            pluginSources!!.searchLyrics(pluginId, target)
                        } ?: run {
                            errors += "$pluginId 超时"
                            emptyList()
                        }
                    }.getOrElse {
                        errors += "$pluginId 失败：${it.message}"
                        emptyList()
                    }
                }
            }

            jobs.awaitAll().flatten()
        }

        val deduped = dedupe(collected)
        val ranked = LyricsRanking.rank(target, deduped)
        Result(candidates = ranked, errors = synchronized(errors) { errors.toList() })
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
}
