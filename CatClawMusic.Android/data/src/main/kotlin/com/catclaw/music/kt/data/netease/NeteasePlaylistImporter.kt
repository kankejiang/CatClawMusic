package com.catclaw.music.kt.data.netease

import androidx.room.withTransaction
import com.catclaw.music.kt.core.model.ProtocolType
import com.catclaw.music.kt.core.model.SongSource
import com.catclaw.music.kt.core.netease.MatchCandidate
import com.catclaw.music.kt.core.netease.NeteasePlaylistLink
import com.catclaw.music.kt.core.netease.SongMatcher
import com.catclaw.music.kt.data.db.CatClawDatabase
import com.catclaw.music.kt.data.db.PlaylistDao
import com.catclaw.music.kt.data.db.entity.AlbumEntity
import com.catclaw.music.kt.data.db.entity.ArtistEntity
import com.catclaw.music.kt.data.db.entity.PlaylistEntity
import com.catclaw.music.kt.data.db.entity.SongEntity
import javax.inject.Inject
import javax.inject.Singleton
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.withContext

/**
 * 把网易云歌单导成一个本地歌单。
 *
 * 落库策略（决定了这个功能的边界，写在最前面）：
 *
 * - **本地已有的曲目优先**。标题+艺术家（含 feat./多艺术家/全角差异）归一化后能对上的，
 *   直接引用本地 `SongId` —— 这样导入的歌单播的是用户自己那份文件，音质/离线都不受影响。
 * - **本地没有的记成在线曲**。`FilePath` 存 `song/media/outer/url` 跳转入口（不是最终 CDN 地址，
 *   那个几小时就过期），`Source=WebDAV` 让它走现有的远程播放链路（`AudioCache` 先落盘再播，
 *   第二次起离线可听）。`RemoteId = netease:<songId>` 保证同一首歌在多个歌单里只入库一次。
 * - 两样都不成立的（超长歌单接口只给前 1000 首、曲目字段残缺）计入 `skipped`，不硬塞。
 *
 * 写入整段包在一个事务里：中途失败不会留下"歌单建了但只有一半歌"的半成品。
 */
@Singleton
class NeteasePlaylistImporter @Inject constructor(
    private val db: CatClawDatabase,
    private val client: NeteasePlaylistClient,
) {

    enum class Reason { INVALID_LINK, FETCH_FAILED, EMPTY_PLAYLIST }

    class ImportException(val reason: Reason, message: String? = null) : Exception(message)

    data class Result(
        val playlistId: Int,
        val playlistName: String,
        /** 实际进了歌单的曲目数 */
        val imported: Int,
        /** 其中命中本地库的 */
        val localMatched: Int,
        /** 其中走在线播放的（含此前已入库复用的） */
        val online: Int,
        /** 本次新入库的在线曲 */
        val onlineAdded: Int,
        /** 歌单自称有、接口没给到的（超长歌单） */
        val skipped: Int,
    )

    suspend fun import(
        rawInput: String,
        onProgress: (done: Int, total: Int) -> Unit = { _, _ -> },
    ): Result = withContext(Dispatchers.IO) {
        val playlistId = NeteasePlaylistLink.parse(rawInput)
            ?: throw ImportException(Reason.INVALID_LINK)

        val playlist = client.fetch(playlistId) ?: throw ImportException(Reason.FETCH_FAILED)
        if (playlist.tracks.isEmpty()) throw ImportException(Reason.EMPTY_PLAYLIST)

        val library = db.libraryDao()
        val playlists = db.playlistDao()

        // 匹配只读一次库：618 首的库把行投影成匹配候选，比逐首歌查一次 SQL 快两个数量级
        val matcher = SongMatcher(
            library.localSongRows().map {
                MatchCandidate(it.id, it.title, it.artist, it.durationSeconds)
            },
        )
        val knownRemote = library.remoteKeys().associateBy { it.remoteId }

        var localMatched = 0
        var online = 0
        var onlineAdded = 0
        val songIds = ArrayList<Int>(playlist.tracks.size)
        val artistIds = HashMap<String, Int>()
        val albumIds = HashMap<String, Int>()
        val now = System.currentTimeMillis()
        val total = playlist.tracks.size

        val newPlaylistId = db.withTransaction {
            for ((index, track) in playlist.tracks.withIndex()) {
                val local = matcher.match(track.title, track.artist, track.durationSeconds)
                if (local != null) {
                    localMatched++
                    songIds += local.id
                } else {
                    val remoteKey = NeteasePlaylistClient.REMOTE_PREFIX + track.id
                    val previous = knownRemote[remoteKey]
                    if (previous != null) {
                        online++
                        songIds += previous.id
                    } else {
                        val artistId = artistIds.getOrPut(track.artist) {
                            track.artist.takeIf { it.isNotBlank() }?.let { name ->
                                library.artistIdByName(name)
                                    ?: library.insertArtist(ArtistEntity(name = name)).toInt()
                            } ?: 0
                        }
                        // 专辑同名不同艺术家是两件事，缓存键必须带 artistId，否则会串专辑
                        val albumKey = "${track.album}|$artistId"
                        val albumId = albumIds.getOrPut(albumKey) {
                            track.album.takeIf { it.isNotBlank() }?.let { title ->
                                library.albumIdByTitle(title, artistId)
                                    ?: library.insertAlbum(
                                        AlbumEntity(
                                            title = title,
                                            name = title,
                                            artist = track.artist,
                                            artistId = artistId,
                                            coverArtPath = track.coverUrl,
                                        ),
                                    ).toInt()
                            } ?: 0
                        }
                        val newId = library.upsertSong(
                            SongEntity(
                                title = track.title,
                                artistId = artistId,
                                albumId = albumId,
                                durationSeconds = track.durationSeconds,
                                // 入口 URL 而不是最终 CDN 地址：后者带时间戳，几小时就失效
                                filePath = NeteasePlaylistClient.playUrlFallback(track.id),
                                dateAdded = now,
                                dateModified = now,
                                coverArtPath = track.coverUrl ?: playlist.coverUrl,
                                source = SongSource.WebDAV.ordinal,
                                protocol = ProtocolType.WebDAV.ordinal,
                                remoteId = remoteKey,
                            ),
                        ).toInt()
                        online++
                        onlineAdded++
                        songIds += newId
                    }
                }
                if ((index + 1) % PROGRESS_STEP == 0 || index == total - 1) {
                    onProgress(index + 1, total)
                }
            }

            val playlistName = uniqueName(playlists, playlist.name)
            val created = playlists.upsert(
                PlaylistEntity(
                    name = playlistName,
                    createdAt = now,
                    updatedAt = now,
                    songCount = songIds.size,
                ),
            ).toInt()
            playlists.appendAll(created, songIds.distinct())
            created
        }

        Result(
            playlistId = newPlaylistId,
            playlistName = db.playlistDao().byId(newPlaylistId)?.name ?: playlist.name,
            imported = songIds.distinct().size,
            localMatched = localMatched,
            online = online,
            onlineAdded = onlineAdded,
            skipped = (playlist.trackCount - total).coerceAtLeast(0),
        )
    }

    /**
     * 同名歌单不覆盖也不合并：覆盖会删掉用户自己往里加的歌，合并则让"再导一次"变成静默翻倍。
     * 追加序号是唯一无损的选择，用户不想要旧的那份自己删。
     */
    private suspend fun uniqueName(dao: PlaylistDao, base: String): String {
        val name = base.trim().ifEmpty { "网易云歌单" }
        if (dao.byName(name) == null) return name
        for (index in 2..99) {
            val candidate = "$name（$index）"
            if (dao.byName(candidate) == null) return candidate
        }
        return "$name（${System.currentTimeMillis() % 1000}）"
    }

    private companion object {
        const val PROGRESS_STEP = 20
    }
}
