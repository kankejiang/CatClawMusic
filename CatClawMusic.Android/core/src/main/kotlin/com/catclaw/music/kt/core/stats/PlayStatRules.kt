package com.catclaw.music.kt.core.stats

import java.time.Instant
import java.time.ZoneId
import java.time.format.DateTimeFormatter

/**
 * 听歌统计的键规则 —— 对应 C# `MusicDatabase.PlayHistory.cs` 的 `IncrementDailyStatsAsync`。
 *
 * 三张汇总表（按天 / 按时段 / 按歌曲）全靠这几个字符串键做 UPSERT 的目标，
 * 所以键的形状必须和读侧、和 C# 线上库完全一致，差一个分隔符就是两份数据：
 * - `date` = 本地日期 `yyyy-MM-dd`；
 * - `dateHour` = `"yyyy-MM-dd HH"`（中间一个空格，小时两位）；
 * - `dateSong` = `"yyyy-MM-dd|songId"`（**竖线**，不是下划线）；
 * - 深夜 = 21 点之后或 5 点之前，按**本地时区**判，不按 UTC ——
 *   用 UTC 判会让东八区用户的"深夜听歌"整段跑到白天去。
 */
object PlayStatRules {

    const val NIGHT_START_HOUR = 21
    const val NIGHT_END_HOUR = 5

    private val DATE_FORMAT: DateTimeFormatter = DateTimeFormatter.ofPattern("yyyy-MM-dd")
    private val HOUR_FORMAT: DateTimeFormatter = DateTimeFormatter.ofPattern("HH")

    data class Keys(
        val date: String,
        val dateHour: String,
        val dateSong: String,
        val hour: Int,
        val night: Boolean,
    )

    fun isNight(hour: Int): Boolean = hour >= NIGHT_START_HOUR || hour < NIGHT_END_HOUR

    fun keysFor(epochSeconds: Long, songId: Int, zone: ZoneId = ZoneId.systemDefault()): Keys {
        val time = Instant.ofEpochSecond(epochSeconds).atZone(zone)
        val date = time.toLocalDate().format(DATE_FORMAT)
        val hour = time.toLocalTime().format(HOUR_FORMAT)
        return Keys(
            date = date,
            dateHour = "$date $hour",
            dateSong = "$date|$songId",
            hour = time.hour,
            night = isNight(time.hour),
        )
    }
}
