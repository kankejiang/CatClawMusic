package com.catclaw.music.kt.lyrico.plugin

import android.content.Context
import com.catclaw.music.kt.lyrico.db.LyricoSourcePluginDao
import com.catclaw.music.kt.lyrico.db.LyricoSourcePluginEntity
import java.io.File
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.withContext

/** 一个可供 UI 展示的插件条目（内置源与用户安装源合并后的视图）。 */
data class PluginEntry(
    val pluginId: String,
    val displayName: String,
    val author: String,
    val versionName: String,
    val versionCode: Int,
    val description: String,
    val capabilities: List<String>,
    val enabled: Boolean,
    /** true = 随 APK 内置，不可卸载 */
    val bundled: Boolean,
    /** 缺失/异常时的说明 */
    val issue: String? = null,
)

/**
 * 源插件注册表：统一管理「内置源」与「用户安装源」。
 *
 * ## 为什么内置源也要进数据库
 *
 * 内置源随 APK 走，但它同样需要「启用/停用」状态。把状态存在 `lyrico_source_plugin`
 * 让两类源走同一条查询，UI 与搜索逻辑就不必判断「这个源是不是内置的」——
 * 只有卸载与版本升级时才区分。
 *
 * ## 目录约定
 *
 * - 内置：`assets/lyrico/<name>/`（只读，随版本更新）
 * - 用户安装：`filesDir/lyrico-plugins/<pluginId>/`（可卸载）
 */
class PluginRegistry(
    private val context: Context,
    private val dao: LyricoSourcePluginDao,
    private val locale: () -> String = { "zh-Hans" },
    private val logSink: (String, String, String) -> Unit = { _, _, _ -> },
) {

    private val pluginsRoot: File get() = File(context.filesDir, PLUGINS_DIR)

    /**
     * 同步磁盘上的插件到数据库，返回合并后的完整列表。
     *
     * 每次调用都会重新扫描：用户可能手工删了目录，或装了新插件。
     */
    suspend fun refresh(): List<PluginEntry> = withContext(Dispatchers.IO) {
        val localeTag = locale()
        val bundled = loadBundled(localeTag)
        val installed = loadInstalled(localeTag)

        // 数据库里的启用状态：没有记录时默认启用
        val states = runCatching { dao.all().associateBy { it.pluginId } }.getOrDefault(emptyMap())

        val merged = (bundled + installed).map { entry ->
            val enabled = states[entry.pluginId]?.enabled ?: true
            entry.copy(enabled = enabled)
        }
        // 把（重新）发现的插件写回库，同时清掉已经不存在于磁盘的记录
        val aliveIds = merged.map { it.pluginId }.toSet()
        runCatching {
            states.keys.filter { it !in aliveIds }.forEach { dao.delete(it) }
            merged.forEach { entry ->
                val existing = states[entry.pluginId]
                dao.upsert(
                    LyricoSourcePluginEntity(
                        id = existing?.id ?: 0,
                        pluginId = entry.pluginId,
                        displayName = entry.displayName,
                        author = entry.author,
                        versionName = entry.versionName,
                        versionCode = existing?.versionCode ?: 0,
                        entry = existing?.entry ?: "source.js",
                        manifestJson = existing?.manifestJson,
                        enabled = entry.enabled,
                        installedAt = existing?.installedAt ?: System.currentTimeMillis(),
                        updatedAt = System.currentTimeMillis(),
                    ),
                )
            }
        }.onFailure { logSink("warn", "PluginRegistry", "写库失败：${it.message}") }

        merged.sortedWith(compareByDescending<PluginEntry> { it.bundled }.thenBy { it.displayName })
    }

    private fun loadBundled(localeTag: String): List<PluginEntry> {
        val errors = mutableListOf<String>()
        return LyricoPluginLoader.loadFromAssets(context.assets, errors = errors).map { plugin ->
            val strings = LyricoPluginLoader.resolveStrings(plugin, localeTag)
            plugin.toEntry(strings, bundled = true, issue = null)
        }.also { errors.forEach { e -> logSink("warn", "PluginRegistry", e) } }
    }

    private fun loadInstalled(localeTag: String): List<PluginEntry> {
        val dirs = pluginsRoot.listFiles()?.filter { it.isDirectory && !it.name.startsWith(".tmp-") }
            ?: return emptyList()
        return dirs.mapNotNull { dir ->
            val plugin = runCatching { LyricoPluginLoader.loadFromDirectory(dir) }.getOrNull()
            if (plugin == null) {
                logSink("warn", "PluginRegistry", "目录 ${dir.name} 无法解析为插件，已跳过")
                return@mapNotNull null
            }
            val strings = LyricoPluginLoader.resolveStrings(plugin, localeTag)
            plugin.toEntry(strings, bundled = false, issue = null)
        }
    }

    private fun LyricoPlugin.toEntry(
        strings: Map<String, String>,
        bundled: Boolean,
        issue: String?,
    ): PluginEntry = PluginEntry(
        pluginId = id,
        // name 也可能是 `@plugin.name` 这种 i18n key，必须一起解析 ——
        // 只给 description 解析会漏掉它（真机验收时标题就显示成了 `@plugin.name`）
        displayName = resolveI18n(manifest.name, strings).ifBlank { id },
        author = manifest.author,
        versionName = manifest.versionName,
        versionCode = manifest.versionCode,
        description = resolveI18n(manifest.description, strings),
        capabilities = manifest.capabilities,
        enabled = true,
        bundled = bundled,
        issue = issue,
    )

    /** manifest 里的文案可能是 `@key` 形式。 */
    private fun resolveI18n(value: String, strings: Map<String, String>): String {
        if (!value.startsWith("@")) return value
        return strings[value.removePrefix("@")] ?: value.removePrefix("@")
    }

    /** 启用/停用插件。内置源也可以停用（只是不能卸载）。 */
    suspend fun setEnabled(pluginId: String, enabled: Boolean): Boolean = withContext(Dispatchers.IO) {
        runCatching {
            dao.setEnabled(pluginId, enabled)
            true
        }.getOrDefault(false)
    }

    /** 卸载用户安装的插件；内置源拒绝卸载。 */
    suspend fun uninstall(pluginId: String, bundled: Boolean): PluginInstallOutcome =
        withContext(Dispatchers.IO) {
            if (bundled) {
                return@withContext PluginInstallOutcome.Rejected("内置源不能卸载，只能停用")
            }
            val removed = PluginInstaller.uninstall(pluginsRoot, pluginId)
            if (!removed) {
                return@withContext PluginInstallOutcome.Invalid("插件目录不存在")
            }
            runCatching { dao.delete(pluginId) }
            PluginInstallOutcome.Success(pluginId, "", "")
        }

    /** 从 ZIP 安装。成功后立即写库，无需等下次 refresh。 */
    suspend fun install(zipBytes: ByteArray): PluginInstallOutcome = withContext(Dispatchers.IO) {
        val outcome = PluginInstaller.install(zipBytes.inputStream(), pluginsRoot)
        if (outcome is PluginInstallOutcome.Success) {
            val plain = PluginInstaller.sanitizeDirName(outcome.pluginId)
            val plugin = LyricoPluginLoader.loadFromDirectory(File(pluginsRoot, plain))
            if (plugin != null) {
                runCatching {
                    dao.upsert(
                        LyricoSourcePluginEntity(
                            pluginId = plugin.id,
                            displayName = plugin.displayName,
                            author = plugin.manifest.author,
                            versionName = plugin.manifest.versionName,
                            entry = plugin.manifest.entry,
                            enabled = true,
                            installedAt = System.currentTimeMillis(),
                            updatedAt = System.currentTimeMillis(),
                        ),
                    )
                }
            }
        }
        outcome
    }

    /** 插件安装根目录（供连通性测试与诊断日志使用）。 */
    fun pluginsRoot(): File = pluginsRoot

    companion object {
        const val PLUGINS_DIR = "lyrico-plugins"
    }
}
