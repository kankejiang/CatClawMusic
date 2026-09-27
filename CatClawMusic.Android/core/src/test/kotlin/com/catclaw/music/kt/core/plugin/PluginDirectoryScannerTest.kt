package com.catclaw.music.kt.core.plugin

import java.io.File
import kotlinx.serialization.json.Json
import kotlinx.serialization.json.JsonElement
import org.junit.Assert.assertEquals
import org.junit.Assert.assertTrue
import org.junit.Test

/** 目录扫描 + 注册表的端到端装配。用真实临时目录，不碰 Android 也不碰网络。 */
class PluginDirectoryScannerTest {

    private fun manifestText(id: String, protocol: Int = 2, platforms: List<String> = listOf("android")): String =
        Json.encodeToString(
            JsonElement.serializer(),
            PluginManifest(
                protocolVersion = protocol, id = id, name = id, version = "1.0.0",
                platforms = platforms, capabilities = listOf("search"),
            ).toJson(),
        )

    private fun dirWith(vararg entries: Pair<String, String?>): File {
        val root = File.createTempFile("plugins", "").apply { delete(); mkdirs() }
        entries.forEach { (name, content) ->
            val dir = File(root, name).apply { mkdirs() }
            if (content != null) File(dir, "plugin.json").writeText(content)
        }
        return root
    }

    @Test
    fun findsManifestsOneLevelDeepSortedByDirectory() {
        val root = dirWith("b-second" to manifestText("b"), "a-first" to manifestText("a"))
        val found = PluginDirectoryScanner(root).readManifests()
        assertEquals(listOf("a-first", "b-second"), found.map { it.directory })
        assertTrue(found.all { it.text.isNotBlank() })
    }

    @Test
    fun looseFilesAndManifestlessDirectoriesAreNotPlugins() {
        val root = dirWith("real" to manifestText("x"))
        File(root, "README.txt").writeText("散文件不该被当成插件")
        File(root, "no-manifest").mkdirs()
        assertEquals(listOf("real"), PluginDirectoryScanner(root).readManifests().map { it.directory })
    }

    @Test
    fun missingRootYieldsNoPluginsInsteadOfCrashing() {
        val scanner = PluginDirectoryScanner(File("/definitely/not/here/catclaw"))
        assertEquals(emptyList<PluginDirectoryScanner.Found>(), scanner.readManifests())
    }

    @Test
    fun unreadableManifestIsReportedAsMalformedNotSilentlySkipped() {
        val root = dirWith("broken" to "", "good" to manifestText("ok-source"))
        val registry = pluginRegistry(root, hostPlatform = "android", hostVersion = "1.8.13")
        val scan = registry.scan()
        assertEquals(2, scan.size)
        assertEquals(ManifestProblem.Malformed, scan.first { it.problem != null }.problem)
        assertEquals(listOf("ok-source"), registry.usable().map { it.id })
    }

    @Test
    fun platformAndProtocolFiltersApplyThroughTheAssembledRegistry() {
        val root = dirWith(
            "android-one" to manifestText("android-one"),
            "windows-only" to manifestText("windows-only", platforms = listOf("windows")),
            "legacy-proto" to manifestText("legacy-proto", protocol = 1),
        )
        val registry = pluginRegistry(root, hostPlatform = "android", hostVersion = "1.8.13")
        assertEquals(listOf("android-one"), registry.usable().map { it.id })
        val problems = registry.scan().associate { it.manifest.id to it.problem }
        assertEquals(ManifestProblem.PlatformUnavailable("android"), problems["windows-only"])
        assertEquals(ManifestProblem.UnsupportedProtocol(1), problems["legacy-proto"])
    }

    @Test
    fun sameDiskLooksDifferentPerHostPlatform() {
        val root = dirWith("w" to manifestText("w", platforms = listOf("windows")))
        assertTrue(pluginRegistry(root, "android", "1.8.13").usable().isEmpty())
        assertEquals(listOf("w"), pluginRegistry(root, "windows", "1.8.13").usable().map { it.id })
    }
}
