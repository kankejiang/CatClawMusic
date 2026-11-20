package com.catclaw.music.kt.data.lyrics

import javax.inject.Inject
import javax.inject.Singleton
import kotlinx.coroutines.flow.MutableSharedFlow
import kotlinx.coroutines.flow.SharedFlow
import kotlinx.coroutines.flow.asSharedFlow

/**
 * 歌词变更广播。
 *
 * 场景：歌词匹配页把候选写进音频内嵌标签后，**正在播放的那首歌**的播放页/全屏歌词
 * 还拿着旧数据 —— `LyricsViewModel` 只在切歌时重载，同一个 songId 不会重来一次。
 * 界面于是表现为「写入成功但歌词没变」，用户只能切歌一次才看到。
 *
 * 所以写入成功后广播 songId，播放侧的 ViewModel 收到且匹配当前歌曲时主动重载。
 *
 * 用 SharedFlow 而不是 StateFlow：这是**一次性事件**，重复订阅不该重放历史。
 * `extraBufferCapacity` 给一点余量，避免发送方在没有订阅者时被挂住。
 */
@Singleton
class LyricsRefreshBus @Inject constructor() {

    private val _events = MutableSharedFlow<Int>(extraBufferCapacity = 16)
    val events: SharedFlow<Int> = _events.asSharedFlow()

    /** @param songId 受影响歌曲的本地 id；0 或负数表示「无法定位，全部重载」 */
    fun notifyChanged(songId: Int) {
        _events.tryEmit(songId)
    }
}
