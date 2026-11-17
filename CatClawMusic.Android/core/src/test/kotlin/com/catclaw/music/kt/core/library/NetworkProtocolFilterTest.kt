package com.catclaw.music.kt.core.library

import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Test

/**
 * 「按网络服务来源筛选」的语义测试。
 *
 * 这套规则和 `ConnectionFilter` 是同一档粒度（协议级），两者叠在一起用。
 * 每一处写错都是静默的：不崩溃，只是"某些歌莫名消失"或"筛了 Navidrome 却看到 WebDAV"。
 */
class NetworkProtocolFilterTest {

    private data class Row(val id: Int, val source: Int, val protocol: Int)

    /** 真机实测的三组：本地 10 / WebDAV 613(protocol=0) / Navidrome 613(protocol=1) */
    private val rows = listOf(
        Row(1, source = 0, protocol = 0),   // 本地曲（protocol 字段没用）
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
    fun `All 不过滤任何行`() {
        assertEquals(listOf(1, 2, 3, 4), filter(NetworkProtocolFilter.All))
    }

    @Test
    fun `WebDAV 只留 WebDAV 远程曲，本地与缓存都不算`() {
        // 本地曲(1)与缓存曲(4)的 protocol 也是 0，但它们没有网络服务归属 → 不显示
        assertEquals(listOf(2), filter(NetworkProtocolFilter.WebDAV))
    }

    @Test
    fun `Navidrome 只留 Navidrome 远程曲`() {
        assertEquals(listOf(3), filter(NetworkProtocolFilter.Navidrome))
    }

    @Test
    fun `没有该协议的曲时结果为空而不是全留`() {
        // 真机没有 SMB 曲；筛 SMB 应得空列表，不能退化成"不过滤"
        assertTrue(filter(NetworkProtocolFilter.SMB).isEmpty())
    }

    @Test
    fun `本地曲永远不匹配具体协议`() {
        for (mode in listOf(
            NetworkProtocolFilter.WebDAV,
            NetworkProtocolFilter.Navidrome,
            NetworkProtocolFilter.SMB,
        )) {
            assertFalse("$mode 不应匹配本地曲", mode.accepts(protocol = 0, source = 0))
        }
    }

    @Test
    fun `缓存曲永远不匹配具体协议`() {
        // 缓存曲 source=3，即使 protocol 字段是 0 也不该被 WebDAV 选中
        assertFalse(NetworkProtocolFilter.WebDAV.accepts(protocol = 0, source = 3))
    }

    @Test
    fun `All 对本地曲也返回 true`() {
        assertTrue(NetworkProtocolFilter.All.accepts(protocol = 0, source = 0))
        assertTrue(NetworkProtocolFilter.All.accepts(protocol = 9, source = 1))
    }

    @Test
    fun `of 按 ProtocolType ordinal 反查`() {
        // ProtocolType { WebDAV(0), Navidrome(1), SMB(2) }
        assertEquals(NetworkProtocolFilter.WebDAV, NetworkProtocolFilter.of(0))
        assertEquals(NetworkProtocolFilter.Navidrome, NetworkProtocolFilter.of(1))
        assertEquals(NetworkProtocolFilter.SMB, NetworkProtocolFilter.of(2))
    }

    @Test
    fun `of 遇到未知 ordinal 回落到 All 而不是抛异常`() {
        assertEquals(NetworkProtocolFilter.All, NetworkProtocolFilter.of(99))
        assertEquals(NetworkProtocolFilter.All, NetworkProtocolFilter.of(-1))
    }

    @Test
    fun `与 SongSourceFilter 叠加：网络 + Navidrome`() {
        // 先按本地/网络，再按具体服务 —— ViewModel 里就是这个顺序
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
    fun `与 SongSourceFilter 叠加：本地 + 任意协议 = 空`() {
        // 这也是 setSongSource(Local) 必须把协议筛选清回 All 的原因：
        // 留着它会让「本地曲库」恒空，看起来像曲库丢了
        val afterSource = filterBySource(rows, SongSourceFilter.Local) { it.source }
        val result = filterByNetworkProtocol(
            afterSource,
            NetworkProtocolFilter.WebDAV,
            protocolOf = { it.protocol },
            sourceOf = { it.source },
        )
        assertTrue(result.isEmpty())
    }
}
