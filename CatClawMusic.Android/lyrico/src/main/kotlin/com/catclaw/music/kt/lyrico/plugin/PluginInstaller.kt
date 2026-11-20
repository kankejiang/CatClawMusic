package com.catclaw.music.kt.lyrico.plugin

import java.io.File
import java.io.InputStream
import java.util.zip.ZipInputStream

/** 插件安装结果。 */
sealed interface PluginInstallOutcome {
    data class Success(val pluginId: String, val displayName: String, val versionName: String) :
        PluginInstallOutcome

    /** 包本身有问题（缺 manifest、manifest 坏、无入口脚本） */
    data class Invalid(val reason: String) : PluginInstallOutcome

    /** 安全拒绝：路径穿越、条目过多、解压体积过大 */
    data class Rejected(val reason: String) : PluginInstallOutcome

    data class Failed(val message: String) : PluginInstallOutcome
}

/**
 * 从 ZIP 安装 Lyrico 源插件。
 *
 * ## 安全边界（这是不可信输入）
 *
 * 插件的 ZIP 是外部下载物，必须当成攻击面处理：
 *  - **路径穿越**：`../` 或绝对路径的条目一律拒绝（否则能写到应用目录之外）
 *  - **条目数 / 解压体积上限**：防 zip bomb（解压后撑爆磁盘）
 *  - 只解 `.js` / `.json` / 图片等文本与资源，其余跳过（不要的可执行物不落地）
 *  - 最终只保留插件根目录下的文件，符号链接条目忽略
 *
 * ## 为什么解到临时目录再移动
 *
 * 直接往目标目录解，中途失败会留下半个插件（manifest 有、source.js 没有），
 * 界面上表现为「装了但加载失败」。先解到 `.tmp-<id>` 再整体重命名，失败就删掉。
 */
object PluginInstaller {

    private const val MAX_ENTRIES = 2_000
    private const val MAX_TOTAL_BYTES = 64L * 1024 * 1024
    private const val MAX_SINGLE_BYTES = 16L * 1024 * 1024

    /** 允许落地的扩展名：插件只该有脚本、文案、图标 */
    private val ALLOWED_EXTENSIONS = setOf(
        "js", "json", "png", "jpg", "jpeg", "webp", "svg", "txt", "md",
    )

    /**
     * @param zipStream 插件包字节流（调用方负责关闭）
     * @param pluginsRoot 插件安装根目录（每个插件一个子目录）
     */
    fun install(zipStream: InputStream, pluginsRoot: File): PluginInstallOutcome {
        if (!pluginsRoot.exists() && !pluginsRoot.mkdirs()) {
            return PluginInstallOutcome.Failed("无法创建插件目录")
        }
        val staging = File(pluginsRoot, ".tmp-${System.currentTimeMillis()}")
        if (staging.exists()) staging.deleteRecursively()
        if (!staging.mkdirs()) return PluginInstallOutcome.Failed("无法创建临时目录")

        try {
            val extracted = extract(zipStream, staging)
            if (extracted != null) {
                staging.deleteRecursively()
                return extracted
            }
            // 有的包把插件放在顶层子目录里：往下找一层含 manifest.json 的目录
            val pluginRoot = resolvePluginRoot(staging)
                ?: run {
                    staging.deleteRecursively()
                    return PluginInstallOutcome.Invalid("压缩包里没有 manifest.json")
                }
            val plugin = LyricoPluginLoader.loadFromDirectory(pluginRoot)
                ?: run {
                    staging.deleteRecursively()
                    return PluginInstallOutcome.Invalid("manifest.json 无法解析")
                }
            if (plugin.files[plugin.manifest.entry] == null) {
                staging.deleteRecursively()
                return PluginInstallOutcome.Invalid("缺少入口脚本 ${plugin.manifest.entry}")
            }
            val hostApi = plugin.manifest.minHostApiVersion
            if (hostApi > LyricoManifest.HOST_API_VERSION) {
                staging.deleteRecursively()
                return PluginInstallOutcome.Invalid(
                    "插件要求宿主 API v$hostApi，当前只支持 v${LyricoManifest.HOST_API_VERSION}",
                )
            }

            val target = File(pluginsRoot, sanitizeDirName(plugin.id))
            if (target.exists()) target.deleteRecursively()
            if (!pluginRoot.renameTo(target)) {
                // rename 跨文件系统会失败：退回复制
                pluginRoot.copyRecursively(target, overwrite = true)
                pluginRoot.deleteRecursively()
            }
            staging.deleteRecursively()
            return PluginInstallOutcome.Success(
                pluginId = plugin.id,
                displayName = plugin.displayName,
                versionName = plugin.manifest.versionName,
            )
        } catch (t: Throwable) {
            runCatching { staging.deleteRecursively() }
            return PluginInstallOutcome.Failed("${t.javaClass.simpleName}: ${t.message}")
        }
    }

    /** @return null 表示成功；否则是应当直接返回的失败结果 */
    private fun extract(zipStream: InputStream, dest: File): PluginInstallOutcome? {
        var entries = 0
        var total = 0L
        val destPath = dest.canonicalPath + File.separator

        ZipInputStream(zipStream).use { zip ->
            while (true) {
                val entry = zip.nextEntry ?: break
                entries++
                if (entries > MAX_ENTRIES) return PluginInstallOutcome.Rejected("条目过多（>$MAX_ENTRIES）")

                val name = entry.name
                if (name.isBlank()) continue
                // 拒绝绝对路径与路径穿越
                if (name.startsWith("/") || name.startsWith("\\") || name.contains("..")) {
                    return PluginInstallOutcome.Rejected("检测到非法路径：$name")
                }
                if (entry.isDirectory) continue

                val ext = name.substringAfterLast('.', "").lowercase()
                if (ext !in ALLOWED_EXTENSIONS) continue

                val target = File(dest, name)
                // 二次校验：解析后的真实路径必须仍在目标目录内
                if (!target.canonicalPath.startsWith(destPath)) {
                    return PluginInstallOutcome.Rejected("路径越界：$name")
                }
                target.parentFile?.mkdirs()

                var written = 0L
                target.outputStream().use { out ->
                    val buf = ByteArray(8 * 1024)
                    while (true) {
                        val n = zip.read(buf)
                        if (n <= 0) break
                        written += n
                        total += n
                        if (written > MAX_SINGLE_BYTES) {
                            return PluginInstallOutcome.Rejected("单文件过大：$name")
                        }
                        if (total > MAX_TOTAL_BYTES) {
                            return PluginInstallOutcome.Rejected("解压体积过大（>${MAX_TOTAL_BYTES / 1024 / 1024}MB）")
                        }
                        out.write(buf, 0, n)
                    }
                }
            }
        }
        return null
    }

    /** 找出插件根目录：优先根层，其次唯一的下一层子目录。 */
    private fun resolvePluginRoot(staging: File): File? {
        if (File(staging, "manifest.json").isFile) return staging
        val dirs = staging.listFiles()?.filter { it.isDirectory } ?: return null
        // 顶层只有一个目录时直接用；多个目录则找第一个带 manifest 的
        dirs.firstOrNull { File(it, "manifest.json").isFile }?.let { return it }
        return dirs.singleOrNull()?.let { dir ->
            if (File(dir, "manifest.json").isFile) dir else null
        }
    }

    /** 插件 id 会变成目录名：去掉路径分隔符等危险字符。 */
    fun sanitizeDirName(id: String): String {
        // 先判空再去危险字符：否则 "   " 会先被替换成 "___"，空白检查永远不成立
        if (id.isBlank()) return "plugin"
        return id.replace(Regex("""[\\/:*?"<>|\s]"""), "_").trim().ifBlank { "plugin" }
    }

    /** 删除已安装插件目录。 */
    fun uninstall(pluginsRoot: File, pluginId: String): Boolean {
        val dir = File(pluginsRoot, sanitizeDirName(pluginId))
        if (!dir.exists()) return false
        return runCatching { dir.deleteRecursively() }.getOrDefault(false)
    }
}
