package com.catclaw.music.kt.lyrico.batch

import com.catclaw.music.kt.data.db.LibraryDao
import com.catclaw.music.kt.lyrico.db.BatchItemDao
import com.catclaw.music.kt.lyrico.db.BatchItemEntity
import com.catclaw.music.kt.lyrico.db.BatchItemStatus
import com.catclaw.music.kt.lyrico.db.BatchStatus
import com.catclaw.music.kt.lyrico.db.BatchTaskDao
import com.catclaw.music.kt.lyrico.db.BatchTaskEntity
import java.util.concurrent.ConcurrentHashMap
import kotlinx.coroutines.CancellationException
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.ensureActive
import kotlinx.coroutines.flow.Flow
import kotlinx.coroutines.withContext

/**
 * 批量任务执行器。
 *
 * ## 串行而不是并行
 *
 * 每个条目都要写同一个文件（标签/歌词）甚至重命名它，并发会带来锁竞争与更难复现的
 * 半写状态；而且网络源（LRCLIB / 插件）对并发也不友好。所以**一次只处理一首**。
 *
 * ## 可中断、可恢复
 *
 * - 取消：置内存标记 + DB 状态，循环在条目边界检查，已写入的文件不回滚
 *   （写进文件的标签已经落盘，回滚反而危险）
 * - 恢复：进程被杀后残留的 running 任务在下次启动标为 failed，未处理条目仍是 pending，
 *   用户可以重新建任务；不自动续跑，避免用户不知情地又改一遍文件
 */
class BatchRunner(
    private val taskDao: BatchTaskDao,
    private val itemDao: BatchItemDao,
    private val libraryDao: LibraryDao,
    private val processors: BatchProcessors,
) {

    /** 需要取消的任务 id（内存标记，立刻生效，不必等 DB 回调） */
    private val cancelRequested = ConcurrentHashMap.newKeySet<Long>()

    fun observeTasks(): Flow<List<BatchTaskEntity>> = taskDao.observeTasks()
    fun observeTask(taskId: Long): Flow<BatchTaskEntity?> = taskDao.observeTask(taskId)
    fun observeItems(taskId: Long): Flow<List<BatchItemEntity>> = itemDao.observeItems(taskId)

    suspend fun task(taskId: Long): BatchTaskEntity? = taskDao.taskById(taskId)

    /** 建任务（不入队执行，由调用方决定何时跑）。 */
    suspend fun createTask(
        operation: BatchOperation,
        options: BatchOptions,
        songs: List<SelectedSong>,
        title: String,
    ): Long = withContext(Dispatchers.IO) {
        val now = System.currentTimeMillis()
        val taskId = taskDao.insertTask(
            BatchTaskEntity(
                operation = operation.key,
                status = BatchStatus.PENDING,
                title = title.ifBlank { "${operation.label} · ${songs.size} 首" },
                optionsJson = BatchOptionsCodec.encode(options),
                total = songs.size,
                createdAt = now,
            ),
        )
        itemDao.insertItems(
            songs.map { song ->
                BatchItemEntity(
                    taskId = taskId,
                    songId = song.id,
                    filePath = song.filePath,
                    songTitle = song.title,
                    artist = song.artist,
                )
            },
        )
        taskId
    }

    /** 执行任务。可重复调用：只处理仍为 pending 的条目。@return 结束状态（[BatchStatus] 常量） */
    suspend fun run(taskId: Long): String = withContext(Dispatchers.IO) {
        val task = taskDao.taskById(taskId) ?: return@withContext BatchStatus.FAILED
        val operation = BatchOperation.fromKey(task.operation) ?: return@withContext BatchStatus.FAILED
        val processor = processors.all().firstOrNull { it.operation == operation }
            ?: return@withContext BatchStatus.FAILED
        val options = BatchOptionsCodec.decode(task.optionsJson)

        taskDao.upsertTask(
            task.copy(status = BatchStatus.RUNNING, startedAt = System.currentTimeMillis()),
        )

        var succeeded = 0
        var failed = 0
        var skipped = 0

        // 先把已完成的计数恢复（续跑场景）
        succeeded += itemDao.countByStatus(taskId, BatchItemStatus.SUCCESS)
        failed += itemDao.countByStatus(taskId, BatchItemStatus.FAILED)
        skipped += itemDao.countByStatus(taskId, BatchItemStatus.SKIPPED)

        val pending = itemDao.pendingItems(taskId)
        for (item in pending) {
            if (cancelRequested.remove(taskId)) {
                finish(taskId, BatchStatus.CANCELLED, succeeded, failed, skipped)
                return@withContext BatchStatus.CANCELLED
            }
            ensureActive()

            val context = buildContext(item, options)
            val result = try {
                processor.process(context)
            } catch (ce: CancellationException) {
                finish(taskId, BatchStatus.CANCELLED, succeeded, failed, skipped)
                throw ce
            } catch (t: Throwable) {
                // 处理器本该自己吞异常，这里兜底：一首歌炸掉不能带崩整批
                BatchItemResult.Failed("${t.javaClass.simpleName}: ${t.message}")
            }

            when (result) {
                is BatchItemResult.Success -> {
                    succeeded++
                    itemDao.updateItem(item.id, BatchItemStatus.SUCCESS, result.message, System.currentTimeMillis())
                }
                is BatchItemResult.Skipped -> {
                    skipped++
                    itemDao.updateItem(item.id, BatchItemStatus.SKIPPED, result.reason, System.currentTimeMillis())
                }
                is BatchItemResult.Failed -> {
                    failed++
                    itemDao.updateItem(item.id, BatchItemStatus.FAILED, result.message, System.currentTimeMillis())
                }
            }
            // 每条都回写计数：进程中途被杀时详情页数据也不会全丢
            taskDao.upsertTask(
                task.copy(
                    status = BatchStatus.RUNNING,
                    startedAt = task.startedAt,
                    succeeded = succeeded,
                    failed = failed,
                    skipped = skipped,
                ),
            )
        }

        val finalStatus = if (failed > 0 && succeeded == 0 && skipped == 0) {
            BatchStatus.FAILED
        } else {
            BatchStatus.COMPLETED
        }
        finish(taskId, finalStatus, succeeded, failed, skipped)
        finalStatus
    }

    /** 请求取消：条目边界生效，已写入的不回滚。 */
    fun cancel(taskId: Long) {
        cancelRequested.add(taskId)
    }

    /** 启动时把上次残留的 running/paused 任务标为失败（未处理条目仍是 pending）。 */
    suspend fun recoverInterrupted(): Int = withContext(Dispatchers.IO) {
        val unfinished = taskDao.unfinishedTasks()
        unfinished.forEach {
            taskDao.finishTask(it.id, BatchStatus.FAILED, System.currentTimeMillis())
        }
        unfinished.size
    }

    suspend fun deleteTask(taskId: Long) = withContext(Dispatchers.IO) {
        cancelRequested.remove(taskId)
        itemDao.deleteItemsOf(taskId)
        taskDao.deleteTask(taskId)
    }

    suspend fun clearFinished(): Int = withContext(Dispatchers.IO) { taskDao.clearFinished() }

    private suspend fun finish(taskId: Long, status: String, ok: Int, failed: Int, skipped: Int) {
        taskDao.taskById(taskId)?.let { task ->
            taskDao.upsertTask(
                task.copy(
                    status = status,
                    succeeded = ok,
                    failed = failed,
                    skipped = skipped,
                    finishedAt = System.currentTimeMillis(),
                ),
            )
        }
        cancelRequested.remove(taskId)
    }

    private suspend fun buildContext(
        item: BatchItemEntity,
        options: BatchOptions,
    ): BatchItemContext {
        // 曲库可能已经被改动：优先用最新行，取不到就退回条目里冗余的快照
        val song = runCatching { libraryDao.songById(item.songId) }.getOrNull()
        val title = song?.title ?: item.songTitle
        val artist = song?.let { row ->
            runCatching { libraryDao.artistName(row.artistId) }.getOrNull()
        }.orEmpty().ifBlank { item.artist }
        val album = song?.let { row ->
            runCatching { libraryDao.albumTitle(row.albumId) }.getOrNull()
        }.orEmpty()
        return BatchItemContext(
            songId = item.songId,
            title = title,
            artist = artist,
            album = album,
            trackNumber = song?.trackNumber ?: 0,
            year = song?.year ?: 0,
            filePath = song?.filePath?.ifBlank { item.filePath } ?: item.filePath,
            durationMs = (song?.durationSeconds ?: 0).toLong() * 1000,
            options = options,
        )
    }
}

/** 建任务时传入的歌曲快照。 */
data class SelectedSong(
    val id: Int,
    val title: String,
    val artist: String = "",
    val album: String = "",
    val filePath: String = "",
)
