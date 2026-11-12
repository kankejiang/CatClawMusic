package com.catclaw.music.kt.data.repository.download

import com.catclaw.music.kt.data.di.IoDispatcher
import com.catclaw.music.kt.data.download.DownloadSettings
import com.catclaw.music.kt.data.download.DownloadTask
import com.catclaw.music.kt.data.download.MusicDownloader
import javax.inject.Inject
import javax.inject.Singleton
import kotlinx.coroutines.CoroutineDispatcher
import kotlinx.coroutines.delay
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.withContext

/**
 * 下载域唯一入口。
 *
 * 关键点：目录占用（`walkTopDown().sumOf { length() }`）是**磁盘遍历**，
 * 必须在 [IoDispatcher] 上跑 —— 旧实现由 `DownloadsViewModel.refresh()` 在
 * `LaunchedEffect` 里同步执行，进下载页会卡首帧（真机实测）。
 */
@Singleton
class DownloadsRepository @Inject constructor(
    private val downloader: MusicDownloader,
    private val settings: DownloadSettings,
    @IoDispatcher private val io: CoroutineDispatcher,
) {
    data class Snapshot(val directoryPath: String = "", val directoryBytes: Long = 0L)

    val tasks: StateFlow<List<DownloadTask>> = downloader.tasks

    private val _speedKbps = MutableStateFlow(settings.speedKbps)
    val speedKbps: StateFlow<Int> = _speedKbps.asStateFlow()

    private val _concurrent = MutableStateFlow(settings.concurrent)
    val concurrent: StateFlow<Int> = _concurrent.asStateFlow()

    fun cycleSpeedLimit() {
        _speedKbps.value = settings.nextSpeedKbps()
    }

    fun cycleConcurrent() {
        _concurrent.value = settings.nextConcurrent()
        downloader.setConcurrent(_concurrent.value)
    }

    /** 目录路径 + 占用（IO 线程遍历） */
    suspend fun snapshot(): Snapshot = withContext(io) {
        Snapshot(downloader.directoryPath(), downloader.directorySizeBytes())
    }

    /** 删除文件后再算一次占用；`deleteFile` 自身异步，稍等落盘 */
    suspend fun deleteAndSnapshot(songId: Int): Snapshot = withContext(io) {
        downloader.deleteFile(songId)
        delay(200)
        Snapshot(downloader.directoryPath(), downloader.directorySizeBytes())
    }

    fun pause(songId: Int) = downloader.pause(songId)

    fun resume(songId: Int) = downloader.resume(songId)

    fun cancel(songId: Int) = downloader.cancel(songId)

    fun dismiss(songId: Int) = downloader.dismiss(songId)
}
