package com.catclaw.music.kt.core.playback

import com.catclaw.music.kt.core.model.Song
import kotlinx.coroutines.flow.StateFlow

/**
 * 播放器面向「工具/自动化」这一层的窄接口。
 *
 * 存在的理由只有一个，但很实在：`PlayerController` 拖着 ExoPlayer 和 Context，
 * `MusicAgentTools` 直接依赖它就等于**所有 AI 工具都只能在真机上测**。
 * 抽出这几名之后，工具层可以在 JVM 里用假实现跑完全部参数与结果分支。
 *
 * 成员签名与 `PlayerController` 上已有的一一对应，不新增行为。
 */
interface PlayerApi {
    val state: StateFlow<PlaybackUiState>
    var volume: Float

    fun playSongs(songs: List<Song>, startIndex: Int = 0)
    fun pause()
    fun resume()
    fun next()
    fun previous()
    fun seekTo(positionMs: Long)
    fun currentPositionMs(): Long
    fun queueSongs(): List<Song>
    fun clearQueue()
    fun addToQueue(song: Song, nextUp: Boolean = false)
}

/** 从 UI/工具侧发起播放前，先把 MediaSessionService 拉起来（通知栏与锁屏控制要它） */
fun interface SessionLauncher {
    fun ensure()
}
