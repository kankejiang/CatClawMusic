package com.catclaw.music.kt.ui.netease

import com.catclaw.music.kt.core.diagnostic.AppLog
import com.catclaw.music.kt.core.playback.PlayMode
import com.catclaw.music.kt.core.playback.PlayerController
import com.catclaw.music.kt.data.netease.NeteaseApiClient
import com.catclaw.music.kt.data.netease.NeteasePlayUrlResolver
import com.catclaw.music.kt.data.netease.NeteasePlaylistClient
import com.catclaw.music.kt.data.netease.NeteaseSession
import com.catclaw.music.kt.data.service.PlaybackSessionStarter
import javax.inject.Inject
import javax.inject.Singleton
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.SupervisorJob
import kotlinx.coroutines.delay
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.launch

/**
 * 私人漫游电台（**功能态，不是页面**）—— 对齐插件 `PlayFmSongAsync` / `EnsureFmBufferAsync`。
 *
 * 做成进程内单例，是因为入口不止一个：网易云页的「私人 FM」按钮、发现页的功能按钮
 * 都要能直接开播，且关掉页面后电台必须继续（插件的 VM 也是插件级单例、页面开关不影响补货）。
 *
 * 规则全部照插件：初拉 3 首、整批取链、`PlayMode.Sequential`（不循环早期歌曲）、
 * 播到本批最后一首再补 3 首、20 秒定时兜底、退出时恢复原播放模式。
 */
@Singleton
class NeteaseRadioController @Inject constructor(
    private val api: NeteaseApiClient,
    private val resolver: NeteasePlayUrlResolver,
    private val player: PlayerController,
    private val playbackSession: PlaybackSessionStarter,
    private val session: NeteaseSession,
) {
    private val scope = CoroutineScope(SupervisorJob() + Dispatchers.Main.immediate)

    private val _fmMode = MutableStateFlow(false)
    val fmMode: StateFlow<Boolean> = _fmMode.asStateFlow()

    private val _loggedIn = MutableStateFlow(session.isLoggedIn)
    val loggedIn: StateFlow<Boolean> = _loggedIn.asStateFlow()

    private var savedMode: PlayMode? = null
    private var refilling = false
    private var starting = false

    init {
        // 事件路径 + 定时兜底，与插件双路径一致（宿主可能不回调播完事件）
        scope.launch { player.state.collect { if (_fmMode.value) ensureBuffer() } }
        scope.launch {
            while (true) {
                delay(20_000)
                if (_fmMode.value) ensureBuffer()
            }
        }
    }

    /** 登录发生在网易云页，回到发现页时刷新一次 */
    fun refreshLogin() {
        _loggedIn.value = session.isLoggedIn
    }

    fun start() {
        if (starting) return
        scope.launch {
            starting = true
            try {
                val songs = runCatching { api.privateFm(FM_BATCH) }.getOrDefault(emptyList())
                if (songs.isEmpty()) return@launch
                val queue = songs.mapIndexed { index, song ->
                    val fallback = NeteasePlaylistClient.playUrlFallback(
                        song.id.toLongOrNull() ?: 0L,
                    )
                    val url = song.id.toLongOrNull()?.let {
                        runCatching { resolver.resolve(it) }.getOrNull()
                    }
                    song.toQueueSong(index, url?.takeIf { it.isNotBlank() } ?: fallback)
                }
                if (savedMode == null) savedMode = player.state.value.mode
                player.setMode(PlayMode.Sequential)
                playbackSession.ensure()
                player.playSongs(queue, 0)
                _fmMode.value = true
            } finally {
                starting = false
            }
        }
    }

    /** 播放非 FM 歌曲时退出电台，恢复进入前的播放模式（插件 LeaveFmMode） */
    fun stop() {
        if (!_fmMode.value && savedMode == null) return
        _fmMode.value = false
        savedMode?.let { player.setMode(it) }
        savedMode = null
    }

    private suspend fun ensureBuffer() {
        if (!_fmMode.value || refilling) return
        val current = player.state.value.currentSong ?: return
        val queueSongs = player.queueSongs.value
        if (queueSongs.isEmpty() || queueSongs.last().id != current.id) return
        refilling = true
        try {
            val existing = queueSongs.map { it.id }.toSet()
            val next = runCatching { api.privateFm(FM_BATCH) }.getOrDefault(emptyList())
                .filter { it.toQueueSong(0, "").id !in existing }
            if (next.isNotEmpty()) {
                player.appendSongs(next.mapIndexed { index, song ->
                    song.toQueueSong(index, NeteasePlaylistClient.playUrlFallback(song.id.toLongOrNull() ?: 0L))
                })
                AppLog.d(
                    "NeteaseFm",
                    "续播追加 ${next.size} 首，队列 ${queueSongs.size} -> ${queueSongs.size + next.size}",
                )
            }
        } finally {
            refilling = false
        }
    }

    private companion object {
        /** 插件 FmBatchSize：官方 3 首一批 */
        const val FM_BATCH = 3
    }
}
