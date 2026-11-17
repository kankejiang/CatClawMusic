package com.catclaw.music.kt.core.library

import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Test

/**
 * 「网络服务来源」筛选的语义测试。
 *
 * 这个筛选**没有「全部服务」档**：一次只允许一个网络服务生效。
 * 把两个服务混在一起恰恰是它要解决的问题（同一份文件两个协议各导一遍，歌名完全一样）。
 *
 * 想看"不分服务的全部网络曲"，用上级的 `SongSourceFilter.Network`。
 */
class NetworkProtocolFilterTest {

    private data class Row(val id: Int, val source: Int, val protocol: Int)

    /** 真机实测：本地 10 / WebDAV 613(protocol=0) / Navidrome 613(protocol=1) */
    private val rows = listOf(
        Row(1, source = 0, protocol = 0),   // 本地曲（protocol 字段无意义）
        Row(2, source = 1, protocol = 0),   // WebDAV 远程曲
        Row(3, source = 1, protocol = 1),   // Navidrome 远程曲
        Row(4, source = 3, protocol = 0),   // 缓存曲
    )

    private fun filter(mode: NetworkProtocolFilter): List<Int> = filterByNetworkProtocol(
        rows,
        mode,
        protocolOf = { it.protocol },
        sourceOf = { it.source },
    ).map { it.id }

    @Test
    fun `没有 全部服务 这一档`() {
        // 枚举里只有三个具体服务 —— 加回 All 会让"一次只有一个生效"失去保证
        assertEquals(
            listOf(
                NetworkProtocolFilter.WebDAV,
                NetworkProtocolFilter.Navidrome,
                NetworkProtocolFilter.SMB,
            ),
            NetworkProtocolFilter.entries.toList(),
        )
    }

    @Test
    fun `WebDAV 只留 WebDAV 远程曲`() {
        assertEquals(listOf(2), filter(NetworkProtocolFilter.WebDAV))
    }

    @Test
    fun `Navidrome 只留 Navidrome 远程曲`() {
        assertEquals(listOf(3), filter(NetworkProtocolFilter.Navidrome))
    }

    @Test
    fun `两个服务的结果互斥且合起来等于全部网络曲`() {
        val dav = filter(NetworkProtocolFilter.WebDAV)
        val ndr = filter(NetworkProtocolFilter.Navidrome)
        // 互斥：同一首歌不会同时出现在两个服务下
        assertTrue(dav.intersect(ndr.toSet()).isEmpty())
        // 合起来 = 全部远程曲（不含本地/缓存）
        val allRemote = rows.filter { it.source != 0 && it.source != 3 }.map { it.id }
        assertEquals(allRemote.toSet(), (dav + ndr).toSet())
    }

    @Test
    fun `没有该协议的曲时结果为空而不是全留`() {
        // 真机没有 SMB 曲；筛 SMB 应得空列表
        assertTrue(filter(NetworkProtocolFilter.SMB).isEmpty())
    }

    @Test
    fun `本地曲永远不匹配任何具体服务`() {
        for (mode in NetworkProtocolFilter.entries) {
            assertFalse("$mode 不应匹配本地曲", mode.accepts(protocol = 0, source = 0))
        }
    }

    @Test
    fun `缓存曲永远不匹配任何具体服务`() {
        // 缓存曲 source=3，即使 protocol 字段是 0 也不该被 WebDAV 选中
        for (mode in NetworkProtocolFilter.entries) {
            assertFalse("$mode 不应匹配缓存曲", mode.accepts(protocol = 0, source = 3))
        }
    }

    @Test
    fun `of 按 ProtocolType ordinal 反查`() {
        // ProtocolType { WebDAV(0), Navidrome(1), SMB(2) }
        assertEquals(NetworkProtocolFilter.WebDAV, NetworkProtocolFilter.of(0))
        assertEquals(NetworkProtocolFilter.Navidrome, NetworkProtocolFilter.of(1))
        assertEquals(NetworkProtocolFilter.SMB, NetworkProtocolFilter.of(2))
    }

    @Test
    fun `of 遇到未知 ordinal 回落到 WebDAV 而不是抛异常`() {
        assertEquals(NetworkProtocolFilter.WebDAV, NetworkProtocolFilter.of(99))
        assertEquals(NetworkProtocolFilter.WebDAV, NetworkProtocolFilter.of(-1))
    }

    @Test
    fun `与 SongSourceFilter 叠加：网络 + Navidrome`() {
        val afterSource = filterBySource(rows, SongSourceFilter.Network) { it.source }
        val result = filterByNetworkProtocol(
            afterSource,
            NetworkProtocolFilter.Navidrome,
            protocolOf = { it.protocol },
            sourceOf = { it.source },
        ).map { it.id }
        assertEquals(listOf(3), result)
    }

    @Test
    fun `与 SongSourceFilter 叠加：本地 应跳过网络服务筛选`() {
        // ViewModel 在 source=Local 时**不做**网络服务筛选（否则本地曲库会恒空）。
        // 这里钉住"如果硬套上去会变成空"这个事实，防止有人把跳过那一段删掉。
        val afterSource = filterBySource(rows, SongSourceFilter.Local) { it.source }
        assertEquals(listOf(1L), afterSource.map { it.id.toLong() }) // 本地确实有 1 首
        val forced = filterByNetworkProtocol(
            afterSource,
            NetworkProtocolFilter.WebDAV,
            protocolOf = { it.protocol },
            sourceOf = { it.source },
        )
        assertTrue("硬套网络服务筛选会清空本地曲库", forced.isEmpty())
    }
}
