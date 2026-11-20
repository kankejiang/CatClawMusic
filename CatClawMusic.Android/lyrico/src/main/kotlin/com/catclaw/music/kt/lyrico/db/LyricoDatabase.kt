package com.catclaw.music.kt.lyrico.db

import androidx.room.Database
import androidx.room.RoomDatabase

/**
 * Lyrico 移植层的独立数据库。
 *
 * **为什么不并进 catclaw.db**：`catclaw.db` 的 schema 被 `SchemaParityTest` 锁死成与
 * MAUI 参考库逐表逐列一致，而 Lyrico 的移植需要自己的表（源插件、批量任务、库索引、
 * 自定义标签键…）。分库之后两边互不牵制：catclaw.db 继续与参考实现对齐，
 * 这里的 schema 由 `LyricoSchemaTest` 单独守护。
 *
 * 迁移策略：本库对应的是「应用内可重建的派生数据」（歌曲索引可从文件重建、
 * 日志可清空），因此开发阶段允许破坏性迁移；正式发版前把 version 冻结并补迁移。
 */
@Database(
    entities = [
        LyricoSongEntity::class,
        LyricoFolderEntity::class,
        LyricoCustomTagKeyEntity::class,
        LyricoSourcePluginEntity::class,
        LyricoPluginSubscriptionEntity::class,
        LyricoAppLogEntity::class,
        BatchTaskEntity::class,
        BatchItemEntity::class,
    ],
    version = 2,
    exportSchema = true,
)
abstract class LyricoDatabase : RoomDatabase() {
    abstract fun songDao(): LyricoSongDao
    abstract fun folderDao(): LyricoFolderDao
    abstract fun sourcePluginDao(): LyricoSourcePluginDao
    abstract fun pluginSubscriptionDao(): LyricoPluginSubscriptionDao
    abstract fun appLogDao(): LyricoAppLogDao
    abstract fun batchTaskDao(): BatchTaskDao
    abstract fun batchItemDao(): BatchItemDao

    companion object {
        const val NAME = "lyrico.db"
    }
}
