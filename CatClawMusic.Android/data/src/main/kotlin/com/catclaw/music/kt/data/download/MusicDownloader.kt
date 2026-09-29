package com.catclaw.music.kt.data.download

import android.content.Context
import com.catclaw.music.kt.core.download.RangeDownloader
import com.catclaw.music.kt.core.model.Song
import com.catclaw.music.kt.data.db.CatClawDatabase
import com.catclaw.music.kt.data.db.entity.CachedSongEntity
import dagger.hilt.android.qualifiers.ApplicationContext
import java.io.File
import javax.inject.Inject
import javax.inject.Singleton
import kotlinx.coroutines.CancellationException
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.Job
import kotlinx.coroutines.SupervisorJob
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.launch
import java.util.concurrent.ConcurrentHashMap

enum class DownloadState { Running, Paused, Done, Failed }

data class DownloadTask(
    val songId: Int,
    val title: String,
    val fileName: String,
    val downloaded: Long,
    val total: Long,
    val state: DownloadState,
)

/**
 * 远程曲下载到应用私有的 downloads 目录，完成后在 CachedSongs 记账
 * （该表在 C# 侧就是「网络曲 → 本地文件」的映射，供缓存大小与清空缓存用，
 * 不参与选路：播放仍然走远程 URL，与 MAUI 行为一致）。
 */
@Singleton
class MusicDownloader @Inject constructor(
    @ApplicationContext private val context: Context,
    private val db: CatClawDatabase,
) {
    private val scope = CoroutineScope(SupervisorJob() + Dispatchers.IO)
    // 任务从 IO 线程与主线程两边读写，普通 map 会在扩容时读到半截状态
    private val jobs = ConcurrentHashMap<Int, Job>()

    private val directory: File
        get() = File(context.getExternalFilesDir(null), "downloads").apply { if (!exists()) mkdirs() }

    private val downloader = RangeDownloader(directory)

    /** 下载目录的绝对路径，供设置页展示 */
    fun directoryPath(): String = directory.absolutePath

    /** 下载目录占用（含未完成的任务） */
    fun directorySizeBytes(): Long = runCatching {
        directory.walkTopDown().filter { it.isFile }.sumOf { it.length() }
    }.getOrDefault(0L)

    private val _tasks = MutableStateFlow<List<DownloadTask>>(emptyList())
    val tasks = _tasks.asStateFlow()

    val cachedSongIds = db.libraryDao().observeCachedSongIds()

    /**
     * 排队时按 songId 记住 Song：重试与「删除（含文件）」都要拿得回原来的直链与文件名。
     *
     * 任务表是内存态（进程死了就空），这份映射和它同生同死，不去库里重建 ——
     * 远程曲的 FilePath 是**带签名的临时地址**，从库里读出来重试大概率已经失效。
     */
    private val songsById = mutableMapOf<Int, Song>()

    /** 已取消的 songId：挡掉取消之后才到达的最后几次进度回调（否则那一行会复活成"下载中"） */
    private val cancelled = mutableSetOf<Int>()

    fun enqueue(song: Song) {
        if (jobs[song.id]?.isActive == true) return
        if (urlOf(song) == null) return
        val fileName = fileNameFor(song)
        songsById[song.id] = song
        clearCancelled(song.id)
        replace(DownloadTask(song.id, song.title, fileName, 0, 0, DownloadState.Running))
        jobs[song.id] = scope.launch {
            try {
                val result = downloader.download(fileName, urlOf(song)!!) { done, total ->
                    // cancel 是异步的：pause 写下 Paused 之后最后一批进度回调还可能到达，
                    // 这里再写 Running 会让页面上的「已暂停」闪回「下载中」
                    if (isPaused(song.id)) return@download
                    replace(DownloadTask(song.id, song.title, fileName, done, total, DownloadState.Running))
                }
                upsert(song.id, fileName, result.file.length())
                replace(DownloadTask(song.id, song.title, fileName, result.totalBytes, result.totalBytes, DownloadState.Done))
            } catch (e: CancellationException) {
                // 暂停/取消由调用方改状态。`runCatching` 会把 CancellationException 一起吞成失败，
                // 真机上表现成：点「暂停」，那一行写着「下载失败」
                throw e
            } catch (e: Exception) {
                replace(DownloadTask(song.id, song.title, fileName, 0, 0, DownloadState.Failed))
            } finally {
                jobs.remove(song.id)
            }
        }
    }

    private fun isPaused(songId: Int): Boolean =
        _tasks.value.firstOrNull { it.songId == songId }?.state == DownloadState.Paused

    /** 「继续」/「重试」：任务还留在列表里，靠排队时记下的 Song 重新入队；`.part` 在就是断点续传 */
    fun resume(songId: Int) {
        songsById[songId]?.let { enqueue(it) }
    }

    /**
     * 「暂停」：停掉 Job 但**保留任务行与已下字节**。
     *
     * 已下量不能清零 —— RangeDownloader 留着 `.part`，进度条退回 0 会让人以为白下了。
     */
    fun pause(songId: Int) {
        jobs.remove(songId)?.cancel()
        val task = _tasks.value.firstOrNull { it.songId == songId } ?: return
        replace(task.copy(state = DownloadState.Paused))
    }

    /** 从列表里去掉这条任务；不动文件，也不动 `.part` */
    fun dismiss(songId: Int) {
        cancel(songId)
        songsById.remove(songId)
    }

    /** 删掉已下载的文件并撤掉 CachedSongs 记账；任务一并从列表去掉 */
    fun deleteFile(songId: Int) {
        val fileName = _tasks.value.firstOrNull { it.songId == songId }?.fileName
        cancel(songId)
        songsById.remove(songId)
        scope.launch {
            if (fileName != null) runCatching { File(directory, fileName).delete() }
            db.libraryDao().removeCached(songId)
        }
    }

    /** 取消：RangeDownloader 会保留 .part，下次 enqueue 从断点续 */
    fun cancel(songId: Int) {
        jobs.remove(songId)?.cancel()
        markCancelled(songId)
        _tasks.value = _tasks.value.filterNot { it.songId == songId }
    }

    suspend fun clearCache() {
        db.libraryDao().cachedSongs().forEach { entry ->
            val file = File(entry.localPath)
            if (file.canonicalPath.startsWith(directory.canonicalPath)) runCatching { file.delete() }
        }
        db.libraryDao().clearCached()
        _tasks.value = emptyList()
    }

    private fun urlOf(song: Song): String? =
        song.filePath.takeIf { it.startsWith("http", ignoreCase = true) }

    private fun fileNameFor(song: Song): String {
        val extension = song.filePath.substringBefore('?').substringAfterLast('.', "bin").lowercase()
            .takeIf { it.length in 1..5 && it.all(Char::isLetterOrDigit) } ?: "bin"
        val safe = song.title.filter { !"\"#%&*/:<=>?\\^|".contains(it) }.take(60).ifBlank { "song" }
        return "${song.id}_$safe.$extension"
    }

    private fun replace(task: DownloadTask) {
        // 取消之后 socket 上可能还有最后几次回调没走完，它们会把这一行重新"复活"成下载中
        if (songIdIn(cancelled, task.songId)) return
        _tasks.value = _tasks.value.filterNot { it.songId == task.songId } + task
    }

    private fun songIdIn(set: MutableSet<Int>, songId: Int): Boolean = synchronized(set) { songId in set }

    private fun markCancelled(songId: Int) = synchronized(cancelled) { cancelled.add(songId) }

    private fun clearCancelled(songId: Int) = synchronized(cancelled) { cancelled.remove(songId) }

    private fun upsert(songId: Int, fileName: String, size: Long) {
        scope.launch {
            db.libraryDao().upsertCached(
                CachedSongEntity(
                    songId = songId,
                    localPath = File(directory, fileName).absolutePath,
                    cachedAt = System.currentTimeMillis(),
                    fileSize = size,
                ),
            )
        }
    }
}
