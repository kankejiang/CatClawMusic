package com.catclaw.music.kt.ui.netease

import androidx.lifecycle.ViewModel
import androidx.lifecycle.viewModelScope
import com.catclaw.music.kt.core.model.ProtocolType
import com.catclaw.music.kt.core.model.Song
import com.catclaw.music.kt.core.model.SongSource
import com.catclaw.music.kt.core.playback.PlayerController
import com.catclaw.music.kt.data.netease.NeteaseApiClient
import com.catclaw.music.kt.data.netease.NeteaseLoginClient
import com.catclaw.music.kt.data.netease.NeteasePlayUrlResolver
import com.catclaw.music.kt.data.netease.NeteaseArtist
import com.catclaw.music.kt.data.netease.NeteasePlaylistClient
import com.catclaw.music.kt.data.netease.OnlinePlaylist
import com.catclaw.music.kt.data.netease.OnlineSong
import com.catclaw.music.kt.data.service.PlaybackSessionStarter
import dagger.hilt.android.lifecycle.HiltViewModel
import javax.inject.Inject
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.delay
import kotlinx.coroutines.launch

/**
 * 网易云在线音乐的状态机：首页（推荐歌单 + 榜单）、搜索、歌单详情、我的音乐、登录、播放。
 *
 * 播放桥接的做法：把 [OnlineSong] 转成 core 的 `Song` 塞进现有队列 —— 在线音乐不另造一套播放器。
 * `FilePath` 用 `song/media/outer/url` 跳转入口（稳定、可被 AudioCache 缓存），
 * 登录后当前曲再尝试用 eapi 换成直链（音质更高，但地址会过期，所以只用于这一首）。
 */
@HiltViewModel
class NeteaseMusicViewModel @Inject constructor(
    private val api: NeteaseApiClient,
    private val loginClient: NeteaseLoginClient,
    private val playUrlResolver: NeteasePlayUrlResolver,
    private val controller: PlayerController,
    private val playbackSession: PlaybackSessionStarter,
) : ViewModel() {

    data class Home(
        val loading: Boolean = false,
        val recommend: List<OnlinePlaylist> = emptyList(),
        val toplists: List<OnlinePlaylist> = emptyList(),
        val error: Boolean = false,
    )

    data class Search(
        val query: String = "",
        val loading: Boolean = false,
        val songs: List<OnlineSong> = emptyList(),
        val playlists: List<OnlinePlaylist> = emptyList(),
        val artists: List<NeteaseArtist> = emptyList(),
        val page: Int = 1,
        val loadingMore: Boolean = false,
        val hasMore: Boolean = false,
    )

    data class Detail(
        val playlist: OnlinePlaylist? = null,
        val songs: List<OnlineSong> = emptyList(),
        val loading: Boolean = false,
    )

    sealed interface Login {
        data object Idle : Login
        data object Busy : Login
        /** 短信已下发，等待用户填验证码 */
        data object SmsSent : Login
        data class Failed(val message: String) : Login
    }

    private val _home = MutableStateFlow(Home())
    val home: StateFlow<Home> = _home.asStateFlow()

    private val _search = MutableStateFlow(Search())
    val search: StateFlow<Search> = _search.asStateFlow()

    private val _detail = MutableStateFlow<Detail?>(null)
    val detail: StateFlow<Detail?> = _detail.asStateFlow()

    private val _account = MutableStateFlow<NeteaseApiClient.Account?>(null)
    val account: StateFlow<NeteaseApiClient.Account?> = _account.asStateFlow()

    private val _myPlaylists = MutableStateFlow<List<OnlinePlaylist>>(emptyList())
    val myPlaylists: StateFlow<List<OnlinePlaylist>> = _myPlaylists.asStateFlow()

    private val _login = MutableStateFlow<Login>(Login.Idle)
    val login: StateFlow<Login> = _login.asStateFlow()

    /** 验证码重发倒计时（秒）；0 表示可以发送 */
    private val _smsCountdown = MutableStateFlow(0)
    val smsCountdown: StateFlow<Int> = _smsCountdown.asStateFlow()

    /** 当前列表里已红心的歌（用于列表 ❤ 标记；登录后才有值） */
    private val _likedIds = MutableStateFlow<Set<String>>(emptySet())
    val likedIds: StateFlow<Set<String>> = _likedIds.asStateFlow()

    private val _toast = MutableStateFlow<String?>(null)
    val toast: StateFlow<String?> = _toast.asStateFlow()

    /** 播放音质：无损 / 320k / 128k（点击顶部胶囊循环） */
    private val _quality = MutableStateFlow(playUrlResolver.quality)
    val quality: StateFlow<NeteasePlaylistClient.Quality> = _quality.asStateFlow()

    fun cycleQuality() {
        _quality.value = playUrlResolver.cycleQuality()
        _toast.value = "音质：" + when (_quality.value) {
            NeteasePlaylistClient.Quality.Lossless -> "无损"
            NeteasePlaylistClient.Quality.Exhigh -> "320k"
            NeteasePlaylistClient.Quality.Standard -> "128k"
        }
    }

    /** 私人 FM 续播的防重入闩：一次只拉一批，避免同一首末尾歌触发多次请求 */
    private var fmRefilling = false

    init {
        viewModelScope.launch {
            _account.value = api.account()
            if (_account.value != null) {
                _myPlaylists.value = api.userPlaylists()
            }
        }
        // 私人 FM 无限续播：当前曲已是队列最后一首时预取下一批并追加（addMediaItems 不打断播放）
        viewModelScope.launch {
            controller.state.collect { state ->
                val detail = _detail.value ?: return@collect
                if (detail.playlist?.id != FM_ID) return@collect
                val current = state.currentSong ?: return@collect
                val queueSongs = controller.queueSongs.value
                if (queueSongs.isEmpty() || queueSongs.last().id != current.id) return@collect
                if (fmRefilling) return@collect
                fmRefilling = true
                try {
                    val existing = queueSongs.map { it.id }.toSet()
                    val next = runCatching { api.privateFm(FM_BATCH) }.getOrDefault(emptyList())
                        .filter { it.toSong(0).id !in existing }
                    if (next.isNotEmpty()) {
                        controller.appendSongs(next.mapIndexed { index, song -> song.toSong(index) })
                        _detail.value = _detail.value?.copy(songs = detail.songs + next)
                        com.catclaw.music.kt.core.diagnostic.AppLog.d(
                            "NeteaseFm",
                            "续播追加 ${next.size} 首，队列 ${queueSongs.size} -> ${queueSongs.size + next.size}",
                        )
                    }
                } finally {
                    fmRefilling = false
                }
            }
        }
    }

    fun loadHome() {
        if (_home.value.loading) return
        viewModelScope.launch {
            _home.value = _home.value.copy(loading = true, error = false)
            val recommend = runCatching { api.recommendPlaylists() }.getOrDefault(emptyList())
            val toplists = runCatching { api.toplists() }.getOrDefault(emptyList())
            _home.value = Home(
                loading = false,
                recommend = recommend,
                toplists = toplists,
                error = recommend.isEmpty() && toplists.isEmpty(),
            )
        }
    }

    fun search(query: String) {
        val keyword = query.trim()
        if (keyword.isEmpty()) {
            _search.value = Search()
            return
        }
        viewModelScope.launch {
            _search.value = Search(query = keyword, loading = true)
            val songs = runCatching { api.searchSongs(keyword, page = 1) }.getOrNull().orEmpty()
            val playlists = runCatching { api.searchPlaylists(keyword) }.getOrDefault(emptyList())
            val artists = runCatching { api.searchArtists(keyword, 12) }.getOrDefault(emptyList())
            _search.value = Search(
                query = keyword,
                loading = false,
                songs = songs,
                playlists = playlists,
                artists = artists,
                page = 1,
                hasMore = songs.size >= SEARCH_PAGE_SIZE,
            )
        }
    }

    /** 搜索结果下一页（追加，按 id 去重） */
    fun loadMoreSearch() {
        val current = _search.value
        if (current.query.isBlank() || current.loadingMore || !current.hasMore) return
        viewModelScope.launch {
            _search.value = current.copy(loadingMore = true)
            val nextPage = current.page + 1
            val more = runCatching {
                api.searchSongs(current.query, page = nextPage, pageSize = SEARCH_PAGE_SIZE)
            }.getOrNull().orEmpty()
            val existing = current.songs.map { it.id }.toSet()
            _search.value = _search.value.copy(
                songs = current.songs + more.filter { it.id !in existing },
                page = nextPage,
                loadingMore = false,
                hasMore = more.size >= SEARCH_PAGE_SIZE,
            )
        }
    }

    fun clearSearch() {
        _search.value = Search()
    }

    fun openPlaylist(playlist: OnlinePlaylist) {
        viewModelScope.launch {
            _detail.value = Detail(playlist = playlist, loading = true)
            val songs = runCatching { api.playlistSongs(playlist.id) }.getOrDefault(emptyList())
            _detail.value = Detail(playlist = playlist, songs = songs, loading = false)
        }
    }

    // ── 我的音乐（需登录） ─────────────────────────────────────

    fun openDaily() {
        viewModelScope.launch {
            val virtual = OnlinePlaylist(id = "daily", name = "每日推荐")
            _detail.value = Detail(virtual, loading = true)
            val songs = runCatching { api.dailyRecommend(50) }.getOrDefault(emptyList())
            _detail.value = Detail(virtual, songs, loading = false)
            if (songs.isEmpty()) _toast.value = "每日推荐为空，可能未登录或今天还没生成"
        }
    }

    fun openPrivateFm() {
        viewModelScope.launch {
            val virtual = OnlinePlaylist(id = "fm", name = "私人 FM")
            _detail.value = Detail(virtual, loading = true)
            val songs = runCatching { api.privateFm(30) }.getOrDefault(emptyList())
            _detail.value = Detail(virtual, songs, loading = false)
            if (songs.isEmpty()) _toast.value = "私人 FM 需要登录后才能使用"
        }
    }

    fun openLiked() {
        viewModelScope.launch {
            val virtual = OnlinePlaylist(id = "liked", name = "我喜欢的音乐")
            _detail.value = Detail(virtual, loading = true)
            val songs = runCatching { api.likedSongs() }.getOrDefault(emptyList())
            _detail.value = Detail(virtual, songs, loading = false)
            _likedIds.value = songs.map { it.id }.toSet()
            if (songs.isEmpty()) _toast.value = "还没有红心歌曲，或未登录"
        }
    }

    fun refreshMyPlaylists() {
        viewModelScope.launch {
            _myPlaylists.value = runCatching { api.userPlaylists() }.getOrDefault(emptyList())
        }
    }

    fun like(song: OnlineSong, like: Boolean) {
        viewModelScope.launch {
            val ok = runCatching { api.likeSong(song.id, like) }.getOrDefault(false)
            if (ok) {
                _likedIds.value = if (like) _likedIds.value + song.id else _likedIds.value - song.id
                _toast.value = if (like) "已加入我喜欢的音乐" else "已取消红心"
            } else {
                _toast.value = "操作失败，先确认已登录"
            }
        }
    }

    // ── 登录 ───────────────────────────────────────────────────

    fun sendSmsCode(phone: String, countryCode: String = "86") {
        if (_smsCountdown.value > 0) return
        if (_login.value is Login.Busy) return
        viewModelScope.launch {
            _login.value = Login.Busy
            val outcome = runCatching { loginClient.sendSmsCode(phone, countryCode) }.getOrNull()
            if (outcome?.ok == true) {
                _login.value = Login.SmsSent
                _smsCountdown.value = 60
                while (_smsCountdown.value > 0) {
                    delay(1_000)
                    _smsCountdown.value -= 1
                }
                if (_login.value is Login.SmsSent) _login.value = Login.Idle
            } else {
                _login.value = Login.Failed(outcome?.message ?: "验证码发送失败")
            }
        }
    }

    fun loginWithSms(phone: String, captcha: String, countryCode: String = "86") {
        if (_login.value is Login.Busy) return
        viewModelScope.launch {
            _login.value = Login.Busy
            val outcome = runCatching { loginClient.loginWithSms(phone, captcha, countryCode) }.getOrNull()
            if (outcome?.ok == true) {
                onLoggedIn()
            } else {
                _login.value = Login.Failed(outcome?.message ?: "登录失败")
            }
        }
    }

    fun loginWithPassword(account: String, password: String, countryCode: String = "86") {
        if (_login.value is Login.Busy) return
        viewModelScope.launch {
            _login.value = Login.Busy
            val outcome = runCatching {
                loginClient.loginWithPassword(account, password, countryCode)
            }.getOrNull()
            if (outcome?.ok == true) {
                onLoggedIn()
            } else {
                _login.value = Login.Failed(outcome?.message ?: "登录失败")
            }
        }
    }

    private suspend fun onLoggedIn() {
        _account.value = runCatching { api.account() }.getOrNull()
        _login.value = Login.Idle
        _myPlaylists.value = runCatching { api.userPlaylists() }.getOrDefault(emptyList())
        _toast.value = "已登录：${_account.value?.nickname.orEmpty()}"
    }

    fun logout() {
        loginClient.logout()
        _account.value = null
        _myPlaylists.value = emptyList()
        _likedIds.value = emptySet()
        _login.value = Login.Idle
    }

    fun resetLoginState() {
        if (_login.value is Login.Failed || _login.value is Login.SmsSent) _login.value = Login.Idle
    }

    fun consumeToast() {
        _toast.value = null
    }

    fun closeDetail() {
        _detail.value = null
    }

    fun playFrom(songs: List<OnlineSong>, index: Int) {
        if (songs.isEmpty()) return
        viewModelScope.launch {
            val queue = songs.mapIndexed { position, online -> online.toSong(position) }.toMutableList()
            // 当前曲按偏好音质取链（20 分钟缓存 + 自动降档）；整队逐首取链会让点击后等很久
            if (index in queue.indices) {
                val songId = songs[index].id.toLongOrNull()
                if (songId != null) {
                    val better = runCatching { playUrlResolver.resolve(songId) }.getOrNull()
                    if (!better.isNullOrBlank()) {
                        queue[index] = queue[index].copy(filePath = better)
                    }
                }
            }
            playbackSession.ensure()
            controller.playSongs(queue, startIndex = index)
        }
    }

    fun playAll(songs: List<OnlineSong>) = playFrom(songs, 0)

    private companion object {
        const val SEARCH_PAGE_SIZE = 20
        /** 私人 FM 的虚拟歌单 id，与 openPrivateFm 里构造的一致 */
        const val FM_ID = "fm"
        const val FM_BATCH = 20
    }

    /**
     * 在线曲 → 本地播放模型的映射。
     *
     * id 用**负数 + 歌曲 id**：本地库的 SongId 是自增正数，负数不会撞上；
     * 同一首在线歌在不同入口进来会得到同一个 id，队列去重/定位行为与本地曲一致。
     */
    private fun OnlineSong.toSong(position: Int): Song {
        val numeric = id.toLongOrNull()
        // -1 在列表 key / 队列恢复里有"未分配"语义，这里一律偏移到 <= -2；
        // 数字 id 直接取负再减一，非数字 id 用哈希兜底（同一首歌不同入口得到同一个负数）
        val songId = when {
            numeric != null && numeric in 1..Int.MAX_VALUE -> -(numeric.toInt() + 1)
            else -> -(kotlin.math.abs(id.hashCode().toLong()).mod(1_000_000_000L) + position + 2).toInt()
        }
        return Song(
            id = if (songId >= -1) -2 else songId,
            title = title,
            artist = artist,
            album = album,
            durationSeconds = durationSeconds,
            filePath = api.fallbackPlayUrl(id).orEmpty(),
            coverArtPath = coverUrl,
            source = SongSource.WebDAV,
            protocol = ProtocolType.WebDAV,
            remoteId = "netease:$id",
        )
    }
}
