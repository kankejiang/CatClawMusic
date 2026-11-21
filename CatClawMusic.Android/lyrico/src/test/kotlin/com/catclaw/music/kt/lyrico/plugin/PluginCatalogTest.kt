package com.catclaw.music.kt.lyrico.plugin

import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Test

/**
 * catalog 解析、版本比对、摘要校验的单测。
 *
 * 特别注意 [parsesCurrentUpstreamReleaseManifest]：上游把 `catalog.json` 改成了
 * `release-manifest.json`，字段也少了 downloadUrl / sha256。真机验收时点「检查更新」
 * 一直失败，追下去才发现是上游改了格式 —— 这条测试用**真实上游结构**钉住它。
 */
class PluginCatalogTest {

    /** 旧格式（历史 catalog.json） */
    private val legacySample = """
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

    /** 当前上游真实结构（release-manifest.json，取自 v0.5.0 release） */
    private val currentSample = """
    {
      "generatedAt": "2026-10-19T09:22:02.305Z",
      "plugins": [
        {"id":"com.applemusic.source","name":"Apple Music","versionCode":8,"versionName":"0.4.1",
         "root":"apple","asset":"com.applemusic.source-0.4.1.zip"},
        {"id":"com.kugou.source","name":"酷狗音乐","versionCode":9,"versionName":"0.5.0",
         "root":"kugou","asset":"com.kugou.source-0.5.0.zip"},
        {"id":"com.lrcshare.source","name":"LrcShare","versionCode":13,"versionName":"1.6.2",
         "root":"lrcshare","asset":"com.lrcshare.source-1.6.2.zip"}
      ],
      "assets": [
        "com.applemusic.source-0.4.1.zip",
        "com.kugou.source-0.5.0.zip",
        "com.lrcshare.source-1.6.2.zip"
      ]
    }
    """.trimIndent()

    private val baseUrl =
        "https://github.com/Replica0110/Lyrico-Plugins/releases/download/v0.5.0"

    // ─────────────────── 当前上游格式（关键） ───────────────────

    @Test
    fun parsesCurrentUpstreamReleaseManifest() {
        val result = PluginCatalogParser.parse(currentSample, baseUrl = baseUrl)
        assertTrue("新格式必须能解析，实际=$result", result is CatalogResult.Success)
        val ok = result as CatalogResult.Success
        assertEquals("三个插件都应识别", 3, ok.plugins.size)

        val lrc = ok.plugins.first { it.id == "com.lrcshare.source" }
        assertEquals("LrcShare", lrc.name)
        assertEquals(13, lrc.versionCode)
        assertEquals("1.6.2", lrc.versionName)
        // 新格式没有 downloadUrl，要用 baseUrl + asset 拼出来
        assertEquals("$baseUrl/com.lrcshare.source-1.6.2.zip", lrc.downloadUrl)
        // 新格式没有 minHostApiVersion：按默认 1 处理，不能被判为不兼容
        assertEquals(1, lrc.minHostApiVersion)
        assertTrue("应全部可安装", ok.incompatible == 0)
    }

    @Test
    fun newFormatWithoutBaseUrlIsRejectedCleanly() {
        // 没有 baseUrl 时无法拼下载地址 —— 该条应被跳过，而不是产生一个空 URL
        val result = PluginCatalogParser.parse(currentSample, baseUrl = "")
        assertTrue("没有可拼地址时应整体报失败，实际=$result", result is CatalogResult.Failed)
    }

    @Test
    fun newFormatAcceptsAbsoluteAssetUrl() {
        val raw = """
        {"plugins":[{"id":"a","name":"A","versionCode":1,
          "asset":"https://cdn.example.com/a.zip"}]}
        """.trimIndent()
        val ok = PluginCatalogParser.parse(raw, baseUrl = "") as CatalogResult.Success
        assertEquals("https://cdn.example.com/a.zip", ok.plugins.first().downloadUrl)
    }

    // ─────────────────── 旧格式（向后兼容） ───────────────────

    @Test
    fun parsesLegacyCatalogAndFiltersIncompatible() {
        val result = PluginCatalogParser.parse(legacySample, hostApiVersion = 5)
        assertTrue(result is CatalogResult.Success)
        val ok = result as CatalogResult.Success
        assertEquals(1, ok.plugins.size)
        assertEquals(1, ok.incompatible)
        assertEquals("abc123", ok.plugins.first().sha256) // 统一转小写
    }

    @Test
    fun rejectsHigherSchemaVersion() {
        val result = PluginCatalogParser.parse("""{"schemaVersion": 2, "plugins": []}""")
        assertTrue("不认识的 schema 应整体拒绝", result is CatalogResult.Failed)
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
        {"plugins":[
          {"id":"good","name":"Good","asset":"1.zip"},
          {"name":"NoId","asset":"2.zip"},
          {"id":"noUrl","name":"NoUrl"},
          {"id":"","name":"EmptyId","asset":"3.zip"}
        ]}
        """.trimIndent()
        val ok = PluginCatalogParser.parse(raw, baseUrl = "https://x") as CatalogResult.Success
        assertEquals(listOf("good"), ok.plugins.map { it.id })
    }

    // ─────────────────── 版本比对与摘要 ───────────────────

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
        val sha = "2cf24dba5fb0a30e26e83b2ac5b9e29e1b161e5c1fa7425e73043362938b9824"
        assertTrue(PluginUpdateChecker.verify(bytes, sha))
        assertTrue("大小写不敏感", PluginUpdateChecker.verify(bytes, sha.uppercase()))
        assertFalse(PluginUpdateChecker.verify(bytes, "deadbeef"))
        // 未提供 sha256 时不阻断（但调用方要知道这是"未校验"）
        assertTrue(PluginUpdateChecker.verify(bytes, ""))
    }
}
