package com.catclaw.music.kt.ui.playlist

import androidx.lifecycle.ViewModel
import androidx.lifecycle.viewModelScope
import com.catclaw.music.kt.core.playback.PlayerController
import com.catclaw.music.kt.data.db.PlaylistDao
import com.catclaw.music.kt.data.db.entity.PlaylistEntity
import com.catclaw.music.kt.data.db.toSong
import com.catclaw.music.kt.data.service.PlaybackSessionStarter
import dagger.hilt.android.lifecycle.HiltViewModel
import javax.inject.Inject
import kotlinx.coroutines.ExperimentalCoroutinesApi
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.SharingStarted
import kotlinx.coroutines.flow.flatMapLatest
import kotlinx.coroutines.flow.flowOf
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

    private val _openId = MutableStateFlow<Int?>(null)
    val openId = _openId

    /** 打开某个歌单才订阅它的曲目，没打开时给空流 */
    val items = _openId.flatMapLatest { id ->
        if (id == null) flowOf(emptyList()) else playlistDao.observeItems(id)
    }.stateIn(viewModelScope, SharingStarted.WhileSubscribed(5_000), emptyList())

    fun open(id: Int) {
        _openId.value = id
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
     * 从**当前打开的**歌单里移掉一首。只断歌单与这首歌的关联，歌还在库里、收藏与统计都不动。
     * 计数同步在 DAO 的 `removeFromPlaylist` 里，不在这里补第二刀。
     */
    fun removeItem(songId: Int) {
        val playlistId = _openId.value ?: return
        viewModelScope.launch { playlistDao.removeFromPlaylist(playlistId, songId) }
    }

    fun playAll() = playFrom(0)

    fun playAt(index: Int) = playFrom(index)

    private fun playFrom(index: Int) {
        val songs = items.value.map { it.toSong() }
        if (songs.isEmpty()) return
        session.ensure()
        controller.playSongs(songs, startIndex = index)
    }
}
