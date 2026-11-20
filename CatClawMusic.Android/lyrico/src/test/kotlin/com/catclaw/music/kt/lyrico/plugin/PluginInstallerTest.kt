package com.catclaw.music.kt.lyrico.plugin

import java.io.ByteArrayOutputStream
import java.io.File
import java.util.zip.ZipEntry
import java.util.zip.ZipOutputStream
import org.junit.After
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertNotNull
import org.junit.Assert.assertTrue
import org.junit.Test

/**
 * 插件安装器的单测。
 *
 * 重点不是「能装上」，而是**装上不该装的东西要拒绝**：ZIP 是外部下载物，
 * 路径穿越与 zip bomb 都必须在这一层挡住。
 */
class PluginInstallerTest {

    private val root: File = File(System.getProperty("java.io.tmpdir"), "lyrico-installer-test-${System.nanoTime()}")

    @After
    fun cleanup() {
        runCatching { root.deleteRecursively() }
    }

    private fun zip(vararg entries: Pair<String, ByteArray>): ByteArray {
        val out = ByteArrayOutputStream()
        ZipOutputStream(out).use { zos ->
            entries.forEach { (name, bytes) ->
                zos.putNextEntry(ZipEntry(name))
                zos.write(bytes)
                zos.closeEntry()
            }
        }
        return out.toByteArray()
    }

    private fun manifest(id: String = "com.test.source", entry: String = "source.js"): ByteArray =
        """
        {"id":"$id","name":"Test Source","versionName":"1.0.0","entry":"$entry",
         "apiVersion":5,"minHostApiVersion":4,"capabilities":["getLyrics"]}
        """.trimIndent().toByteArray()

    @Test
    fun installsValidPlugin() {
        val bytes = zip(
            "manifest.json" to manifest(),
            "source.js" to "function getLyrics(req){ return []; }".toByteArray(),
            "lib/helper.js" to "var x = 1;".toByteArray(),
        )
        val result = PluginInstaller.install(bytes.inputStream(), root)
        assertTrue("应安装成功，实际=$result", result is PluginInstallOutcome.Success)
        val ok = result as PluginInstallOutcome.Success
        assertEquals("com.test.source", ok.pluginId)
        assertEquals("Test Source", ok.displayName)
        assertTrue(File(root, "com.test.source/source.js").isFile)
        assertTrue(File(root, "com.test.source/lib/helper.js").isFile)
    }

    @Test
    fun installsPluginNestedInSingleDirectory() {
        // 不少包是 <name>/manifest.json 这种结构
        val bytes = zip(
            "mysource/manifest.json" to manifest("com.nested.source"),
            "mysource/source.js" to "function getLyrics(){return []}".toByteArray(),
        )
        val result = PluginInstaller.install(bytes.inputStream(), root)
        assertTrue("嵌套一层也应能装，实际=$result", result is PluginInstallOutcome.Success)
        assertTrue(File(root, "com.nested.source/source.js").isFile)
    }

    @Test
    fun rejectsPathTraversal() {
        val bytes = zip(
            "manifest.json" to manifest(),
            "source.js" to "x".toByteArray(),
            "../../evil.js" to "pwned".toByteArray(),
        )
        val result = PluginInstaller.install(bytes.inputStream(), root)
        assertTrue("路径穿越必须拒绝，实际=$result", result is PluginInstallOutcome.Rejected)
        // 确认真的没写到外面
        assertFalse(File(root.parentFile, "evil.js").exists())
    }

    @Test
    fun rejectsAbsolutePath() {
        val bytes = zip(
            "manifest.json" to manifest(),
            "/data/local/tmp/evil.js" to "pwned".toByteArray(),
        )
        val result = PluginInstaller.install(bytes.inputStream(), root)
        assertTrue("绝对路径必须拒绝，实际=$result", result is PluginInstallOutcome.Rejected)
    }

    @Test
    fun skipsDisallowedExtensions() {
        val bytes = zip(
            "manifest.json" to manifest(),
            "source.js" to "ok".toByteArray(),
            "lib/evil.so" to ByteArray(64),
            "run.sh" to "#!/bin/sh".toByteArray(),
        )
        val result = PluginInstaller.install(bytes.inputStream(), root)
        assertTrue(result is PluginInstallOutcome.Success)
        assertFalse("原生库不该落地", File(root, "com.test.source/lib/evil.so").exists())
        assertFalse("脚本不该落地", File(root, "com.test.source/run.sh").exists())
    }

    @Test
    fun rejectsMissingManifest() {
        val bytes = zip("source.js" to "x".toByteArray())
        val result = PluginInstaller.install(bytes.inputStream(), root)
        assertTrue("缺 manifest 应判无效，实际=$result", result is PluginInstallOutcome.Invalid)
    }

    @Test
    fun rejectsMissingEntryScript() {
        val bytes = zip(
            "manifest.json" to manifest(entry = "index.js"),
            "source.js" to "x".toByteArray(),
        )
        val result = PluginInstaller.install(bytes.inputStream(), root)
        assertTrue("缺入口脚本应判无效，实际=$result", result is PluginInstallOutcome.Invalid)
    }

    @Test
    fun rejectsIncompatibleHostApi() {
        val m = """
            {"id":"com.future.source","name":"Future","entry":"source.js","minHostApiVersion":99}
        """.trimIndent().toByteArray()
        val bytes = zip(
            "manifest.json" to m,
            "source.js" to "x".toByteArray(),
        )
        val result = PluginInstaller.install(bytes.inputStream(), root)
        assertTrue("宿主 API 不兼容应拒绝，实际=$result", result is PluginInstallOutcome.Invalid)
    }

    @Test
    fun replacesExistingVersion() {
        val v1 = zip(
            "manifest.json" to manifest(),
            "source.js" to "v1".toByteArray(),
        )
        assertTrue(PluginInstaller.install(v1.inputStream(), root) is PluginInstallOutcome.Success)
        assertEquals("v1", File(root, "com.test.source/source.js").readText())

        val v2 = zip(
            "manifest.json" to manifest(),
            "source.js" to "v2".toByteArray(),
        )
        assertTrue(PluginInstaller.install(v2.inputStream(), root) is PluginInstallOutcome.Success)
        assertEquals("同名插件应覆盖为新版本", "v2", File(root, "com.test.source/source.js").readText())
    }

    @Test
    fun uninstallRemovesDirectory() {
        val bytes = zip(
            "manifest.json" to manifest(),
            "source.js" to "x".toByteArray(),
        )
        PluginInstaller.install(bytes.inputStream(), root)
        assertTrue(File(root, "com.test.source").exists())
        assertTrue(PluginInstaller.uninstall(root, "com.test.source"))
        assertFalse(File(root, "com.test.source").exists())
        // 再删一次返回 false，不抛
        assertFalse(PluginInstaller.uninstall(root, "com.test.source"))
    }

    @Test
    fun sanitizesDangerousDirectoryNames() {
        assertEquals("a_b_c", PluginInstaller.sanitizeDirName("a/b\\c"))
        assertEquals("plugin", PluginInstaller.sanitizeDirName("   "))
        // 正常 id 不变
        assertEquals("com.lrcshare.source", PluginInstaller.sanitizeDirName("com.lrcshare.source"))
    }

    @Test
    fun installedPluginIsLoadable() {
        val bytes = zip(
            "manifest.json" to manifest(),
            "source.js" to "function getLyrics(){ return []; }".toByteArray(),
            "locales/zh-Hans.json" to """{"plugin.name":"测试源"}""".toByteArray(),
        )
        PluginInstaller.install(bytes.inputStream(), root)
        val plugin = LyricoPluginLoader.loadFromDirectory(File(root, "com.test.source"))
        assertNotNull("装完后应能被加载器读出", plugin)
        assertEquals("com.test.source", plugin!!.id)
        assertNotNull("入口脚本应在内存里", plugin.buildCompositeScript())
    }
}
