package com.catclaw.music.kt.core.library

import com.catclaw.music.kt.core.model.ConnectionProfile
import com.catclaw.music.kt.core.model.ProtocolType
import org.junit.Assert.assertEquals
import org.junit.Assert.assertTrue
import org.junit.Test

/**
 * 「停用连接 → 它的歌不显示」的语义测试。
 *
 * 这套规则的每一处写错都是**静默**的：不是崩溃，而是「有些歌莫名消失了」
 * 或「明明关了连接歌还在」。两种都很难从截图看出原因，所以逐条钉住。
 *
 * 语义对齐 MAUI `MusicDatabase.FilterByEnabledProtocols`。
 */
class ConnectionFilterTest {

    private data class Row(val id: Int, val source: Int, val protocol: Int)

    private fun profile(protocol: ProtocolType, enabled: Boolean) = ConnectionProfile(
        name = "p-${protocol.name}",
        protocol = protocol,
        isEnabled = enabled,
    )

    private val rows = listOf(
        Row(1, source = 0, protocol = 0),   // 本地曲
        Row(2, source = 1, protocol = 0),   // WebDAV 远程曲
        Row(3, source = 1, protocol = 1),   // Navidrome 远程曲
        Row(4, source = 2, protocol = 2),   // SMB 远程曲
        Row(5, source = 3, protocol = 0),   // 缓存曲
    )

    private fun filter(protocols: Set<Int>) = ConnectionFilter.filterByEnabledProtocols(
        rows,
        protocols,
        sourceOf = { it.source },
        protocolOf = { it.protocol },
    ).map { it.id }

    @Test
    fun `全部连接启用时五首都在`() {
        val all = setOf(ProtocolType.WebDAV.ordinal, ProtocolType.Navidrome.ordinal, ProtocolType.SMB.ordinal)
        assertEquals(listOf(1, 2, 3, 4, 5), filter(all))
    }

    @Test
    fun `停用 WebDAV 后它的远程曲消失，本地与缓存仍在`() {
        val remain = setOf(ProtocolType.Navidrome.ordinal, ProtocolType.SMB.ordinal)
        // 2 是 WebDAV 远程曲 → 消失；1（本地）与 5（缓存）虽然 protocol 也是 0，但必须保留
        assertEquals(listOf(1, 3, 4, 5), filter(remain))
    }

    @Test
    fun `所有连接都停用时只剩本地与缓存`() {
        assertEquals(listOf(1, 5), filter(emptySet()))
    }

    @Test
    fun `缓存曲不受连接影响`() {
        // 缓存曲 source=Cache(3)，即使 protocol 对应的连接全关也显示
        assertTrue(ConnectionFilter.isConnectionFree(source = 3))
    }

    @Test
    fun `本地曲不受连接影响`() {
        assertTrue(ConnectionFilter.isConnectionFree(source = 0))
    }

    @Test
    fun `远程曲不豁免`() {
        assertTrue(!ConnectionFilter.isConnectionFree(source = 1))
        assertTrue(!ConnectionFilter.isConnectionFree(source = 2))
    }

    @Test
    fun `enabledProtocols 只收启用的连接`() {
        val protocols = ConnectionFilter.enabledProtocols(
            listOf(
                profile(ProtocolType.WebDAV, enabled = true),
                profile(ProtocolType.Navidrome, enabled = false),
                profile(ProtocolType.SMB, enabled = true),
            ),
        )
        assertEquals(setOf(ProtocolType.WebDAV.ordinal, ProtocolType.SMB.ordinal), protocols)
    }

    @Test
    fun `同协议两条连接只停一条时该协议仍算启用`() {
        // 这是「按协议而非按连接」的已知取舍：精确到连接需要给 Songs 加列
        val protocols = ConnectionFilter.enabledProtocols(
            listOf(
                profile(ProtocolType.WebDAV, enabled = false),
                profile(ProtocolType.WebDAV, enabled = true),
            ),
        )
        assertEquals(setOf(ProtocolType.WebDAV.ordinal), protocols)
    }

    @Test
    fun `没有连接时哪些都不显示`() {
        val protocols = ConnectionFilter.enabledProtocols(emptyList())
        assertEquals(emptySet<Int>(), protocols)
    }

    @Test
    fun `空列表直接返回不炸`() {
        val out = ConnectionFilter.filterByEnabledProtocols(
            emptyList<Row>(),
            emptySet(),
            sourceOf = { it.source },
            protocolOf = { it.protocol },
        )
        assertTrue(out.isEmpty())
    }
}
