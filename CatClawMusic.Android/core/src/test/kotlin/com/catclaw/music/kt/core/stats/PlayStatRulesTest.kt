package com.catclaw.music.kt.core.stats

import java.time.LocalDateTime
import java.time.ZoneOffset
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Test

/**
 * 统计键的形状与深夜判定。
 *
 * 这些键是 UPSERT 的目标：写侧和读侧算出不同的串，统计就会静默分裂成两份，
 * 页面看到的只是其中一份（或者全是 0）。所以逐个钉死字面量。
 */
class PlayStatRulesTest {

    private val plus8 = ZoneOffset.ofHours(8)

    private fun epochOf(y: Int, mo: Int, d: Int, h: Int, mi: Int): Long =
        LocalDateTime.of(y, mo, d, h, mi).toEpochSecond(plus8)

    @Test
    fun keysMatchTheShapeTheSumTablesUse() {
        val k = PlayStatRules.keysFor(epochOf(2026, 9, 29, 14, 5), songId = 42, zone = plus8)
        assertEquals("2026-09-29", k.date)
        assertEquals("日期与两位小时之间是一个空格", "2026-09-29 14", k.dateHour)
        assertEquals("日期与歌曲 id 之间是竖线", "2026-09-29|42", k.dateSong)
        assertEquals(14, k.hour)
        assertFalse(k.night)
    }

    @Test
    fun singleDigitHourIsZeroPadded() {
        val k = PlayStatRules.keysFor(epochOf(2026, 1, 2, 3, 4), songId = 7, zone = plus8)
        assertEquals("2026-01-02 03", k.dateHour)
        assertEquals("2026-01-02|7", k.dateSong)
    }

    @Test
    fun nightWindowIsLocalTimeNotUtc() {
        // 本地 03:15（东八区）= UTC 前一天 19:15。
        // 按 UTC 算会得到 date=2026-09-28、hour=19、night=false —— 东八区用户的深夜听歌整段跑到白天。
        val k = PlayStatRules.keysFor(epochOf(2026, 9, 29, 3, 15), songId = 1, zone = plus8)
        assertEquals("2026-09-29", k.date)
        assertEquals(3, k.hour)
        assertTrue("凌晨三点算深夜", k.night)
    }

    @Test
    fun nightBoundaries() {
        assertFalse(PlayStatRules.isNight(20))
        assertTrue(PlayStatRules.isNight(21))
        assertTrue(PlayStatRules.isNight(23))
        assertTrue(PlayStatRules.isNight(0))
        assertTrue(PlayStatRules.isNight(4))
        assertFalse(PlayStatRules.isNight(5))
        assertFalse(PlayStatRules.isNight(12))
    }
}
