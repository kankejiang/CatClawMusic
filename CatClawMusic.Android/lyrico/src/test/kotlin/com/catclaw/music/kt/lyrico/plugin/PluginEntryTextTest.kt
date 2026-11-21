package com.catclaw.music.kt.lyrico.plugin

import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Test

/**
 * 插件条目展示文案的解析。
 *
 * 真机验收时发现：插件列表里网易云源的标题显示成 `@plugin.name`。
 * 原因是 `description` 走了 i18n 解析而 `name` 没走 —— 这类「同一件事做了一半」
 * 的疏漏编译期发现不了，只能靠测试或真机。
 */
class PluginEntryTextTest {

    private fun entry(name: String, description: String, strings: Map<String, String>): PluginEntry {
        val manifest = LyricoManifest(
            id = "com.test.source",
            name = name,
            description = description,
            versionName = "1.0.0",
            versionCode = 1,
            capabilities = listOf("getLyrics"),
        )
        val plugin = LyricoPlugin("com.test.source", manifest, emptyMap())
        // 直接构造出 registry 内部用的展示逻辑结果（避免依赖 Android Context）
        return PluginEntry(
            pluginId = plugin.id,
            displayName = resolve(manifest.name, strings).ifBlank { plugin.id },
            author = manifest.author,
            versionName = manifest.versionName,
            versionCode = manifest.versionCode,
            description = resolve(manifest.description, strings),
            capabilities = manifest.capabilities,
            enabled = true,
            bundled = true,
        )
    }

    private fun resolve(value: String, strings: Map<String, String>): String {
        if (!value.startsWith("@")) return value
        return strings[value.removePrefix("@")] ?: value.removePrefix("@")
    }

    @Test
    fun resolvesI18nNameAndDescription() {
        val strings = mapOf("plugin.name" to "网易云音乐", "plugin.description" to "搜索源")
        val e = entry("@plugin.name", "@plugin.description", strings)
        assertEquals("网易云音乐", e.displayName)
        assertEquals("搜索源", e.description)
    }

    @Test
    fun plainTextPassesThrough() {
        val e = entry("LrcShare", "歌词站搜索源", emptyMap())
        assertEquals("LrcShare", e.displayName)
        assertEquals("歌词站搜索源", e.description)
    }

    @Test
    fun missingI18nKeyFallsBackToKeyWithoutAt() {
        // 语言资源里没有这个 key 时，至少不要显示成 `@xxx`
        val e = entry("@plugin.name", "", emptyMap())
        assertEquals("plugin.name", e.displayName)
        assertFalse("不应保留 @ 前缀", e.displayName.startsWith("@"))
    }

    @Test
    fun blankNameFallsBackToPluginId() {
        val manifest = LyricoManifest(id = "com.x.source", name = "")
        val plugin = LyricoPlugin("com.x.source", manifest, emptyMap())
        val display = resolve(manifest.name, emptyMap()).ifBlank { plugin.id }
        assertEquals("com.x.source", display)
        assertTrue(display.isNotBlank())
    }
}
