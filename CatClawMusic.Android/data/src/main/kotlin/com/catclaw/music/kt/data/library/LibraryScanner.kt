package com.catclaw.music.kt.data.library

import android.content.Context
import android.provider.MediaStore
import android.util.Log
import androidx.room.withTransaction
import com.catclaw.music.kt.core.library.MediaScanRules
import com.catclaw.music.kt.data.db.CatClawDatabase
import com.catclaw.music.kt.data.db.LibraryDao
import com.catclaw.music.kt.data.db.entity.AlbumEntity
import com.catclaw.music.kt.data.db.entity.ArtistEntity
import com.catclaw.music.kt.data.db.entity.SongArtistEntity
import com.catclaw.music.kt.data.db.entity.SongEntity
import dagger.hilt.android.qualifiers.ApplicationContext
import javax.inject.Inject
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.withContext

data class ScanResult(
    val scanned: Int = 0,
    val inserted: Int = 0,
    val updated: Int = 0,
    val removed: Int = 0,
    val artists: Int = 0,
    val albums: Int = 0,
    val elapsedMs: Long = 0,
)

/**
 * 曲库扫描：MediaStore → Songs / Artists / Albums / SongArtists，按 UQ(FilePath) 去重，
 * 并清掉已消失的本地曲目（等价 C# 的 MusicScanner + RemoveStaleSongs）。
 *
 * 全程一次事务 + 批量写入：首扫 616 曲若逐条提交实测要 17 秒，
 * 这里把往返压到常数次。
 */
class LibraryScanner @Inject constructor(
    @ApplicationContext private val context: Context,
    private val db: CatClawDatabase,
    private val dao: LibraryDao,
) {

    suspend fun scanFull(): ScanResult = withContext(Dispatchers.IO) {
        val startedAt = System.currentTimeMillis()
        val tracks = queryMediaStore()

        val result = db.withTransaction {
            val existingSongs = dao.allLocalSongs().associateBy { it.filePath }
            val artistIds = dao.artistNames().associate { it.name to it.id }.toMutableMap()
            val albumIds = dao.albumKeys().associate { (it.title to it.artistId) to it.id }.toMutableMap()

            // ① 歌手：先把新歌手插完，后面算专辑 key 才拿得到真 artistId
            val newArtistNames = LinkedHashSet<String>()
            tracks.map { MediaScanRules.normalizeArtist(it.artistRaw) }.filterTo(newArtistNames) { it !in artistIds }
            val newArtistList = newArtistNames.map { ArtistEntity(name = it) }
            if (newArtistList.isNotEmpty()) {
                dao.insertArtists(newArtistList).forEachIndexed { i, rowId ->
                    artistIds[newArtistList[i].name] = rowId.toInt()
                }
            }

            // ② 专辑：key = (标题, artistId)
            val newAlbumKeys = LinkedHashMap<Pair<String, Int>, AlbumEntity>()
            for (track in tracks) {
                val artistName = MediaScanRules.normalizeArtist(track.artistRaw)
                val artistId = artistIds[artistName] ?: continue
                val key = track.album.ifBlank { UNKNOWN_ALBUM } to artistId
                if (key !in albumIds && key !in newAlbumKeys) {
                    newAlbumKeys[key] = AlbumEntity(
                        title = key.first,
                        name = key.first,
                        artist = artistName,
                        artistId = artistId,
                        year = track.year,
                        releaseYear = track.year,
                    )
                }
            }
            val newAlbumList = newAlbumKeys.values.toList()
            if (newAlbumList.isNotEmpty()) {
                dao.insertAlbums(newAlbumList).forEachIndexed { i, rowId ->
                    val e = newAlbumList[i]
                    albumIds[e.title to e.artistId] = rowId.toInt()
                }
            }

            // ③ 歌曲 + 关联表
            val newSongs = mutableListOf<SongEntity>()
            val changedSongs = mutableListOf<SongEntity>()
            val links = mutableListOf<SongArtistEntity>()

            for (track in tracks) {
                val path = MediaScanRules.contentUri(track.mediaStoreId)
                val artistName = MediaScanRules.normalizeArtist(track.artistRaw)
                val artistId = artistIds[artistName] ?: continue
                val albumTitle = track.album.ifBlank { UNKNOWN_ALBUM }
                val albumId = albumIds[albumTitle to artistId] ?: 0
                val existing = existingSongs[path]

                val entity = SongEntity(
                    id = existing?.id ?: 0,
                    title = track.title.ifBlank { MediaScanRules.titleFromPath(path) },
                    artistId = artistId,
                    albumId = albumId,
                    durationSeconds = MediaScanRules.durationSeconds(track.durationMs),
                    filePath = path,
                    fileSize = track.size,
                    bitrate = MediaScanRules.bitrateKbps(track.bitrateBps),
                    trackNumber = track.trackNumber,
                    year = track.year,
                    genre = track.genre,
                    dateAdded = existing?.dateAdded ?: (System.currentTimeMillis() / 1000L),
                    dateModified = track.dateModified,
                    coverArtPath = existing?.coverArtPath,
                    lyricsPath = existing?.lyricsPath,
                    source = SOURCE_LOCAL,
                    protocol = PROTOCOL_NONE,
                    mediaStoreId = track.mediaStoreId,
                )
                if (existing == null) newSongs += entity else changedSongs += entity
                // 已有歌曲 id 已知；新歌曲要等插入后拿 rowId
                if (existing != null) links += SongArtistEntity(songId = entity.id, artistId = artistId)
            }

            val newIds = if (newSongs.isEmpty()) emptyList() else dao.insertSongs(newSongs)
            newSongs.zip(newIds).forEach { (song, rowId) ->
                links += SongArtistEntity(songId = rowId.toInt(), artistId = song.artistId)
            }
            if (links.isNotEmpty()) dao.upsertSongArtists(links)
            if (changedSongs.isNotEmpty()) dao.upsertSongs(changedSongs)

            val seen = tracks.mapTo(HashSet()) { MediaScanRules.contentUri(it.mediaStoreId) }
            // 扫描一条都没返回时绝不裁剪 —— 否则 seen 是空集，整库都会被判成"文件已消失"而删光。
            // 权限被撤 / MediaProvider 短暂不可用 / 查询异常被吞成空列表，都会走到这条路上。
            val stale = MediaScanRules.stalePaths(
                existingPaths = existingSongs.values.map { it.filePath },
                seenPaths = seen,
                scannedAny = tracks.isNotEmpty(),
            )
            if (stale.isNotEmpty()) {
                dao.deleteSongsByPath(stale)
                dao.pruneSongArtists()
            }

            ScanResult(
                scanned = tracks.size,
                inserted = newSongs.size,
                updated = changedSongs.size,
                removed = stale.size,
                artists = artistIds.size,
                albums = albumIds.size,
            )
        }

        val elapsed = System.currentTimeMillis() - startedAt
        Log.i(
            TAG,
            "scan done scanned=${result.scanned} inserted=${result.inserted} updated=${result.updated} " +
                "removed=${result.removed} artists=${result.artists} albums=${result.albums} " +
                "elapsed=${elapsed}ms totalSongs=${dao.songCount()}",
        )
        result.copy(elapsedMs = elapsed)
    }

    private data class Track(
        val mediaStoreId: Long,
        val title: String,
        val artistRaw: String?,
        val album: String,
        val durationMs: Long,
        val size: Long,
        val bitrateBps: Int,
        val trackNumber: Int,
        val year: Int,
        val genre: String?,
        val dateModified: Long,
    )

    private fun queryMediaStore(): List<Track> {
        val projection = arrayOf(
            MediaStore.Audio.Media._ID,
            MediaStore.Audio.Media.TITLE,
            MediaStore.Audio.Media.ARTIST,
            MediaStore.Audio.Media.ALBUM,
            MediaStore.Audio.Media.DURATION,
            MediaStore.Audio.Media.SIZE,
            MediaStore.Audio.Media.BITRATE,
            MediaStore.Audio.Media.TRACK,
            MediaStore.Audio.Media.YEAR,
            MediaStore.Audio.Media.GENRE,
            MediaStore.Audio.Media.DATE_MODIFIED,
        )
        return context.contentResolver.query(
            MediaStore.Audio.Media.EXTERNAL_CONTENT_URI,
            projection,
            "${MediaStore.Audio.Media.IS_MUSIC} != 0",
            null,
            "${MediaStore.Audio.Media.TITLE} ASC",
        )?.use { c ->
            val iId = c.getColumnIndexOrThrow(MediaStore.Audio.Media._ID)
            val iTitle = c.getColumnIndexOrThrow(MediaStore.Audio.Media.TITLE)
            val iArtist = c.getColumnIndexOrThrow(MediaStore.Audio.Media.ARTIST)
            val iAlbum = c.getColumnIndexOrThrow(MediaStore.Audio.Media.ALBUM)
            val iDuration = c.getColumnIndexOrThrow(MediaStore.Audio.Media.DURATION)
            val iSize = c.getColumnIndexOrThrow(MediaStore.Audio.Media.SIZE)
            val iBitrate = c.getColumnIndexOrThrow(MediaStore.Audio.Media.BITRATE)
            val iTrack = c.getColumnIndexOrThrow(MediaStore.Audio.Media.TRACK)
            val iYear = c.getColumnIndexOrThrow(MediaStore.Audio.Media.YEAR)
            val iGenre = c.getColumnIndexOrThrow(MediaStore.Audio.Media.GENRE)
            val iModified = c.getColumnIndexOrThrow(MediaStore.Audio.Media.DATE_MODIFIED)

            buildList(c.count) {
                while (c.moveToNext()) {
                    if (c.isNull(iDuration)) continue
                    add(
                        Track(
                            mediaStoreId = c.getLong(iId),
                            title = c.getString(iTitle).orEmpty(),
                            artistRaw = if (c.isNull(iArtist)) null else c.getString(iArtist),
                            album = c.getString(iAlbum).orEmpty(),
                            durationMs = c.getLong(iDuration),
                            size = c.getLong(iSize),
                            bitrateBps = c.getInt(iBitrate),
                            trackNumber = c.getInt(iTrack),
                            year = c.getInt(iYear),
                            genre = if (c.isNull(iGenre)) null else c.getString(iGenre),
                            dateModified = c.getLong(iModified),
                        ),
                    )
                }
            }
        } ?: emptyList()
    }

    private companion object {
        const val TAG = "CatClawLibrary"
        const val SOURCE_LOCAL = 0
        const val PROTOCOL_NONE = 0
        const val UNKNOWN_ALBUM = "未知专辑"
    }
}
