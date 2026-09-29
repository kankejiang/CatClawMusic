package com.catclaw.music.kt.core.stats

import java.time.LocalDate
import org.junit.Assert.assertEquals
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Test

/**
 * 听歌统计页的数字。全部用固定输入构造，因为这一页最容易出的错正是"看着挺像对的 0"：
 * 空库、缺日、上一周期没数据 —— 三种情况都该画出东西来，而不是一律 0 或干脆 NaN。
 */
class ListeningStatsRulesTest {

    private fun day(s: String, plays: Int, ms: Long = 0, night: Int = 0) =
        ListeningStatsRules.Day(LocalDate.parse(s), plays, ms, night)

    private val today = LocalDate.of(2026, 3, 10)

    @Test
    fun totalsAndNightPercentFollowTheSummaryRows() {
        val days = listOf(
            day("2026-03-08", 3, 180_000, 1),
            day("2026-03-09", 5, 300_000, 2),
            day("2026-03-10", 2, 120_000, 0),
        )
        val r = ListeningStatsRules.summarize(days, emptyList(), 6, ListeningStatsRules.Range.WEEK, today)
        assertEquals(10, r.totalPlays)
        assertEquals(600_000L, r.totalMs)
        assertEquals(3, r.listeningDays)
        assertEquals(6, r.songsHeard)
        assertEquals("深夜占比按四舍五入：3/10", 30, r.nightPercent)
    }

    @Test
    fun avgDayDividesByListeningDaysNotByRange() {
        val days = listOf(day("2026-03-09", 5, 600_000), day("2026-03-10", 5, 600_000))
        val r = ListeningStatsRules.summarize(days, emptyList(), 2, ListeningStatsRules.Range.MONTH, today)
        assertEquals("1_200_000 除以真的听过歌的 2 天，而不是范围天数 30", 600_000L, r.avgDayMs)
        assertEquals(
            "有记录的天数为 0 时不能给除零结果", 0L,
            ListeningStatsRules.summarize(listOf(day("2026-03-09", 0, 0)), emptyList(), 0, ListeningStatsRules.Range.MONTH, today)
                .avgDayMs,
        )
    }

    @Test
    fun weeklyBarsAreOnePerDayEndOnTodayAndKeepEmptyDays() {
        val days = listOf(day("2026-03-08", 4, 400_000), day("2026-03-10", 8, 800_000))
        val bars = ListeningStatsRules.barsFor(days, ListeningStatsRules.Range.WEEK, today)
        assertEquals(7, bars.size)
        assertEquals("最后一根必须是今天，否则整条横轴错一天", today, bars.last().key)
        assertEquals(LocalDate.of(2026, 3, 4), bars.first().key)
        assertEquals(listOf(0, 0, 0, 0, 4, 0, 8), bars.map { it.plays })
        assertEquals(1f, bars.last().playsRatio, 0f)
        assertEquals("峰值 8，那天的 4 次占一半", 0.5f, bars[4].playsRatio, 0f)
        assertEquals("没听的那天画 0 高而不是 NaN", 0f, bars.first().playsRatio, 0f)
        assertEquals("有听的天数按柱子的峰值口径算", 2, bars.count { it.plays > 0 })
    }

    @Test
    fun allRangeGroupsByMonthAndFillsGapMonths() {
        val days = listOf(
            day("2026-01-15", 2, 100_000), day("2026-01-31", 3, 100_000),
            day("2026-03-02", 7, 900_000),
        )
        val bars = ListeningStatsRules.barsFor(days, ListeningStatsRules.Range.ALL, today)
        assertEquals("1 月到 3 月连排，空着的 2 月也要有柱子", 3, bars.size)
        assertEquals(
            listOf(
                LocalDate.of(2026, 1, 1) to 5,
                LocalDate.of(2026, 2, 1) to 0,
                LocalDate.of(2026, 3, 1) to 7,
            ),
            bars.map { it.key to it.plays },
        )
        assertEquals(listOf(200_000L, 0L, 900_000L), bars.map { it.ms })
        assertEquals(1f, bars[2].msRatio, 0f)
        assertEquals("时长口径的柱高独立于次数口径", 2f / 9f, bars[0].msRatio, 1e-6f)
        assertEquals(0, ListeningStatsRules.barsFor(emptyList(), ListeningStatsRules.Range.ALL, today).size)
    }

    @Test
    fun slotsFoldHoursIntoTheSixBuckets() {
        val hours = listOf(
            ListeningStatsRules.HourBucket(3, 5, 0), // 凌晨 0–5
            ListeningStatsRules.HourBucket(7, 10, 0), // 清晨 5–9
            ListeningStatsRules.HourBucket(10, 20, 0), // 上午 9–12
            ListeningStatsRules.HourBucket(14, 30, 0), // 午后 12–17
            ListeningStatsRules.HourBucket(19, 25, 0), // 傍晚 17–21
            ListeningStatsRules.HourBucket(22, 10, 0), // 夜晚 21–24
        )
        val slots = ListeningStatsRules.slotsFor(hours)
        assertEquals(6, slots.size)
        assertEquals("页面顺序即 SLOT_RANGES 顺序：清晨/上午/午后/傍晚/夜晚/凌晨", listOf(10, 20, 30, 25, 10, 5), slots.map { it.plays })
        assertEquals(listOf(10, 20, 30, 25, 10, 5), slots.map { it.percent })
        assertEquals("占比按总次数，最大那格满高", 1f, slots[2].ratio, 0f)
        assertEquals("深夜那两格是索引 4 与 5，不是相邻的 0 与 1", 15, slots[4].percent + slots[5].percent)
    }

    @Test
    fun lateNightPercentCountsPlaysNotRoundedSlots() {
        // 三格里两格各 1 次：各自四舍五入是 33% + 33% = 66%，而真实占比是 2/3 = 67%。
        // 页面文案要说的是后者，所以这个数必须由 :core 按次数算出来。
        val hours = listOf(
            ListeningStatsRules.HourBucket(22, 1, 0),
            ListeningStatsRules.HourBucket(2, 1, 0),
            ListeningStatsRules.HourBucket(10, 1, 0),
        )
        val r = ListeningStatsRules.summarize(emptyList(), hours, 0, ListeningStatsRules.Range.WEEK, today)
        assertEquals(67, r.lateNightPercent)
        assertEquals("两格各自的显示值仍是各自四舍五入的", listOf(33, 33), listOf(r.slots[4].percent, r.slots[5].percent))
        assertEquals(0, ListeningStatsRules.summarize(emptyList(), emptyList(), 0, ListeningStatsRules.Range.WEEK, today).lateNightPercent)
    }

    @Test
    fun streakAnchorsOnYesterdayAndBestSpansHistory() {
        val dates = setOf(
            LocalDate.of(2026, 2, 1), LocalDate.of(2026, 2, 2), LocalDate.of(2026, 2, 3),
            LocalDate.of(2026, 3, 8), LocalDate.of(2026, 3, 9),
        )
        assertEquals("今天没听但昨天听了，连续不该归零", 2, ListeningStatsRules.currentStreak(dates, today))
        assertEquals(3, ListeningStatsRules.bestStreak(dates))
        assertEquals("今天补上后，3-08/09/10 连成三天", 3, ListeningStatsRules.currentStreak(dates + today, today))
        assertEquals("断了就是断了", 0, ListeningStatsRules.currentStreak(dates, LocalDate.of(2026, 6, 1)))
        assertEquals(0, ListeningStatsRules.bestStreak(emptySet()))
    }

    @Test
    fun compareDeltasAndTheAllRangeWithoutBaseline() {
        val days = listOf(day("2026-03-09", 4, 400_000), day("2026-03-10", 6, 600_000))
        val prev = listOf(day("2026-02-08", 20, 2_000_000))
        val r = ListeningStatsRules.summarize(days, emptyList(), 2, ListeningStatsRules.Range.MONTH, today, prev)
        assertEquals("10 次 vs 20 次", -50, r.compare?.playsDelta)
        assertEquals("1 百万毫秒 vs 2 百万", -50, r.compare?.msDelta)
        assertEquals("听歌天数 2 vs 1", 100, r.compare?.daysDelta)

        val firstTime = ListeningStatsRules.summarize(days, emptyList(), 2, ListeningStatsRules.Range.MONTH, today)
        assertEquals("上一周期完全没记录时记成 +100%（新增），而不是除零", 100, firstTime.compare?.playsDelta)
        assertNull(
            "「全部」没有上一周期可言，环比整块不画",
            ListeningStatsRules.summarize(days, emptyList(), 2, ListeningStatsRules.Range.ALL, today).compare,
        )
    }

    @Test
    fun emptyLibraryGivesZerosNotNaN() {
        val r = ListeningStatsRules.summarize(emptyList(), emptyList(), 0, ListeningStatsRules.Range.WEEK, today)
        assertEquals(0, r.totalPlays)
        assertEquals(0, r.nightPercent)
        assertEquals(0, r.slots.first().percent)
        assertEquals(7, r.bars.size)
        assertTrue(r.bars.all { it.playsRatio == 0f && it.msRatio == 0f })
        assertEquals(0, r.streakCurrent)
        assertEquals(0, r.streakBest)
        assertEquals("陪伴天数最少算 1 天", 1, ListeningStatsRules.companionDays(ListeningStatsRules.EPOCH))
        assertEquals("纪元之前不该出现负数", 1, ListeningStatsRules.companionDays(LocalDate.of(2020, 1, 1)))
        assertTrue(ListeningStatsRules.companionDays(today) > 700)
    }

    @Test
    fun percentAndDeltaRoundTheWayCSharpDid() {
        assertEquals(0, ListeningStatsRules.percent(0, 0))
        assertEquals(0, ListeningStatsRules.percent(3, 0))
        assertEquals(50, ListeningStatsRules.percent(1, 2))
        assertEquals(33, ListeningStatsRules.percent(1, 3))
        assertEquals(67, ListeningStatsRules.percent(2, 3))
        assertEquals(0, ListeningStatsRules.delta(0, 0))
        assertEquals(100, ListeningStatsRules.delta(1, 0))
        assertEquals(-100, ListeningStatsRules.delta(0, 5))
        assertEquals("差值不足 1% 时显示持平而不是 ▲0%", 0, ListeningStatsRules.delta(1004, 1000))
    }
}
