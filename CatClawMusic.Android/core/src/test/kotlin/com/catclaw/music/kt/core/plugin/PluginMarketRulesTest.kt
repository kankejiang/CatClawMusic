package com.catclaw.music.kt.core.plugin

import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Test

class PluginMarketRulesTest {

    /** 原始字符串里 `$` 照样插值，而原始字符串又不认 `\$` 转义 —— 只能让它插一个常量 */
    private companion object {
        const val META = "\$meta"
    }

    private val sample = """
        {
          "${PluginMarketRulesTest.META}": { "name": "猫爪插件市场", "version": 1 },
          "kankejiang/netdisk": {
            "name": "netdisk",
            "display_name": "网盘音源",
            "author": "kankejiang",
            "version": "1.2.0",
            "short_desc": "接网盘",
            "desc": "把网盘挂成音源",
            "repo": "https://github.com/kankejiang/x",
            "download_url": "https://example.com/netdisk.ccp",
            "sha256": "abc",
            "stars": 12,
            "updated_at": "2026-01-01T00:00:00Z",
            "platforms": ["android", "windows"],
            "tags": ["source"]
          },
          "someone/plain": { "name": "plain", "author": "someone", "version": "0.9" },
          "someone/broken": "这不是对象"
        }
    """.trimIndent()

    /**
     * **真清单**（2026-09-13 拉取的 `index.json`，`desc` 为可读性截短，其余逐字）。
     *
     * 为什么值得钉住：合成样本会漏掉真实清单里的字段（`$meta.schema_version`、条目上的`host_version`、
     * 数字型的 `download_size`），而解析器对未知字段的态度恰恰是「忽略」—— 一旦哪天改成抛异常，
     * 合成样本照样绿，真机上却是整个市场打不开。
     *
     * 顺带记下一件事：这份清单里的 `download_url` **全是 `.ccp`（.NET 宿主的程序集）**，
     * 且 `host_version` 要求 `>=1.8.0`（那是 .NET 应用的版本号），全清单没有任何 v2 协议字段。
     * 也就是说它是 **.NET 宿主的市场**，本端（进程外 v2 插件）装不了 —— 见 `docs/android-port-decisions.md`。
     */
    private val realIndex = """
        {
          "${'$'}meta": {
            "schema_version": 1,
            "name": "CatClawMusic 官方插件市场",
            "version": "2026-08-27",
            "homepage": "https://github.com/kankejiang/CatClawMusic",
            "repository": "https://github.com/kankejiang/CatClawMusic.PluginMarket",
            "description": "猫爪音乐官方插件市场清单，全 GitHub 托管、零服务器",
            "updated_at": "2026-09-13T08:14:00Z"
          },
          "CatClawMusic/lrclib": {
            "author": "CatClawMusic",
            "name": "lrclib",
            "display_name": "LRCLIB 歌词",
            "short_desc": "LRCLIB 在线歌词与多源兜底",
            "version": "1.2.0.9",
            "repo": "https://github.com/kankejiang/CatClawMusic.Plugins.Lrclib",
            "desc": "LRCLIB 开放歌词库 + Lyrico 多源歌词兜底",
            "tags": ["lyrics", "music-library"],
            "platforms": ["android", "windows"],
            "host_version": ">=1.8.0",
            "updated_at": "2026-09-13T08:06:44Z",
            "stars": 0,
            "download_url": "https://github.com/kankejiang/CatClawMusic.Plugins.Lrclib/releases/download/v1.2.0.9/CatClawMusic.Plugins.Lrclib.ccp",
            "sha256": "534c1070932f83d022d5f3c85209aabd73991d8fe6a82d37ce1ada6f1b5049cd",
            "download_size": 515584
          },
          "CatClawMusic/lxSource": {
            "author": "CatClawMusic",
            "name": "lxSource",
            "display_name": "LX 源音乐",
            "version": "0.4.4",
            "repo": "https://github.com/kankejiang/CatClawMusic.Plugins.LxSource",
            "tags": ["online-music", "lyrics", "script"],
            "platforms": ["android", "windows"],
            "host_version": ">=1.8.0",
            "stars": 1,
            "download_url": "https://github.com/kankejiang/CatClawMusic.Plugins.LxSource/releases/download/v0.4.4/CatClawMusic.Plugins.LxSource.ccp",
            "sha256": "aaaa"
          }
        }
    """.trimIndent()

    @Test
    fun `真清单里的未知字段不影响解析`() {
        val index = PluginMarketRules.parseIndex(realIndex)!!
        assertEquals("CatClawMusic 官方插件市场", index.name)
        assertEquals(2, index.plugins.size)
        val lrclib = index.plugins.first { it.name == "lrclib" }
        assertEquals("CatClawMusic/lrclib", lrclib.pluginId)
        assertEquals("LRCLIB 歌词", lrclib.displayName)
        assertEquals("1.2.0.9", lrclib.version)
        assertEquals(0, lrclib.stars)
        assertTrue("sha256 要原样带出来", lrclib.sha256.startsWith("534c1070"))
        assertTrue("download_url 原样带出来", lrclib.downloadUrl.endsWith(".ccp"))
        assertEquals(listOf("lyrics", "music-library"), lrclib.tags)
        // 数字型的 download_size 与未知的 host_version 都该被忽略，但已知的 updated_at 要照常解出来
        assertEquals("2026-09-13T08:06:44Z", lrclib.updatedAt)
    }
    @Test
    fun `解析市场名与条目`() {
        val index = PluginMarketRules.parseIndex(sample)!!
        assertEquals("猫爪插件市场", index.name)
        assertEquals(2, index.plugins.size)
        val netdisk = index.plugins.first { it.name == "netdisk" }
        assertEquals("kankejiang/netdisk", netdisk.pluginId)
        assertEquals("网盘音源", netdisk.displayName)
        assertEquals("1.2.0", netdisk.version)
        assertEquals(12, netdisk.stars)
        assertEquals(listOf("android", "windows"), netdisk.platforms)
        assertEquals(listOf("source"), netdisk.tags)
        assertEquals("https://example.com/netdisk.ccp", netdisk.downloadUrl)
    }

    @Test
    fun `缺 display_name 时回退 name、缺 stars 记 -1`() {
        val plain = PluginMarketRules.parseIndex(sample)!!.plugins.first { it.name == "plain" }
        assertEquals("回退到 name", "plain", plain.displayName)
        assertEquals(-1, plain.stars)
        assertEquals(emptyList<String>(), plain.platforms)
    }

    @Test
    fun `非对象条目被跳过而不是让整份清单失败`() {
        // 一个作者写坏一个条目，不该让所有人的市场都打不开
        val index = PluginMarketRules.parseIndex(sample)!!
        assertFalse(index.plugins.any { it.pluginId == "someone/broken" })
    }

    @Test
    fun `非法 JSON 返回 null 而不是抛异常`() {
        assertNull(PluginMarketRules.parseIndex("<html>502</html>"))
        assertNull(PluginMarketRules.parseIndex(""))
    }

    @Test
    fun `没有 meta 时用默认市场名`() {
        val index = PluginMarketRules.parseIndex("{}")!!
        assertEquals(PluginMarketRules.DEFAULT_MARKET_NAME, index.name)
        assertTrue(index.plugins.isEmpty())
    }

    @Test
    fun `候选源顺序：SHA 钉定在前、raw 兜底在后`() {
        val plain = PluginMarketRules.indexUrls()
        assertEquals(2, plain.size)
        assertTrue(plain[0].contains("@main"))
        assertTrue(plain[1].startsWith("https://raw.githubusercontent.com/"))
        val pinned = PluginMarketRules.indexUrls("abc123")
        assertEquals(3, pinned.size)
        assertTrue("钉定的 SHA 必须排在第一个", pinned[0].contains("@abc123"))
        assertEquals("没有 SHA 时不该凭空多出一个空地址", plain, PluginMarketRules.indexUrls(""))
    }

    @Test
    fun `HEAD SHA 取 sha 字段，取不到给空串`() {
        assertEquals("deadbeef", PluginMarketRules.headShaFrom("{ \"sha\": \"deadbeef\" }"))
        assertEquals("", PluginMarketRules.headShaFrom("{ \"message\": \"Not Found\" }"))
        assertEquals("", PluginMarketRules.headShaFrom("rate limited"))
    }

    @Test
    fun `已安装判据是作者加插件名、忽略大小写`() {
        val item = PluginMarketRules.parseIndex(sample)!!.plugins.first { it.name == "netdisk" }
        assertTrue(PluginMarketRules.isInstalled(item, "KankeJiang", "NetDisk"))
        assertFalse("作者不同不算已装", PluginMarketRules.isInstalled(item, "other", "netdisk"))
        assertFalse(PluginMarketRules.isInstalled(item, "kankejiang", "other"))
    }

    @Test
    fun `版本比较按段走，带 v 前缀与短版本都要能比`() {
        assertTrue(PluginMarketRules.isNewerVersion("1.2.1", "1.2.0"))
        assertTrue(PluginMarketRules.isNewerVersion("1.3", "1.2.9"))
        assertTrue(PluginMarketRules.isNewerVersion("2", "1.9.9"))
        assertTrue("短的一侧缺段按 0", PluginMarketRules.isNewerVersion("1.2", "1.1.9"))
        assertTrue(PluginMarketRules.isNewerVersion("v1.0.1", "1.0.0"))
        assertFalse("相同版本不算新", PluginMarketRules.isNewerVersion("1.2.0", "1.2.0"))
        assertFalse(PluginMarketRules.isNewerVersion("1.2.0", "1.2.1"))
        assertFalse("1.0 与 v1.0 同版本", PluginMarketRules.isNewerVersion("1.0", "v1.0"))
        assertFalse("非数字段按 0：预发布后缀不该被当成更新", PluginMarketRules.isNewerVersion("1.2.0-beta", "1.2.0"))
    }
}
