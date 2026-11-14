package com.catclaw.music.kt.data.repository.netease

import com.catclaw.music.kt.core.diagnostic.AppLog
import com.catclaw.music.kt.core.playback.PlayMode
import com.catclaw.music.kt.core.playback.PlayerController
import com.catclaw.music.kt.data.netease.NeteaseApiClient
import com.catclaw.music.kt.data.netease.OnlineSong
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
import kotlinx.coroutines.flow.stateIn
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
    @dagger.hilt.android.qualifiers.ApplicationContext private val context: android.content.Context,
) {

    private val prefs = context.getSharedPreferences("netease_fm", android.content.Context.MODE_PRIVATE)

    private val _fmMode = MutableStateFlow(false)
    val fmMode: StateFlow<Boolean> = _fmMode.asStateFlow()

    /** 推荐模式（插件 `_currentFmMode`）：DEFAULT/FAMILIAR/EXPLORE */
    private val _mode = MutableStateFlow(FmMode.DEFAULT)
    val mode: StateFlow<FmMode> = _mode.asStateFlow()

    /** 场景模式关键词（插件 `_currentFmMode` 为场景码时走关键词搜索池） */
    private val _scene = MutableStateFlow<String?>(null)
    val scene: StateFlow<String?> = _scene.asStateFlow()

    /** 顶部「私人漫游 · XXX」显示名 */
    val label: StateFlow<String> = kotlinx.coroutines.flow.combine(_mode, _scene) { mode, scene ->
        scene?.takeIf { it.isNotBlank() } ?: mode.label
    }.stateIn(scope, kotlinx.coroutines.flow.SharingStarted.Eagerly, FmMode.DEFAULT.label)

    val loggedIn: StateFlow<Boolean> get() = account.loggedIn

    private var savedMode: PlayMode? = null
    private var refilling = false
    private var starting = false
    /** 上次退出时仍在漫游：等播放器第一次发状态时恢复，避免与队列 restore 抢顺序 */
    private var restoring = prefs.getBoolean(KEY_ACTIVE, false)
    private val scenePool = ArrayDeque<OnlineSong>()
    private val sceneServed = HashSet<String>()
    private var scenePage = 1
    private var poolKeyword: String? = null

    init {
        val savedMode = prefs.getString(KEY_MODE, "DEFAULT")
        _mode.value = FmMode.entries.firstOrNull { it.code == savedMode || it.name == savedMode }
            ?: FmMode.DEFAULT
        _scene.value = prefs.getString(KEY_SCENE, null)?.takeIf { it.isNotBlank() }
        // 事件路径 + 定时兜底，与插件双路径一致（宿主可能不回调播完事件）
        scope.launch {
            player.state.collect { state ->
                if (restoring) {
                    // 冷启动：队列由 PlayerController.restore() 恢复，这里只补回 FM 身份，
                    // 不自动起播（不扰民），按播放/切歌即继续电台与补货
                    restoring = false
                    _fmMode.value = true
                    player.setFmMode(true)
                    player.setMode(PlayMode.Sequential)
                    return@collect
                }
                // 播放任意歌单（本地/在线）都会走 playSongs → 清 isFmMode：
                // 被动退出时不恢复旧播放模式，否则会覆盖新歌单刚设的随机/单曲循环
                if (!state.isFmMode && _fmMode.value) stop(restoreMode = false)
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

    /**
     * 插件 FillSceneBatchAsync：场景模式用中文名当关键词搜歌建池、按页增量取、已播过去重；
     * 推荐模式仍走 /api/v1/radio/get（带 mode）。两种都凑 FM_BATCH 首。
     */
    private suspend fun nextBatch(n: Int): List<OnlineSong> {
        val keyword = _scene.value
        if (keyword.isNullOrBlank()) {
            val apiMode = if (_mode.value == FmMode.PUZZLE) "DEFAULT" else _mode.value.code
            return runCatching { api.privateFm(n, apiMode) }.getOrDefault(emptyList())
        }
        if (poolKeyword != keyword) {
            scenePool.clear()
            sceneServed.clear()
            scenePage = 1
            poolKeyword = keyword
        }
        var attempts = 0
        while (scenePool.size < n && attempts < 6) {
            val before = scenePool.size
            val found = runCatching { api.searchSongs(keyword, scenePage++, 50) }.getOrNull().orEmpty()
            found.forEach { song -> if (song.id.isNotBlank() && sceneServed.add(song.id)) scenePool.addLast(song) }
            if (scenePool.size == before) break
            attempts++
        }
        return buildList { repeat(n) { scenePool.removeFirstOrNull()?.let(::add) } }
    }

    /** 插件 TrySetFmModeAsync：切模式 → 重新拉一批（保留电台态） */
    fun setMode(mode: FmMode) {
        _mode.value = mode
        _scene.value = null
        prefs.edit().putString(KEY_MODE, mode.code).putString(KEY_SCENE, "").apply()
        refresh()
    }

    /** 切场景：清池换关键词、重新拉一批（插件 TrySetFmModeAsync → FillSceneBatchAsync） */
    fun setScene(keyword: String?) {
        _scene.value = keyword
        prefs.edit().putString(KEY_SCENE, keyword ?: "").apply()
        refresh()
    }

    /** 官方私人漫游右上 ↻：立刻换一批 */
    fun refresh() {
        stop()
        start()
    }

    /** 插件 FmLikeAsync：FM 曲红心影响推荐，不走普通歌单 like */
    fun like(songId: String, liked: Boolean) {
        scope.launch { runCatching { api.fmLike(songId, liked) } }
    }

    /** 插件 FmTrashAsync：垃圾桶 + 立刻切下一首 */
    fun trash(songId: String) {
        scope.launch {
            if (runCatching { api.fmTrash(songId) }.getOrDefault(false)) player.next()
        }
    }

    fun start() {
        if (starting) return
        scope.launch {
            starting = true
            try {
                val fmT0 = System.currentTimeMillis()
                val songs = nextBatch(FM_BATCH)
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
                prefs.edit()
                    .putBoolean(KEY_ACTIVE, true)
                    .putString(KEY_MODE, _mode.value.code)
                    .putString(KEY_SCENE, _scene.value ?: "")
                    .apply()
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
    fun stop(restoreMode: Boolean = true) {
        if (!_fmMode.value && savedMode == null) return
        _fmMode.value = false
        prefs.edit().putBoolean(KEY_ACTIVE, false).apply()
        player.setFmMode(false)
        if (restoreMode) savedMode?.let { player.setMode(it) }
        savedMode = null
    }

    private suspend fun ensureBuffer() {
        if (!_fmMode.value || refilling) return
        val current = player.state.value.currentSong ?: return
        val queueSongs = player.queueSongs.value
        if (queueSongs.isEmpty()) return
        val currentIndex = queueSongs.indexOfFirst { it.id == current.id }
        if (currentIndex < 0) return
        // MAUI 插件维持「当前 + 后 3 首」的缓冲深度：不能等播到最后一首才补，
        // 否则一次取链 + 网络往返很容易赶不上切歌，表现成 FM 偶尔卡住/断档。
        // 剩 0~1 首就提前补，补完队列重新回到 3~4 首待播。
        val remaining = queueSongs.size - currentIndex - 1
        if (remaining >= 2) return
        refilling = true
        try {
            val existing = queueSongs.map { it.id }.toSet()
            var next = nextBatch(FM_BATCH)
                .filter { NeteaseSongMapper.toQueueSong(it, 0, "").id !in existing }
            if (next.isEmpty()) {
                // 插件：网易云常返回固定候选集，一批全被去重时放宽重试，保证电台不断
                AppLog.w("NeteaseFm", "补货首批全部被去重，放宽重试")
                next = nextBatch(FM_BATCH)
            }
            if (next.isNotEmpty()) {
                player.appendSongs(next.mapIndexed { index, song ->
                    NeteaseSongMapper.toQueueSong(song, index, NeteasePlaylistClient.playUrlFallback(song.id.toLongOrNull() ?: 0L))
                })
                AppLog.d(
                    "NeteaseFm",
                    "续播追加 ${next.size} 首（剩余 $remaining 首时触发），队列 ${queueSongs.size} -> ${queueSongs.size + next.size}",
                )
            }
        } finally {
            refilling = false
        }
    }

    private companion object {
        /** 插件 FmBatchSize：官方 3 首一批 */
        const val FM_BATCH = 3
        const val KEY_ACTIVE = "fm_active"
        const val KEY_MODE = "fm_mode"
        const val KEY_SCENE = "fm_scene"
    }
}

/** 私人漫游推荐模式（插件 GetFmModesAsync 的 3 档 mode） */
enum class FmMode(val code: String, val label: String) {
    DEFAULT("DEFAULT", "默认模式"),
    FAMILIAR("FAMILIAR", "熟悉模式"),
    EXPLORE("EXPLORE", "探索模式"),
    /** 官方互动玩法：接口没有对应 mode，取歌仍用 DEFAULT，只在播放页换成拼图封面 */
    PUZZLE("PUZZLE", "拼图模式"),
}
