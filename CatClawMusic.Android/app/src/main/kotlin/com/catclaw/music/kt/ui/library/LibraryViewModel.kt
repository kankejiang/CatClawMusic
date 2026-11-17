package com.catclaw.music.kt.ui.library

import android.content.Context
import androidx.lifecycle.ViewModel
import androidx.lifecycle.viewModelScope
import com.catclaw.music.kt.core.audio.AudioCache
import com.catclaw.music.kt.core.library.SongStat
import com.catclaw.music.kt.core.library.SongSortFields
import com.catclaw.music.kt.core.library.SongSortKey
import com.catclaw.music.kt.core.library.defaultDescending
import com.catclaw.music.kt.core.library.SongSourceFilter
import com.catclaw.music.kt.core.library.filterAndSortSongs
import com.catclaw.music.kt.core.library.filterBySource
import com.catclaw.music.kt.data.repository.library.LibraryRepository
import com.catclaw.music.kt.data.repository.scan.ScanRepository
import com.catclaw.music.kt.data.db.SongListItem
import com.catclaw.music.kt.data.db.toSong
import com.catclaw.music.kt.data.db.entity.FavoriteEntity
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
import com.catclaw.music.kt.core.library.ConnectionFilter
import com.catclaw.music.kt.core.library.NetworkProtocolFilter
import com.catclaw.music.kt.core.library.filterByNetworkProtocol
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.map

@HiltViewModel
class LibraryViewModel @Inject constructor(
    @ApplicationContext private val context: Context,
    private val library: LibraryRepository,
    private val scanRepository: ScanRepository,
) : ViewModel() {

    /**
     * 音乐库 Hero 上那行「存储占用」的两个数：曲库文件与音频缓存**分开报**。
     * 合成一个总数就变成谁也对不上的数字（曲库是声明式大小，缓存是实际落盘字节）。
     */
    suspend fun storage(): Pair<Long, Long> = library.storage()

    /**
     * 「存储占用 / 数据洞察」的原料。与 storage() 分开：那个只要两个总数，
     * 这两张卡要按档分组（分组规则在 :core 的 sizeBuckets / qualityInsight / artistInsight）。
     */
    suspend fun localSongStats(): List<SongStat> = library.localSongStats()

    /**
     * 设备剩余空间。取不到就回 0，由界面显示 "--" —— 编一个数字比不显示更糟。
     *
     * 用 availableBlocksLong * blockSizeLong：availableBlocks/blockSize 是 Int，
     * 在现在的容量下相乘会溢出成负数（C# 那边也踩过同一个坑，注释就写在 GetFreeSpaceText 里）。
     */
    suspend fun freeSpaceBytes(): Long = kotlinx.coroutines.withContext(Dispatchers.IO) {
        runCatching {
            val path = context.getExternalFilesDir(null) ?: context.filesDir
            val stat = android.os.StatFs(path.absolutePath)
            stat.availableBlocksLong * stat.blockSizeLong
        }.getOrDefault(0L)
    }

    /** 下钻页要按艺人/专辑现读一次歌单 */
    suspend fun songsOfArtist(artistId: Int) = library.songsOfArtist(artistId)

    suspend fun songsOfAlbum(albumId: Int) = library.songsOfAlbum(albumId)

    /**
     * 歌曲详情页的歌词预览。走与歌词页**同一条**取词链路（DB 缓存 → 侧车文件 → 内嵌标签），
     * 而不是只读缓存 —— 后者会让"从没在歌词页打开过"的歌永远显示「暂无歌词」。
     *
     * 只给行文本：逐字填充 / 译文 / 罗马音是歌词页的活，详情页要的是「有没有词、长什么样」。
     * 空行剔掉：那是 LRC 的排版留白，在预览里只会变成一段一段的空档。
     */
    suspend fun lyricsFor(songId: Int): List<String> = library.lyricsFor(songId)

    /** 艺人页「专辑」Tab 的那份专辑。与 [songsOfArtist] 同一套成员判据（含挂名合作） */
    suspend fun albumsOfArtist(artistId: Int) = library.albumsOfArtist(artistId)

    /** 专辑二级页的头卡：封面 / 歌手 / 年份 */
    suspend fun albumHeader(albumId: Int) = library.albumHeader(albumId)

    /** 最近添加（资料库首页那张横滑卡）。限额与发现页的「新曲」段一致，两处看到的数是同一批 */
    val newArrivals = library.newArrivals
        .stateIn(viewModelScope, SharingStarted.WhileSubscribed(5_000), emptyList())

    /**
     * 详情页的一行。本地曲库里存的是 MediaStore 的 content URI，
     * 而 URI 里没有文件名与后缀 —— 不解回真实路径，「文件格式」这一格就永远是"无"。
     */
    suspend fun songDetail(id: Int) = library.songDetail(id)

    /** 最近播放：歌单页顶部那个入口卡片与它点进去的列表，同一份数据源 */
    val recent = library.recent.stateIn(
        viewModelScope, SharingStarted.WhileSubscribed(5_000), emptyList(),
    )

    fun playRows(rows: List<SongListItem>, index: Int) = library.play(rows.map { it.toSong() }, index)

    /**
     * 从歌曲详情页起播某一首（MAUI `SongDetailPage.PlaySongAsync` 的对应物）。
     * 按 id 现取一行再入队：详情页手上只有 id，而队列要的是完整 Song。
     */
    fun playSongById(songId: Int) {
        viewModelScope.launch {
            val row = library.songListItemById(songId) ?: return@launch
            library.play(listOf(row.toSong()), 0)
        }
    }

    /**
     * 收藏的唯一写入口。MAUI 侧这个入口是歌曲长按菜单（`SongContextMenu` 的 ♥ 那一行），
     * 本端此前只有读没有写 —— "我喜欢的"这张表于是永远为空，点了统计格只会看到空页。
     */
    fun toggleFavorite(songId: Int) {
        viewModelScope.launch { library.toggleFavorite(songId) }
    }

    /**
     * 加进已有歌单。走 [PlaylistDao.appendAll] 而不是自己插一行：
     * 位置由它从当前最大值续起、重复由 `INSERT OR IGNORE` 吃掉（PlaylistSongs 上有唯一索引），
     * 而 `SongCount` 那列**只有它会顺手更新**——早先这里直接 insertItem，
     * 于是从菜单加进去的歌，歌单卡上的数字一直不涨。
     */
    fun addToPlaylist(playlistId: Int, songId: Int) {
        viewModelScope.launch { library.addToPlaylist(playlistId, songId) }
    }

    /**
     * 整批加进歌单（专辑页那颗「＋ 添加到」）。
     *
     * C# 的 `AlbumDetailPage.xaml:55` 也绑了一颗同名按钮，但它绑的 `AddToPlaylistCommand`
     * **在整个 C# 工程里都不存在** —— MAUI 的绑定失败是静默的，那颗按钮点下去什么都不会发生。
     * 本端把它做实：歌单列表与批量追加的 DAO 都是现成的，没有理由再留一个坏按钮。
     */
    fun addToPlaylist(playlistId: Int, songIds: List<Int>) {
        if (songIds.isEmpty()) return
        viewModelScope.launch { library.addToPlaylist(playlistId, songIds) }
    }

    val songs = library.localSongs.stateIn(
        viewModelScope, SharingStarted.WhileSubscribed(5_000), emptyList(),
    )

    private val _songSort = MutableStateFlow(SongSortKey.Title)
    val songSort = _songSort.asStateFlow()

    private val _songSortDescending = MutableStateFlow(SongSortKey.Title.defaultDescending)
    val songSortDescending = _songSortDescending.asStateFlow()

    private val _songQuery = MutableStateFlow("")
    val songQuery = _songQuery.asStateFlow()

    /** 页面上真正显示的那份列表。过滤与排序的规则在 `:core`（[filterAndSortSongs]），这里只接线 */
    val displaySongs = combine(songs, _songSort, _songQuery, _songSortDescending) { list, sort, query, desc ->
        filterAndSortSongs(list, sort, query, descending = desc) {
            SongSortFields(it.title, it.artist, it.album, it.durationSeconds, it.dateAdded, it.id)
        }
    }.stateIn(viewModelScope, SharingStarted.WhileSubscribed(5_000), emptyList())

    /**
     * 选排序档：换档就换成该档的默认方向，点同一档就是把方向翻过来 ——
     * 与 C# `AllSongsViewModel.ToggleSort` 同一条规则（`SortAscending = key != "added" && ...`）。
     *
     * 区别在于 C# 那边翻了之后 `SortSongs` 里那个 `dir` 从没被用过，升降序只是界面上的一颗 chip；
     * 本端把它做实。
     */
    fun setSongSort(sort: SongSortKey) {
        if (_songSort.value == sort) {
            _songSortDescending.value = !_songSortDescending.value
        } else {
            _songSort.value = sort
            _songSortDescending.value = sort.defaultDescending
        }
    }

    fun setSongQuery(query: String) {
        _songQuery.value = query
    }

    /**
     * 全部来源的歌曲 —— 「全部歌曲」页来源筛选的底座。
     *
     * 刻意与 [songs] 分开：hub 上「曲库时长 / 占用」报的是**本地曲库**，
     * 把网络曲混进去那两个数字会凭空空涨（网络曲的 FileSize 根本不是本地占用）。
     */
    private val allSongs = library.allSongs.stateIn(
        viewModelScope, SharingStarted.WhileSubscribed(5_000), emptyList(),
    )

    /** 「全部 / 本地 / 网络」来源筛选（MAUI `AllSongsViewModel.FilterMode` 同三档） */
    private val _songSource = MutableStateFlow(SongSourceFilter.All)
    val songSource = _songSource.asStateFlow()

    fun setSongSource(filter: SongSourceFilter) {
        _songSource.value = filter
        // 切到「本地」时把网络服务来源清掉：本地曲没有网络归属，
        // 留着一个已选中的 Navidrome 筛选会让列表恒空，看起来像"本地曲库空了"。
        if (filter == SongSourceFilter.Local) _songProtocol.value = NetworkProtocolFilter.All
    }

    /**
     * 网络服务来源筛选（WebDAV / Navidrome / SMB），只在看网络曲时有意义。
     *
     * 与 [songSource] 是两个维度：那个分本地 vs 网络，这个再细分网络里是哪一种服务。
     * 入口在「⋯」更多菜单里 —— 它不是每次都要动的开关，放进常驻区会挤掉排序。
     */
    private val _songProtocol = MutableStateFlow(NetworkProtocolFilter.All)
    val songProtocol = _songProtocol.asStateFlow()

    fun setSongProtocol(filter: NetworkProtocolFilter) {
        _songProtocol.value = filter
        // 选了具体服务说明用户想看网络曲：若当前停在「本地」就顺手切到「网络」，
        // 否则选了 Navidrome 却因为停留在"本地"而一首都不显示。
        if (filter != NetworkProtocolFilter.All && _songSource.value == SongSourceFilter.Local) {
            _songSource.value = SongSourceFilter.Network
        }
    }

    /**
     * 全部歌曲页真正显示的那份（含网络曲）。与 [displaySongs] 正交：那份是"本地曲库"这一档。
     *
     * 叠加次序是"先按来源筛、再过滤排序"：MAUI 也是拿已加载的数据在内存里筛（不重查 DB），
     * 两者结果相同，而这样写少一次查询。
     */
    /**
     * 已停用连接覆盖的协议；`null` = **还没从库里读出来**。
     *
     * 初始值绝不能是 `emptySet()`：那在 combine 首次计算时等于「所有连接都停用」，
     * 会把全部远程曲滤掉，界面先闪一次「只剩本地曲」。真机上就撞到过
     * ——关掉一条连接后音乐库显示 10 首（本该 623 首），而播放队列是对的。
     *
     * `null` 表示「状态未知」，此时**不做过滤**（宁可多显示，也不要凭空少歌）。
     * 数据到达后（哪怕真的是空集）才按真实值过滤。
     */
    private val enabledProtocols: StateFlow<Set<Int>?> = library.enabledProtocols
        .map<Set<Int>, Set<Int>?> { it }
        .stateIn(viewModelScope, SharingStarted.WhileSubscribed(5_000), null)

    /** 网络曲库一行的计数：全部曲里 Source != 0 的部分，且同样受「停用连接」影响 */
    val networkSongCount: StateFlow<Int> = combine(
        library.allSongs,
        enabledProtocols,
    ) { list, protocols ->
        if (protocols == null) {
            list.count { it.source != 0 }
        } else {
            list.count { row -> row.source != 0 && protocols.contains(row.protocol) }
        }
    }.stateIn(viewModelScope, SharingStarted.WhileSubscribed(5_000), 0)

    val displayAllSongs: StateFlow<List<SongListItem>> =
        combine(
            allSongs,
            _songSort,
            _songQuery,
            _songSource,
            _songSortDescending,
            enabledProtocols,
            _songProtocol,
        ) { values ->
            @Suppress("UNCHECKED_CAST")
            val list = values[0] as List<SongListItem>
            val sort = values[1] as SongSortKey
            val query = values[2] as String
            val source = values[3] as SongSourceFilter
            val desc = values[4] as Boolean
            @Suppress("UNCHECKED_CAST")
            val protocols = values[5] as Set<Int>?
            val protocolFilter = values[6] as NetworkProtocolFilter

            // 先剔掉「来自已停用连接」的曲目，再做来源筛选与排序。
            // 次序重要：先过滤再排序，用户看到的序就是点下去播的序。
            // protocols == null（还没读到连接表）时跳过这一步，不凭空隐藏任何歌。
            val sorted = filterAndSortSongs(list, sort, query, descending = desc) {
                SongSortFields(it.title, it.artist, it.album, it.durationSeconds, it.dateAdded, it.id)
            }
            val visible = if (protocols == null) {
                sorted
            } else {
                ConnectionFilter.filterByEnabledProtocols(
                    sorted,
                    protocols,
                    sourceOf = { it.source },
                    protocolOf = { it.protocol },
                )
            }
            // 两个筛选叠加：先按本地/网络，再按具体网络服务。
            // 后者在 source=Local 时恒空，所以 setSongSource(Local) 会把它清回 All。
            filterByNetworkProtocol(
                filterBySource(visible, source) { it.source },
                protocolFilter,
                protocolOf = { it.protocol },
                sourceOf = { it.source },
            )
        }.stateIn(viewModelScope, SharingStarted.WhileSubscribed(5_000), emptyList())

    val artists = library.artists.stateIn(
        viewModelScope, SharingStarted.WhileSubscribed(5_000), emptyList(),
    )

    val albums = library.albums.stateIn(
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

    val favorites = library.favorites.stateIn(
        viewModelScope, SharingStarted.WhileSubscribed(5_000), emptyList(),
    )

    val playlists = library.playlists.stateIn(
        viewModelScope, SharingStarted.WhileSubscribed(5_000), emptyList(),
    )

    val scanning = MutableStateFlow(false)

    fun scan() {
        if (scanning.value) return
        viewModelScope.launch {
            scanning.value = true
            try {
                scanRepository.scanFull()
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
        library.playNext(song)
    }

    fun playFavoriteAt(index: Int) = library.play(favorites.value.map { it.toSong() }, index)

    /** 「随机播放」按钮：MAUI `AllSongsViewModel.ShufflePlayAsync` 的对应物，规则在播放器那一侧 */
    fun playShuffled(rows: List<SongListItem>) {
        library.playShuffled(rows.map { it.toSong() })
    }

    fun playRecentAt(index: Int) = library.play(recent.value.map { it.toSong() }, index)

}
