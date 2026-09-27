package com.catclaw.music.kt.data.remote

import android.os.SystemClock
import com.catclaw.music.kt.core.model.ProtocolType
import com.catclaw.music.kt.core.model.SongSource
import com.catclaw.music.kt.data.db.CatClawDatabase
import com.catclaw.music.kt.data.db.entity.AlbumEntity
import com.catclaw.music.kt.data.db.entity.ArtistEntity
import com.catclaw.music.kt.data.db.entity.SongEntity
import androidx.room.withTransaction
import javax.inject.Inject
import javax.inject.Singleton
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.withContext

/**
 * 猫爪服务器元数据入库，逐条对齐 C# `CatClawServerClient.SyncMetadataAsync`：
 * 去重键是 **FilePath**（C# 用 `GetLocalSongPathModTimesAsync()` 的键集），命中就跳过、不回写；
 * 入库标记 `Source = Cache(3)` + `Protocol = WebDAV(0)`（C# 注释原话是「Reuse existing protocol」），
 * `RemoteId = "catclaw-server:{服务端 id}"`，`DateAdded` 用当前时间、`DateModified` 用服务端给的值。
 */
@Singleton
class CatClawServerImporter @Inject constructor(
    private val db: CatClawDatabase,
) {
    data class Result(val total: Int, val inserted: Int, val skipped: Int, val elapsedMs: Long)

    suspend fun sync(
        profile: com.catclaw.music.kt.core.model.ConnectionProfile,
        client: CatClawServerClient = CatClawServerClient(profile.baseUrl()),
    ): Result = withContext(Dispatchers.IO) {
        val startedAt = SystemClock.elapsedRealtime()
        val serverSongs = client.songs()
        if (serverSongs.isEmpty()) return@withContext Result(0, 0, 0, SystemClock.elapsedRealtime() - startedAt)

        val library = db.libraryDao()
        val existing = library.songPaths().toMutableSet()
        val artistIds = HashMap<String, Int>()
        val albumIds = HashMap<String, Int>()
        val now = System.currentTimeMillis()
        var inserted = 0
        var skipped = 0

        db.withTransaction {
            for (ss in serverSongs) {
                if (ss.filePath.isEmpty() || ss.filePath in existing) {
                    skipped++
                    continue
                }
                val artistId = ss.artist.takeIf { it.isNotBlank() }?.let { name ->
                    artistIds.getOrPut(name) {
                        library.artistIdByName(name) ?: library.insertArtist(ArtistEntity(name = name)).toInt()
                    }
                } ?: 0
                val albumId = ss.album.takeIf { it.isNotBlank() }?.let { title ->
                    albumIds.getOrPut("$title|$artistId") {
                        library.albumIdByTitle(title, artistId)
                            ?: library.insertAlbum(
                                AlbumEntity(
                                    title = title,
                                    name = title,
                                    artist = ss.artist,
                                    artistId = artistId,
                                    coverArtPath = client.assetUrl(ss.coverArtPath),
                                ),
                            ).toInt()
                    }
                } ?: 0

                library.upsertSong(
                    SongEntity(
                        title = ss.title,
                        artistId = artistId,
                        albumId = albumId,
                        durationSeconds = ss.durationSeconds,
                        filePath = ss.filePath,
                        fileSize = ss.fileSize,
                        bitrate = ss.bitrate,
                        trackNumber = ss.trackNumber,
                        year = ss.year,
                        genre = ss.genre,
                        dateAdded = now,
                        dateModified = if (ss.dateModified > 0) ss.dateModified else now,
                        coverArtPath = client.assetUrl(ss.coverArtPath),
                        lyricsPath = client.assetUrl(ss.lyricsPath),
                        source = SongSource.Cache.ordinal,
                        protocol = ProtocolType.WebDAV.ordinal,
                        remoteId = "catclaw-server:${ss.id}",
                    ),
                )
                existing += ss.filePath
                inserted++
            }
        }
        Result(
            total = serverSongs.size,
            inserted = inserted,
            skipped = skipped,
            elapsedMs = SystemClock.elapsedRealtime() - startedAt,
        )
    }
}
