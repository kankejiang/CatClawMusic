package com.catclaw.music.kt.data.download

import java.util.concurrent.ConcurrentHashMap
import kotlinx.coroutines.Job

/**
 * 下载任务的 `Job` 表（键可以是库内 id，也可以是清洗后的文件名）。单独拎出来只为一件事：
 * **摘除必须认货**。
 *
 * 两个下载器原来收尾时都写 `jobs.remove(key)` —— 无条件按键删。而这一格在任务活着的时候
 * 可能被换掉：`pause()` 摘走并取消 J1，紧接着 `resume()` 又放进 J2；若 J1 的收尾在 J2 入表之后
 * 才跑到，它删掉的是 **J2**。之后：
 * - `pause()` / `cancel()` 拿不到 J2（表里已经没有了），**这一首再也停不下来**；
 * - 排队去重看的是 `isActive`，也看不到 J2，于是再起一个 J3 ——
 *   两个写者往同一个 `.part` 里写，文件大概率废掉。
 *
 * [release] 用 `ConcurrentHashMap.remove(key, value)`：只有表里确实是这一份时才摘。
 */
class JobTable<K : Any> {
    private val jobs = ConcurrentHashMap<K, Job>()

    /** 这个键是否还在下（排队去重看这个） */
    fun isActive(key: K): Boolean = jobs[key]?.isActive == true

    fun jobOf(key: K): Job? = jobs[key]

    fun activeKeys(): List<K> = jobs.filterValues { it.isActive }.keys.toList()

    /** 登记一份新任务。已经跑完的 Job 不入表 —— 表里留个死任务只是噪音 */
    fun put(key: K, job: Job) {
        if (job.isActive) jobs[key] = job
    }

    /** 取消并取走（暂停 / 取消都用它）。表里没有这个键时返回 null */
    fun cancel(key: K): Job? = jobs.remove(key)?.also { it.cancel() }

    /** 只摘掉「自己那一份」：表里已经换成别的 Job 时不动它 */
    fun release(key: K, job: Job): Boolean = jobs.remove(key, job)
}
