package com.catclaw.music.kt.lyrico.plugin

import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Test

/** catalog 解析、版本比对、摘要校验的单测。 */
class PluginCatalogTest {

    private val sample = """
    {
      "schemaVersion": 1,
      "plugins": [
        {"id":"com.lrcshare.source","name":"LrcShare","versionCode":13,"versionName":"1.6.2",
         "apiVersion":5,"minHostApiVersion":4,
         "downloadUrl":"https://example.com/lrcshare-1.6.2.zip",
         "sha256":"ABC123","size":2048,"changelog":"fix"},
        {"id":"com.future.source","name":"Future","versionCode":99,"versionName":"9.0",
         "minHostApiVersion":99,
         "downloadUrl":"https://example.com/future.zip","sha256":"","size":10}
      ]
    }
    """.trimIndent()

    @Test
    fun parsesCatalogAndFiltersIncompatible() {
        val result = PluginCatalogParser.parse(sample, hostApiVersion = 5)
        assertTrue("应解析成功，实际=$result", result is CatalogResult.Success)
        val ok = result as CatalogResult.Success
        assertEquals("只保留兼容的插件", 1, ok.plugins.size)
        assertEquals(1, ok.incompatible)
        val p = ok.plugins.first()
        assertEquals("com.lrcshare.source", p.id)
        assertEquals(13, p.versionCode)
        assertEquals("1.6.2", p.versionName)
        // sha256 统一转小写，便于比对
        assertEquals("abc123", p.sha256)
    }

    @Test
    fun rejectsHigherSchemaVersion() {
        val raw = """{"schemaVersion": 2, "plugins": []}"""
        val result = PluginCatalogParser.parse(raw)
        assertTrue("不认识的 schema 应整体拒绝，实际=$result", result is CatalogResult.Failed)
    }

    @Test
    fun rejectsMissingPluginsArray() {
        assertTrue(PluginCatalogParser.parse("""{"schemaVersion":1}""") is CatalogResult.Failed)
        assertTrue(PluginCatalogParser.parse("not json") is CatalogResult.Failed)
        assertTrue(PluginCatalogParser.parse("") is CatalogResult.Failed)
    }

    @Test
    fun skipsBrokenEntriesButKeepsRest() {
        val raw = """
        {"schemaVersion":1,"plugins":[
          {"id":"good","name":"Good","downloadUrl":"https://x/1.zip"},
          {"name":"NoId","downloadUrl":"https://x/2.zip"},
          {"id":"noUrl","name":"NoUrl"},
          {"id":"","name":"EmptyId","downloadUrl":"https://x/3.zip"}
        ]}
        """.trimIndent()
        val ok = PluginCatalogParser.parse(raw) as CatalogResult.Success
        assertEquals("坏条目跳过，好条目保留", listOf("good"), ok.plugins.map { it.id })
    }

    @Test
    fun updateStatusReflectsVersionCode() {
        val plugin = CatalogPlugin(
            id = "a", name = "A", versionCode = 5, versionName = "1.1",
            apiVersion = 5, minHostApiVersion = 1,
            downloadUrl = "https://x", sha256 = "", size = 0, changelog = "",
        )
        assertEquals(PluginUpdateStatus.NOT_INSTALLED, PluginUpdateChecker.statusOf(plugin, null))
        assertEquals(PluginUpdateStatus.UP_TO_DATE, PluginUpdateChecker.statusOf(plugin, 5))
        assertEquals(PluginUpdateStatus.UP_TO_DATE, PluginUpdateChecker.statusOf(plugin, 6))
        assertEquals(PluginUpdateStatus.UPDATE_AVAILABLE, PluginUpdateChecker.statusOf(plugin, 4))
    }

    @Test
    fun verifiesChecksumWhenProvided() {
        val bytes = "hello".toByteArray()
        // SHA-256("hello")
        val sha = "2cf24dba5fb0a30e26e83b2ac5b9e29e1b161e5c1fa7425e73043362938b9824"
        assertTrue(PluginUpdateChecker.verify(bytes, sha))
        assertTrue("大小写不敏感", PluginUpdateChecker.verify(bytes, sha.uppercase()))
        assertFalse(PluginUpdateChecker.verify(bytes, "deadbeef"))
        // 未提供 sha256 时不阻断（但调用方要知道这是"未校验"）
        assertTrue(PluginUpdateChecker.verify(bytes, ""))
    }
}
