package com.catclaw.music.kt.ui.netease

import androidx.lifecycle.ViewModel
import androidx.lifecycle.viewModelScope
import com.catclaw.music.kt.core.model.ProtocolType
import com.catclaw.music.kt.core.model.Song
import com.catclaw.music.kt.core.model.SongSource
import com.catclaw.music.kt.core.playback.PlayerController
import com.catclaw.music.kt.data.netease.NeteaseApiClient
import com.catclaw.music.kt.data.netease.NeteasePlaylistClient
import com.catclaw.music.kt.data.netease.NeteaseSession
import com.catclaw.music.kt.data.netease.OnlinePlaylist
import com.catclaw.music.kt.data.netease.OnlineSong
import com.catclaw.music.kt.data.service.PlaybackSessionStarter
import dagger.hilt.android.lifecycle.HiltViewModel
import javax.inject.Inject
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.launch

/**
 * 网易云在线音乐的状态机：首页（推荐歌单 + 榜单）、搜索、歌单详情、播放。
 *
 * 播放桥接的做法：把 [OnlineSong] 转成 core 的 `Song` 塞进现有队列 —— 在线音乐不另造一套播放器。
 * `FilePath` 用 `song/media/outer/url` 跳转入口（稳定、可被 AudioCache 缓存），
 * 登录后当前曲再尝试用 eapi 换成直链（音质更高，但地址会过期，所以只用于这一首）。
 */
@HiltViewModel
class NeteaseMusicViewModel @Inject constructor(
    private val api: NeteaseApiClient,
    private val session: NeteaseSession,
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
    )

    data class Detail(
        val playlist: OnlinePlaylist? = null,
        val songs: List<OnlineSong> = emptyList(),
        val loading: Boolean = false,
    )

    private val _home = MutableStateFlow(Home())
    val home: StateFlow<Home> = _home.asStateFlow()

    private val _search = MutableStateFlow(Search())
    val search: StateFlow<Search> = _search.asStateFlow()

    private val _detail = MutableStateFlow<Detail?>(null)
    val detail: StateFlow<Detail?> = _detail.asStateFlow()

    val loggedIn: Boolean get() = session.isLoggedIn

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
            val songs = runCatching { api.searchSongs(keyword) }.getOrNull().orEmpty()
            val playlists = runCatching { api.searchPlaylists(keyword) }.getOrDefault(emptyList())
            _search.value = Search(
                query = keyword,
                loading = false,
                songs = songs,
                playlists = playlists,
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

    fun closeDetail() {
        _detail.value = null
    }

    fun playFrom(songs: List<OnlineSong>, index: Int) {
        if (songs.isEmpty()) return
        viewModelScope.launch {
            val queue = songs.mapIndexed { position, online -> online.toSong(position) }.toMutableList()
            // 当前曲登录态下换 eapi 直链：只有这一首值得多一次请求，整队逐首取链会让点击后等很久
            if (session.isLoggedIn && index in queue.indices) {
                val better = runCatching {
                    api.playUrl(songs[index].id, NeteasePlaylistClient.Quality.Lossless)
                }.getOrNull()
                if (!better.isNullOrBlank()) {
                    queue[index] = queue[index].copy(filePath = better)
                }
            }
            playbackSession.ensure()
            controller.playSongs(queue, startIndex = index)
        }
    }

    fun playAll(songs: List<OnlineSong>) = playFrom(songs, 0)

    /**
     * 在线曲 → 本地播放模型的映射。
     *
     * id 用**负数 + 歌曲 id 取模**：本地库的 SongId 是自增正数，负数不会撞上；
     * 同一首在线歌在不同入口进来会得到同一个 id，队列去重/定位行为与本地曲一致。
     */
    private fun OnlineSong.toSong(position: Int): Song {
        val numeric = id.toLongOrNull()
        val songId = when {
            numeric != null && numeric <= Int.MAX_VALUE -> -numeric.toInt().coerceAtMost(-1)
            else -> -(id.hashCode().toLong().let { if (it == 0L) 1L else kotlin.math.abs(it) } % 1_000_000_000L + position + 1).toInt()
        }
        return Song(
            id = if (songId >= 0) -1 else songId,
            title = title,
            artist = artist,
            album = album,
            durationSeconds = durationSeconds,
            filePath = api.fallbackPlayUrl(id) ?: "",
            coverArtPath = coverUrl,
            source = SongSource.WebDAV,
            protocol = ProtocolType.WebDAV,
            remoteId = "netease:$id",
        )
    }
}
