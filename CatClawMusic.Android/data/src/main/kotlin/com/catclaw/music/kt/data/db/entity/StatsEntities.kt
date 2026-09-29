package com.catclaw.music.kt.data.db.entity

import androidx.room.ColumnInfo
import androidx.room.Entity
import androidx.room.Index
import androidx.room.PrimaryKey

@Entity(
    tableName = "PlayHistory",
    indices = [Index("SongId"), Index("PlayedAt")],
)
data class PlayHistoryEntity(
    @PrimaryKey(autoGenerate = true) @ColumnInfo(name = "Id") val id: Int = 0,
    @ColumnInfo(name = "SongId") val songId: Int,
    @ColumnInfo(name = "PlayedAt") val playedAt: Long = 0,
    @ColumnInfo(name = "PlayCount") val playCount: Int = 1,
)

/** 单次收听会话；C# 侧只保留最近 2000 条 */
@Entity(
    tableName = "PlaySession",
    indices = [Index("SongId"), Index("PlayedAt")],
)
data class PlaySessionEntity(
    @PrimaryKey(autoGenerate = true) @ColumnInfo(name = "Id") val id: Int = 0,
    @ColumnInfo(name = "SongId") val songId: Int,
    @ColumnInfo(name = "PlayedAt") val playedAt: Long = 0,
    @ColumnInfo(name = "DurationMs") val durationMs: Long = 0,
)

@Entity(tableName = "DailyPlayStat", primaryKeys = ["Date"])
data class DailyPlayStatEntity(
    @ColumnInfo(name = "Date") val date: String,
    @ColumnInfo(name = "PlayCount") val playCount: Int = 0,
    @ColumnInfo(name = "TotalDurationMs") val totalDurationMs: Long = 0,
    @ColumnInfo(name = "NightPlayCount") val nightPlayCount: Int = 0,
)

@Entity(
    tableName = "DailySongStat",
    primaryKeys = ["DateSong"],
    indices = [Index("Date")],
)
data class DailySongStatEntity(
    /** 复合唯一键 `"yyyy-MM-dd|songId"`（**竖线**），线上库就是这么存的 */
    @ColumnInfo(name = "DateSong") val dateSong: String,
    @ColumnInfo(name = "Date") val date: String,
    @ColumnInfo(name = "SongId") val songId: Int,
    @ColumnInfo(name = "PlayCount") val playCount: Int = 0,
    @ColumnInfo(name = "TotalDurationMs") val totalDurationMs: Long = 0,
)

@Entity(
    tableName = "HourlyPlayStat",
    primaryKeys = ["DateHour"],
    indices = [Index("Date")],
)
data class HourlyPlayStatEntity(
    @ColumnInfo(name = "DateHour") val dateHour: String,
    @ColumnInfo(name = "Date") val date: String,
    @ColumnInfo(name = "Hour") val hour: Int,
    @ColumnInfo(name = "PlayCount") val playCount: Int = 0,
    @ColumnInfo(name = "TotalDurationMs") val totalDurationMs: Long = 0,
)
