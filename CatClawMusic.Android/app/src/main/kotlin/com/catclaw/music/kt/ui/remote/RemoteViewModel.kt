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

    private val _message = MutableStateFlow<String?>(null)
    val message: StateFlow<String?> = _message

    fun save(profile: ConnectionProfile) {
        if (profile.host.isBlank()) {
            _message.value = "host required"
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
                when (profile.protocol) {
                    ProtocolType.Navidrome -> SubsonicClient(profile).ping().let {
                        if (it.ok) "ok ${it.version}" else "fail ${it.message}"
                    }
                    ProtocolType.WebDAV -> WebDavClient(profile).test().let {
                        if (it.ok) "ok ${it.serverType.name} ${it.davPrefix}" else "fail ${it.message}"
                    }
                    ProtocolType.SMB -> SmbProbe.test(profile)
                }
            }
            _busy.value = false
        }
    }

    fun import(profile: ConnectionProfile) {
        if (_busy.value) return
        viewModelScope.launch {
            _busy.value = true
            _message.value = runCatching {
                withContext(Dispatchers.IO) {
                    when (profile.protocol) {
                        ProtocolType.Navidrome -> importer.importAll(profile).let {
                            "albums=${it.albums} tracks=${it.tracks} +${it.inserted} ~${it.updated} ${it.elapsedMs}ms"
                        }
                        ProtocolType.WebDAV -> webDavImporter.importAll(profile).let {
                            "files=${it.listed} audio=${it.audio} +${it.inserted} ~${it.updated} ${it.elapsedMs}ms"
                        }
                        ProtocolType.SMB -> smbImporter.importAll(profile).let {
                            "files=${it.listed} audio=${it.audio} +${it.inserted} ~${it.updated} ${it.elapsedMs}ms"
                        }
                    }
                }
            }.getOrElse { "fail ${it.message}" }
            _busy.value = false
        }
    }

    fun playAt(index: Int) {
        val songs = remoteSongs.value.map { it.toSong() }
        if (songs.isEmpty()) return
        session.ensure()
        controller.playSongs(songs, startIndex = index)
    }
}
