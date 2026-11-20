package com.catclaw.music.kt.lyrico.plugin

import android.content.res.AssetManager
import java.io.File
import kotlinx.serialization.json.Json
import kotlinx.serialization.json.JsonObject
import kotlinx.serialization.json.JsonPrimitive

/**
 * 一个已装载的 Lyrico 源插件：manifest + 全部文本文件（相对路径 → 内容）。
 *
 * 文件内容一次性读进内存：插件脚本普遍只有几十 KB，而按需读盘会让
 * 「拼接 lib + entry」这类操作变成多次 I/O。
 */
data class LyricoPlugin(
    val id: String,
    val manifest: LyricoManifest,
    val files: Map<String, String>,
    val iconBytes: ByteArray? = null,
) {
    val displayName: String get() = manifest.name.ifBlank { id }

    /**
     * 拼接出可执行脚本：includeDirs 下的 .js（按路径排序）先拼，entry 最后。
     *
     * 顺序很重要：插件的 entry 直接调用 lib 里定义的函数，反过来就 ReferenceError。
     */
    fun buildCompositeScript(): String? {
        val entryCode = files[manifest.entry] ?: return null
        val sb = StringBuilder()
        manifest.includeDirs.forEach { dir ->
            val prefix = dir.trim('/').let { if (it.isEmpty()) "" else "$it/" }
            files.keys
                .filter { it.startsWith(prefix) && it.endsWith(".js", ignoreCase = true) }
                .sorted()
                .forEach { path ->
                    sb.append("// lib: ").append(path).append('\n')
                    sb.append(files.getValue(path)).append("\n\n")
                }
        }
        sb.append("// entry: ").append(manifest.entry).append('\n')
        sb.append(entryCode)
        return sb.toString()
    }
}

/** 从 assets（内置源）或磁盘目录（用户导入）装载插件。 */
object LyricoPluginLoader {

    private val json = Json { ignoreUnknownKeys = true; isLenient = true }

    /**
     * 装载 assets 下 `rootDir/<插件>/` 里的全部插件。
     *
     * 约定的内置源目录：`assets/lyrico/netease/...`。
     *
     * @param errors 可选：收集装载失败原因（插件坏了要能告诉用户坏在哪，不能静默丢）
     */
    fun loadFromAssets(
        assets: AssetManager,
        rootDir: String = "lyrico",
        errors: MutableList<String>? = null,
    ): List<LyricoPlugin> {
        val pluginDirs = runCatching { assets.list(rootDir)?.toList() }
            .onFailure { errors?.add("列不出 assets/$rootDir：${it.message}") }
            .getOrNull() ?: return emptyList()
        return pluginDirs.mapNotNull { name ->
            try {
                loadAssetPlugin(assets, "$rootDir/$name", name)
            } catch (t: Throwable) {
                errors?.add("$name 装载失败：${t.javaClass.simpleName}: ${t.message}")
                null
            }
        }
    }

    private fun loadAssetPlugin(assets: AssetManager, path: String, id: String): LyricoPlugin? {
        val files = linkedMapOf<String, String>()
        collectAssets(assets, path, "", files)
        val manifestText = files["manifest.json"] ?: return null
        val manifest = LyricoManifest.parse(manifestText) ?: return null
        val realId = manifest.id.ifBlank { id }
        return LyricoPlugin(
            id = realId,
            manifest = manifest.copy(id = realId),
            files = files,
            iconBytes = null,
        )
    }

    private fun collectAssets(
        assets: AssetManager,
        base: String,
        rel: String,
        out: MutableMap<String, String>,
    ) {
        val dir = if (rel.isEmpty()) base else "$base/$rel"
        val children = runCatching { assets.list(dir)?.toList() }.getOrNull() ?: return
        children.forEach { child ->
            val childRel = if (rel.isEmpty()) child else "$rel/$child"
            val childPath = "$base/$childRel"
            val isDir = runCatching { assets.list(childPath)?.isNotEmpty() == true }.getOrDefault(false)
            if (isDir) {
                collectAssets(assets, base, childRel, out)
            } else {
                // 只读文本类文件；图标等二进制后面单独按需取
                if (childRel.endsWith(".js") || childRel.endsWith(".json")) {
                    runCatching {
                        assets.open(childPath).bufferedReader().use { it.readText() }
                    }.getOrNull()?.let { out[childRel] = it }
                }
            }
        }
    }

    /** 从磁盘目录装载（用户导入 / ZIP 解包后的目录）。 */
    fun loadFromDirectory(dir: File): LyricoPlugin? {
        if (!dir.isDirectory) return null
        val manifestFile = File(dir, "manifest.json")
        if (!manifestFile.isFile) return null
        val manifestText = runCatching { manifestFile.readText() }.getOrNull() ?: return null
        val manifest = LyricoManifest.parse(manifestText) ?: return null
        val files = linkedMapOf<String, String>()
        dir.walkTopDown().filter { it.isFile }.forEach { f ->
            val rel = f.relativeTo(dir).invariantSeparatorsPath
            if (rel.endsWith(".js") || rel.endsWith(".json")) {
                runCatching { files[rel] = f.readText() }.getOrNull()
            }
        }
        val icon = manifest.icon?.let { iconRel ->
            val f = File(dir, iconRel)
            if (f.isFile) runCatching { f.readBytes() }.getOrNull() else null
        }
        val id = manifest.id.ifBlank { dir.name }
        return LyricoPlugin(id, manifest.copy(id = id), files, icon)
    }

    /**
     * 取插件某个语言的文案表（key → 模板）。找不到对应 locale 时回退 defaultLocale。
     *
     * 插件在 manifest.i18n.resources 里声明「语言 → 相对路径」，文件是扁平的 key→文本 JSON。
     */
    fun resolveStrings(plugin: LyricoPlugin, preferredLocale: String): Map<String, String> {
        val i18n = plugin.manifest.i18n ?: return emptyMap()
        val candidates = buildList {
            add(preferredLocale)
            // zh-Hans / zh-CN 这类只要语言前缀能对上就用
            preferredLocale.substringBefore('-').let { add(it) }
            add(i18n.defaultLocale)
            addAll(i18n.resources.keys)
        }
        for (locale in candidates) {
            val path = i18n.resources[locale] ?: continue
            val text = plugin.files[path] ?: continue
            val obj = runCatching { json.parseToJsonElement(text) as? JsonObject }.getOrNull() ?: continue
            return obj.mapValues { (_, v) -> (v as? JsonPrimitive)?.content ?: "" }
        }
        return emptyMap()
    }
}
