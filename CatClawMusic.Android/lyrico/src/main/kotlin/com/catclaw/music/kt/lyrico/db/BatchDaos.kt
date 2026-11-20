package com.catclaw.music.kt.lyrico.db

import androidx.room.Dao
import androidx.room.Insert
import androidx.room.Query
import androidx.room.Update
import androidx.room.Upsert
import kotlinx.coroutines.flow.Flow

@Dao
interface BatchTaskDao {

    @Insert
    suspend fun insertTask(task: BatchTaskEntity): Long

    @Upsert
    suspend fun upsertTask(task: BatchTaskEntity)

    @Update
    suspend fun updateTask(task: BatchTaskEntity)

    @Query("SELECT * FROM lyrico_batch_task ORDER BY createdAt DESC")
    fun observeTasks(): Flow<List<BatchTaskEntity>>

    @Query("SELECT * FROM lyrico_batch_task ORDER BY createdAt DESC LIMIT :limit")
    suspend fun recentTasks(limit: Int = 50): List<BatchTaskEntity>

    @Query("SELECT * FROM lyrico_batch_task WHERE id = :id")
    suspend fun taskById(id: Long): BatchTaskEntity?

    @Query("SELECT * FROM lyrico_batch_task WHERE id = :id")
    fun observeTask(id: Long): Flow<BatchTaskEntity?>

    /** 上次被中断的任务（进程被杀后残留 running/paused），启动时可标为失败 */
    @Query("SELECT * FROM lyrico_batch_task WHERE status IN ('running', 'paused')")
    suspend fun unfinishedTasks(): List<BatchTaskEntity>

    @Query("UPDATE lyrico_batch_task SET status = :status, finishedAt = :finishedAt WHERE id = :id")
    suspend fun finishTask(id: Long, status: String, finishedAt: Long)

    @Query("DELETE FROM lyrico_batch_task WHERE id = :id")
    suspend fun deleteTask(id: Long)

    @Query("DELETE FROM lyrico_batch_task WHERE status IN ('completed', 'cancelled', 'failed')")
    suspend fun clearFinished(): Int
}

@Dao
interface BatchItemDao {

    @Insert
    suspend fun insertItems(items: List<BatchItemEntity>): List<Long>

    @Query("SELECT * FROM lyrico_batch_item WHERE taskId = :taskId ORDER BY id")
    fun observeItems(taskId: Long): Flow<List<BatchItemEntity>>

    @Query("SELECT * FROM lyrico_batch_item WHERE taskId = :taskId ORDER BY id")
    suspend fun itemsOf(taskId: Long): List<BatchItemEntity>

    /** 只取待处理的：任务被取消后重启不会重复处理已完成的条目 */
    @Query("SELECT * FROM lyrico_batch_item WHERE taskId = :taskId AND status = 'pending' ORDER BY id")
    suspend fun pendingItems(taskId: Long): List<BatchItemEntity>

    @Query("SELECT * FROM lyrico_batch_item WHERE id = :id")
    suspend fun itemById(id: Long): BatchItemEntity?

    @Query("UPDATE lyrico_batch_item SET status = :status, message = :message, updatedAt = :at WHERE id = :id")
    suspend fun updateItem(id: Long, status: String, message: String, at: Long)

    @Query("SELECT COUNT(*) FROM lyrico_batch_item WHERE taskId = :taskId AND status = :status")
    suspend fun countByStatus(taskId: Long, status: String): Int

    @Query("DELETE FROM lyrico_batch_item WHERE taskId = :taskId")
    suspend fun deleteItemsOf(taskId: Long)
}
