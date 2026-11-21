package com.catclaw.music.kt.lyrico.write

import javax.inject.Inject
import javax.inject.Singleton
import kotlinx.coroutines.flow.MutableSharedFlow
import kotlinx.coroutines.flow.SharedFlow
import kotlinx.coroutines.flow.asSharedFlow

/**
 * 歌曲元数据变更广播。
 *
 * ## 为什么需要
 *
 * 标签保存成功后，**文件已经改对了、曲库也同步了**，但详情页还显示旧标题。
 * 原因：`SongDetailScreen` 用 `produceState(initialValue, songId)` 取数据，
 * key 只有 songId —— 同一个 songId 不会重跑，所以页面不会重新读库。
 *
 * 真机验收时表现为「点保存 → 回到详情 → 标题没变」，很容易被误判成「保存失败」。
 *
 * 与 `LyricsRefreshBus` 同一个思路：写入方广播 songId，展示方收到后主动重载。
 */
@Singleton
class SongMetadataRefreshBus @Inject constructor() {

    private val _events = MutableSharedFlow<Int>(extraBufferCapacity = 16)
    val events: SharedFlow<Int> = _events.asSharedFlow()

    /** @param songId 受影响歌曲的本地 id；<=0 表示「无法定位，全部重载」 */
    fun notifyChanged(songId: Int) {
        _events.tryEmit(songId)
    }
}
