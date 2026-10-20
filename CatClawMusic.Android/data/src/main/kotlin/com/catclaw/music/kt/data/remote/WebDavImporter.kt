package com.catclaw.music.kt.data.remote

import android.os.SystemClock
import com.catclaw.music.kt.core.library.MediaScanRules
import com.catclaw.music.kt.core.library.SidecarRules
import com.catclaw.music.kt.core.model.ConnectionProfile
import com.catclaw.music.kt.core.model.ProtocolType
import com.catclaw.music.kt.core.model.SongSource
import com.catclaw.music.kt.data.db.CatClawDatabase
import com.catclaw.music.kt.data.db.entity.SongEntity
import java.io.IOException
import javax.inject.Inject
import javax.inject.Singleton
import kotlinx.coroutines.Dispatchers
import androidx.room.withTransaction
import kotlinx.coroutines.withContext

/**
 * WebDAV / OpenList 曲库入库，字段取法与 C# `NetworkMusicService.Scan.ProcessFileListAsync` 一致：
 * Title = 文件名去扩展名，Artist/Album 留空（远程骨架行，标签要等播放时再读），
 * Duration = 0，RemoteId = 远端路径，FilePath = 可播 URL，
 * 再按「同目录同名」配对侧车歌词（.lrc/.ttml）与侧车封面（同名图 > 目录通用封面）。
 */
@Singleton
class WebDavImporter @Inject constructor(
    private val db: CatClawDatabase,
) {
    data class Result(
        val listed: Int,
        val audio: Int,
        val inserted: Int,
        val updated: Int,
        val elapsedMs: Long,
    )

    suspend fun importAll(
        profile: ConnectionProfile,
        client: WebDavClient = WebDavClient(profile),
        onProgress: (SyncProgress) -> Unit = {},
    ): Result = withContext(Dispatchers.IO) {
        val startedAt = SystemClock.elapsedRealtime()
        // 列目录之前总数未知（页面据此走不定进度），这一步和 C# 一样先探测前缀（/dav、/webdav）与服务器类型，
        // 少了这步，挂在 /dav 下的 NAS 会直接 404
        onProgress(SyncProgress(0, 0))
        val probe = client.test()
        if (!probe.ok) throw IOException(probe.message)
        val all = client.listAll(profile.basePath)
        val audio = all.filter { !it.isDirectory && MediaScanRules.isAudioFile(it.name.ifEmpty { it.path }) }
        onProgress(SyncProgress(0, audio.size))
        val lyrics = all.filter { isLyricsSidecar(it.name) }.associateBy { sidecarKey(it) }
        val covers = all.filter { isImageSidecar(it.name) }.associateBy { sidecarKey(it) }
        val dirCovers = all.filter { isImageSidecar(it.name) }
            .associateBy({ MediaScanRules.parentDir(it.path) }, { it.path })

        val library = db.libraryDao()
        val known = library.remoteKeys().associateBy { it.remoteId }
        var inserted = 0
        var updated = 0
        var done = 0
        val now = System.currentTimeMillis()

        db.withTransaction {
            for (file in audio) {
                val prev = known[file.path]
                if (prev == null) inserted++ else updated++
                val nameNoExt = MediaScanRules.titleFromFileName(file.name)
                val parent = MediaScanRules.parentDir(file.path)
                library.upsertSong(
                    SongEntity(
                        id = prev?.id ?: 0,
                        title = nameNoExt,
                        durationSeconds = 0,
                        filePath = client.streamUrlFor(file.path),
                        fileSize = file.size,
                        dateAdded = prev?.dateAdded ?: now,
                        dateModified = now,
                        // 封面列存的是 **URL**，不是远端路径 —— 与 Subsonic / 猫爪服务器两个导入器一致
                        // （`SubsonicImporter` 写 `track.coverArtUrl`、`CatClawServerImporter` 写 `assetUrl(...)`）。
                        // 这条以前写的是 `file.path`（音频自己的远端路径）：`CoverTile` 只认 http(s) 直链或本地文件，
                        // 于是 WebDAV 曲目的封面永远显示首字母。`remoteCoverPath` 那列仍存远端路径，两者分工不变
                        coverArtPath = (covers["$parent/$nameNoExt"]?.path ?: dirCovers[parent])
                            ?.let { client.streamUrlFor(it) },
                        lyricsPath = lyrics["$parent/$nameNoExt"]?.let { client.streamUrlFor(it.path) },
                        source = SongSource.WebDAV.ordinal,
                        protocol = profile.protocol.ordinal,
                        remoteId = file.path,
                        remoteCoverPath = covers["$parent/$nameNoExt"]?.path ?: dirCovers[parent],
                    ),
                )
                done++
                if (done % PROGRESS_STEP == 0 || done == audio.size) onProgress(SyncProgress(done, audio.size))
            }
        }
        Result(
            listed = all.size,
            audio = audio.size,
            inserted = inserted,
            updated = updated,
            elapsedMs = SystemClock.elapsedRealtime() - startedAt,
        )
    }

    // 侧车判定搬去 `:core` 的 SidecarRules（同一份规则将来 SMB 也要用）——
    // 留在这里的话，`:core` 里那个只认 .lrc 的窄规则就会被下一个人接错
    private fun isLyricsSidecar(name: String): Boolean = SidecarRules.isLyricsSidecar(name)

    private fun isImageSidecar(name: String): Boolean = SidecarRules.isImageSidecar(name)

    private fun sidecarKey(file: RemoteFile): String =
        SidecarRules.sidecarKey(MediaScanRules.parentDir(file.path), file.name)
}
