package com.catclaw.music.kt.core.playback

import android.content.BroadcastReceiver
import android.content.Context
import android.content.Intent
import android.content.IntentFilter
import android.net.Uri
import android.os.Looper
import com.catclaw.music.kt.core.diagnostic.AppLog
import androidx.media3.common.AudioAttributes
import androidx.media3.common.C
import androidx.media3.common.MediaItem
import androidx.media3.common.MediaMetadata
import androidx.media3.common.PlaybackException
import androidx.media3.common.Player
import androidx.media3.exoplayer.ExoPlayer
import com.catclaw.music.kt.core.audio.AudioCache
import com.catclaw.music.kt.core.model.Song
import java.io.File
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.delay
import kotlinx.coroutines.flow.Flow
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.flow.flow
import kotlinx.coroutines.launch
import kotlinx.coroutines.withContext

data class PlaybackUiState(
    val currentSong: Song? = null,
    val isPlaying: Boolean = false,
    val isBuffering: Boolean = false,
    val mode: PlayMode = PlayMode.ListRepeat,
    val queueSize: Int = 0,
    val durationMs: Long = 0,
    val error: String? = null,
)

/**
 * 播放内核：[PlayQueue] 决定顺序与模式，ExoPlayer 持有同一顺序的媒体列表并被 MediaSession 直接暴露，
 * 这样锁屏 / 车机的上一曲下一曲才与 App 内一致。
 *
 * 与 MAUI 侧一致的取舍：
 * - **无 gapless**：仅有"下一首网络曲目提前落盘"的预缓冲（同 C# AudioCacheService.PreBufferSeconds 语义）；
 * - 随机模式复用同一份洗牌序循环，直到用户再次洗牌（洗牌序体现在列表顺序上，不开 ExoPlayer 自带 shuffle）；
 * - 上一曲走历史栈；
 * - 必须显式设 AudioAttributes：MIUI/HyperOS 上不设会路由错误表现为静音。
 */
class PlayerController(
    context: Context,
    private val cache: AudioCache,
    private val store: KeyValueStore,
    private val scope: CoroutineScope,
) : PlayerApi {
    private val appContext = context.applicationContext
    private val queue = PlayQueue()
    private val idToIndex = HashMap<Int, Int>()
    private var preBufferedForId = -1
    private var lastPlayerErrorAtMs = 0L
    private var prebufferWatcher: kotlinx.coroutines.Job? = null
    private var started = false

    private val _state = MutableStateFlow(PlaybackUiState())
    override val state: StateFlow<PlaybackUiState> = _state.asStateFlow()

    private val _queueSongs = MutableStateFlow<List<Song>>(emptyList())
    val queueSongs: StateFlow<List<Song>> = _queueSongs.asStateFlow()

    val player: ExoPlayer by lazy {
        ExoPlayer.Builder(appContext)
            .setMediaSourceFactory(
                androidx.media3.exoplayer.source.DefaultMediaSourceFactory(
                    RemoteAuthDataSources.factory(appContext),
                ),
            )
            .build().apply {
            setAudioAttributes(
                AudioAttributes.Builder()
                    .setUsage(C.USAGE_MEDIA)
                    .setContentType(C.AUDIO_CONTENT_TYPE_MUSIC)
                    .build(),
                /* handleAudioFocus = */ true,
            )
            addListener(playerListener)
        }
    }

    private val noisyReceiver = object : BroadcastReceiver() {
        override fun onReceive(ctx: Context, intent: Intent) {
            if (intent.action == ACTION_AUDIO_BECOMING_NOISY && player.isPlaying) player.pause()
        }
    }

    /**
     * 必须在主线程调用：ExoPlayer 的构造与全部 setter 都带 application-looper 线程校验，
     * 跨线程访问会抛 IllegalStateException 或造成 native 数据竞争。
     * 播放器不在构造函数里建、队列不在构造函数里恢复，避免 Hilt 在任意线程构造本类时踩到该约束。
     */
    fun start() {
        if (started) return
        check(Looper.myLooper() == Looper.getMainLooper()) { "PlayerController.start() 必须在主线程调用" }
        started = true
        player
        scope.launch { restore() }
        runCatching { appContext.registerReceiver(noisyReceiver, IntentFilter(ACTION_AUDIO_BECOMING_NOISY)) }
    }

    override var volume: Float
        get() = player.volume
        set(value) {
            player.volume = value.coerceIn(0f, 1f)
        }

    /** 用一批歌替换队列并从 startIndex 播 */
    override fun playSongs(songs: List<Song>, startIndex: Int) {
        if (songs.isEmpty()) return
        queue.setSongs(songs)
        val start = songs.getOrNull(startIndex) ?: return
        queue.selectSong(start.id)
        syncPlayerList(resumeAtMs = 0, autoplay = true)
        publish()
        preBufferedForId = -1
        save()
    }

    /** 队列的只读快照，供 AI 工具与统计展示；改队列必须走下面的显式方法 */
    override fun queueSongs(): List<Song> = queue.songs()

    /** 当前播放位置（毫秒）。UI 走 positionMsFlow 轮询，工具这种一次性读取用这个 */
    override fun currentPositionMs(): Long = player.currentPosition

    fun playSongAt(index: Int) {
        queue.songs().getOrNull(index)?.let {
            queue.selectSong(it.id)
            syncPlayerList(resumeAtMs = 0, autoplay = true)
            publish()
            save()
        }
    }

    /**
     * 播放/暂停翻的是**用户意图** `playWhenReady`，不是 `player.isPlaying`。
     *
     * `isPlaying == playWhenReady && playbackState == READY`，所以缓冲期间它是 false 而意图是 true。
     * 若按 `isPlaying` 判断再决定 play/pause，缓冲中点按钮会走到 `play()` —— 而 `playWhenReady` 本来就是 true，
     * 于是**什么都不变**，按钮看起来是死的（真机上就是这么复现的：日志 playing=true，按钮仍是 ▶，点三次无反应）。
     */
    fun togglePlayPause() {
        if (player.mediaItemCount == 0) {
            queue.songs().firstOrNull()?.let { playSongAt(0) }
            return
        }
        player.playWhenReady = !player.playWhenReady
    }

    override fun pause() = player.pause()

    override fun resume() = player.play()

    override fun seekTo(positionMs: Long) = player.seekTo(positionMs.coerceAtLeast(0))

    override fun next() {
        queue.next()?.let { moveToCurrent(it, resumeAtMs = 0) }
        publish()
        save()
    }

    /** 上一曲走历史栈；播放超 3 秒时先回到本曲开头（播放器惯例，MAUI 同行为） */
    override fun previous() {
        if (player.currentPosition > 3_000) {
            player.seekTo(0)
            return
        }
        queue.previous()?.let { moveToCurrent(it, resumeAtMs = 0) }
        publish()
        save()
    }

    fun setMode(mode: PlayMode) {
        queue.playMode = mode
        syncPlayerList(resumeAtMs = player.currentPosition)
        publish()
        save()
    }

    /** 顺序 → 随机 → 单曲循环 → 列表循环 → 顺序 */
    fun cycleMode() {
        val modes = PlayMode.entries
        setMode(modes[(modes.indexOf(queue.playMode) + 1) % modes.size])
    }

    override fun addToQueue(song: Song, nextUp: Boolean) {
        if (nextUp) queue.addNext(song) else queue.addToEnd(song)
        syncPlayerList(resumeAtMs = player.currentPosition)
        publish()
        save()
    }

    fun removeFromQueue(songId: Int) {
        if (queue.removeSong(songId)) {
            syncPlayerList(resumeAtMs = player.currentPosition)
            publish()
            save()
        }
    }

    override fun clearQueue() {
        queue.setSongs(emptyList())
        player.stop()
        _queueSongs.value = emptyList()
        idToIndex.clear()
        publish(current = null)
        save()
    }

    /** 冷流：仅在前台 collect 期间轮询进度，不留常驻定时器 */
    fun positionMsFlow(intervalMs: Long = 250): Flow<Long> = flow {
        while (true) {
            emit(player.currentPosition.coerceAtLeast(0))
            delay(intervalMs)
        }
    }

    fun release() {
        if (!started) return
        runCatching { appContext.unregisterReceiver(noisyReceiver) }
        player.removeListener(playerListener)
        player.release()
    }

    /** 把队列当前顺序灌进播放器；ExoPlayer 的列表即 UI / 媒体会话看到的列表 */
    private fun syncPlayerList(resumeAtMs: Long, autoplay: Boolean = false) {
        val songs = queue.songs()
        publishQueue(songs)
        // 一次性批量设置：clearMediaItems + 逐条 addMediaItem 在几百首时会把主线程拖到 ANR
        applyItems(songs.map { buildMediaItem(it) }, resumeAtMs, autoplay)
    }

    private fun publishQueue(songs: List<Song>) {
        idToIndex.clear()
        songs.forEachIndexed { i, s -> idToIndex[s.id] = i }
        _queueSongs.value = songs
    }

    private fun applyItems(items: List<MediaItem>, resumeAtMs: Long, autoplay: Boolean) {
        player.setMediaItems(items)
        applyRepeatAndShuffle()

        val index = queue.currentSong?.let { idToIndex[it.id] } ?: -1
        if (index >= 0) {
            player.seekTo(index, resumeAtMs)
            player.prepare()
            // 只有显式的播放指令才置 playWhenReady；换模式/增删曲目不该顺手开播
            if (autoplay) player.playWhenReady = true
        } else {
            player.prepare()
        }
    }

    private fun applyRepeatAndShuffle() {
        player.shuffleModeEnabled = false
        player.repeatMode = when (queue.playMode) {
            PlayMode.Sequential -> Player.REPEAT_MODE_OFF
            PlayMode.SingleRepeat -> Player.REPEAT_MODE_ONE
            PlayMode.ListRepeat, PlayMode.Shuffle -> Player.REPEAT_MODE_ALL
        }
    }

    /** 队列已推进到 currentSong，只移动播放器位置，不重排列表 */
    private fun moveToCurrent(song: Song, resumeAtMs: Long) {
        val index = idToIndex[song.id] ?: run {
            syncPlayerList(resumeAtMs, autoplay = true)
            return
        }
        // 同一首歌且只是从暂停恢复：直接 play()，重新加载会从头播（C# 侧同规则）
        if (player.currentMediaItem?.mediaId == song.id.toString() && !player.isPlaying && resumeAtMs == 0L) {
            player.play()
            publish(song)
            return
        }
        preBufferedForId = -1
        if (song.isRemote) {
            scope.launch {
                runCatching { cache.getOrCreate(song.filePath) }
                    .onSuccess { player.seekTo(index, 0); player.playWhenReady = true }
                    .onFailure { publish(song, error = it.message) }
            }
        } else {
            player.seekTo(index, resumeAtMs)
            player.playWhenReady = true
        }
        publish(song)
    }

    private fun buildMediaItem(song: Song): MediaItem =
        MediaItem.Builder()
            .setUri(uriOf(song))
            .setMediaId(song.id.toString())
            .setMediaMetadata(
                MediaMetadata.Builder()
                    .setTitle(song.title)
                    .setArtist(song.artist)
                    .setAlbumTitle(song.album)
                    .setMediaType(MediaMetadata.MEDIA_TYPE_MUSIC)
                    .setDurationMs(if (song.durationSeconds > 0) song.durationSeconds * 1000L else null)
                    .build(),
            )
            .build()

    private fun uriOf(song: Song): Uri {
        val path = song.filePath
        return if (path.contains("://")) Uri.parse(path) else Uri.fromFile(File(path))
    }

    /**
     * 下一首若是网络曲目，在其剩余 [AudioCache.PRE_BUFFER_SECONDS] 秒内提前落到磁盘缓存。
     * 与 C# NowPlayingViewModel 一致：按"当前曲 id"加闩，一首歌只触发一次；本地曲目与已缓存的直接跳过。
     */
    private fun maybePrebufferNext() {
        val current = queue.currentSong ?: return
        if (preBufferedForId == current.id) return
        val durationSec = _state.value.durationMs / 1000.0
        if (durationSec <= 0) return
        if (durationSec - player.currentPosition / 1000.0 > AudioCache.PRE_BUFFER_SECONDS) return
        preBufferedForId = current.id
        val upcoming = queue.peekNextSong() ?: return
        if (!upcoming.isRemote) return
        if (cache.cachedFile(upcoming.filePath) != null) return
        scope.launch { runCatching { cache.getOrCreate(upcoming.filePath) } }
    }

    /**
     * 每开始一次聆听回调一次。:core 不碰数据库，所以由 :data 侧（服务）接给播放历史记录器。
     * 判定规则在 [ListenCounter] —— 挂错地方要么虚高要么漏记，两种都静默。
     */
    var onListenStarted: ((Int) -> Unit)? = null
    private val listens = ListenCounter()

    /**
     * 音频会话就绪时回调（STATE_READY）。:core 不认识 android.media.audiofx，音效层由 :data 接。
     *
     * 为什么不在播放器建好时就给会话 id：ExoPlayer 在 AudioTrack 创建前 `audioSessionId` 是 0，
     * 挂到 0 等于把均衡器绑到全局会话上，对歌曲不生效且之后不会再更新（C# 侧踩过，注释里写着）。
     */
    var onAudioSessionReady: ((Int) -> Unit)? = null

    private fun publish(current: Song? = queue.currentSong, error: String? = null) {
        _state.value = _state.value.copy(
            currentSong = current,
            mode = queue.playMode,
            queueSize = _queueSongs.value.size,
            isPlaying = player.playWhenReady,
            isBuffering = player.playbackState == Player.STATE_BUFFERING,
            durationMs = if (player.duration > 0) player.duration else (current?.durationSeconds ?: 0) * 1000L,
            error = error,
        )
        val s = _state.value
        AppLog.i(TAG, "state song=${current?.id} mode=${s.mode} playing=${s.isPlaying} size=${s.queueSize} err=$error")
        // 没接监听器时**不许**推进计数器：否则 restore() 那一次 publish 就把当前歌"用掉"了，
        // 之后同一首再怎么播都不会再记 —— 真机上就是这么漏的（队列里恢复出的歌与用户点的正是同一首）
        val listener = onListenStarted
        if (listener != null) {
            if (s.queueSize == 0) listens.onQueueCleared()
            listens.onPublish(current?.id ?: 0, s.isPlaying)?.let { id -> listener(id) }
        }
    }

    fun save() {
        val snapshot = queue.snapshot()
        if (snapshot.songs.isEmpty()) return
        // 几百首的队列编码不该占主线程
        scope.launch(Dispatchers.IO) { store.put(KEY, QueueSnapshotCodec.encode(snapshot)) }
    }

    /** 冷启动恢复：读盘、解码、几百首的 MediaItem 构造全在 IO，只有落到播放器这一步占主线程 */
    private suspend fun restore() {
        val snapshot = withContext(Dispatchers.IO) {
            val text = store.get(KEY) ?: return@withContext null
            runCatching { QueueSnapshotCodec.decode(text) }.getOrNull()
        } ?: return
        if (snapshot.songs.isEmpty()) return
        queue.restorePersisted(snapshot.songs, snapshot.currentIndex, snapshot.mode)
        val songs = queue.songs()
        publishQueue(songs)
        val items = withContext(Dispatchers.IO) { songs.map { buildMediaItem(it) } }
        // 恢复期间用户可能已经手动点播，别再拿恢复前的旧队列盖回去
        if (player.mediaItemCount != 0) return
        applyItems(items, resumeAtMs = 0, autoplay = false)
        player.playWhenReady = false
        publish()
    }

    private val playerListener = object : Player.Listener {
        override fun onIsPlayingChanged(isPlaying: Boolean) {
            // 不要再往 isPlaying 里写这个值 —— 那个字段的含义是"用户意图"(playWhenReady)，
            // 由 publish() 单点推导。两处用不同定义写同一个字段，就会出现
            // "日志说 playing=true、按钮却画着 ▶" 这种自相矛盾的界面。
            publish()
            if (isPlaying) {
                queue.currentSong?.let { save() }
                if (prebufferWatcher?.isActive != true) {
                    prebufferWatcher = scope.launch {
                        while (true) {
                            maybePrebufferNext()
                            delay(PREBUFFER_POLL_MS)
                        }
                    }
                }
            } else {
                prebufferWatcher?.cancel()
                prebufferWatcher = null
            }
        }

        override fun onPlaybackStateChanged(playbackState: Int) {
            when (playbackState) {
                Player.STATE_BUFFERING -> _state.value = _state.value.copy(isBuffering = true)
                Player.STATE_READY -> {
                    _state.value = _state.value.copy(
                        isBuffering = false,
                        durationMs = if (player.duration > 0) player.duration else _state.value.durationMs,
                    )
                    // 列表位置可能由系统控件改变，回来时把队列索引对齐并预缓冲下一首
                    syncQueueIndexToPlayer()
                    // 到这儿 AudioTrack 已经建好，会话 id 才是真值 —— 音效层挂在这里
                    onAudioSessionReady?.invoke(player.audioSessionId)
                }

                Player.STATE_ENDED -> {
                    if (player.repeatMode == Player.REPEAT_MODE_OFF) player.pause()
                }

                // IDLE 不能自己写 isPlaying=false：意图(playWhenReady)可能仍是 true，
                // 那样按钮画着 ▶ 而 togglePlayPause() 会把 true 翻成 false —— 同一个值再发布一次，
                // StateFlow 去重、不重绘，按钮就"点了没反应"。字段只在 publish() 里定义。
                Player.STATE_IDLE -> publish()
            }
        }

        override fun onEvents(player: Player, events: Player.Events) {
            if (events.containsAny(
                    Player.EVENT_TIMELINE_CHANGED,
                    Player.EVENT_MEDIA_ITEM_TRANSITION,
                )
            ) {
                syncQueueIndexToPlayer()
            }
        }

        override fun onPlayerError(error: PlaybackException) {
            publish(error = error.errorCodeName + ": " + (error.message ?: ""))
            // 首次解码错误按"自然播完"处理切下一首；5 秒内连续错误则退避不切歌。
            // 竞态/解码器故障会在切歌后立即再触发错误，无脑切歌会形成崩溃循环
            // （C# 侧实测：flac 解码丢帧 → BufferPool 耗尽 → 堆 7MB→99MB → GC 165ms → 被 LMK 杀）。
            val now = android.os.SystemClock.elapsedRealtime()
            val withinBackoff = now - lastPlayerErrorAtMs < ERROR_BACKOFF_MS
            lastPlayerErrorAtMs = now
            if (withinBackoff) player.pause() else next()
        }
    }

    /** 系统控件（锁屏/车机/耳机）可能直接改播放器位置，这里把队列索引拉齐 */
    private fun syncQueueIndexToPlayer() {
        val index = player.currentMediaItemIndex
        val song = _queueSongs.value.getOrNull(index) ?: return
        if (queue.currentSong?.id != song.id) {
            queue.selectSong(song.id)
            preBufferedForId = -1
            publish(song)
            save()
        }
    }

    private companion object {
        const val TAG = "CatClawPlayback"
        const val KEY = "play_queue_snapshot"
        const val ACTION_AUDIO_BECOMING_NOISY = "android.media.AUDIO_BECOMING_NOISY"
        const val PREBUFFER_POLL_MS = 500L
        const val ERROR_BACKOFF_MS = 5_000L
    }
}
