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

    fun playAll() = playFrom(0)

    fun playAt(index: Int) = playFrom(index)

    private fun playFrom(index: Int) {
        val songs = items.value.map { it.toSong() }
        if (songs.isEmpty()) return
        session.ensure()
        controller.playSongs(songs, startIndex = index)
    }
}
