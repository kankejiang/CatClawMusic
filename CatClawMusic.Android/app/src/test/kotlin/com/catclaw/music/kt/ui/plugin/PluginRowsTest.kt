package com.catclaw.music.kt.ui.plugin

import com.catclaw.music.kt.R
import com.catclaw.music.kt.core.plugin.pluginRegistry
import java.io.File
import org.junit.Assert.assertEquals
import org.junit.Assert.assertTrue
import org.junit.Test

/**
 * 插件管理页的行映射：走**真目录 + 真扫描器 + 真注册表**，
 * 这样"磁盘布局 → 清单解析 → 校验 → 页面行"整条链都被钉住，而不是只测我自己抄的一份映射。
 */
class PluginRowsTest {

    private fun manifest(
        id: String,
        protocol: Int = 2,
        platforms: String = "\"android\"",
        caps: String = "\"search\", \"play\"",
        minHost: String = "",
    ) = """
        {"protocolVersion": $protocol, "id": "$id", "name": "$id 显示名", "version": "1.2.0",
         "platforms": [$platforms], "capabilities": [$caps], "minHostVersion": "$minHost"}
    """.trimIndent()

    private fun rows(hostVersion: String = "1.0.0"): List<PluginRow> {
        val root = File.createTempFile("plugins", "").apply { delete(); mkdirs() }
        val manifests = mapOf(
            "good" to manifest("good", caps = "\"search\", \"play\", \"telepathy\""),
            "proto3" to manifest("proto3", protocol = 3),
            "winsonly" to manifest("winsonly", platforms = "\"windows\""),
            "broken" to "{ 这不是 JSON",
            "noid" to manifest(""),
            "needshost" to manifest("needshost", minHost = "9.9.9"),
        )
        manifests.forEach { (name, text) ->
            val dir = File(root, name).apply { mkdirs() }
            File(dir, "plugin.json").writeText(text)
        }
        return pluginRows(pluginRegistry(root, "android", hostVersion).scan())
    }

    @Test
    fun `可用的排前面，坏的一条都不吞`() {
        val rows = rows()
        assertEquals(6, rows.size)
        assertEquals(listOf(true, false, false, false, false, false), rows.map { it.usable })
    }

    @Test
    fun `可用插件的状态是「可用」，未知能力被剔掉而不是报错`() {
        val good = rows().first()
        assertEquals(R.string.plugin_status_ok, good.statusRes)
        assertEquals("good 显示名", good.name)
        // telepathy 不在 KNOWN_CAPABILITIES 里：剔除、保留其余，按字典序
        assertEquals("play, search", good.capabilities)
        assertTrue("未知能力不该让插件变不可用", good.usable)
    }

    @Test
    fun `每种坏法都映射到各自的资源键，并带上给用户看的参数`() {
        val rows = rows()
        // 「读不出 JSON」与「缺 id」两条的 id 都是空串，只能按状态键区分
        val statuses = rows.map { it.statusRes }.toSet()
        assertEquals(
            setOf(
                R.string.plugin_status_ok,
                R.string.plugin_problem_protocol,
                R.string.plugin_problem_platform,
                R.string.plugin_problem_malformed,
                R.string.plugin_problem_missing_id,
                R.string.plugin_problem_host_old,
            ),
            statuses,
        )

        val protocol = rows.first { it.statusRes == R.string.plugin_problem_protocol }
        assertEquals(listOf(3, 2), protocol.statusArgs)
        val platform = rows.first { it.statusRes == R.string.plugin_problem_platform }
        assertEquals(listOf("android"), platform.statusArgs)
        val hostOld = rows.first { it.statusRes == R.string.plugin_problem_host_old }
        assertEquals(listOf("9.9.9"), hostOld.statusArgs)
    }
}
