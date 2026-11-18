package com.catclaw.music.kt.data.smb

import com.catclaw.music.kt.core.model.ConnectionProfile
import com.catclaw.music.kt.core.model.ProtocolType
import com.catclaw.music.kt.core.model.SongSource
import com.catclaw.music.kt.core.library.MediaScanRules
import com.catclaw.music.kt.data.db.CatClawDatabase
import com.catclaw.music.kt.data.db.entity.SongEntity
import com.catclaw.music.kt.data.remote.PROGRESS_STEP
import com.catclaw.music.kt.data.remote.RemoteFile
import com.catclaw.music.kt.data.remote.SyncProgress
import androidx.room.withTransaction
import javax.inject.Inject
import javax.inject.Singleton
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.withContext

/**
 * 进程内唯一的 SMB→HTTP 代理持有者。
 * Media3 没有 SMB 的 DataSource，所以入库时就把 `smb://` 换成本地 `http://127.0.0.1:<port>/smb/...`，
 * 播放器只管按 HTTP 取流 —— 与 C# `SmbStreamProxy` 同一个架构决定。
 */
@Singleton
class SmbProxyHost @Inject constructor() {

    @Volatile
    private var proxy: SmbStreamProxy? = null

    @Synchronized
    private fun proxy(): SmbStreamProxy? = proxy ?: SmbStreamProxy { target -> SmbjReader(target) }
        .takeIf { it.start() }
        ?.also { proxy = it }

    /** @return null 表示 19876..19975 全被占，起不了代理 */
    fun httpUrlFor(smbUrl: String): String? = proxy()?.toHttpUrl(smbUrl)

    /**
     * 代理的 URL 前缀（`http://127.0.0.1:<port>/smb/`），与具体文件无关。
     *
     * 批量入库时先取一次、再逐条拼 —— [httpUrlFor] 是 `@Synchronized`，
     * 上千首每首调一次就是上千次拿锁。返回 null 表示代理起不来。
     */
    fun httpPrefix(): String? = proxy()?.let { p ->
        if (!p.start()) return@let null
        "http://127.0.0.1:${p.boundPort}/${SmbStreamProxy.SMB_PREFIX}"
    }
}

/** SMB 连通性探测：能连上共享并列出根目录就算通，返回给 UI 的文案与 WebDAV/Subsonic 同形 */
object SmbProbe {
    fun test(profile: ConnectionProfile): String {
        val target = parseSmbUrl(smbUrlOf(profile, "\\")) ?: return "fail smb 地址解析不了（检查主机与共享名）"
        val reader = runCatching { SmbjReader(target) }.getOrElse { return "fail ${it.message}" }
        return try {
            val entries = reader.list("\\")
            "ok ${entries.size} 项"
        } catch (e: Exception) {
            "fail ${e.message ?: e.javaClass.simpleName}"
        } finally {
            runCatching { reader.close() }
        }
    }
}

/**
 * 由入库配置 + 共享内路径拼出规范 smb:// 地址。
 * 凭据按 C# 的形态嵌进 userinfo（C# 对 WebDAV 也是 `http://user:pass@host/...`），代理侧再解回来认证；
 * 不这么做的代价是 `smb://` 里没身份，代理就只能匿名连。
 */
internal fun smbUrlOf(profile: ConnectionProfile, pathWithBackslash: String): String {
    val share = profile.shareName?.takeIf { it.isNotEmpty() } ?: "Music"
    val relative = pathWithBackslash.trimStart('\\').replace('\\', '/')
    val port = if (profile.port > 0 && profile.port != SMB_DEFAULT_PORT) ":${profile.port}" else ""
    val userInfo = if (profile.userName.isEmpty()) "" else
        percentEncode(profile.userName) + ":" + percentEncode(profile.password) + "@"
    return "smb://$userInfo${profile.host}$port/$share/$relative"
}

/**
 * SMB 曲库入库：递归列共享，挑音频文件，`FilePath` 直接存代理后的本地 HTTP 地址，
 * 这样播放路径与 WebDAV/Subsonic 一致（一条可播 URL）。
 * `Source = SMB(2)` + `Protocol = SMB`，去重键仍是 RemoteId（完整 smb 地址）。
 */
@Singleton
class SmbImporter @Inject constructor(
    private val db: CatClawDatabase,
    private val proxyHost: SmbProxyHost,
) {
    data class Result(val listed: Int, val audio: Int, val inserted: Int, val updated: Int, val elapsedMs: Long)

    suspend fun importAll(
        profile: ConnectionProfile,
        onProgress: (SyncProgress) -> Unit = {},
    ): Result = withContext(Dispatchers.IO) {
        val startedAt = android.os.SystemClock.elapsedRealtime()
        // 递归 walk 完之前总数未知（SMB 的目录遍历可能就是几分钟的事），这正是不定进度该出现的场合
        onProgress(SyncProgress(0, 0))
        val target = parseSmbUrl(smbUrlOf(profile, "\\")) ?: return@withContext Result(0, 0, 0, 0, 0)
        val reader = SmbjReader(target)
        val files = try {
            walk(reader, "\\")
        } finally {
            runCatching { reader.close() }
        }
        val audio = files.filter { !it.isDirectory && MediaScanRules.isAudioFile(it.name) }
        onProgress(SyncProgress(0, audio.size))

        val library = db.libraryDao()
        val known = library.remoteKeys().associateBy { it.remoteId }
        var inserted = 0
        var updated = 0
        var done = 0
        val now = System.currentTimeMillis()
        // 代理前缀只取一次：httpUrlFor 内部 @Synchronized，逐首调用在上千首时是纯开销。
        // 前缀与文件无关（只有端口 + /smb/），拼上完整 url 的百分号编码即可等价于 toHttpUrl。
        val httpBase = proxyHost.httpPrefix()

        db.withTransaction {
            // 批量 upsert：逐首调 upsertSong 是 613 次独立 DB 操作（每次都要开游标、
            // 走一遍 SQL 编译与事务日志），真机实测 613 首要 31 秒，几乎全耗在这上面。
            // 攒成批一次提交，同样的量降到秒级以内。
            val rows = ArrayList<SongEntity>(audio.size.coerceAtMost(BATCH_SIZE))
            for (file in audio) {
                val smbUrl = smbUrlOf(profile, file.path)
                val prev = known[smbUrl]
                if (prev == null) inserted++ else updated++
                rows += SongEntity(
                    id = prev?.id ?: 0,
                    title = MediaScanRules.titleFromFileName(file.name),
                    durationSeconds = 0,
                    // 代理地址在循环外统一取：httpUrlFor 内部是 @Synchronized，
                    // 每首调一次就是每次拿锁 —— 上千首时这笔开销不可忽略。
                    filePath = httpBase?.let { it + percentEncode(smbUrl) } ?: smbUrl,
                    fileSize = file.size,
                    dateAdded = prev?.dateAdded ?: now,
                    dateModified = now,
                    source = SongSource.SMB.ordinal,
                    protocol = ProtocolType.SMB.ordinal,
                    remoteId = smbUrl,
                )
                done++
                if (done % PROGRESS_STEP == 0 || done == audio.size) {
                    onProgress(SyncProgress(done, audio.size))
                }
                if (rows.size >= BATCH_SIZE) {
                    library.upsertSongs(rows)
                    rows.clear()
                }
            }
            if (rows.isNotEmpty()) library.upsertSongs(rows)
        }
        Result(files.size, audio.size, inserted, updated, android.os.SystemClock.elapsedRealtime() - startedAt)
    }

    private fun walk(reader: SmbReader, dir: String, depth: Int = 0): List<RemoteFile> {
        if (depth > MAX_DEPTH) return emptyList()
        val entries = runCatching { reader.list(dir) }.getOrDefault(emptyList())
        return entries.flatMap { entry ->
            if (entry.isDirectory) walk(reader, entry.path, depth + 1) else listOf(entry)
        }
    }

    private companion object {
        const val MAX_DEPTH = 8

        /**
         * 每次批量 upsert 的行数。
         *
         * 500 是折中：太小则 DB 调用次数还是多；太大则单条 SQL 的绑定变量超过
         * SQLite 默认上限（999），会被 Room 拆成多条反而变慢。
         */
        const val BATCH_SIZE = 500
    }
}
