package com.catclaw.music.kt.core.plugin

import java.io.File

/**
 * 插件目录扫描：约定 `<root>/<插件目录名>/plugin.json`。
 *
 * 为什么扫目录而不是维护一份 `installed.json`：C# 那份索引会在"文件被手动删了"和
 * "索引还写着"之间脱节（它的加载路径就是按索引逐项恢复、文件不存在即跳过，
 * `PluginManager.cs:1187`），用户看到的是"装了但没反应"且没有任何地方说明为什么。
 * 以磁盘上的清单为唯一事实，坏掉的安装自然就不存在了，而"为什么没生效"由
 * [PluginRegistry] 的具名问题回答。
 */
class PluginDirectoryScanner(
    private val root: File,
    private val manifestFileName: String = "plugin.json",
) {
    /** 扫到的每份清单原文 + 它来自哪个目录名（管理页要显示来源，排错时靠它定位） */
    data class Found(val directory: String, val text: String)

    fun readManifests(): List<Found> = runCatching {
        val children = root.listFiles()?.toList().orEmpty()
        children.mapNotNull { dir ->
            if (!dir.isDirectory) return@mapNotNull null
            val manifest = File(dir, manifestFileName)
            if (!manifest.isFile) return@mapNotNull null
            Found(dir.name, runCatching { manifest.readText() }.getOrDefault(""))
        }.sortedBy { it.directory }
    }.getOrDefault(emptyList())

    /**
     * 供 [PluginRegistry] 用的原文列表。
     * 读不到内容的空清单也保留，让注册表把它报成 `Malformed` —— 静默跳过就是"装了没反应"。
     */
    fun manifestTexts(): List<String> = readManifests().map { it.text }
}

/**
 * 装配一个可用的注册表：扫描 → 校验 → 平台/版本过滤。
 * 放在这里是为了让"扫描 + 注册表"这套组合只有一处定义，宿主与测试都走它。
 */
fun pluginRegistry(
    root: File,
    hostPlatform: String,
    hostVersion: String,
): PluginRegistry = PluginDirectoryScanner(root).let { scanner ->
    PluginRegistry(
        hostPlatform = hostPlatform,
        hostVersion = hostVersion,
        readManifests = scanner::manifestTexts,
    )
}
