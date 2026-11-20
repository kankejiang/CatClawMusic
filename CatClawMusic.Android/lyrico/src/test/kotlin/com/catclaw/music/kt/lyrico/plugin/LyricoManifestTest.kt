package com.catclaw.music.kt.lyrico.plugin

import org.junit.Assert.assertEquals
import org.junit.Assert.assertNotNull
import org.junit.Assert.assertTrue
import org.junit.Test

/** manifest 解析与宿主纯逻辑的单测（不需要设备）。 */
class LyricoManifestTest {

    private val sample = """
    {
      "id": "com.lrcshare.source",
      "name": "LrcShare",
      "versionCode": 13,
      "versionName": "1.6.2",
      "author": "X2ISPANDA",
      "description": "@plugin.description",
      "apiVersion": 5,
      "entry": "source.js",
      "includeDirs": ["lib"],
      "icon": "icon.png",
      "capabilities": ["searchSongs", "getLyrics", "searchCovers"],
      "configFields": [
        {"key":"enrich","title":"@config.enrich.title","type":"switch","defaultValue":"true"}
      ],
      "minHostApiVersion": 4,
      "i18n": {"defaultLocale":"en","resources":{"en":"locales/en.json","zh-Hans":"locales/zh-Hans.json"}},
      "updateUrl": "https://example.com/catalog.json"
    }
    """.trimIndent()

    @Test
    fun parsesRealShapeManifest() {
        val m = LyricoManifest.parse(sample)
        assertNotNull("manifest 应能解析（解析失败通常意味着缺 kotlinx-serialization 编译插件）", m)
        m!!
        assertEquals("com.lrcshare.source", m.id)
        assertEquals("LrcShare", m.name)
        assertEquals(5, m.apiVersion)
        assertEquals(4, m.minHostApiVersion)
        assertEquals("source.js", m.entry)
        assertEquals(listOf("lib"), m.includeDirs)
        assertTrue(m.hasGetLyrics)
        assertTrue(m.hasSearchCovers)
        assertEquals(1, m.configFields.size)
        // i18n.resources 是「语言 → 资源文件相对路径」
        assertEquals("locales/zh-Hans.json", m.i18n?.resources?.get("zh-Hans"))
    }

    @Test
    fun toleratesUnknownFieldsAndMissingOptionals() {
        val minimal = """{"id":"x","name":"X"}"""
        val m = LyricoManifest.parse(minimal)
        assertNotNull(m)
        assertEquals("source.js", m!!.entry)
        assertTrue("没声明 capabilities 时视为支持全部", m.hasGetLyrics)
    }

    @Test
    fun invalidJsonReturnsNull() {
        assertEquals(null, LyricoManifest.parse("not json"))
    }
}
