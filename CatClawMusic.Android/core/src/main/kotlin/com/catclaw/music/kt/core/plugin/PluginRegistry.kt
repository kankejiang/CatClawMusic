package com.catclaw.music.kt.core.plugin

/**
 * 插件目录的读取与筛选。
 *
 * 现状对照：C# 侧不是目录扫描，而是靠 `installed.json` 逐项恢复
 * （`PluginManager.cs:1205-1272`，文件不存在即跳过），所以"装了但没登记"和"登记了但文件没了"
 * 是两种不同的坏法。v2 改成**以清单为唯一事实**：每个插件目录里一个 `plugin.json`，
 * 扫到就算装、读不出就算坏 —— 少一份索引，就少一份会与磁盘脱节的影子状态。
 *
 * [readManifests] 是文件缝：宿主侧接真实的 `filesDir/plugins/<id>/plugin.json`，
 * 测试里给一个内存列表，于是筛选逻辑不需要 IO 也不需要 Robolectric。
 */
class PluginRegistry(
    private val hostPlatform: String,
    private val hostVersion: String,
    private val readManifests: () -> List<String>,
) {
    /** [problem] 为 null 表示可用；不用的插件**不会被静默吞掉**，管理页要能列出原因 */
    data class Entry(val manifest: PluginManifest, val problem: ManifestProblem?)

    fun scan(): List<Entry> = readManifests().map { raw ->
        val manifest = PluginManifest.fromJson(raw)
        if (manifest == null) {
            return@map Entry(UNKNOWN, ManifestProblem.Malformed)
        }
        Entry(manifest, manifest.validate(hostPlatform) ?: tooOld(manifest))
    }.sortedWith(compareBy({ it.problem != null }, { it.manifest.id }))

    fun usable(): List<PluginManifest> = scan().filterNotNullIfOk()

    /** 只支持某项能力的插件排在后面，让"全能音源"优先被选中（C# 侧只有 ThemeOrder 一个优先级字段） */
    fun rankedByCapability(capability: String): List<PluginManifest> =
        usable().sortedWith(
            compareByDescending<PluginManifest> { it.capabilities.contains(capability) }.thenBy { it.id },
        )

    private fun tooOld(manifest: PluginManifest): ManifestProblem? =
        if (hostSatisfies(hostVersion, manifest)) null else ManifestProblem.HostTooOld(manifest.minHostVersion)

    private fun List<Entry>.filterNotNullIfOk(): List<PluginManifest> =
        filter { it.problem == null }.map { it.manifest }

    private companion object {
        val UNKNOWN = PluginManifest(protocolVersion = 0, id = "", name = "", version = "")
    }
}
