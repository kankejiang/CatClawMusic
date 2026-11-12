package com.catclaw.music.kt.data.repository.library

import com.catclaw.music.kt.core.audio.AudioCache
import com.catclaw.music.kt.core.library.SongStat
import com.catclaw.music.kt.core.model.Song
import com.catclaw.music.kt.core.playback.PlayerController
import com.catclaw.music.kt.data.db.LibraryDao
import com.catclaw.music.kt.data.db.PlaylistDao
import com.catclaw.music.kt.data.db.SongListItem
import com.catclaw.music.kt.data.db.entity.FavoriteEntity
import com.catclaw.music.kt.data.db.toSong
import com.catclaw.music.kt.data.lyrics.LyricsReader
import com.catclaw.music.kt.data.media.MediaPathResolver
import com.catclaw.music.kt.data.service.PlaybackSessionStarter
import javax.inject.Inject
import javax.inject.Singleton
import kotlinx.coroutines.flow.Flow

/**
 * 音乐库唯一数据入口（MVVM 重构 B2）。
 *
 * 只做两件事：
 * 1. 聚合 DAO / 文件路径解析 / 歌词读取 / 音频缓存，向 VM 暴露领域数据；
 * 2. 播放编排（起播 / 随机 / 下一首播放）—— 这是跨页共享的写入口，
 *    `PlayingScreen` 曾靠 `hiltViewModel<LibraryViewModel>()` 借这些能力，就是缺这一层的症状。
 *
 * 列表流保持 cold `Flow`：`stateIn(viewModelScope, WhileSubscribed(5_000))` 属于页面生命周期，留在 VM。
 */
@Singleton
class LibraryRepository @Inject constructor(
    private val libraryDao: LibraryDao,
    private val playlistDao: PlaylistDao,
    private val paths: MediaPathResolver,
    private val lyricsReader: LyricsReader,
    private val audioCache: AudioCache,
    private val player: PlayerController,
    private val playbackSession: PlaybackSessionStarter,
) {
    // ── 只读流 ────────────────────────────────────────────────

    val localSongs: Flow<List<SongListItem>> = libraryDao.observeLocalSongs()

    val allSongs: Flow<List<SongListItem>> = libraryDao.observeAllSongs()

    val artists: Flow<List<com.catclaw.music.kt.data.db.ArtistListItem>> = libraryDao.observeArtists()

    val albums: Flow<List<com.catclaw.music.kt.data.db.AlbumListItem>> = libraryDao.observeAlbums()

    val favorites: Flow<List<SongListItem>> = libraryDao.observeFavorites()

    val recent: Flow<List<SongListItem>> = libraryDao.observeRecentSongs(100)

    val newArrivals: Flow<List<SongListItem>> = libraryDao.observeNewArrivals(20)

    val playlists: Flow<List<com.catclaw.music.kt.data.db.entity.PlaylistEntity>> = playlistDao.observePlaylists()

    // ── 查询 ──────────────────────────────────────────────────

    suspend fun storage(): Pair<Long, Long> = libraryDao.localBytesTotal() to audioCache.totalBytes()

    suspend fun localSongStats(): List<SongStat> = libraryDao.localSongStats()

    suspend fun songsOfArtist(artistId: Int) = libraryDao.songsOfArtist(artistId)

    suspend fun songsOfAlbum(albumId: Int) = libraryDao.songsOfAlbum(albumId)

    suspend fun albumsOfArtist(artistId: Int) = libraryDao.albumsOfArtist(artistId)

    suspend fun albumHeader(albumId: Int) = libraryDao.albumHeader(albumId)

    suspend fun songListItemById(songId: Int) = libraryDao.songListItemById(songId)

    /** 详情页一行：MediaStore content URI 解回真实路径，否则「文件格式」永远是"无" */
    suspend fun songDetail(id: Int) = libraryDao.songDetail(id)?.let { it.copy(filePath = paths.resolve(it.filePath)) }

    /**
     * 歌词预览：与歌词页同一条取词链路（DB 缓存 → 侧车 → 内嵌标签）。
     * 在线曲（负数 id）回落到当前队列里的 Song。
     */
    suspend fun lyricsFor(songId: Int): List<String> {
        val entity = libraryDao.songById(songId)
        val song = entity?.toSong()
            ?: player.state.value.currentSong?.takeIf { it.id == songId }
            ?: return emptyList()
        val lyrics = lyricsReader.loadFor(song) ?: return emptyList()
        return lyrics.lines.map { it.text }.filter { it.isNotBlank() }
    }

    // ── 收藏（唯一写入口） ────────────────────────────────────

    suspend fun toggleFavorite(songId: Int) {
        if (libraryDao.isFavorite(songId)) libraryDao.removeFavorite(songId)
        else libraryDao.upsertFavorite(FavoriteEntity(songId = songId, addedAt = System.currentTimeMillis()))
    }

    suspend fun addToPlaylist(playlistId: Int, songId: Int) {
        playlistDao.appendAll(playlistId, listOf(songId))
    }

    suspend fun addToPlaylist(playlistId: Int, songIds: List<Int>) {
        if (songIds.isEmpty()) return
        playlistDao.appendAll(playlistId, songIds)
    }

    // ── 播放编排 ──────────────────────────────────────────────

    fun play(songs: List<Song>, index: Int) {
        if (songs.isEmpty()) return
        playbackSession.ensure()
        player.playSongs(songs, startIndex = index)
    }

    fun playShuffled(songs: List<Song>) {
        if (songs.isEmpty()) return
        playbackSession.ensure()
        player.playShuffled(songs)
    }

    fun playNext(song: Song) {
        playbackSession.ensure()
        player.addToQueue(song, nextUp = true)
    }
}
