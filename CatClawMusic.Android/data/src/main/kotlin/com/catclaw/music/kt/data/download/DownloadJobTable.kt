package com.catclaw.music.kt.data.download

import java.util.concurrent.ConcurrentHashMap
import kotlinx.coroutines.Job

/**
 * 下载任务的 `Job` 表。单独拎出来只为一件事：**摘除必须认货**。
 *
 * 原来 `MusicDownloader` 里任务收尾时写的是 `jobs.remove(songId)` —— 无条件按键删。
 * 但这一格在任务活着的时候可能被换掉：`pause()` 把 J1 摘走并取消，紧接着 `resume()` 又放进 J2；
 * 若 J1 的 `finally` 在 J2 入表之后才跑到，它删掉的是 **J2**。之后：
 * - `pause()` / `cancel()` 拿不到 J2（表里已经没有了），**这一首再也停不下来**；
 * - `enqueue()` 的去重看的是 `jobs[songId]?.isActive`，也看不到 J2，于是再起一个 J3 ——
 *   两个写者往同一个 `.part` 里写，文件大概率废掉。
 *
 * [release] 用 `ConcurrentHashMap.remove(key, value)`：只有表里确实是这一份时才摘。
 */
class DownloadJobTable {
    private val jobs = ConcurrentHashMap<Int, Job>()

    /** 这一首是否还在下（排队去重看这个） */
    fun isActive(songId: Int): Boolean = jobs[songId]?.isActive == true

    fun jobOf(songId: Int): Job? = jobs[songId]

    /** 登记一份新任务。已经跑完的 Job 不入表 —— 表里留个死任务只是噪音 */
    fun put(songId: Int, job: Job) {
        if (job.isActive) jobs[songId] = job
    }

    /** 取消并取走（暂停 / 取消都用它）。表里没有这一首时返回 null */
    fun cancel(songId: Int): Job? = jobs.remove(songId)?.also { it.cancel() }

    /** 只摘掉「自己那一份」：表里已经换成别的 Job 时不动它 */
    fun release(songId: Int, job: Job): Boolean = jobs.remove(songId, job)
}
