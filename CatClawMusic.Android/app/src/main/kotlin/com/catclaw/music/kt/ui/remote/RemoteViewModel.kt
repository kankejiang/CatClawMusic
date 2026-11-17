package com.catclaw.music.kt.ui.remote

import androidx.lifecycle.ViewModel
import androidx.lifecycle.viewModelScope
import com.catclaw.music.kt.core.model.ConnectionProfile
import com.catclaw.music.kt.core.model.ProtocolType
import com.catclaw.music.kt.core.model.SongSource
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
import com.catclaw.music.kt.data.remote.RemoteFile
import com.catclaw.music.kt.data.smb.SmbImporter
import com.catclaw.music.kt.data.smb.SmbProbe
import com.catclaw.music.kt.data.remote.SubsonicImporter
import com.catclaw.music.kt.data.remote.RemoteMetadataBackfiller
import com.catclaw.music.kt.data.media.RemoteCoverBackfiller
import com.catclaw.music.kt.data.service.PlaybackSessionStarter
import dagger.hilt.android.lifecycle.HiltViewModel
import java.util.concurrent.atomic.AtomicBoolean
import javax.inject.Inject
import kotlin.coroutines.cancellation.CancellationException
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.SharingStarted
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.flow.map
import kotlinx.coroutines.flow.stateIn
import kotlinx.coroutines.launch
import kotlinx.coroutines.withContext
import com.catclaw.music.kt.data.smb.SmbTarget
import com.catclaw.music.kt.data.smb.SmbBrowser
import com.catclaw.music.kt.core.smb.SmbPath

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

    /** 补全元数据的结果：四个数分开报，「跳过」和「失败」不是一回事 */
    data class BackfillResult(
        val total: Int,
        val updated: Int,
        val skipped: Int,
        val failed: Int,
        val elapsedMs: Long,
    ) : RemoteMessage

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
    private val backfiller: RemoteMetadataBackfiller,
    private val coverBackfiller: RemoteCoverBackfiller,
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

    /**
     * 播放到远程曲、且这首歌还缺东西（时长/歌手 或 封面）→ 自动补一轮。
     *
     * 用户不该需要知道「补全」是个手动按钮。一次会话只自动触发一次：
     * 补完库就干净了，后面播放到的歌如果还缺，说明上一轮没跑完或断网，手动按钮兜底。
     *
     * **封面与元数据是两轮**：判据不同（前者看 CoverArtPath 空，后者看 Duration/ArtistId），
     * 而且封面要落盘 —— 合成一轮会让「元数据早已齐全但没封面」的歌被整批漏掉。
     */
    private val autoBackfillStarted = AtomicBoolean(false)

    init {
        viewModelScope.launch {
            controller.state.collect { state ->
                val song = state.currentSong ?: return@collect
                if (song.source == SongSource.Local) return@collect
                val needsMeta = song.durationSeconds <= 0
                val needsCover = song.coverArtPath.isNullOrBlank()
                if (!needsMeta && !needsCover) return@collect
                if (autoBackfillStarted.getAndSet(true)) return@collect
                if (profiles.value.none { it.protocol != ProtocolType.Navidrome }) return@collect
                // 先补元数据（便宜、快），再补封面（要落盘、按首归位）
                backfillAll(thenCovers = true)
            }
        }
    }

    private val _busy = MutableStateFlow(false)
    val busy: StateFlow<Boolean> = _busy

    /**
     * WebDAV 目录浏览的一层状态。表单里那格 BasePath 此前只能手打，
     * 打错一个字符就是"连得上却导不到歌"，所以给它一个逐层点选出来的入口。
     */
    data class RemoteBrowser(        val path: String,
        val dirs: List<RemoteFile>,
        val busy: Boolean,
        val error: String?,
        /**
         * SMB 专用：正在选**共享**（而不是共享内的目录）。
         *
         * 共享名不必先手填 —— 打开浏览器时若还没定共享，先列出来让用户点一个，
         * 点完自动填进表单再进目录树。这是 MAUI 的行为，也是所有 SMB 客户端的常规做法。
         */
        val pickingShare: Boolean = false,
        /**
         * 当前层有多少**文件**（不含目录）。
         *
         * 用来区分「空目录」和「只有文件、没有子目录」：NAS 上 `music` 这类共享
         * 常常根目录平铺上千首歌。只显示"没有子目录"会让人以为坏了，
         * 而这时用户要选的恰好是当前这层。
         */
        val fileCount: Int = 0,
    )

    /**
     * 一次浏览请求的结果（IO 线程产出、主线程消费）。
     *
     * 不用嵌套 `Pair`：那东西一旦扩到三个字段就没人读得懂谁是谁了。
     */
    private data class Loaded(
        val items: List<RemoteFile>,
        val picking: Boolean,
        val fileCount: Int = 0,
    )

    private val _browse = MutableStateFlow<RemoteBrowser?>(null)
    val browse: StateFlow<RemoteBrowser?> = _browse.asStateFlow()

    /**
     * 用**表单当前这份**去列目录：用户是边填边试的，读已保存的那条会浏览到另一台服务器。
     *
     * 子目录的 `path` 直接就能当新的 BasePath —— 它是含 `/dav` 前缀的服务器绝对路径，
     * 而 `buildUrl` 有"带前缀不重复拼"的守卫。第一条是 PROPFIND 返回目录自身，按路径剔掉。
     *
     * BasePath 还空着时先走一遍 `test()` 的那套探测（空前缀 → /dav → /webdav）：
     * 直接打服务器根多半是 404，真机就是这样第一次点「浏览目录」报"列目录失败"的。
     * 探到的那一层要回写进浏览状态，后面「用这个目录」才会填对路径。
     */
    fun browseWebDav(profile: ConnectionProfile, path: String) {
        _browse.value = RemoteBrowser(path, emptyList(), busy = true, error = null)
        viewModelScope.launch(Dispatchers.IO) {
            val outcome = runCatching {
                val client = WebDavClient(profile.copy(basePath = path))
                var at = path
                if (at.isBlank() || at == "/") {
                    val probed = client.test()
                    if (!probed.ok) error(probed.message)
                    at = probed.davPrefix.ifBlank { "/" }
                }
                val dirs = client.list(at)
                    .filter { it.isDirectory && it.path.trimEnd('/') != at.trimEnd('/') }
                    .sortedBy { it.name.lowercase() }
                at to dirs
            }
            withContext(Dispatchers.Main) {
                _browse.value = outcome.fold(
                    onSuccess = { (at, dirs) -> RemoteBrowser(at, dirs, busy = false, error = null) },
                    onFailure = { RemoteBrowser(path, emptyList(), busy = false, error = it.message ?: "PROPFIND") },
                )
            }
        }
    }

    fun closeWebDavBrowser() {
        _browse.value = null
    }

    /**
     * SMB 浏览。**共享名不是必填项**：
     *
     * - 表单里已填共享名 → 直接进共享根目录，逐级选文件夹。
     * - 没填 → 先列出服务器上的共享（[SmbBrowser.listShares]），点一个自动填进表单，
     *   再进该共享内部选目录。这正是 MAUI 的行为（`IsShare` 那一档），
     *   也是所有 SMB 客户端的常规做法 —— 用户不该被迫先知道共享叫什么。
     *
     * 路径是**共享内相对路径**（`\` 分隔），与 WebDAV 的服务器绝对 URL 是两套语义。
     */
    fun browseSmb(profile: ConnectionProfile, path: String) {
        val target = SmbTarget(
            host = profile.host.trim(),
            port = profile.port,
            user = profile.userName,
            password = profile.password,
            domain = profile.domainName.orEmpty(),
            share = profile.shareName.orEmpty().trim(),
        )
        val at = SmbPath.normalize(path)
        _browse.value = RemoteBrowser(at, emptyList(), busy = true, error = null)
        viewModelScope.launch(Dispatchers.IO) {
            val outcome = runCatching {
                if (target.share.isBlank()) {
                    // 还没定共享：把能探到的共享列出来。候选里带上从当前路径猜的词，
                    // 用户若粘过 smb://host/music/... 那个 music 会排在前面。
                    val candidates = listOfNotNull(SmbBrowser.guessShare(path).ifEmpty { null })
                    val shares = SmbBrowser.listShares(target, candidates)
                    Loaded(
                        items = shares.map { name ->
                            RemoteFile(
                                name = name,
                                // 共享用 `\\name` 表示，和共享内目录（`\dir`）区分开，
                                // 点进去时据此判断该填共享名还是该进目录。
                                path = "\\\\$name",
                                isDirectory = true,
                            )
                        },
                        picking = true,
                    )
                } else {
                    val entries = SmbBrowser.listEntries(target, at)
                    Loaded(
                        items = entries.filter { it.isDirectory }.sortedBy { it.name.lowercase() },
                        picking = false,
                        fileCount = entries.count { !it.isDirectory },
                    )
                }
            }
            withContext(Dispatchers.Main) {
                _browse.value = outcome.fold(
                    onSuccess = { loaded ->
                        if (loaded.picking && loaded.items.isEmpty()) {
                            RemoteBrowser(
                                at, emptyList(), busy = false,
                                error = "没能列出共享名，请在表单里手填（如 music）",
                                pickingShare = true,
                            )
                        } else {
                            RemoteBrowser(
                                at, loaded.items, busy = false, error = null,
                                pickingShare = loaded.picking,
                                fileCount = loaded.fileCount,
                            )
                        }
                    },
                    onFailure = {
                        RemoteBrowser(
                            at, emptyList(), busy = false,
                            error = it.message ?: "无法访问 SMB 服务器",
                            pickingShare = target.share.isBlank(),
                        )
                    },
                )
            }
        }
    }

    /**
     * 在「选择共享」列表里点了一个共享名：写回表单字段并进入该共享根目录。
     *
     * 共享名必须落到表单里（而不是只存在浏览状态里）—— 用户确认后点保存时，
     * `buildDraft()` 读的是表单字段，否则会存下一条没有共享名的连接。
     */
    fun pickSmbShare(profile: ConnectionProfile, shareName: String) {
        browseSmb(profile.copy(shareName = shareName), SmbPath.ROOT)
    }


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

    /**
     * 启用 / 停用一条连接。
     *
     * 只改 `IsEnabled` 这一列，**不动库里的歌** —— 停用后这些歌不再出现在音乐库里
     * （过滤规则在 `ConnectionFilter`，按协议判断），但收藏、歌单、播放统计全都保留，
     * 重新启用立刻恢复。删除连接才是真的把这些歌一起丢掉。
     *
     * 语义对齐 MAUI `MusicDatabase.FilterByEnabledProtocols`：那个方法一直存在，
     * 只是 MAUI 的界面从没做过开关、保存时永远写 `IsEnabled = true`，所以从未生效过。
     */
    fun setEnabled(profile: ConnectionProfile, enabled: Boolean) {
        viewModelScope.launch {
            runCatching { connectionDao.setEnabled(profile.id, enabled) }
                .onFailure { _message.value = RemoteMessage.Failed(it.message ?: it.javaClass.simpleName) }
        }
    }

    /** 清掉网络歌曲的离线缓存（.part 与已完成件一起删，条目清单同步清空） */
    fun clearCache() {
        viewModelScope.launch { runCatching { downloader.clearCache() } }
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

    /**
     * 补全这条源里骨架行的元数据（时长、歌手、专辑、码率、年份、曲目号、类型）。
     *
     * 只对 WebDAV/SMB 提供：Subsonic 的曲清单本来就带着这些列，给它做这个按钮就是放假控件。
     */
    fun backfillMetadata(profile: ConnectionProfile) {
        if (_busy.value) return
        viewModelScope.launch {
            _busy.value = true
            try {
                _message.value = runCatching {
                    backfiller.run(profile) { done, total -> _progress.value = SyncProgress(done, total) }
                        .let { RemoteMessage.BackfillResult(it.total, it.updated, it.skipped, it.failed, it.elapsedMs) }
                }.getOrElse { RemoteMessage.Failed(it.message ?: it.javaClass.simpleName) }
            } finally {
                _progress.value = null
                _busy.value = false
            }
        }
    }

    /**
     * 一次把所有 WebDAV/SMB 连接补完，对位 C# `BackfillAllMetadataAsync`。
     *
     * 逐条按钮解决不了"换过一次 NAS、旧库里几千首全是 0:00"这种场景 ——
     * 那要连着点十几条连接，而用户不知道哪几条有欠账。
     * 进度沿用「每条连接各自从 0 走到满」的形状（C# 也是复用同一个状态栏），
     * 结果把四条连接的数字合并成一句。
     *
     * @param thenCovers 元数据补完后接着补封面。封面是独立一轮（判据不同），
     *   自动触发时希望一次做完，手动按钮也走同一条链。
     */
    fun backfillAll(thenCovers: Boolean = false) {
        if (_busy.value) return
        val targets = profiles.value.filter { it.protocol != ProtocolType.Navidrome }
        if (targets.isEmpty()) return
        viewModelScope.launch {
            _busy.value = true
            var total = 0
            var updated = 0
            var skipped = 0
            var failed = 0
            var elapsed = 0L
            try {
                targets.forEach { profile ->
                    val one = backfiller.run(profile) { done, count -> _progress.value = SyncProgress(done, count) }
                    total += one.total
                    updated += one.updated
                    skipped += one.skipped
                    failed += one.failed
                    elapsed += one.elapsedMs
                }
                if (thenCovers) {
                    val cover = runCovers(targets)
                    total += cover.total
                    updated += cover.updated
                    skipped += cover.skipped
                    failed += cover.failed
                    elapsed += cover.elapsedMs
                }
                _message.value = RemoteMessage.BackfillResult(total, updated, skipped, failed, elapsed)
            } catch (e: CancellationException) {
                throw e
            } catch (e: Exception) {
                // 中途断线：前面几条已经落库了，把已经攒到的数照实报出来
                _message.value = RemoteMessage.Failed(e.message ?: e.javaClass.simpleName)
            } finally {
                _progress.value = null
                _busy.value = false
            }
        }
    }

    /**
     * 只补封面（手动入口）。与 [backfillAll] 分开，因为封面这轮可能几百首、
     * 而元数据那轮通常一次就完；两者进度条含义不一样。
     */
    fun backfillCovers() {
        if (_busy.value) return
        val targets = profiles.value.filter { it.protocol != ProtocolType.Navidrome }
        if (targets.isEmpty()) return
        viewModelScope.launch {
            _busy.value = true
            try {
                val one = runCovers(targets)
                _message.value = RemoteMessage.BackfillResult(one.total, one.updated, one.skipped, one.failed, one.elapsedMs)
            } catch (e: CancellationException) {
                throw e
            } catch (e: Exception) {
                _message.value = RemoteMessage.Failed(e.message ?: e.javaClass.simpleName)
            } finally {
                _progress.value = null
                _busy.value = false
            }
        }
    }

    /** 按连接逐条补封面；进度沿用同一个状态栏（每条连接从 0 走到满） */
    private suspend fun runCovers(targets: List<ConnectionProfile>): RemoteCoverBackfiller.Result {
        var total = 0
        var updated = 0
        var skipped = 0
        var failed = 0
        var elapsed = 0L
        for (profile in targets) {
            val source = when (profile.protocol) {
                ProtocolType.SMB -> SongSource.SMB.ordinal
                // Subsonic/Navidrome 的曲目自带封面 URL，没有可抽的内嵌图
                ProtocolType.Navidrome -> continue
                ProtocolType.WebDAV -> SongSource.WebDAV.ordinal
            }
            val one = coverBackfiller.run(source) { done, count -> _progress.value = SyncProgress(done, count) }
            total += one.total
            updated += one.updated
            skipped += one.skipped
            failed += one.failed
            elapsed += one.elapsedMs
        }
        return RemoteCoverBackfiller.Result(total, updated, skipped, failed, elapsed)
    }

    fun playAt(index: Int) {
        val songs = remoteSongs.value.map { it.toSong() }
        if (songs.isEmpty()) return
        session.ensure()
        controller.playSongs(songs, startIndex = index)
    }
}
