package com.catclaw.music.kt.ui.download

import androidx.lifecycle.ViewModel
import com.catclaw.music.kt.data.download.DownloadTask
import com.catclaw.music.kt.data.download.MusicDownloader
import dagger.hilt.android.lifecycle.HiltViewModel
import javax.inject.Inject
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow

/**
 * 下载管理 —— MAUI `DownloadsPage` 的对应物（磁力那半按既定判例不做）。
 *
 * 任务表是 `MusicDownloader` 的内存 StateFlow，这里只补两件它没有的：
 * 下载目录的路径与占用（磁盘上的数，进页现读一次，不跟着任务流走）。
 */
@HiltViewModel
class DownloadsViewModel @Inject constructor(
    private val downloader: MusicDownloader,
) : ViewModel() {

    data class Snapshot(val directoryPath: String = "", val directoryBytes: Long = 0L)

    val tasks: StateFlow<List<DownloadTask>> = downloader.tasks

    private val _snapshot = MutableStateFlow(Snapshot())
    val snapshot: StateFlow<Snapshot> = _snapshot.asStateFlow()

    /**
     * 进页时读一次。
     *
     * 不在 `init` 里读：这页是设置页里的一层覆盖层，`hiltViewModel()` 挂在设置的导航条目上，
     * 覆盖层关掉 VM 也不死 —— 只在 init 读会让第二次进来看到第一次的目录占用。
     */
    fun refresh() {
        _snapshot.value = Snapshot(
            directoryPath = downloader.directoryPath(),
            directoryBytes = downloader.directorySizeBytes(),
        )
    }

    fun pause(songId: Int) = downloader.pause(songId)

    fun resume(songId: Int) = downloader.resume(songId)

    fun cancel(songId: Int) = downloader.cancel(songId)

    fun dismiss(songId: Int) = downloader.dismiss(songId)

    fun deleteFile(songId: Int) {
        downloader.deleteFile(songId)
        // 删完立刻重读目录占用，否则页面上那个数还留着刚删掉的字节
        refresh()
    }
}
