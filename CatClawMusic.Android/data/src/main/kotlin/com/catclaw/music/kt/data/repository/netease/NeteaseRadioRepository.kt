package com.catclaw.music.kt.data.repository.netease

import com.catclaw.music.kt.core.diagnostic.AppLog
import com.catclaw.music.kt.core.playback.PlayMode
import com.catclaw.music.kt.core.playback.PlayerController
import com.catclaw.music.kt.data.netease.NeteaseApiClient
import com.catclaw.music.kt.data.netease.NeteasePlayUrlResolver
import com.catclaw.music.kt.data.netease.NeteaseSongMapper
import com.catclaw.music.kt.data.netease.NeteasePlaylistClient
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
class NeteaseRadioRepository @Inject constructor(
    private val api: NeteaseApiClient,
    private val resolver: NeteasePlayUrlResolver,
    private val player: PlayerController,
    private val playbackSession: PlaybackSessionStarter,
    private val account: NeteaseAccountRepository,
    @com.catclaw.music.kt.data.di.AppScope private val scope: CoroutineScope,
) {

    private val _fmMode = MutableStateFlow(false)
    val fmMode: StateFlow<Boolean> = _fmMode.asStateFlow()

    val loggedIn: StateFlow<Boolean> get() = account.loggedIn

    private var savedMode: PlayMode? = null
    private var refilling = false
    private var starting = false

    init {
        // 事件路径 + 定时兜底，与插件双路径一致（宿主可能不回调播完事件）
        scope.launch {
            player.state.collect { state ->
                // 外部换了队列（playSongs 清了 FM 态）→ 与插件 LeaveFmMode 一样恢复原模式
                if (!state.isFmMode && _fmMode.value) stop()
                if (_fmMode.value) ensureBuffer()
            }
        }
        scope.launch {
            while (true) {
                delay(20_000)
                if (_fmMode.value) ensureBuffer()
            }
        }
    }

    fun start() {
        if (starting) return
        scope.launch {
            starting = true
            try {
                val fmT0 = System.currentTimeMillis()
                val songs = runCatching { api.privateFm(FM_BATCH) }.getOrDefault(emptyList())
                com.catclaw.music.kt.core.diagnostic.AppLog.d(
                    "NeteaseTiming", "fm privateFm=${System.currentTimeMillis() - fmT0}ms n=${songs.size}",
                )
                if (songs.isEmpty()) return@launch
                // 只等第一首的直链（保证起播音质）；其余 outer 入队、READY 后并发升级 ——
                // 旧实现串行等 3 次取链，FM 点开要 2.5 秒才出声
                val firstUrl = songs.firstOrNull()?.id?.toLongOrNull()?.let {
                    runCatching { resolver.resolve(it) }.getOrNull()
                }
                val queue = songs.mapIndexed { index, song ->
                    val fallback = NeteasePlaylistClient.playUrlFallback(
                        song.id.toLongOrNull() ?: 0L,
                    )
                    val url = if (index == 0) firstUrl?.takeIf { it.isNotBlank() } ?: fallback else fallback
                    NeteaseSongMapper.toQueueSong(song, index, url)
                }
                if (savedMode == null) savedMode = player.state.value.mode
                playbackSession.ensure()
                player.playSongs(queue, 0)
                com.catclaw.music.kt.core.diagnostic.AppLog.d(
                    "NeteaseTiming", "fm playSongs done total=${System.currentTimeMillis() - fmT0}ms",
                )
                // playSongs 会清 FM 态，所以标记必须放在入队之后；
                // 播放页随即显示「无限」，且循环只剩两档
                player.setFmMode(true)
                player.setMode(PlayMode.Sequential)
                _fmMode.value = true
                // 其余几首并发升级直链（upgradeQueueItem 会跳过正在播的第一首）
                songs.drop(1).forEach { song ->
                    scope.launch {
                        val id = song.id.toLongOrNull() ?: return@launch
                        runCatching { resolver.resolve(id) }.getOrNull()?.let { url ->
                            player.upgradeQueueItem(NeteaseSongMapper.toQueueSong(song, 0, "").id, url)
                        }
                    }
                }
            } finally {
                starting = false
            }
        }
    }

    /** 播放非 FM 歌曲时退出电台，恢复进入前的播放模式（插件 LeaveFmMode） */
    fun stop() {
        if (!_fmMode.value && savedMode == null) return
        _fmMode.value = false
        player.setFmMode(false)
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
                .filter { NeteaseSongMapper.toQueueSong(it, 0, "").id !in existing }
            if (next.isNotEmpty()) {
                player.appendSongs(next.mapIndexed { index, song ->
                    NeteaseSongMapper.toQueueSong(song, index, NeteasePlaylistClient.playUrlFallback(song.id.toLongOrNull() ?: 0L))
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
