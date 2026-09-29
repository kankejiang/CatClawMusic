package com.catclaw.music.kt.data.remote

/**
 * 导入曲库的进度。
 *
 * `total == 0` 表示"还在列远端目录，总数未知"—— 与 MAUI 那条
 * `IProgress<(done, total, label)>`（VM 里 total 为 0 就走不定进度条）同一约定。
 * 这里刻意**不带文案**：进度发生在 :data，而应用内语言是 Activity 的 `attachBaseContext`
 * 盖上去的，:data 拿到的 context 不一定带这个覆盖，把中文写死在这层就会跟着语言设置失灵。
 */
data class SyncProgress(val done: Int, val total: Int) {
    val unknownTotal: Boolean get() = total <= 0
}

/** 每多少首报一次进度：太密会让页面每帧重画，太疏又看不出在动 */
const val PROGRESS_STEP = 20
