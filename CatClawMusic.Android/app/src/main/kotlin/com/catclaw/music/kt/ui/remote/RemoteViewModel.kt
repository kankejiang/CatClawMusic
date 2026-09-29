package com.catclaw.music.kt.ui.remote

import androidx.lifecycle.ViewModel
import androidx.lifecycle.viewModelScope
import com.catclaw.music.kt.core.model.ConnectionProfile
import com.catclaw.music.kt.core.model.ProtocolType
import com.catclaw.music.kt.core.playback.PlayerController
import com.catclaw.music.kt.data.db.ConnectionDao
import com.catclaw.music.kt.data.download.MusicDownloader
import com.catclaw.music.kt.data.db.LibraryDao
import com.catclaw.music.kt.data.db.SongListItem
import com.catclaw.music.kt.data.db.toModel
import com.catclaw.music.kt.data.db.toSong
import com.catclaw.music.kt.data.remote.SubsonicClient
import com.catclaw.music.kt.data.remote.SyncProgress
import com.catclaw.music.kt.data.remote.WebDavClient
import com.catclaw.music.kt.data.remote.WebDavImporter
import com.catclaw.music.kt.data.smb.SmbImporter
import com.catclaw.music.kt.data.smb.SmbProbe
import com.catclaw.music.kt.data.remote.SubsonicImporter
import com.catclaw.music.kt.data.service.PlaybackSessionStarter
import dagger.hilt.android.lifecycle.HiltViewModel
import javax.inject.Inject
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.SharingStarted
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.map
import kotlinx.coroutines.flow.stateIn
import kotlinx.coroutines.launch
import kotlinx.coroutines.withContext

/**
 * 远程页要给用户看的一句话。
 *
 * 为什么是类型而不是现成的字符串：本端语言是应用内切换的（Activity 的 `attachBaseContext`
 * 里覆盖 configuration），ViewModel 拿到的 application context **不一定带这个覆盖**，
 * 在那边 `getString` 会拿到错语言。所以 VM 只报"发生了什么"，措辞交给页面组合期的 `stringResource`。
 */
sealed interface RemoteMessage {
    data object NeedHost : RemoteMessage
    data class TestResult(val ok: Boolean, val detail: String) : RemoteMessage
    data class SyncResult(val scanned: Int, val inserted: Int, val updated: Int, val elapsedMs: Long) : RemoteMessage
    data class Failed(val detail: String) : RemoteMessage
}

/** 发现 tab 的远程音源：配置 → ping 测试 → 导库 → 直接播 */
@HiltViewModel
class RemoteViewModel @Inject constructor(
    private val connectionDao: ConnectionDao,
    libraryDao: LibraryDao,
    private val importer: SubsonicImporter,
    private val webDavImporter: WebDavImporter,
    private val smbImporter: SmbImporter,
    private val controller: PlayerController,
    private val session: PlaybackSessionStarter,
    private val downloader: MusicDownloader,
) : ViewModel() {

    val downloadTasks = downloader.tasks
    val cachedSongIds = downloader.cachedSongIds
        .stateIn(viewModelScope, SharingStarted.WhileSubscribed(5_000), emptyList())

    /** 下载远程曲：断线后再次点击即从 .part 续传 */
    fun download(song: com.catclaw.music.kt.core.model.Song) = downloader.enqueue(song)

    fun cancelDownload(songId: Int) = downloader.cancel(songId)

    val profiles: StateFlow<List<ConnectionProfile>> = connectionDao.observeAll()
        .map { rows -> rows.map { it.toModel() } }
        .stateIn(viewModelScope, SharingStarted.WhileSubscribed(5_000), emptyList())

    val remoteSongs: StateFlow<List<SongListItem>> = libraryDao.observeRemoteSongs()
        .stateIn(viewModelScope, SharingStarted.WhileSubscribed(5_000), emptyList())

    private val _busy = MutableStateFlow(false)
    val busy: StateFlow<Boolean> = _busy

    private val _message = MutableStateFlow<RemoteMessage?>(null)
    val message: StateFlow<RemoteMessage?> = _message

    private val _progress = MutableStateFlow<SyncProgress?>(null)
    /**
     * 导入进度，null = 没在导。总数未知时是 `SyncProgress(0, 0)`，
     * 页面据此显示"正在列出远端目录…"——MAUI 那条 `total == 0` 走不定进度的规则。
     */
    val progress: StateFlow<SyncProgress?> = _progress

    fun save(profile: ConnectionProfile) {
        if (profile.host.isBlank()) {
            _message.value = RemoteMessage.NeedHost
            return
        }
        viewModelScope.launch {
            connectionDao.upsert(
                com.catclaw.music.kt.data.db.entity.ConnectionProfileEntity(
                    id = profile.id,
                    name = profile.name,
                    protocol = profile.protocol.ordinal,
                    host = profile.host,
                    port = profile.port,
                    userName = profile.userName,
                    password = profile.password,
                    basePath = profile.basePath,
                    isEnabled = profile.isEnabled,
                    apiVersion = profile.apiVersion,
                    clientName = profile.clientName,
                    useHttps = profile.useHttps,
                    domainName = profile.domainName,
                    shareName = profile.shareName,
                    serverType = profile.serverType,
                ),
            )
        }
    }

    fun delete(id: Int) {
        viewModelScope.launch { connectionDao.delete(id) }
    }

    fun test(profile: ConnectionProfile) {
        viewModelScope.launch {
            _busy.value = true
            _message.value = withContext(Dispatchers.IO) {
                runCatching {
                    when (profile.protocol) {
                        ProtocolType.Navidrome -> SubsonicClient(profile).ping().let {
                            RemoteMessage.TestResult(it.ok, if (it.ok) it.version.orEmpty() else it.message.orEmpty())
                        }
                        ProtocolType.WebDAV -> WebDavClient(profile).test().let {
                            RemoteMessage.TestResult(
                                it.ok,
                                if (it.ok) "${it.serverType.name} ${it.davPrefix}" else it.message.orEmpty(),
                            )
                        }
                        ProtocolType.SMB -> smbProbeText(profile)
                    }
                }.getOrElse { RemoteMessage.TestResult(false, it.message ?: it.javaClass.simpleName) }
            }
            _busy.value = false
        }
    }

    /** `SmbProbe.test` 的形状是 "ok …" / "fail …"（:data 那层的约定），这里只翻一次成成/败 */
    private fun smbProbeText(profile: ConnectionProfile): RemoteMessage.TestResult {
        val text = SmbProbe.test(profile)
        return RemoteMessage.TestResult(
            ok = text.startsWith("ok"),
            detail = text.removePrefix("ok ").removePrefix("fail "),
        )
    }

    fun import(profile: ConnectionProfile) {
        if (_busy.value) return
        viewModelScope.launch {
            _busy.value = true
            try {
                _message.value = runCatching {
                    withContext(Dispatchers.IO) {
                        val report: (SyncProgress) -> Unit = { _progress.value = it }
                        // scanned 一律报"这首歌源里有多少**首**"：Navidrome 的 albums、
                        // WebDAV/SMB 的 listed(文件数) 都不是首数，混进去就是页面上一个对不上的数
                        when (profile.protocol) {
                            ProtocolType.Navidrome -> importer.importAll(profile, onProgress = report).let {
                                RemoteMessage.SyncResult(it.tracks, it.inserted, it.updated, it.elapsedMs)
                            }
                            ProtocolType.WebDAV -> webDavImporter.importAll(profile, onProgress = report).let {
                                RemoteMessage.SyncResult(it.audio, it.inserted, it.updated, it.elapsedMs)
                            }
                            ProtocolType.SMB -> smbImporter.importAll(profile, onProgress = report).let {
                                RemoteMessage.SyncResult(it.audio, it.inserted, it.updated, it.elapsedMs)
                            }
                        }
                    }
                }.getOrElse { RemoteMessage.Failed(it.message ?: it.javaClass.simpleName) }
            } finally {
                // 失败也要把进度条收掉：留在页面上就是一条永远走不完的进度
                _progress.value = null
                _busy.value = false
            }
        }
    }

    fun playAt(index: Int) {
        val songs = remoteSongs.value.map { it.toSong() }
        if (songs.isEmpty()) return
        session.ensure()
        controller.playSongs(songs, startIndex = index)
    }
}
