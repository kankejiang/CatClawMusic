package com.catclaw.music.kt.data.db

import androidx.room.Dao
import androidx.room.Insert
import androidx.room.Query
import com.catclaw.music.kt.data.db.entity.ChatMessageEntity

/**
 * 聊天历史持久化，语义对齐 C# `MusicDatabase.Chat.cs`。
 * ⚠ 排序列用 Id 而不是 Timestamp，与 C# 的 `OrderByDescending(r => r.Id)` 保持一致：
 * 同一毫秒内连写两条时 Timestamp 并列，只有自增 Id 能定序。
 */
@Dao
interface ChatDao {
    @Insert
    suspend fun insert(message: ChatMessageEntity): Long

    /** 最近 N 条，按 Id 升序返回（UI 直接自上而下渲染，最旧的在前） */
    @Query(SQL_RECENT)
    suspend fun recent(limit: Int): List<ChatMessageEntity>

    /** 翻更早的页：只取比锚点更旧的 N 条 */
    @Query(SQL_OLDER_THAN)
    suspend fun olderThan(beforeId: Int, limit: Int): List<ChatMessageEntity>

    @Query("SELECT COUNT(*) FROM ChatMessageRecord")
    suspend fun count(): Int

    /** 裁掉最旧的多余部分，只保留最近 keep 条（C# `TrimChatMessagesAsync` 同做法） */
    @Query(SQL_TRIM_KEEPING_NEWEST)
    suspend fun trimKeepingNewest(keep: Int)
}

/**
 * 提成常量是为了让这些 SQL 能被真 SQLite 执行到（`ChatDaoSqlTest`）——
 * 分页方向与裁剪边界此前只有 Room 注解里一份，假 DAO 测不到、仪器化测试又依赖设备。
 * 注解与测试引用同一份字符串，DAO 一改测试就跟着动，不会出现"测的是抄本"。
 */
internal const val SQL_RECENT = """
    SELECT * FROM (
        SELECT * FROM ChatMessageRecord ORDER BY Id DESC LIMIT :limit
    ) ORDER BY Id ASC
"""

internal const val SQL_OLDER_THAN = """
    SELECT * FROM (
        SELECT * FROM ChatMessageRecord WHERE Id < :beforeId ORDER BY Id DESC LIMIT :limit
    ) ORDER BY Id ASC
"""

internal const val SQL_TRIM_KEEPING_NEWEST = """
    DELETE FROM ChatMessageRecord WHERE Id NOT IN (
        SELECT Id FROM ChatMessageRecord ORDER BY Id DESC LIMIT :keep
    )
"""
