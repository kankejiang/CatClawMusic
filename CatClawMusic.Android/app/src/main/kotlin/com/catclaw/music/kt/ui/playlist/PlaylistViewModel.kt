package com.catclaw.music.kt.ui.playlist

import androidx.lifecycle.ViewModel
import androidx.lifecycle.viewModelScope
import com.catclaw.music.kt.core.library.SongSortFields
import com.catclaw.music.kt.core.library.SongSortKey
import com.catclaw.music.kt.core.library.defaultDescending
import com.catclaw.music.kt.core.library.filterAndSortSongs
import com.catclaw.music.kt.core.playback.PlayerController
import com.catclaw.music.kt.data.db.PlaylistCover
import com.catclaw.music.kt.data.db.PlaylistDao
import com.catclaw.music.kt.data.db.entity.PlaylistEntity
import com.catclaw.music.kt.data.db.toSong
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
import kotlinx.coroutines.flow.flowOf
import kotlinx.coroutines.flow.map
import kotlinx.coroutines.flow.stateIn
import kotlinx.coroutines.launch

@OptIn(ExperimentalCoroutinesApi::class)
@HiltViewModel
class PlaylistViewModel @Inject constructor(
    private val playlistDao: PlaylistDao,
    private val controller: PlayerController,
    private val session: PlaybackSessionStarter,
) : ViewModel() {

    val playlists = playlistDao.observePlaylists().stateIn(
        scope = viewModelScope,
        started = SharingStarted.WhileSubscribed(5_000),
        initialValue = emptyList(),
    )

    /**
     * 歌单封面来源表（SQL 见 `PlaylistDao.SQL_PLAYLIST_COVERS`）。
     * 没曲的歌单不在表里，卡片自然退回首字 / 系统图标——不需要在 UI 里再判一次空。
     */
    val covers: StateFlow<Map<Int, PlaylistCover>> = playlistDao.observePlaylistCovers()
        .map { rows -> rows.associateBy { it.playlistId } }
        .stateIn(viewModelScope, SharingStarted.WhileSubscribed(5_000), emptyMap())

    private val _openId = MutableStateFlow<Int?>(null)
    val openId = _openId

    /** 打开某个歌单才订阅它的曲目，没打开时给空流 */
    val items = _openId.flatMapLatest { id ->
        if (id == null) flowOf(emptyList()) else playlistDao.observeItems(id)
    }.stateIn(viewModelScope, SharingStarted.WhileSubscribed(5_000), emptyList())

    private val _itemSort = MutableStateFlow(SongSortKey.Title)
    val itemSort = _itemSort.asStateFlow()

    private val _itemSortDescending = MutableStateFlow(SongSortKey.Title.defaultDescending)
    val itemSortDescending = _itemSortDescending.asStateFlow()

    private val _itemQuery = MutableStateFlow("")
    val itemQuery = _itemQuery.asStateFlow()

    /** 屏幕上那份歌单列表。规则与全部歌曲页同一条（core/library/SongListRules.kt），这里只接线 */
    val displayItems = combine(items, _itemSort, _itemQuery, _itemSortDescending) { list, sort, query, desc ->
        filterAndSortSongs(list, sort, query, descending = desc) {
            SongSortFields(it.title, it.artist, it.album, it.durationSeconds, it.dateAdded, it.id)
        }
    }.stateIn(viewModelScope, SharingStarted.WhileSubscribed(5_000), emptyList())

    fun open(id: Int) {
        // 换歌单要清掉搜索框：留着上一个的关键词，新打开的这个看着像是空的
        _itemQuery.value = ""
        _openId.value = id
    }

    /** 与全部歌曲页同一条规则：换档取该档默认方向，点同一档翻方向（见 `LibraryViewModel.setSongSort`） */
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

    fun close() {
        _openId.value = null
    }

    fun create(name: String) {
        val trimmed = name.trim()
        if (trimmed.isEmpty()) return
        viewModelScope.launch {
            if (playlistDao.byName(trimmed) == null) {
                val now = System.currentTimeMillis()
                playlistDao.upsert(PlaylistEntity(name = trimmed, createdAt = now, updatedAt = now))
            }
        }
    }

    /**
     * 重命名。空名与"别的歌单已占用这个名字"直接拒，不发 SQL。
     *
     * 同名判据用内存里那份 [playlists] 而不是再查一次 `byName`：编辑框就在列表页上，
     * 那一刻列表流必然被订阅着（值是当前屏幕看到的值），多一次磁盘读换不到更多正确性。
     * 内置单的保护在 SQL 里（`IsSystem = 0`），不靠这一层自觉。
     *
     * @return false 表示没改，UI 应当保留编辑框并说明原因
     */
    fun rename(id: Int, name: String): Boolean {
        val trimmed = name.trim()
        if (trimmed.isEmpty()) return false
        if (playlists.value.any { it.id != id && it.name == trimmed }) return false
        viewModelScope.launch { playlistDao.rename(id, trimmed, System.currentTimeMillis()) }
        return true
    }

    /** 内置歌单（收藏 / 最近播放）删不掉：DAO 的 DELETE 带 IsSystem = 0 */
    fun delete(id: Int) {
        if (_openId.value == id) _openId.value = null
        viewModelScope.launch {
            playlistDao.clearItems(id)
            playlistDao.delete(id)
        }
    }

    /** 把当前正在播的那首加进打开的歌单 */
    fun addCurrentSong() {
        val playlistId = _openId.value ?: return
        val song = controller.state.value.currentSong ?: return
        viewModelScope.launch { playlistDao.appendAll(playlistId, listOf(song.id)) }
    }

    /**
     * 提交拖拽后的歌单顺序（MAUI `CommitPlaylistOrderAsync` 的对应物）。
     *
     * 只提交自建歌单：系统歌单在列表里恒在最前，重写它们的序号也不会改变显示顺序
     * （DAO 那条 SQL 里另有 `IsSystem = 0` 兜底）。写库失败不影响本次会话内的显示，
     * 与 C# 同规则：落库失败只是下次进页恢复旧顺序。
     */
    fun commitPlaylistOrder(orderedIds: List<Int>) {
        val ids = orderedIds.filter { id -> playlists.value.any { it.id == id && !it.isSystem } }
        if (ids.isEmpty()) return
        viewModelScope.launch { playlistDao.reorderPlaylists(ids) }
    }

    /**
     * 从**当前打开的**歌单里移掉一首。只断歌单与这首歌的关联，歌还在库里、收藏与统计都不动。
     * 计数同步在 DAO 的 `removeFromPlaylist` 里，不在这里补第二刀。
     */
    fun removeItem(songId: Int) {
        val playlistId = _openId.value ?: return
        viewModelScope.launch { playlistDao.removeFromPlaylist(playlistId, songId) }
    }

    fun playAll() = playFrom(0)

    /** 随机播放当前歌单（MAUI `PlaylistDetailViewModel.ShufflePlayAsync` 的对应物） */
    fun playAllShuffled() {
        val songs = displayItems.value.map { it.toSong() }
        if (songs.isEmpty()) return
        session.ensure()
        controller.playShuffled(songs)
    }

    fun playAt(index: Int) = playFrom(index)

    /**
     * 从可见列表的某个位置起播。用 [displayItems] 而不是 [items]：
     * 页面上排过序、搜过之后，"点第 3 行"必须是那三行里的第 3 行，而不是库里的第 3 行。
     */
    private fun playFrom(index: Int) {
        val songs = displayItems.value.map { it.toSong() }
        if (songs.isEmpty()) return
        session.ensure()
        controller.playSongs(songs, startIndex = index)
    }
}
