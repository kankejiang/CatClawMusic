package com.catclaw.music.kt.data.playback

import android.util.Log
import com.catclaw.music.kt.data.db.LibraryDao
import javax.inject.Inject
import javax.inject.Singleton
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.SupervisorJob
import kotlinx.coroutines.launch

/**
 * 把"开始一次聆听"落成 `PlayHistory` 的一行。
 *
 * 为什么要有这个类：`PlayerController` 在 `:core`，那里没有数据库也不该有；
 * 而回调又是 fire-and-forget 的，不能让用户点下去等一次磁盘写。
 * 所以 :core 只发一个"该记一首"的信号，落库、开 IO 协程都在这层。
 *
 * 这条链子此前是断的：`PlayHistory` 只有备份恢复会写，播放从来不记，
 * 于是「最近播放」和「常听排行」永远是空的 —— 而查询、页面、编译、测试全都是绿的。
 */
@Singleton
class PlayHistoryRecorder @Inject constructor(
    private val dao: LibraryDao,
) {
    private val scope = CoroutineScope(SupervisorJob() + Dispatchers.IO)

    /** C# 用的是 unix 秒（`DateTimeOffset.ToUnixTimeSeconds`），这里保持同一单位 */
    fun record(songId: Int) {
        if (songId <= 0) return
        Log.i(TAG, "record song=$songId")
        val nowSeconds = System.currentTimeMillis() / 1000L
        // 失败必须出声：这条链子整个是 fire-and-forget，静默吞掉就等于"最近播放"又是空的
        scope.launch {
            runCatching { dao.recordPlay(songId, nowSeconds) }
                .onFailure { Log.w(TAG, "record play $songId failed", it) }
        }
    }

    private companion object {
        const val TAG = "CatClawHistory"
    }
}
