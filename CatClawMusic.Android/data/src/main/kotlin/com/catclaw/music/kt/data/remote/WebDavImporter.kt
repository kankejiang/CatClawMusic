package com.catclaw.music.kt.data.remote

import android.os.SystemClock
import com.catclaw.music.kt.core.library.MediaScanRules
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
    ): Result = withContext(Dispatchers.IO) {
        val startedAt = SystemClock.elapsedRealtime()
        // 先探测前缀（/dav、/webdav）与服务器类型，和 C# 列库前的 EnsureDetectedAsync 同一步；
        // 少了这步，挂在 /dav 下的 NAS 会直接 404
        val probe = client.test()
        if (!probe.ok) throw IOException(probe.message)
        val all = client.listAll(profile.basePath)
        val audio = all.filter { !it.isDirectory && MediaScanRules.isAudioFile(it.name.ifEmpty { it.path }) }
        val lyrics = all.filter { isLyricsSidecar(it.name) }.associateBy { sidecarKey(it) }
        val covers = all.filter { isImageSidecar(it.name) }.associateBy { sidecarKey(it) }
        val dirCovers = all.filter { isImageSidecar(it.name) }
            .associateBy({ MediaScanRules.parentDir(it.path) }, { it.path })

        val library = db.libraryDao()
        val known = library.remoteKeys().associateBy { it.remoteId }
        var inserted = 0
        var updated = 0
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
                        coverArtPath = file.path,
                        lyricsPath = lyrics["$parent/$nameNoExt"]?.let { client.streamUrlFor(it.path) },
                        source = SongSource.WebDAV.ordinal,
                        protocol = profile.protocol.ordinal,
                        remoteId = file.path,
                        remoteCoverPath = covers["$parent/$nameNoExt"]?.path ?: dirCovers[parent],
                    ),
                )
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

    private fun isLyricsSidecar(name: String): Boolean =
        name.substringAfterLast('.', "").lowercase() in setOf("lrc", "ttml")

    private fun isImageSidecar(name: String): Boolean =
        name.substringAfterLast('.', "").lowercase() in setOf("jpg", "jpeg", "png", "webp", "bmp")

    private fun sidecarKey(file: RemoteFile): String =
        MediaScanRules.parentDir(file.path) + "/" + MediaScanRules.titleFromFileName(file.name)
}
