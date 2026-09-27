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
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.Job
import kotlinx.coroutines.SupervisorJob
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.launch

enum class DownloadState { Running, Done, Failed }

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
    private val jobs = mutableMapOf<Int, Job>()

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

    fun enqueue(song: Song) {
        if (jobs[song.id]?.isActive == true) return
        if (urlOf(song) == null) return
        val fileName = fileNameFor(song)
        replace(DownloadTask(song.id, song.title, fileName, 0, 0, DownloadState.Running))
        jobs[song.id] = scope.launch {
            val outcome = runCatching {
                downloader.download(fileName, urlOf(song)!!) { done, total ->
                    replace(DownloadTask(song.id, song.title, fileName, done, total, DownloadState.Running))
                }
            }
            outcome.fold(
                onSuccess = {
                    upsert(song.id, fileName, it.file.length())
                    replace(DownloadTask(song.id, song.title, fileName, it.totalBytes, it.totalBytes, DownloadState.Done))
                },
                onFailure = {
                    replace(DownloadTask(song.id, song.title, fileName, 0, 0, DownloadState.Failed))
                },
            )
            jobs.remove(song.id)
        }
    }

    /** 取消：RangeDownloader 会保留 .part，下次 enqueue 从断点续 */
    fun cancel(songId: Int) {
        jobs.remove(songId)?.cancel()
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
        _tasks.value = _tasks.value.filterNot { it.songId == task.songId } + task
    }

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
