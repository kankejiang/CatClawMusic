package com.catclaw.music.kt.ui.search

import androidx.lifecycle.ViewModel
import androidx.lifecycle.viewModelScope
import com.catclaw.music.kt.core.playback.PlayerController
import com.catclaw.music.kt.data.db.AlbumListItem
import com.catclaw.music.kt.data.db.ArtistListItem
import com.catclaw.music.kt.data.db.LibraryDao
import com.catclaw.music.kt.data.db.PlayStats
import com.catclaw.music.kt.data.db.SongListItem
import com.catclaw.music.kt.data.db.toSong
import com.catclaw.music.kt.data.service.PlaybackSessionStarter
import dagger.hilt.android.lifecycle.HiltViewModel
import javax.inject.Inject
import kotlinx.coroutines.FlowPreview
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.flow.debounce
import kotlin.random.Random
import kotlinx.coroutines.launch

/** 发现 tab（MAUI 的 SearchPage）：搜索框 + 每日推荐 / 新曲 / 常听三段 */
@OptIn(FlowPreview::class)
@HiltViewModel
class SearchViewModel @Inject constructor(
    private val dao: LibraryDao,
    private val controller: PlayerController,
    private val session: PlaybackSessionStarter,
    private val radio: com.catclaw.music.kt.data.repository.netease.NeteaseRadioRepository,
    private val catalog: com.catclaw.music.kt.data.repository.netease.NeteaseCatalogRepository,
    private val resolver: com.catclaw.music.kt.data.netease.NeteasePlayUrlResolver,
) : ViewModel() {

    /** 网易云账号登录态：决定发现页是否出现「私人 FM」按钮 */
    val neteaseLoggedIn: kotlinx.coroutines.flow.StateFlow<Boolean> = radio.loggedIn

    fun refreshNeteaseLogin() = Unit

    /** 私人 FM 是电台按钮：点一下直接开播（插件 LoadPrivateFmAsync 同行为） */
    fun startNeteaseFm() = radio.start()

    data class Sections(
        val daily: List<SongListItem> = emptyList(),
        val fresh: List<SongListItem> = emptyList(),
        val frequent: List<SongListItem> = emptyList(),
    )

    private val _query = MutableStateFlow("")
    val query: StateFlow<String> = _query.asStateFlow()

    private val _results = MutableStateFlow<List<SongListItem>>(emptyList())
    val results: StateFlow<List<SongListItem>> = _results.asStateFlow()

    /** 在线音乐结果（与本地结果并列展示；未登录/无网络时为空） */
    private val _onlineResults = MutableStateFlow<List<com.catclaw.music.kt.data.netease.OnlineSong>>(emptyList())
    val onlineResults: StateFlow<List<com.catclaw.music.kt.data.netease.OnlineSong>> = _onlineResults.asStateFlow()

    private val _onlineLoading = MutableStateFlow(false)
    val onlineLoading: StateFlow<Boolean> = _onlineLoading.asStateFlow()

    private val _sections = MutableStateFlow(Sections())
    val sections: StateFlow<Sections> = _sections.asStateFlow()

    // 「歌手」「专辑」两个分类面板要用的数据（MAUI 的 PanelArtist / PanelAlbum）
    private val _artists = MutableStateFlow<List<ArtistListItem>>(emptyList())
    val artists: StateFlow<List<ArtistListItem>> = _artists.asStateFlow()

    private val _albums = MutableStateFlow<List<AlbumListItem>>(emptyList())
    val albums: StateFlow<List<AlbumListItem>> = _albums.asStateFlow()

    /** 「我的最爱」入口：与播放页那颗心同一个数据源（收藏表） */
    private val _favorites = MutableStateFlow<List<SongListItem>>(emptyList())
    val favorites: StateFlow<List<SongListItem>> = _favorites.asStateFlow()

    /** 「随机播放」要全库随机，不是「今日推荐里抽一首」（C# 侧同样是全库随机） */
    private val _allSongs = MutableStateFlow<List<SongListItem>>(emptyList())
    val allSongs: StateFlow<List<SongListItem>> = _allSongs.asStateFlow()

    /** 统计格要的是真实聚合，不是某个货架的长度 */
    private val _stats = MutableStateFlow(PlayStats(0, 0))
    val stats: StateFlow<PlayStats> = _stats.asStateFlow()

    private var activated = false

    init {
        viewModelScope.launch { dao.observeArtists().collect { _artists.value = it } }
        viewModelScope.launch { dao.observeFavorites().collect { _favorites.value = it } }
        viewModelScope.launch { dao.observeAllSongs().collect { _allSongs.value = it } }
        viewModelScope.launch { dao.observeAlbums().collect { _albums.value = it } }
        viewModelScope.launch {
            _query.debounce(350L).collect { text ->
                val trimmed = text.trim()
                _results.value = if (trimmed.isEmpty()) emptyList() else dao.searchSongs(trimmed)
                if (trimmed.isEmpty()) {
                    _onlineResults.value = emptyList()
                    _onlineLoading.value = false
                } else {
                    // 不依赖登录态：网易云 web 搜索允许匿名（登录后音质更好）
                    com.catclaw.music.kt.core.diagnostic.AppLog.d(
                        "NeteaseSearch", "start q=$trimmed loggedIn=${radio.loggedIn.value}",
                    )
                    _onlineLoading.value = true
                    _onlineResults.value = catalog.searchSongs(trimmed)
                    _onlineLoading.value = false
                }
            }
        }
    }

    /** Pager 会预组合相邻页，三段首屏查询要等这一页真被看到再发 */
    fun activate() {
        if (activated) return
        activated = true
        reload()
    }

    fun setQuery(value: String) {
        _query.value = value
    }

    fun reload() {
        viewModelScope.launch {
            _sections.value = loadSections()
            _stats.value = dao.playStats()
        }
    }

    /** 播放在线搜索结果：当前曲先取直链，其余用 outer URL 兜底（与网易云页同规则） */
    fun playOnline(list: List<com.catclaw.music.kt.data.netease.OnlineSong>, index: Int) {
        if (index !in list.indices) return
        viewModelScope.launch {
            session.ensure()
            val songs = list.map { com.catclaw.music.kt.data.netease.NeteaseSongMapper.toSong(it) }.toMutableList()
            val picked = list[index]
            val url = picked.id.toLongOrNull()?.let { runCatching { resolver.resolve(it) }.getOrNull() }
            if (!url.isNullOrBlank()) {
                songs[index] = com.catclaw.music.kt.data.netease.NeteaseSongMapper.toQueueSong(picked, index, url)
            }
            controller.playSongs(songs, startIndex = index)
        }
    }

    fun play(list: List<SongListItem>, index: Int) {
        val songs = list.map { it.toSong() }
        if (songs.isEmpty()) return
        session.ensure()
        controller.playSongs(songs, startIndex = index)
    }

    /** 随机播放：全库随机起播 */
    fun shuffleAll() {
        val songs = _allSongs.value
        if (songs.isEmpty()) return
        play(songs, Random.nextInt(songs.size))
    }

    /** 我的最爱：从收藏里随机起播（没有收藏时不动作） */
    fun playFavorites() {
        val songs = _favorites.value
        if (songs.isEmpty()) return
        play(songs, Random.nextInt(songs.size))
    }

    private suspend fun loadSections(): Sections = Sections(
        daily = dao.dailyRecommendation(20),
        fresh = dao.newArrivals(20),
        frequent = dao.frequentSongs(20),
    )
}
