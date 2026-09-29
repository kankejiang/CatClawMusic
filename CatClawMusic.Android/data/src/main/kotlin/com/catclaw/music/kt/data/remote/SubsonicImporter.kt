package com.catclaw.music.kt.data.remote

import android.os.SystemClock
import com.catclaw.music.kt.core.model.ConnectionProfile
import com.catclaw.music.kt.core.model.ProtocolType
import com.catclaw.music.kt.core.model.SongSource
import com.catclaw.music.kt.data.db.CatClawDatabase
import com.catclaw.music.kt.data.db.entity.AlbumEntity
import com.catclaw.music.kt.data.db.entity.ArtistEntity
import com.catclaw.music.kt.data.db.entity.SongEntity
import javax.inject.Inject
import javax.inject.Singleton
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.async
import kotlinx.coroutines.awaitAll
import kotlinx.coroutines.coroutineScope
import kotlinx.coroutines.sync.Semaphore
import kotlinx.coroutines.sync.withPermit
import kotlinx.coroutines.withContext
import androidx.room.withTransaction

/**
 * 把 Subsonic / Navidrome 服务器上的曲库导进本地 Songs 表。
 * 记账方式与 C# 一致：网络曲的 `Source` 记 WebDAV(1)、用 `Protocol=Navidrome(1)` 区分协议，
 * `FilePath` 存带认证参数的 stream URL，`RemoteId` 存服务器 songId 作为去重主键。
 */
@Singleton
class SubsonicImporter @Inject constructor(
    private val db: CatClawDatabase,
) {
    data class Result(
        val albums: Int,
        val tracks: Int,
        val inserted: Int,
        val updated: Int,
        val elapsedMs: Long,
    )

    suspend fun importAll(
        profile: ConnectionProfile,
        client: SubsonicClient = SubsonicClient(profile),
        onProgress: (SyncProgress) -> Unit = {},
    ): Result = withContext(Dispatchers.IO) {
        val startedAt = SystemClock.elapsedRealtime()
        // 拉到曲目清单之前总数未知；清单到手后按**曲目数**报进度（专辑数不是首数）
        onProgress(SyncProgress(0, 0))
        val refs = client.allAlbums()
        val tracks = fetchTracks(client, refs)
        onProgress(SyncProgress(0, tracks.size))
        val library = db.libraryDao()
        val known = library.remoteKeys().associateBy { it.remoteId }
        val artistIds = HashMap<String, Int>()
        val albumIds = HashMap<String, Int>()
        var inserted = 0
        var updated = 0
        var done = 0
        val now = System.currentTimeMillis()

        db.withTransaction {
            for (track in tracks) {
                if (track.remoteId.isBlank()) continue
                val artistId = artistIds.getOrPut(track.artist) {
                    track.artist.takeIf { it.isNotBlank() }?.let { name ->
                        library.artistIdByName(name) ?: library.insertArtist(ArtistEntity(name = name)).toInt()
                    } ?: 0
                }
                val albumId = albumIds.getOrPut(track.album) {
                    track.album.takeIf { it.isNotBlank() }?.let { title ->
                        library.albumIdByTitle(title, artistId)
                            ?: library.insertAlbum(
                                AlbumEntity(
                                    title = title,
                                    name = title,
                                    artist = track.artist,
                                    artistId = artistId,
                                    coverArtPath = track.coverArtUrl,
                                ),
                            ).toInt()
                    } ?: 0
                }
                val prev = known[track.remoteId]
                if (prev == null) inserted++ else updated++
                library.upsertSong(
                    SongEntity(
                        id = prev?.id ?: 0,
                        title = track.title,
                        artistId = artistId,
                        albumId = albumId,
                        durationSeconds = track.durationSeconds,
                        filePath = track.streamUrl,
                        fileSize = track.sizeBytes,
                        bitrate = track.bitrateKbps,
                        dateAdded = prev?.dateAdded ?: now,
                        dateModified = now,
                        coverArtPath = track.coverArtUrl,
                        source = SongSource.WebDAV.ordinal,
                        protocol = ProtocolType.Navidrome.ordinal,
                        remoteId = track.remoteId,
                    ),
                )
                done++
                if (done % PROGRESS_STEP == 0 || done == tracks.size) onProgress(SyncProgress(done, tracks.size))
            }
        }
        Result(
            albums = refs.size,
            tracks = tracks.size,
            inserted = inserted,
            updated = updated,
            elapsedMs = SystemClock.elapsedRealtime() - startedAt,
        )
    }

    /** 逐张专辑取曲目：与 C# 一样限到 8 路并发，别把服务器打满 */
    private suspend fun fetchTracks(
        client: SubsonicClient,
        refs: List<SubsonicClient.AlbumRef>,
    ): List<RemoteTrack> = coroutineScope {
        val gate = Semaphore(PERMIT)
        refs.map { ref ->
            async {
                gate.withPermit { runCatching { client.albumSongs(ref.id) }.getOrDefault(emptyList()) }
            }
        }.awaitAll().flatten().distinctBy { it.remoteId }
    }

    private companion object {
        const val PERMIT = 8
    }
}
