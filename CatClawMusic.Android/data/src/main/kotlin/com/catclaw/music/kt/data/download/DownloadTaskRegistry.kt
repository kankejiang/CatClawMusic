package com.catclaw.music.kt.data.download

import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.flow.update

/**
 * 下载任务表的唯一写入点。
 *
 * 这些写原来散在 `MusicDownloader` 里，形如 `_tasks.value = _tasks.value.filterNot{…} + task`：
 * **读-改-写不原子**。进度回调在 IO 线程、排队/暂停在主线程，两边同时写时后写的那一份是拿
 * 旧快照算出来的，会把对方刚写进去的那一行整个抹掉 —— 页面上表现成任务行闪一下没了；
 * 而「暂停」被并发的进度回调按旧快照盖回『下载中』，正是 `isPaused()` 那道守卫挡不住的情形。
 *
 * [replace] 走 `MutableStateFlow.update`（CAS 重试循环），并发写不会互相覆盖。
 */
class DownloadTaskRegistry(
    /** 取消名单由调用方持有（它另有同步要求），这里只负责「已经删掉的行别被回调复活」 */
    private val isCancelled: (Int) -> Boolean = { false },
) {
    private val _tasks = MutableStateFlow<List<DownloadTask>>(emptyList())
    val tasks: StateFlow<List<DownloadTask>> = _tasks.asStateFlow()

    /** 同 songId 覆盖（挪到表尾），新 songId 追加 */
    fun replace(task: DownloadTask) {
        if (isCancelled(task.songId)) return
        _tasks.update { list -> list.filterNot { it.songId == task.songId } + task }
    }

    fun remove(songId: Int) {
        _tasks.update { list -> list.filterNot { it.songId == songId } }
    }

    fun find(songId: Int): DownloadTask? = _tasks.value.firstOrNull { it.songId == songId }

    fun clear() {
        _tasks.value = emptyList()
    }
}
