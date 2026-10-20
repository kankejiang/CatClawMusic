package com.catclaw.music.kt.core.download

import kotlinx.coroutines.CompletableDeferred

/**
 * 下载并发闸门 —— 对应 C# `DownloadManager` 里 `_slotWake` 那套。
 *
 * 为什么不用 `kotlinx.coroutines.sync.Semaphore` 了事：许可是**一次性**的，
 * 而这里的上限可以在运行中调（设置页那个滑杆）。把上限调高时，正排着队的任务必须**立刻**开工
 * （C# 就是 `if (delta > 0) _slotWake.Release(delta)`）；调低时则只能等已在跑的任务自然结束，
 * 不能去打断它们 —— 这两条不对称的语义用「显式计数 + 等待队列」写出来才不会含糊。
 *
 * 名额只有两种去向：新任务直接占，或从队首移交给一个等待者。所以 [release] 里**要么**唤醒一个
 * 等待者（名额转移，计数不变），**要么**把计数减一 —— 两件事只做一件，不会多也不会少。
 */
class DownloadGate(limit: Int = DEFAULT_CONCURRENT) {

    private val lock = Any()
    private var active = 0
    private var limit = clampConcurrent(limit)
    private val waiters = ArrayDeque<CompletableDeferred<Unit>>()

    /** 取一个名额；取不到就在队列里等（可取消：暂停/取消时协程被取消，等待自然结束） */
    suspend fun acquire() {
        val token: CompletableDeferred<Unit>
        synchronized(lock) {
            if (active < limit) {
                active++
                return
            }
            token = CompletableDeferred()
            waiters.addLast(token)
        }
        token.await()
    }

    /** 交还名额：有人等着就移交给队首（计数不变），没人等才真的减一 */
    fun release() {
        val next: CompletableDeferred<Unit>? = synchronized(lock) {
            if (waiters.isEmpty()) {
                if (active > 0) active--
                null
            } else {
                waiters.removeFirst()
            }
        }
        next?.complete(Unit)
    }

    /** 调整上限。调高会立刻唤醒相应数量的等待者；调低不会打断已在跑的任务 */
    fun setLimit(value: Int) {
        synchronized(lock) { limit = clampConcurrent(value) }
        drain()
    }

    private fun drain() {
        while (true) {
            val next = synchronized(lock) {
                if (active < limit && waiters.isNotEmpty()) {
                    active++
                    waiters.removeFirst()
                } else {
                    null
                }
            } ?: return
            next.complete(Unit)
        }
    }

    companion object {
        /** 与 C# `DefaultConcurrent` 同值 */
        const val DEFAULT_CONCURRENT = 2
        const val MIN_CONCURRENT = 1
        const val MAX_CONCURRENT = 5

        /** 夹取到 [MIN_CONCURRENT, MAX_CONCURRENT]，与 C# `Math.Clamp(v, 1, 5)` 一致 */
        fun clampConcurrent(value: Int): Int = value.coerceIn(MIN_CONCURRENT, MAX_CONCURRENT)
    }
}
