package com.catclaw.music.kt.ui.library

import android.content.Context
import androidx.lifecycle.ViewModel
import androidx.lifecycle.viewModelScope
import com.catclaw.music.kt.core.audio.AudioCache
import com.catclaw.music.kt.core.library.SongSortFields
import com.catclaw.music.kt.core.library.SongSortKey
import com.catclaw.music.kt.core.library.filterAndSortSongs
import com.catclaw.music.kt.core.playback.PlayerController
import com.catclaw.music.kt.data.db.LibraryDao
import com.catclaw.music.kt.data.db.SongListItem
import com.catclaw.music.kt.data.db.entity.FavoriteEntity
import com.catclaw.music.kt.data.db.PlaylistDao
import com.catclaw.music.kt.data.db.toSong
import com.catclaw.music.kt.data.library.LibraryScanner
import com.catclaw.music.kt.data.media.MediaPathResolver
import com.catclaw.music.kt.data.service.PlaybackSessionStarter
import dagger.hilt.android.lifecycle.HiltViewModel
import dagger.hilt.android.qualifiers.ApplicationContext
import javax.inject.Inject
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.SharingStarted
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.flow.combine
import kotlinx.coroutines.flow.stateIn
import kotlinx.coroutines.launch

@HiltViewModel
class LibraryViewModel @Inject constructor(
    @ApplicationContext private val context: Context,
    private val scanner: LibraryScanner,
    private val controller: PlayerController,
    private val session: PlaybackSessionStarter,
    private val libraryDao: LibraryDao,
    private val playlistDao: PlaylistDao,
    private val paths: MediaPathResolver,
    private val audioCache: AudioCache,
) : ViewModel() {

    /**
     * 音乐库 Hero 上那行「存储占用」的两个数：曲库文件与音频缓存**分开报**。
     * 合成一个总数就变成谁也对不上的数字（曲库是声明式大小，缓存是实际落盘字节）。
     */
    suspend fun storage(): Pair<Long, Long> = libraryDao.localBytesTotal() to audioCache.totalBytes()

    /** 下钻页要按艺人/专辑现读一次歌单 */
    suspend fun songsOfArtist(artistId: Int) = libraryDao.songsOfArtist(artistId)

    suspend fun songsOfAlbum(albumId: Int) = libraryDao.songsOfAlbum(albumId)

    /**
     * 详情页的一行。本地曲库里存的是 MediaStore 的 content URI，
     * 而 URI 里没有文件名与后缀 —— 不解回真实路径，「文件格式」这一格就永远是"无"。
     */
    suspend fun songDetail(id: Int) = libraryDao.songDetail(id)?.let { it.copy(filePath = paths.resolve(it.filePath)) }

    /** 最近播放：歌单页顶部那个入口卡片与它点进去的列表，同一份数据源 */
    val recent = libraryDao.observeRecentSongs(100).stateIn(
        viewModelScope, SharingStarted.WhileSubscribed(5_000), emptyList(),
    )

    fun playRows(rows: List<SongListItem>, index: Int) = playFrom(rows.map { it.toSong() }, index)

    /**
     * 收藏的唯一写入口。MAUI 侧这个入口是歌曲长按菜单（`SongContextMenu` 的 ♥ 那一行），
     * 本端此前只有读没有写 —— "我喜欢的"这张表于是永远为空，点了统计格只会看到空页。
     */
    fun toggleFavorite(songId: Int) {
        viewModelScope.launch {
            if (libraryDao.isFavorite(songId)) libraryDao.removeFavorite(songId)
            else libraryDao.upsertFavorite(FavoriteEntity(songId = songId, addedAt = System.currentTimeMillis()))
        }
    }

    /**
     * 加进已有歌单。走 [PlaylistDao.appendAll] 而不是自己插一行：
     * 位置由它从当前最大值续起、重复由 `INSERT OR IGNORE` 吃掉（PlaylistSongs 上有唯一索引），
     * 而 `SongCount` 那列**只有它会顺手更新**——早先这里直接 insertItem，
     * 于是从菜单加进去的歌，歌单卡上的数字一直不涨。
     */
    fun addToPlaylist(playlistId: Int, songId: Int) {
        viewModelScope.launch { playlistDao.appendAll(playlistId, listOf(songId)) }
    }

    val songs = libraryDao.observeLocalSongs().stateIn(
        viewModelScope, SharingStarted.WhileSubscribed(5_000), emptyList(),
    )

    private val _songSort = MutableStateFlow(SongSortKey.Title)
    val songSort = _songSort.asStateFlow()

    private val _songQuery = MutableStateFlow("")
    val songQuery = _songQuery.asStateFlow()

    /** 页面上真正显示的那份列表。过滤与排序的规则在 `:core`（[filterAndSortSongs]），这里只接线 */
    val displaySongs = combine(songs, _songSort, _songQuery) { list, sort, query ->
        filterAndSortSongs(list, sort, query) {
            SongSortFields(it.title, it.artist, it.album, it.durationSeconds, it.dateAdded, it.id)
        }
    }.stateIn(viewModelScope, SharingStarted.WhileSubscribed(5_000), emptyList())

    fun setSongSort(sort: SongSortKey) {
        _songSort.value = sort
    }

    fun setSongQuery(query: String) {
        _songQuery.value = query
    }

    val artists = libraryDao.observeArtists().stateIn(
        viewModelScope, SharingStarted.WhileSubscribed(5_000), emptyList(),
    )

    val albums = libraryDao.observeAlbums().stateIn(
        viewModelScope, SharingStarted.WhileSubscribed(5_000), emptyList(),
    )

    /**
     * 艺人 / 专辑列表页的搜索词。两类页共用一个词：它们从不同时显示，
     * 各开一个流只是多一份"切页忘了清"的状态 —— 进另一类页时由页面清（见 `LaunchedEffect(kind)`）。
     *
     * 这两个流必须排在 [artists]/[albums] 之后：`combine` 的参数是**立即求值**的，
     * 写在它们上面会在构造期读到还没初始化的字段。
     */
    private val _infoQuery = MutableStateFlow("")
    val infoQuery = _infoQuery.asStateFlow()

    fun setInfoQuery(query: String) {
        _infoQuery.value = query
    }

    /** 名字含关键词就留下。不再排序：DAO 那条 `ORDER BY Name` 已经是名字升序 */
    val displayArtists = combine(artists, _infoQuery) { list, query ->
        val q = query.trim()
        if (q.isEmpty()) list else list.filter { it.name.contains(q, ignoreCase = true) }
    }.stateIn(viewModelScope, SharingStarted.WhileSubscribed(5_000), emptyList())

    val displayAlbums = combine(albums, _infoQuery) { list, query ->
        val q = query.trim()
        if (q.isEmpty()) {
            list
        } else {
            // 专辑页要能按专辑名找也能按歌手找："搜 周传雄 出来他的全部专辑"才是这张页该有的行为
            list.filter { it.title.contains(q, ignoreCase = true) || it.artist.contains(q, ignoreCase = true) }
        }
    }.stateIn(viewModelScope, SharingStarted.WhileSubscribed(5_000), emptyList())

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

    /**
     * 「下一首播放」：插到当前曲目之后。
     * 队列侧的口子一直有（`addToQueue(nextUp = true)`），菜单里此前没摆它，
     * 是因为那条评论误判成"播放器不支持定点插入"——查清了，接上。
     */
    fun playNext(song: com.catclaw.music.kt.core.model.Song) {
        session.ensure()
        controller.addToQueue(song, nextUp = true)
    }

    fun playFavoriteAt(index: Int) = playFrom(favorites.value.map { it.toSong() }, index)

    /** 「随机播放」按钮：MAUI `AllSongsViewModel.ShufflePlayAsync` 的对应物，规则在播放器那一侧 */
    fun playShuffled(rows: List<SongListItem>) {
        if (rows.isEmpty()) return
        session.ensure()
        controller.playShuffled(rows.map { it.toSong() })
    }

    fun playRecentAt(index: Int) = playFrom(recent.value.map { it.toSong() }, index)

    private fun playFrom(list: List<com.catclaw.music.kt.core.model.Song>, index: Int) {
        if (list.isEmpty()) return
        session.ensure()
        controller.playSongs(list, startIndex = index)
    }
}
