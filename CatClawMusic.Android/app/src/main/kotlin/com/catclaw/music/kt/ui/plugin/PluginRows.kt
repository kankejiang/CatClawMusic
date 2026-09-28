package com.catclaw.music.kt.ui.plugin

import com.catclaw.music.kt.R
import com.catclaw.music.kt.core.plugin.ManifestProblem
import com.catclaw.music.kt.core.plugin.PluginManifest
import com.catclaw.music.kt.core.plugin.PluginRegistry
import com.catclaw.music.kt.core.plugin.effectiveCapabilities

/**
 * 插件管理页的一行。
 *
 * 状态文案走资源 id 而不是 [ManifestProblem.message]：core 里那些中文常量是给开发者读的默认值，
 * 直接端给用户会让英文界面整条露出中文（本 App 有 51 个语言）。
 */
data class PluginRow(
    val id: String,
    val name: String,
    val version: String,
    val capabilities: String,
    val statusRes: Int,
    val statusArgs: List<Any> = emptyList(),
    val usable: Boolean,
)

fun pluginRows(entries: List<PluginRegistry.Entry>): List<PluginRow> = entries.map { entry ->
    val manifest = entry.manifest
    val (res, args) = statusOf(entry.problem)
    PluginRow(
        id = manifest.id,
        name = manifest.name.ifBlank { manifest.id.ifBlank { "—" } },
        version = manifest.version,
        capabilities = manifest.effectiveCapabilities().sorted().joinToString(", "),
        statusRes = res,
        statusArgs = args,
        usable = entry.problem == null,
    )
}

private fun statusOf(problem: ManifestProblem?): Pair<Int, List<Any>> = when (problem) {
    null -> R.string.plugin_status_ok to emptyList()
    ManifestProblem.Malformed -> R.string.plugin_problem_malformed to emptyList()
    ManifestProblem.MissingId -> R.string.plugin_problem_missing_id to emptyList()
    ManifestProblem.MalformedId -> R.string.plugin_problem_malformed_id to emptyList()
    is ManifestProblem.UnsupportedProtocol ->
        R.string.plugin_problem_protocol to listOf(problem.found, PluginManifest.SUPPORTED_PROTOCOL)
    is ManifestProblem.PlatformUnavailable -> R.string.plugin_problem_platform to listOf(problem.hostPlatform)
    is ManifestProblem.HostTooOld -> R.string.plugin_problem_host_old to listOf(problem.required)
}
