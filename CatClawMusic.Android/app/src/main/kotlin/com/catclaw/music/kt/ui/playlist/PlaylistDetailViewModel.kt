package com.catclaw.music.kt.ui.playlist

import androidx.lifecycle.ViewModel
import androidx.lifecycle.viewModelScope
import com.catclaw.music.kt.core.library.SongSortFields
import com.catclaw.music.kt.core.library.SongSortKey
import com.catclaw.music.kt.core.library.defaultDescending
import com.catclaw.music.kt.core.library.filterAndSortSongs
import com.catclaw.music.kt.core.playback.PlayerController
import com.catclaw.music.kt.data.db.SongListItem
import com.catclaw.music.kt.data.db.toSong
import com.catclaw.music.kt.data.repository.playlist.PlaylistRepository
import com.catclaw.music.kt.data.service.PlaybackSessionStarter
import dagger.hilt.android.lifecycle.HiltViewModel
import javax.inject.Inject
import kotlinx.coroutines.ExperimentalCoroutinesApi
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.SharingStarted
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.flow.combine
import kotlinx.coroutines.flow.flatMapLatest
import kotlinx.coroutines.flow.stateIn
import kotlinx.coroutines.launch

/**
 * 自定义歌单详情 ViewModel（MVVM B3）。
 *
 * 独立于 [PlaylistViewModel]（后者只管「我的歌单」列表 + 增删改/排序）：
 * 页面打开态、排序、搜索、行内操作都归这里，任何页面 `hiltViewModel()` 都可复用。
 * 数据来自 [PlaylistRepository]，播放入口统一走 `ensure()` + `playSongs`。
 */
@OptIn(ExperimentalCoroutinesApi::class)
@HiltViewModel
class PlaylistDetailViewModel @Inject constructor(
    private val playlists: PlaylistRepository,
    private val controller: PlayerController,
    private val session: PlaybackSessionStarter,
) : ViewModel() {

    private val _openId = MutableStateFlow<Int?>(null)
    val openId: StateFlow<Int?> = _openId.asStateFlow()

    /** 当前打开的歌单实体与封面（独立页面组件自己就能取，不用调用方传） */
    val playlist: StateFlow<com.catclaw.music.kt.data.db.entity.PlaylistEntity?> =
        combine(playlists.playlists, _openId) { list, id -> list.firstOrNull { it.id == id } }
            .stateIn(viewModelScope, SharingStarted.WhileSubscribed(5_000), null)

    val cover: StateFlow<com.catclaw.music.kt.data.db.PlaylistCover?> =
        combine(playlists.covers, _openId) { rows, id -> rows.firstOrNull { it.playlistId == id } }
            .stateIn(viewModelScope, SharingStarted.WhileSubscribed(5_000), null)

    /** 打开某个歌单才订阅它的曲目，没打开时给空流 */
    val items: StateFlow<List<SongListItem>> = _openId
        .flatMapLatest { id -> playlists.items(id) }
        .stateIn(viewModelScope, SharingStarted.WhileSubscribed(5_000), emptyList())

    private val _itemSort = MutableStateFlow(SongSortKey.Title)
    val itemSort: StateFlow<SongSortKey> = _itemSort.asStateFlow()

    private val _itemSortDescending = MutableStateFlow(SongSortKey.Title.defaultDescending)
    val itemSortDescending: StateFlow<Boolean> = _itemSortDescending.asStateFlow()

    private val _itemQuery = MutableStateFlow("")
    val itemQuery: StateFlow<String> = _itemQuery.asStateFlow()

    /** 屏幕上那份列表：排序/搜索规则与音乐库共用 `filterAndSortSongs` */
    val displayItems: StateFlow<List<SongListItem>> =
        combine(items, _itemSort, _itemQuery, _itemSortDescending) { list, sort, query, desc ->
            filterAndSortSongs(list, sort, query, descending = desc) {
                SongSortFields(it.title, it.artist, it.album, it.durationSeconds, it.dateAdded, it.id)
            }
        }.stateIn(viewModelScope, SharingStarted.WhileSubscribed(5_000), emptyList())

    fun open(id: Int) {
        // 换歌单要清掉搜索框：留着上一个的关键词，新打开的这个看着像是空的
        _itemQuery.value = ""
        _openId.value = id
    }

    fun close() {
        _openId.value = null
    }

    /** 与全部歌曲页同一条规则：换档取该档默认方向，点同一档翻方向 */
    fun setItemSort(sort: SongSortKey) {
        if (_itemSort.value == sort) {
            _itemSortDescending.value = !_itemSortDescending.value
        } else {
            _itemSort.value = sort
            _itemSortDescending.value = sort.defaultDescending
        }
    }

    fun setItemQuery(query: String) {
        _itemQuery.value = query
    }

    /** 把当前正在播的那首加进打开的歌单 */
    fun addCurrentSong() {
        val playlistId = _openId.value ?: return
        val song = controller.state.value.currentSong ?: return
        viewModelScope.launch { playlists.appendSongs(playlistId, listOf(song.id)) }
    }

    /** 从当前歌单里移掉一首：只断关联，歌还在库里 */
    fun removeItem(songId: Int) {
        val playlistId = _openId.value ?: return
        viewModelScope.launch { playlists.removeSong(playlistId, songId) }
    }

    fun playAll() = playFrom(0)

    fun playAllShuffled() {
        val songs = displayItems.value.map { it.toSong() }
        if (songs.isEmpty()) return
        session.ensure()
        controller.playShuffled(songs)
    }

    fun playAt(index: Int) = playFrom(index)

    /**
     * 从可见列表的某个位置起播：用 [displayItems] 而不是 [items]，
     * 「点第 3 行」必须是屏幕上那三行里的第 3 行。
     */
    private fun playFrom(index: Int) {
        val songs = displayItems.value.map { it.toSong() }
        if (songs.isEmpty()) return
        session.ensure()
        controller.playSongs(songs, startIndex = index)
    }
}
