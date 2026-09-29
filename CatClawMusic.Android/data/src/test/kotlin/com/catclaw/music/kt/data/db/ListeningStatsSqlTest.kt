package com.catclaw.music.kt.data.db

import java.io.File
import java.sql.Connection
import java.sql.DriverManager
import java.time.LocalDate
import java.time.LocalTime
import java.time.ZoneId
import org.junit.After
import org.junit.Assert.assertEquals
import org.junit.Assert.assertTrue
import org.junit.Before
import org.junit.Test

/**
 * 听歌统计页那几条读 SQL 拿去**真 SQLite** 上跑。
 *
 * 挑这几条的理由和 `LibraryDaoSqlTest` 一样：它们错了不会有编译期或运行期报错，
 * 只会让页面上的数字"看着不太对" —— 而数字看着对恰恰是最难发现的错法。
 * 这里盯四件事：范围参数的空串分支、时段折成 24 根柱子、多歌手各记一次而重复关联行不放大、
 * 以及从会话重建时三个键的形状与写侧完全一致。
 */
class ListeningStatsSqlTest {

    private lateinit var db: Connection

    @Before
    fun setUp() {
        db = DriverManager.getConnection("jdbc:sqlite::memory:")
        schema().split(';').map { it.trim() }
            .filter { it.isNotEmpty() && !it.contains("sqlite_sequence", ignoreCase = true) }
            .forEach { db.createStatement().execute(it) }
    }

    @After
    fun tearDown() {
        runCatching { db.close() }
    }

    private fun schema(): String {
        var dir = File(System.getProperty("user.dir"))
        repeat(4) {
            val f = File(dir, "docs-schema/catclaw.reference.sqlite.sql")
            if (f.isFile) return f.readText()
            dir = dir.parentFile ?: return@repeat
        }
        error("找不到参考 schema")
    }

    /**
     * 具名参数绑定，和 Room 一样按**名字**而不是出现次序。
     *
     * 范围查询里 `:start` 出现两次；按位置绑会把第二次绑成 null，
     * 于是 `Date <= NULL` 恒为 NULL，整条查询空得毫无声响 —— 测试先替真机踩一遍。
     */
    private fun stmt(sql: String, args: Array<out Pair<String, Any?>>): java.sql.PreparedStatement {
        val names = Regex(":(\\w+)").findAll(sql).map { it.groupValues[1] }.toList()
        return db.prepareStatement(Regex(":(\\w+)").replace(sql) { "?" }).apply {
            names.forEachIndexed { i, n ->
                val value = args.firstOrNull { it.first == n }?.second ?: error("缺少参数 :$n")
                setObject(i + 1, value)
            }
        }
    }

    private fun exec(sql: String, vararg args: Pair<String, Any?>) {
        stmt(sql, args).executeUpdate()
    }

    private fun scalar(sql: String, vararg args: Pair<String, Any?>): Long =
        stmt(sql, args).executeQuery().let { if (it.next()) it.getLong(1) else -1L }

    /** 结果按「列名 → 值」收成 map，断言才写得像人话而不是 `rs.getInt(3)` */
    private fun rows(sql: String, vararg args: Pair<String, Any?>): List<Map<String, Any?>> =
        stmt(sql, args).executeQuery().let { rs ->
            val md = rs.metaData
            buildList {
                while (rs.next()) {
                    add((1..md.columnCount).associate { i -> md.getColumnLabel(i) to rs.getObject(i) })
                }
            }
        }

    /** sqlite-jdbc 小整数给 Integer、大整数给 Long，数值断言一律先归一到 Long */
    private fun num(row: Map<String, Any?>, column: String): Long = (row[column] as Number).toLong()

    private fun addArtist(id: Int, name: String) {
        exec("INSERT INTO Artists (Id, Name) VALUES (:id, :name)", "id" to id, "name" to name)
    }

    /** FilePath 是 NOT NULL 且带唯一索引 —— 造数据时漏了它会以约束错误收场，不如一开始就带上 */
    private fun addSong(id: Int, artistId: Int) {
        exec(
            "INSERT INTO Songs (Id, Title, ArtistId, Duration, FilePath, Source) " +
                "VALUES (:id, :t, :a, 200, :p, 0)",
            "id" to id, "t" to "歌$id", "a" to artistId, "p" to "/music/$id.mp3",
        )
    }

    private fun link(songId: Int, artistId: Int) {
        exec(
            "INSERT INTO SongArtists (SongId, ArtistId) VALUES (:s, :a)",
            "s" to songId, "a" to artistId,
        )
    }

    private fun addHistory(songId: Int, plays: Int) {
        exec(
            "INSERT INTO PlayHistory (SongId, PlayedAt, PlayCount) VALUES (:s, 1000, :p)",
            "s" to songId, "p" to plays,
        )
    }

    private fun addDaily(date: String, plays: Int, ms: Long = 0, night: Int = 0) {
        exec(
            "INSERT INTO DailyPlayStat (Date, PlayCount, TotalDurationMs, NightPlayCount) VALUES (:d, :p, :m, :n)",
            "d" to date, "p" to plays, "m" to ms, "n" to night,
        )
    }

    private fun addHourly(date: String, hour: Int, plays: Int, ms: Long = 0) {
        exec(
            "INSERT INTO HourlyPlayStat (DateHour, Date, Hour, PlayCount, TotalDurationMs) VALUES (:dh, :d, :h, :p, :m)",
            "dh" to "$date %02d".format(hour), "d" to date, "h" to hour, "p" to plays, "m" to ms,
        )
    }

    private fun addDailySong(date: String, songId: Int) {
        exec(
            "INSERT INTO DailySongStat (DateSong, Date, SongId, PlayCount, TotalDurationMs) VALUES (:ds, :d, :s, 1, 0)",
            "ds" to "$date|$songId", "d" to date, "s" to songId,
        )
    }

    /** 用本地时刻构造，断言才与跑测试这台机器的时区无关 */
    private fun addSession(songId: Int, date: LocalDate, time: LocalTime, ms: Long) {
        val epoch = date.atTime(time).atZone(ZoneId.systemDefault()).toEpochSecond()
        exec(
            "INSERT INTO PlaySession (SongId, PlayedAt, DurationMs) VALUES (:s, :t, :m)",
            "s" to songId, "t" to epoch, "m" to ms,
        )
    }

    // ── 范围参数：空串=不限 ──

    @Test
    fun dailyStatsRangeTreatsBlankAsUnbounded() {
        addDaily("2026-03-04", 3, 40_000, 1)
        addDaily("2026-03-06", 5, 60_000, 2)
        addDaily("2026-02-28", 9, 90_000, 0)

        val bounded = rows(SQL_DAILY_STATS_RANGE, "start" to "2026-03-01", "end" to "2026-03-31")
        assertEquals("二月的不该进范围", listOf("2026-03-04", "2026-03-06"), bounded.map { it["date"] })
        assertEquals(3L, num(bounded.first(), "plays"))
        assertEquals(40_000L, num(bounded.first(), "ms"))
        assertEquals(1L, num(bounded.first(), "nightPlays"))

        assertEquals(
            "空串=不限：三行全要",
            listOf("2026-02-28", "2026-03-04", "2026-03-06"),
            rows(SQL_DAILY_STATS_RANGE, "start" to "", "end" to "").map { it["date"] },
        )
    }

    @Test
    fun songsHeardRangeCountsDistinctSongs() {
        addDailySong("2026-03-04", 7)
        addDailySong("2026-03-05", 7) // 同一首第二天又听，仍算一首
        addDailySong("2026-03-05", 8)
        addDailySong("2026-02-01", 9) // 范围外

        assertEquals(2L, scalar(SQL_SONGS_HEARD_RANGE, "start" to "2026-03-01", "end" to "2026-03-31"))
        assertEquals("不限时把范围外那首也算进来", 3L, scalar(SQL_SONGS_HEARD_RANGE, "start" to "", "end" to ""))
    }

    @Test
    fun hourlyHoursFoldDaysIntoBuckets() {
        addHourly("2026-03-04", 9, 2, 20_000)
        addHourly("2026-03-05", 9, 3, 30_000)
        addHourly("2026-03-05", 23, 1, 5_000)
        addHourly("2026-01-01", 9, 100, 1) // 范围外

        val buckets = rows(SQL_HOURLY_HOURS_RANGE, "start" to "2026-03-01", "end" to "2026-03-31")
        assertEquals("两天同一个钟点折成一根柱", listOf(9L, 23L), buckets.map { num(it, "hour") })
        assertEquals(5L, num(buckets.first(), "plays"))
        assertEquals(50_000L, num(buckets.first(), "ms"))
    }

    // ── 常听歌手榜 ──

    @Test
    fun topArtistsCreditsEachArtistOnceAndFallsBackToPrimary() {
        addArtist(1, "甲")
        addArtist(2, "乙")
        addArtist(3, "   ") // 有行但名字是空白 → 归到空名那组
        addArtist(4, "丙")
        addSong(10, artistId = 1)
        addSong(11, artistId = 4)
        addSong(12, artistId = 0) // 没有歌手可退
        addSong(13, artistId = 3)
        link(10, 1)
        link(10, 2)
        link(10, 2) // 脏数据：同一 (歌, 歌手) 记了两次
        addHistory(10, 6)
        addHistory(11, 2)
        addHistory(12, 1)
        addHistory(13, 7)

        val ranked = rows(SQL_TOP_ARTISTS, "limit" to 10)
        val plays = ranked.associate { (it["name"] as String) to num(it, "plays") }
        assertEquals("合作歌手各记整首的播放量", 6L, plays["甲"])
        assertEquals("重复关联行不该把播放量放大一倍", 6L, plays["乙"])
        assertEquals(2L, plays["丙"])
        assertEquals("无歌手 + 空名字并成一格", 8L, plays[""])
        assertEquals("按播放量降序", listOf(8L, 6L, 6L, 2L), ranked.map { num(it, "plays") })
        assertEquals(
            "榜上就是这四个键",
            setOf("", "甲", "乙", "丙"),
            ranked.map { it["name"] as String }.toSet(),
        )
        // 播放量并列时谁先谁后不写进断言：`ORDER BY plays DESC, name ASC` 在两台机器上给过不同次序，
        // 而这不是本页的正确性属性。要钉的是"降序"这件事本身。
    }

    @Test
    fun topArtistsHonorsLimit() {
        addArtist(1, "甲")
        addArtist(2, "乙")
        addSong(10, artistId = 1)
        addSong(11, artistId = 2)
        addHistory(10, 9)
        addHistory(11, 1)
        assertEquals(listOf("甲"), rows(SQL_TOP_ARTISTS, "limit" to 1).map { it["name"] })
        assertEquals(listOf("甲", "乙"), rows(SQL_TOP_ARTISTS, "limit" to 5).map { it["name"] })
    }

    // ── 从会话重建 ──

    @Test
    fun rebuildAggregatesSessionsIntoThreeSummaries() {
        addSession(10, LocalDate.of(2026, 3, 5), LocalTime.of(22, 30), 180_000)
        addSession(11, LocalDate.of(2026, 3, 5), LocalTime.of(9, 5), 60_000)
        addSession(10, LocalDate.of(2026, 3, 6), LocalTime.of(4, 0), -5) // 脏时长

        exec(SQL_REBUILD_DAILY, "nightStart" to 21, "nightEnd" to 5)
        exec(SQL_REBUILD_HOURLY)
        exec(SQL_REBUILD_SONG)

        val daily = rows("SELECT Date, PlayCount, TotalDurationMs, NightPlayCount FROM DailyPlayStat ORDER BY Date")
        assertEquals(
            "本地日期分桶：3-05 两场、3-06 一场（时长夹到 0，负数不该把总量扣掉）",
            listOf("2026-03-05|2|240000|1", "2026-03-06|1|0|1"),
            daily.map {
                "${it["Date"]}|${num(it, "PlayCount")}|${num(it, "TotalDurationMs")}|${num(it, "NightPlayCount")}"
            },
        )

        val hours = rows("SELECT DateHour, Hour, PlayCount FROM HourlyPlayStat ORDER BY DateHour")
        assertTrue(
            "DateHour 必须是 \"yyyy-MM-dd HH\"（小时两位），否则与写侧的键变成两份数据",
            hours.all { (it["DateHour"] as String).matches(Regex("""\d{4}-\d{2}-\d{2} \d{2}""")) },
        )
        // 按 DateHour 排序给的是**时间顺序**（9 点 → 22 点 → 隔天 4 点），
        // 这只有在小数补零成 "04" 时才成立：不补零的话 "2026-03-06 4" 会排到 "2026-03-05 9" 前面。
        assertEquals(listOf(9L, 22L, 4L), hours.map { num(it, "Hour") })

        val songStats = rows("SELECT DateSong, SongId, PlayCount FROM DailySongStat ORDER BY DateSong")
        assertTrue(
            "DateSong 必须是 yyyy-MM-dd|songId（竖线，不是下划线）",
            songStats.all { (it["DateSong"] as String).matches(Regex("""\d{4}-\d{2}-\d{2}\|\d+""")) },
        )
        assertEquals(
            "一天一首一行",
            listOf("2026-03-05|10" to 1L, "2026-03-05|11" to 1L, "2026-03-06|10" to 1L),
            songStats.map { (it["DateSong"] as String) to num(it, "PlayCount") },
        )
        assertEquals("重建后汇总总次数应等于会话条数", 3L, scalar(SQL_SUMMARY_PLAY_TOTAL))
        assertEquals(3L, scalar(SQL_SESSION_COUNT))
    }
}
