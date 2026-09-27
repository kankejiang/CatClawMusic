package com.catclaw.music.kt.core.library

import org.junit.Assert.assertEquals
import org.junit.Test

/**
 * 扫描后"哪些歌该当过期"的判定。
 *
 * 这条规则值得单独钉，因为它的一个分支是**数据丢失级**的：
 * 空扫描结果 + 朴素差集 == 把整库判成过期并全删。
 * 而"扫出 0 条"是日常会发生的（权限被撤、MediaProvider 短暂不可用、查询异常被吞成空列表）。
 */
class StalePathGuardTest {

    private val library = (1..616).map { "/storage/emulated/0/Music/song$it.mp3" }

    @Test
    fun emptyScanResultNeverMarksTheWholeLibraryStale() {
        val stale = MediaScanRules.stalePaths(existingPaths = library, seenPaths = emptySet(), scannedAny = false)
        assertEquals(
            "扫描没返回东西时不能裁任何一行 —— 这一条挡住的是 616 首一次删光",
            emptyList<String>(),
            stale,
        )
    }

    @Test
    fun aRealScanStillPrunesTheFilesThatDisappeared() {
        val seen = library.dropLast(3).toSet()
        val stale = MediaScanRules.stalePaths(library, seen, scannedAny = true)
        assertEquals("正常扫描时该删的还是要删，守卫不能把功能一起关掉", library.takeLast(3), stale)
    }

    @Test
    fun nothingMissingYieldsEmptyRatherThanTheWholeList() {
        assertEquals(emptyList<String>(), MediaScanRules.stalePaths(library, library.toSet(), scannedAny = true))
    }

    @Test
    fun staleKeepsLibraryOrderSoTheLogReadsTopDown() {
        val existing = listOf("a.mp3", "b.mp3", "c.mp3")
        assertEquals(listOf("a.mp3", "c.mp3"), MediaScanRules.stalePaths(existing, setOf("b.mp3"), scannedAny = true))
    }

    @Test
    fun emptyLibraryIsAlwaysASafeNoOp() {
        assertEquals(emptyList<String>(), MediaScanRules.stalePaths(emptyList(), emptySet(), scannedAny = false))
        assertEquals(emptyList<String>(), MediaScanRules.stalePaths(emptyList(), setOf("x"), scannedAny = true))
    }
}
