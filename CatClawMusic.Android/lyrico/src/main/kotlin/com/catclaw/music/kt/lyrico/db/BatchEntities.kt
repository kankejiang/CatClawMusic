package com.catclaw.music.kt.lyrico.db

import androidx.room.ColumnInfo
import androidx.room.Entity
import androidx.room.Index
import androidx.room.PrimaryKey

/** 批量任务状态。 */
object BatchStatus {
    const val PENDING = "pending"
    const val RUNNING = "running"
    const val PAUSED = "paused"
    const val COMPLETED = "completed"
    /** 用户取消：已处理的不回滚（写进文件的标签/歌词已经落盘） */
    const val CANCELLED = "cancelled"
    const val FAILED = "failed"
}

/** 单个条目的处理结果。 */
object BatchItemStatus {
    const val PENDING = "pending"
    const val SUCCESS = "success"
    const val FAILED = "failed"
    /** 无需处理（比如已有歌词、时长不匹配），不算失败 */
    const val SKIPPED = "skipped"
}

/**
 * 一次批量任务。
 *
 * [optionsJson] 存操作参数（目标歌词格式、重命名模板、要改的字段值…），
 * 用 JSON 而不是给每个操作加列：7 种操作的参数差别太大，加列会让表迅速变成一堆 nullable。
 */
@Entity(
    tableName = "lyrico_batch_task",
    indices = [Index(value = ["status"]), Index(value = ["createdAt"])],
)
data class BatchTaskEntity(
    @PrimaryKey(autoGenerate = true) val id: Long = 0,
    /** 见 [com.catclaw.music.kt.lyrico.batch.BatchOperation] 的 key */
    @ColumnInfo(name = "operation") val operation: String,
    val status: String = BatchStatus.PENDING,
    /** 展示用标题，例如「匹配歌词 · 12 首」 */
    val title: String = "",
    val optionsJson: String = "",
    val total: Int = 0,
    val succeeded: Int = 0,
    val failed: Int = 0,
    val skipped: Int = 0,
    val createdAt: Long = 0L,
    val startedAt: Long = 0L,
    val finishedAt: Long = 0L,
)

/**
 * 任务里的一个条目（一首歌）。
 *
 * 冗余存 [filePath] 与 [songTitle]：曲库后来被重扫/删除后，历史任务详情仍能显示，
 * 不会出现「一条记录只剩一个 id」。
 */
@Entity(
    tableName = "lyrico_batch_item",
    indices = [Index(value = ["taskId"]), Index(value = ["status"])],
)
data class BatchItemEntity(
    @PrimaryKey(autoGenerate = true) val id: Long = 0,
    val taskId: Long,
    val songId: Int,
    val filePath: String = "",
    val songTitle: String = "",
    val artist: String = "",
    val status: String = BatchItemStatus.PENDING,
    /** 失败原因 / 跳过原因 / 成功说明（如「已写入 增强 LRC」） */
    val message: String = "",
    val updatedAt: Long = 0L,
)
