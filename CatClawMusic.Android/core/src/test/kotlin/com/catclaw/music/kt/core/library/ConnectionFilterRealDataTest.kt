package com.catclaw.music.kt.core.library

import org.junit.Assert.assertEquals
import org.junit.Test

/**
 * 用**真机数据库里的真实取值**验证过滤，而不是我自己编的样例。
 *
 * 真机实测：关掉 Navidrome 后音乐库从 1236 首掉到 10 首（只剩本地），
 * 而 WebDAV 那 613 首本该保留。这里的测试就是为了钉住那个组合。
 */
class ConnectionFilterRealDataTest {

    private data class Row(val id: Int, val source: Int, val protocol: Int, val title: String)

    /** 真机实测的三组行：本地 10 首 / WebDAV 613 首 / Navidrome 613 首 */
    private fun realRows(): List<Row> =
        (1..10).map { Row(it, source = 0, protocol = 0, title = "local$it") } +
            (11..623).map { Row(it, source = 1, protocol = 0, title = "dav$it") } +
            (624..1236).map { Row(it, source = 1, protocol = 1, title = "ndr$it") }

    @Test
    fun `关掉 Navidrome 后 WebDAV 的 613 首必须保留`() {
        val rows = realRows()
        // 只有 WebDAV 启用（Protocol 0 = WebDAV）
        val enabled = setOf(0)
        val out = ConnectionFilter.filterByEnabledProtocols(
            rows,
            enabled,
            sourceOf = { it.source },
            protocolOf = { it.protocol },
        )
        assertEquals("应是 10 本地 + 613 WebDAV", 623, out.size)
        assertEquals("Navidrome 的歌必须全被滤掉", 0, out.count { it.protocol == 1 && it.source == 1 })
    }

    @Test
    fun `关掉 WebDAV 后 Navidrome 的 613 首必须保留`() {
        val rows = realRows()
        val out = ConnectionFilter.filterByEnabledProtocols(
            rows,
            setOf(1),
            sourceOf = { it.source },
            protocolOf = { it.protocol },
        )
        assertEquals(623, out.size)
    }

    @Test
    fun `两条都开时 1236 首全在`() {
        val rows = realRows()
        val out = ConnectionFilter.filterByEnabledProtocols(
            rows,
            setOf(0, 1),
            sourceOf = { it.source },
            protocolOf = { it.protocol },
        )
        assertEquals(1236, out.size)
    }

    @Test
    fun `enabledProtocols 从真实连接列表算出集合`() {
        val webdav = com.catclaw.music.kt.core.model.ConnectionProfile(
            name = "webdav",
            protocol = com.catclaw.music.kt.core.model.ProtocolType.WebDAV,
            isEnabled = true,
        )
        val ndrOff = com.catclaw.music.kt.core.model.ConnectionProfile(
            name = "NDR",
            protocol = com.catclaw.music.kt.core.model.ProtocolType.Navidrome,
            isEnabled = false,
        )
        assertEquals(setOf(0), ConnectionFilter.enabledProtocols(listOf(webdav, ndrOff)))
    }
}
