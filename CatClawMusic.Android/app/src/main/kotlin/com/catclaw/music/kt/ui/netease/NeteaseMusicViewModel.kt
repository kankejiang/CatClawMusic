package com.catclaw.music.kt.ui.netease

import androidx.lifecycle.ViewModel
import androidx.lifecycle.viewModelScope
import com.catclaw.music.kt.core.model.ProtocolType
import com.catclaw.music.kt.core.model.Song
import com.catclaw.music.kt.core.model.SongSource
import com.catclaw.music.kt.core.playback.PlayMode
import com.catclaw.music.kt.core.playback.PlayerController
import com.catclaw.music.kt.data.netease.NeteaseApiClient
import com.catclaw.music.kt.data.repository.netease.NeteaseAccountRepository
import com.catclaw.music.kt.data.netease.NeteasePlayUrlResolver
import com.catclaw.music.kt.data.repository.netease.NeteaseLikeRepository
import com.catclaw.music.kt.data.repository.netease.NeteaseRadioRepository
import com.catclaw.music.kt.data.netease.NeteaseArtist
import com.catclaw.music.kt.data.netease.NeteaseSongMapper
import com.catclaw.music.kt.data.netease.NeteasePlaylistClient
import com.catclaw.music.kt.data.netease.OnlinePlaylist
import com.catclaw.music.kt.data.netease.OnlineSong
import com.catclaw.music.kt.data.service.PlaybackSessionStarter
import dagger.hilt.android.lifecycle.HiltViewModel
import javax.inject.Inject
import kotlinx.coroutines.flow.combine
import kotlinx.coroutines.flow.SharingStarted
import kotlinx.coroutines.flow.stateIn
import com.catclaw.music.kt.core.library.SongSortKey
import com.catclaw.music.kt.core.library.defaultDescending
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
    internal val api: NeteaseApiClient,
    internal val accountRepo: NeteaseAccountRepository,
    internal val playUrlResolver: NeteasePlayUrlResolver,
    internal val radio: NeteaseRadioRepository,
    internal val likes: NeteaseLikeRepository,
    internal val controller: PlayerController,
    internal val playbackSession: PlaybackSessionStarter,
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

    /** 歌单广场状态（对齐插件 精选/歌单广场/排行榜/歌手 四个 HomeTab） */
    data class Square(
        val groups: List<com.catclaw.music.kt.data.netease.CatalogueGroup> = emptyList(),
        val selected: String = "推荐",
        val playlists: List<OnlinePlaylist> = emptyList(),
        val loading: Boolean = false,
        val page: Int = 1,
        val hasMore: Boolean = false,
    )

    internal val _square = MutableStateFlow(Square())
    val square: StateFlow<Square> = _square.asStateFlow()

    /** 0 精选 / 1 歌单广场 / 2 排行榜 / 3 歌手 */
    internal val _homeTab = MutableStateFlow(0)
    val homeTab: StateFlow<Int> = _homeTab.asStateFlow()

    internal val _topArtists = MutableStateFlow<List<NeteaseArtist>>(emptyList())
    val topArtists: StateFlow<List<NeteaseArtist>> = _topArtists.asStateFlow()

    fun selectHomeTab(index: Int) {
        _homeTab.value = index
        when (index) {
            1 -> if (_square.value.playlists.isEmpty() && !_square.value.loading) loadSquare()
            3 -> if (_topArtists.value.isEmpty()) loadTopArtists()
        }
    }

    fun loadSquare(category: String? = null) {
        val name = category ?: _square.value.selected
        if (_square.value.loading) return
        viewModelScope.launch {
            _square.value = _square.value.copy(selected = name, loading = true, playlists = emptyList(), page = 1)
            val groups = if (_square.value.groups.isEmpty()) {
                runCatching { api.playlistCatalogue() }.getOrDefault(emptyList())
            } else {
                _square.value.groups
            }
            val list = runCatching {
                api.playlists(if (name == "推荐") null else name, page = 1, pageSize = SQUARE_PAGE_SIZE)
            }.getOrDefault(emptyList())
            _square.value = _square.value.copy(
                groups = groups,
                playlists = list,
                loading = false,
                page = 1,
                hasMore = list.size >= SQUARE_PAGE_SIZE,
            )
        }
    }

    fun loadMoreSquare() {
        val current = _square.value
        if (current.loading || !current.hasMore) return
        viewModelScope.launch {
            _square.value = current.copy(loading = true)
            val nextPage = current.page + 1
            val list = runCatching {
                api.playlists(
                    if (current.selected == "推荐") null else current.selected,
                    page = nextPage,
                    pageSize = SQUARE_PAGE_SIZE,
                )
            }.getOrDefault(emptyList())
            val existing = current.playlists.map { it.id }.toSet()
            _square.value = _square.value.copy(
                playlists = current.playlists + list.filter { it.id !in existing },
                loading = false,
                page = nextPage,
                hasMore = list.size >= SQUARE_PAGE_SIZE,
            )
        }
    }

    fun loadTopArtists() {
        viewModelScope.launch {
            _topArtists.value = runCatching { api.topArtists(30) }.getOrDefault(emptyList())
        }
    }

    internal val _home = MutableStateFlow(Home())
    val home: StateFlow<Home> = _home.asStateFlow()

    internal val _search = MutableStateFlow(Search())
    val search: StateFlow<Search> = _search.asStateFlow()

    internal val _detail = MutableStateFlow<Detail?>(null)
    val detail: StateFlow<Detail?> = _detail.asStateFlow()

    // 详情页的过滤 / 排序属于页面态，归 VM（UI 只负责渲染，不再自己 remember 一套）
    internal val _detailQuery = MutableStateFlow("")
    val detailQuery: StateFlow<String> = _detailQuery.asStateFlow()

    /** null = 保持歌单原始顺序 */
    internal val _detailSort = MutableStateFlow<com.catclaw.music.kt.core.library.SongSortKey?>(null)
    val detailSort: StateFlow<com.catclaw.music.kt.core.library.SongSortKey?> = _detailSort.asStateFlow()

    internal val _detailDescending = MutableStateFlow(false)
    val detailDescending: StateFlow<Boolean> = _detailDescending.asStateFlow()

    /** 屏幕上那份详情列表：先过滤再（可选）排序 */
    val detailDisplaySongs: StateFlow<List<OnlineSong>> = combine(
        _detail, _detailQuery, _detailSort, _detailDescending,
    ) { detail: Detail?, query: String, sort: SongSortKey?, descending: Boolean ->
        val songs = detail?.songs.orEmpty()
        val filtered = if (query.isBlank()) {
            songs
        } else {
            val q = query.trim()
            songs.filter {
                it.title.contains(q, ignoreCase = true) ||
                    it.artist.contains(q, ignoreCase = true) ||
                    it.album.contains(q, ignoreCase = true)
            }
        }
        sort?.let { key ->
            com.catclaw.music.kt.core.library.filterAndSortSongs(filtered, key, "", descending = descending) {
                com.catclaw.music.kt.core.library.SongSortFields(
                    it.title, it.artist, it.album, it.durationSeconds, 0, it.id.hashCode(),
                )
            }
        } ?: filtered
    }.stateIn(viewModelScope, kotlinx.coroutines.flow.SharingStarted.WhileSubscribed(5_000), emptyList())

    /** 账号唯一源在 NeteaseAccountRepository（登录态/身份/歌单），这里只转发 */
    val account: StateFlow<NeteaseApiClient.Account?> = accountRepo.account
    val myPlaylists: StateFlow<List<OnlinePlaylist>> = accountRepo.myPlaylists

    internal val _login = MutableStateFlow<Login>(Login.Idle)
    val login: StateFlow<Login> = _login.asStateFlow()

    /** 验证码重发倒计时（秒）；0 表示可以发送 */
    internal val _smsCountdown = MutableStateFlow(0)
    val smsCountdown: StateFlow<Int> = _smsCountdown.asStateFlow()

    /** 红心唯一源在 NeteaseLikeRepository：列表页/播放页读同一份，不再持有空副本 */
    val likedIds: StateFlow<Set<String>> = likes.likedIds

    internal val _toast = MutableStateFlow<String?>(null)
    val toast: StateFlow<String?> = _toast.asStateFlow()

    /** 播放音质：无损 / 320k / 128k（点击顶部胶囊循环） */
    internal val _quality = MutableStateFlow(playUrlResolver.quality)
    val quality: StateFlow<NeteasePlaylistClient.Quality> = _quality.asStateFlow()

    fun setDetailQuery(value: String) {
        _detailQuery.value = value
    }

    /** 点同一档翻方向，换档取该档默认方向（与音乐库同一条规则） */
    fun toggleDetailSort(key: com.catclaw.music.kt.core.library.SongSortKey) {
        if (_detailSort.value == key) {
            _detailDescending.value = !_detailDescending.value
        } else {
            _detailSort.value = key
            _detailDescending.value = key.defaultDescending
        }
    }

    fun cycleQuality() {
        _quality.value = playUrlResolver.cycleQuality()
        _toast.value = "音质：" + when (_quality.value) {
            NeteasePlaylistClient.Quality.Lossless -> "无损"
            NeteasePlaylistClient.Quality.Exhigh -> "320k"
            NeteasePlaylistClient.Quality.Standard -> "128k"
        }
    }

    init {
        accountRepo.refresh()
        likes.refresh()
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

    /**
     * 私人漫游 = 电台按钮：点一下直接开播，不进列表页。
     * 电台是 [NeteaseRadioRepository] 单例，关掉本页也继续补货（插件的 VM 同为插件级单例）。
     */
    fun playPrivateFm() {
        radio.start()
        _toast.value = "私人漫游已开始"
    }

    fun openLiked() {
        viewModelScope.launch {
            val virtual = OnlinePlaylist(id = "liked", name = "我喜欢的音乐")
            _detail.value = Detail(virtual, loading = true)
            val songs = runCatching { api.likedSongs() }.getOrDefault(emptyList())
            // 虚拟歌单没有独立封面：用首曲封面兜底（否则头部是大字母占位）
            _detail.value = Detail(
                virtual.copy(coverUrl = songs.firstOrNull()?.coverUrl),
                songs,
                loading = false,
            )
            likes.adopt(songs.map { it.id }.toSet())
            if (songs.isEmpty()) _toast.value = "还没有红心歌曲，或未登录"
        }
    }

    fun refreshMyPlaylists() {
        accountRepo.refresh(force = true)
    }

    fun like(song: OnlineSong, like: Boolean) {
        // 插件：FM 曲红心走 /api/radio/like 影响推荐；普通 like 同时维护「我喜欢的音乐」镜像
        if (radio.fmMode.value) radio.like(song.id, like)
        likes.toggle(song.id)
    }

    /** 插件 FmTrashAsync：电台里不想听这首 */
    fun trashFm(song: OnlineSong) = radio.trash(song.id)

    /** 插件 GetFmModeLabelAsync / GetFmModesAsync 的 3 档推荐模式 */
    val radioMode get() = radio.mode
    val radioScene get() = radio.scene
    val radioLabel get() = radio.label
    fun setRadioMode(mode: com.catclaw.music.kt.data.repository.netease.FmMode) = radio.setMode(mode)
    fun setRadioScene(keyword: String?) = radio.setScene(keyword)
    fun refreshRadio() = radio.refresh()

    // ── 登录 ───────────────────────────────────────────────────

    fun sendSmsCode(phone: String, countryCode: String = "86") {
        if (_smsCountdown.value > 0) return
        if (_login.value is Login.Busy) return
        viewModelScope.launch {
            _login.value = Login.Busy
            val outcome = runCatching { accountRepo.sendSms(phone, countryCode) }.getOrNull()
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
            val outcome = runCatching { accountRepo.loginWithSms(phone, captcha, countryCode) }.getOrNull()
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
                accountRepo.loginWithPassword(account, password, countryCode)
            }.getOrNull()
            if (outcome?.ok == true) {
                onLoggedIn()
            } else {
                _login.value = Login.Failed(outcome?.message ?: "登录失败")
            }
        }
    }

    private suspend fun onLoggedIn() {
        // session.revision 自增会驱动 accountRepo 与 likes 自动刷新
        accountRepo.refresh(force = true)
        _login.value = Login.Idle
        _toast.value = "已登录：${accountRepo.account.value?.nickname.orEmpty()}"
    }

    fun logout() {
        accountRepo.logout() // 登录态清空后 likes 由 Repository 自动 clear
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
        radio.stop()
        viewModelScope.launch {
            val queue = songs.mapIndexed { position, online ->
                NeteaseSongMapper.toQueueSong(online, position, api.fallbackPlayUrl(online.id).orEmpty())
            }.toMutableList()
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

    /** 离开 FM（点别的歌单/搜索播放）时由 playFrom 的 fm=false 收尾 */

    private companion object {
        const val SEARCH_PAGE_SIZE = 20
        const val SQUARE_PAGE_SIZE = 20
    }

    /**
     * 在线曲 → 本地播放模型的映射。
     *
     * id 用**负数 + 歌曲 id**：本地库的 SongId 是自增正数，负数不会撞上；
     * 同一首在线歌在不同入口进来会得到同一个 id，队列去重/定位行为与本地曲一致。
     */
}
