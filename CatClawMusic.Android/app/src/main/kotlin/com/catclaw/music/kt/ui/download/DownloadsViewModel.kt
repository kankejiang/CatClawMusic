package com.catclaw.music.kt.ui.download

import androidx.lifecycle.ViewModel
import androidx.lifecycle.viewModelScope
import com.catclaw.music.kt.data.download.DownloadTask
import com.catclaw.music.kt.data.repository.download.DownloadsRepository
import dagger.hilt.android.lifecycle.HiltViewModel
import javax.inject.Inject
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.launch

/**
 * 下载管理 —— MAUI `DownloadsPage` 的对应物。
 *
 * 目录占用与删除后的重算全部走 [DownloadsRepository]（IO 线程）：
 * 旧实现把 `walkTopDown().sumOf { length() }` 放在 LaunchedEffect 里同步跑，进页会卡首帧。
 */
@HiltViewModel
class DownloadsViewModel @Inject constructor(
    private val downloads: DownloadsRepository,
) : ViewModel() {

    val tasks: StateFlow<List<DownloadTask>> = downloads.tasks

    val speedKbps: StateFlow<Int> = downloads.speedKbps

    fun cycleSpeedLimit() = downloads.cycleSpeedLimit()

    val concurrent: StateFlow<Int> = downloads.concurrent

    fun cycleConcurrent() = downloads.cycleConcurrent()

    private val _snapshot = MutableStateFlow(DownloadsRepository.Snapshot())
    val snapshot: StateFlow<DownloadsRepository.Snapshot> = _snapshot.asStateFlow()

    /**
     * 进页时读一次（IO 线程）。
     * 不在 init 里读：这页是设置页里的一层覆盖层，VM 不会随覆盖层销毁。
     */
    fun refresh() {
        viewModelScope.launch { _snapshot.value = downloads.snapshot() }
    }

    fun pause(songId: Int) = downloads.pause(songId)

    fun resume(songId: Int) = downloads.resume(songId)

    fun cancel(songId: Int) = downloads.cancel(songId)

    fun dismiss(songId: Int) = downloads.dismiss(songId)

    fun deleteFile(songId: Int) {
        viewModelScope.launch { _snapshot.value = downloads.deleteAndSnapshot(songId) }
    }
}
