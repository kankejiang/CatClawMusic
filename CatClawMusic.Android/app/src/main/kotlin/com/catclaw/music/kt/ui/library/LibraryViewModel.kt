package com.catclaw.music.kt.ui.library

import android.content.Context
import androidx.lifecycle.ViewModel
import androidx.lifecycle.viewModelScope
import com.catclaw.music.kt.core.playback.PlayerController
import com.catclaw.music.kt.data.db.LibraryDao
import com.catclaw.music.kt.data.db.SongListItem
import com.catclaw.music.kt.data.db.PlaylistDao
import com.catclaw.music.kt.data.db.toSong
import com.catclaw.music.kt.data.library.LibraryScanner
import com.catclaw.music.kt.data.service.PlaybackSessionStarter
import dagger.hilt.android.lifecycle.HiltViewModel
import dagger.hilt.android.qualifiers.ApplicationContext
import javax.inject.Inject
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.SharingStarted
import kotlinx.coroutines.flow.stateIn
import kotlinx.coroutines.launch

@HiltViewModel
class LibraryViewModel @Inject constructor(
    @ApplicationContext private val context: Context,
    private val scanner: LibraryScanner,
    private val controller: PlayerController,
    private val session: PlaybackSessionStarter,
    private val libraryDao: LibraryDao,
    playlistDao: PlaylistDao,
) : ViewModel() {

    /** 下钻页要按艺人/专辑现读一次歌单 */
    suspend fun songsOfArtist(artistId: Int) = libraryDao.songsOfArtist(artistId)

    suspend fun songsOfAlbum(albumId: Int) = libraryDao.songsOfAlbum(albumId)

    fun playRows(rows: List<SongListItem>, index: Int) = playFrom(rows.map { it.toSong() }, index)

    val songs = libraryDao.observeLocalSongs().stateIn(
        viewModelScope, SharingStarted.WhileSubscribed(5_000), emptyList(),
    )

    val artists = libraryDao.observeArtists().stateIn(
        viewModelScope, SharingStarted.WhileSubscribed(5_000), emptyList(),
    )

    val albums = libraryDao.observeAlbums().stateIn(
        viewModelScope, SharingStarted.WhileSubscribed(5_000), emptyList(),
    )

    val favorites = libraryDao.observeFavorites().stateIn(
        viewModelScope, SharingStarted.WhileSubscribed(5_000), emptyList(),
    )

    val playlists = playlistDao.observePlaylists().stateIn(
        viewModelScope, SharingStarted.WhileSubscribed(5_000), emptyList(),
    )

    val scanning = MutableStateFlow(false)

    fun scan() {
        if (scanning.value) return
        viewModelScope.launch {
            scanning.value = true
            try {
                kotlinx.coroutines.withContext(Dispatchers.IO) { scanner.scanFull() }
            } finally {
                scanning.value = false
            }
        }
    }

    fun playAt(index: Int) = playFrom(songs.value.map { it.toSong() }, index)

    fun playFavoriteAt(index: Int) = playFrom(favorites.value.map { it.toSong() }, index)

    private fun playFrom(list: List<com.catclaw.music.kt.core.model.Song>, index: Int) {
        if (list.isEmpty()) return
        session.ensure()
        controller.playSongs(list, startIndex = index)
    }
}
