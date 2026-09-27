package com.catclaw.music.kt.ui.search

import androidx.lifecycle.ViewModel
import androidx.lifecycle.viewModelScope
import com.catclaw.music.kt.core.playback.PlayerController
import com.catclaw.music.kt.data.db.AlbumListItem
import com.catclaw.music.kt.data.db.ArtistListItem
import com.catclaw.music.kt.data.db.LibraryDao
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
import kotlinx.coroutines.launch

/** 发现 tab（MAUI 的 SearchPage）：搜索框 + 每日推荐 / 新曲 / 常听三段 */
@OptIn(FlowPreview::class)
@HiltViewModel
class SearchViewModel @Inject constructor(
    private val dao: LibraryDao,
    private val controller: PlayerController,
    private val session: PlaybackSessionStarter,
) : ViewModel() {

    data class Sections(
        val daily: List<SongListItem> = emptyList(),
        val fresh: List<SongListItem> = emptyList(),
        val frequent: List<SongListItem> = emptyList(),
    )

    private val _query = MutableStateFlow("")
    val query: StateFlow<String> = _query.asStateFlow()

    private val _results = MutableStateFlow<List<SongListItem>>(emptyList())
    val results: StateFlow<List<SongListItem>> = _results.asStateFlow()

    private val _sections = MutableStateFlow(Sections())
    val sections: StateFlow<Sections> = _sections.asStateFlow()

    // 「歌手」「专辑」两个分类面板要用的数据（MAUI 的 PanelArtist / PanelAlbum）
    private val _artists = MutableStateFlow<List<ArtistListItem>>(emptyList())
    val artists: StateFlow<List<ArtistListItem>> = _artists.asStateFlow()

    private val _albums = MutableStateFlow<List<AlbumListItem>>(emptyList())
    val albums: StateFlow<List<AlbumListItem>> = _albums.asStateFlow()

    private var activated = false

    init {
        viewModelScope.launch { dao.observeArtists().collect { _artists.value = it } }
        viewModelScope.launch { dao.observeAlbums().collect { _albums.value = it } }
        viewModelScope.launch {
            _query.debounce(220L).collect { text ->
                val trimmed = text.trim()
                _results.value = if (trimmed.isEmpty()) emptyList() else dao.searchSongs(trimmed)
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
        viewModelScope.launch { _sections.value = loadSections() }
    }

    fun play(list: List<SongListItem>, index: Int) {
        val songs = list.map { it.toSong() }
        if (songs.isEmpty()) return
        session.ensure()
        controller.playSongs(songs, startIndex = index)
    }

    private suspend fun loadSections(): Sections = Sections(
        daily = dao.dailyRecommendation(20),
        fresh = dao.newArrivals(20),
        frequent = dao.frequentSongs(20),
    )
}
