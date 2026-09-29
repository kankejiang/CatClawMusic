package com.catclaw.music.kt.core.stats

import java.time.LocalDate
import java.time.temporal.ChronoUnit
import kotlin.math.max
import kotlin.math.roundToInt

/**
 * 听歌统计页的算术 —— 输入是 DAO 的汇总行，输出是页面要显示的那些数字。
 *
 * 为什么要单独一层：这些数字全都直接给用户看，而它们**错了不报错** ——
 * 分桶少一天、环比拿错上一周期、深夜按 UTC 取，页面照样漂漂亮亮地给出错的数。
 * 放在这里才能在 JVM 里用固定输入钉住（真机上没法构造"连续 37 天听歌"这种样本），
 * 也才能让 :data 的 SQL 与 :app 的排版互相不迁就。
 *
 * 口径与 C# `ListeningStatsViewModel` 一致，只有一处有意不同：陪伴天数按**跨过的日历天**算，
 * 不像 C# 那样拿秒差四舍五入 —— 两者最多差 1 天，而"第 0 天"本来就没有小时精度可言。
 */
object ListeningStatsRules {

    /** 项目的"第 0 天"。改了它，页面上的陪伴天数会当场跳一截，所以钉在这里 */
    val EPOCH: LocalDate = LocalDate.of(2024, 1, 1)

    /** 时间范围。[days] = 0 表示不限 —— 与 DAO 那对"空串=不限"的范围参数同一个含义 */
    enum class Range(val days: Int) {
        WEEK(7),
        MONTH(30),
        ALL(0),
    }

    data class Day(val date: LocalDate, val plays: Int, val ms: Long, val nightPlays: Int)

    data class HourBucket(val hour: Int, val plays: Int, val ms: Long)

    /**
     * 时段分布的六格，边界是 `[from, to)`。
     *
     * 顺序就是页面顺序，UI 按序号取自己的文案 —— 边界改这里、标签改资源文件，
     * 中间没有第二份数字可以漂移。
     */
    val SLOT_RANGES: List<IntRange> = listOf(
        5 until 9, 9 until 12, 12 until 17, 17 until 21, 21 until 24, 0 until 5,
    )

    /** 一格时段：次数、占比、以及"画多长"的相对高度 */
    data class Slot(val plays: Int, val percent: Int, val ratio: Float)

    /**
     * 趋势图的一根柱子。[key] 在 7/30 范围下是那一天，在"全部"下是该月的第一天。
     *
     * 两根 ratio 分开算：柱高要么按次数归一、要么按时长归一，切指标时不能沿用另一个的峰值；
     * 两者都是 0 高度而不是 NaN —— 空库这一页也要能画。
     */
    data class Bar(
        val key: LocalDate,
        val plays: Int,
        val ms: Long,
        val playsRatio: Float,
        val msRatio: Float,
    )

    /** 环比：三个指标各自的涨跌百分比（正=涨）。范围=ALL 时没有"上一周期"，给 null */
    data class Compare(val playsDelta: Int, val msDelta: Int, val daysDelta: Int)

    data class Result(
        val totalPlays: Int,
        val songsHeard: Int,
        val listeningDays: Int,
        val companionDays: Int,
        val totalMs: Long,
        val avgDayMs: Long,
        val nightPercent: Int,
        val bars: List<Bar>,
        val activeDaysInRange: Int,
        val slots: List<Slot>,
        val streakCurrent: Int,
        val streakBest: Int,
        val compare: Compare?,
    )

    /**
     * 汇总一页数字。
     *
     * @param days 范围内的按天汇总；缺的日子**不在**列表里（DAO 只回有行的那些天）
     * @param prevDays 上一周期的按天汇总，只用于环比；范围=ALL 时传空即可
     */
    fun summarize(
        days: List<Day>,
        hours: List<HourBucket>,
        songsHeard: Int,
        range: Range,
        today: LocalDate,
        prevDays: List<Day> = emptyList(),
    ): Result {
        val totalPlays = days.sumOf { it.plays }
        val totalMs = days.sumOf { it.ms }
        val listeningDays = days.count { it.plays > 0 }
        val dates = days.filter { it.plays > 0 }.map { it.date }.toSet()
        val bars = barsFor(days, range, today)
        return Result(
            totalPlays = totalPlays,
            songsHeard = songsHeard,
            listeningDays = listeningDays,
            companionDays = companionDays(today),
            totalMs = totalMs,
            // 日均的分母是"真的听过歌的天数"，不是范围天数：拿 30 当分母的话，
            // 同一份数据从"全部"切到"近 30 天"会把日均凭空砍掉一大截。
            avgDayMs = if (listeningDays > 0) totalMs / listeningDays else 0L,
            nightPercent = percent(days.sumOf { it.nightPlays }, totalPlays),
            bars = bars,
            activeDaysInRange = bars.count { it.plays > 0 },
            slots = slotsFor(hours),
            streakCurrent = currentStreak(dates, today),
            streakBest = bestStreak(dates),
            compare = compareFor(
                range = range,
                curPlays = totalPlays.toLong(),
                curMs = totalMs,
                curDays = listeningDays.toLong(),
                prev = prevDays,
            ),
        )
    }

    /** 陪伴了多少天。最少算 1 天：今天刚装上也该显示 1，而不是 0 */
    fun companionDays(today: LocalDate): Int =
        max(1, ChronoUnit.DAYS.between(EPOCH, today).toInt())

    /**
     * 趋势柱。7/30 是**逐日**并补齐缺失的日子（不补的话横轴会随数据稀疏而错位、
     * 最后一根柱也不一定就是今天）；"全部"是**逐月**，并从第一个有记录的月连到最后一个，
     * 中间空着的月画成 0 高度 —— 用户才看得出那是"没听"而不是"没画"。
     */
    fun barsFor(days: List<Day>, range: Range, today: LocalDate): List<Bar> {
        if (range != Range.ALL) {
            val byDate = days.associateBy { it.date }
            return (range.days - 1 downTo 0).map { back ->
                val date = today.minusDays(back.toLong())
                val row = byDate[date]
                barOf(date, row?.plays ?: 0, row?.ms ?: 0L)
            }.let { normalize(it) }
        }
        if (days.isEmpty()) return emptyList()
        val months = days.map { it.date.firstOfMonth() }
        val out = ArrayList<Bar>()
        var cursor = months.min()
        val end = months.max()
        while (!cursor.isAfter(end)) {
            val inMonth = days.filter { it.date.firstOfMonth() == cursor }
            out += barOf(cursor, inMonth.sumOf { it.plays }, inMonth.sumOf { it.ms })
            cursor = cursor.plusMonths(1)
        }
        return normalize(out)
    }

    /** 把 24 个钟点折成六格。六格边界覆盖了 0..23，所以没有小时会被漏掉 */
    fun slotsFor(hours: List<HourBucket>): List<Slot> {
        val total = hours.sumOf { it.plays }
        val counts = SLOT_RANGES.map { range -> hours.filter { it.hour in range }.sumOf { it.plays } }
        val maxCount = counts.maxOrNull() ?: 0
        return counts.map { c -> Slot(plays = c, percent = percent(c, total), ratio = ratioOf(c, maxCount)) }
    }

    /**
     * 当前连续天数。锚点是"今天"或"昨天" ——
     * 昨天听过而今天还没听时不能立刻断掉，否则这个数每天早上都会归零一次。
     */
    fun currentStreak(dates: Set<LocalDate>, today: LocalDate): Int {
        val anchor = when {
            dates.contains(today) -> today
            dates.contains(today.minusDays(1)) -> today.minusDays(1)
            else -> return 0
        }
        var n = 1
        var d = anchor.minusDays(1)
        while (dates.contains(d)) {
            n++
            d = d.minusDays(1)
        }
        return n
    }

    /** 历史最长连续。只数"有听的那天"，中间断一天就重新起算 */
    fun bestStreak(dates: Set<LocalDate>): Int {
        if (dates.isEmpty()) return 0
        val sorted = dates.sorted()
        var best = 1
        var run = 1
        for (i in 1 until sorted.size) {
            run = if (sorted[i] == sorted[i - 1].plusDays(1)) run + 1 else 1
            best = max(best, run)
        }
        return best
    }

    /**
     * 占比。分母 <= 0 给 0 而不是 NaN —— 一条记录都没有时这页也会被调用。
     * 四舍五入而不截断：C# 用的就是 `Math.Round`，截断会让"深夜占比"长期偏低一档。
     */
    fun percent(part: Int, whole: Int): Int = if (whole <= 0) 0 else (part * 100.0 / whole).roundToInt()

    /**
     * 环比涨跌百分比，正=涨、负=跌。
     *
     * `prev=0` 不能除：照 C# 记成 +100%（"新增"），两边都零记 0%（"持平"）。
     * 差值不足 1% 时四舍五入成 0，页面显示"持平"而不是"▲ 0%"。
     */
    fun delta(current: Long, prev: Long): Int = when {
        prev == 0L && current == 0L -> 0
        prev == 0L -> 100
        else -> ((current - prev) * 100.0 / prev).roundToInt()
    }

    private fun compareFor(range: Range, curPlays: Long, curMs: Long, curDays: Long, prev: List<Day>): Compare? {
        if (range == Range.ALL) return null
        return Compare(
            playsDelta = delta(curPlays, prev.sumOf { it.plays }.toLong()),
            msDelta = delta(curMs, prev.sumOf { it.ms }),
            daysDelta = delta(curDays, prev.count { it.plays > 0 }.toLong()),
        )
    }

    private fun barOf(key: LocalDate, plays: Int, ms: Long) = Bar(key, plays, ms, 0f, 0f)

    private fun normalize(bars: List<Bar>): List<Bar> {
        val maxPlays = bars.maxOfOrNull { it.plays } ?: 0
        val maxMs = bars.maxOfOrNull { it.ms } ?: 0L
        return bars.map {
            it.copy(playsRatio = ratioOf(it.plays, maxPlays), msRatio = ratioOf(it.ms, maxMs))
        }
    }

    private fun ratioOf(part: Int, whole: Int): Float = if (whole <= 0) 0f else part.toFloat() / whole.toFloat()

    private fun ratioOf(part: Long, whole: Long): Float = if (whole <= 0L) 0f else part.toFloat() / whole.toFloat()

    private fun LocalDate.firstOfMonth(): LocalDate = withDayOfMonth(1)
}
