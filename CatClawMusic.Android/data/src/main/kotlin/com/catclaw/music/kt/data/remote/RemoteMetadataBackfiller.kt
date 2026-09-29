package com.catclaw.music.kt.data.remote

import android.os.SystemClock
import com.catclaw.music.kt.core.diagnostic.AppLog
import com.catclaw.music.kt.core.library.AudioTags
import com.catclaw.music.kt.core.library.MediaScanRules
import com.catclaw.music.kt.core.library.MetadataBackfill
import com.catclaw.music.kt.core.model.ConnectionProfile
import com.catclaw.music.kt.core.model.ProtocolType
import com.catclaw.music.kt.core.model.SongSource
import com.catclaw.music.kt.data.db.LibraryDao
import com.catclaw.music.kt.data.db.entity.AlbumEntity
import com.catclaw.music.kt.data.db.entity.ArtistEntity
import com.catclaw.music.kt.data.db.entity.SongArtistEntity
import com.catclaw.music.kt.data.db.entity.SongEntity
import com.catclaw.music.kt.data.media.AudioHeadTags
import com.catclaw.music.kt.data.media.AudioBytesReader
import java.util.concurrent.atomic.AtomicInteger
import javax.inject.Inject
import javax.inject.Singleton
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.async
import kotlinx.coroutines.awaitAll
import kotlinx.coroutines.coroutineScope
import kotlinx.coroutines.withContext

/** @param skipped 取到头部但补不出可用元数据的行数（不是失败数） */
data class BackfillResult(
    val total: Int,
    val updated: Int,
    val skipped: Int,
    val failed: Int,
    val elapsedMs: Long,
)

/**
 * 远程曲「补全元数据」：对已入库但时长/歌手没填的 WebDAV 与 SMB 行，
 * 取回文件头部解标签，再批量写回。对应 C# `NetworkMusicService.BackfillMetadataAsync`。
 *
 * 三处照 C# 的形状走并说清为什么：
 * - **有界并行**（[MetadataBackfill.WORKERS]）而不是每首开一个协程：库一大，
 *   无上限就是几千个并发连接，服务器开始排队、界面被进度回调淹没；
 * - **先全部取完、再分批事务落库**：解析是网络等待、落库是磁盘写，混在一起会让
 *   事务横跨整轮请求。艺术家/专辑的「查不到就建」也放在落库这一侧（单线程），
 *   否则两个 worker 同时给同一个新歌手各建一行；
 * - **只写真有用的**：解出来没有歌手也没有专辑也没有时长（`hasUseful` 为假）就整行跳过，
 *   宁可不写也不把骨架写成「未知艺术家」—— 那会让这首歌下次还入选，但白白多一次写。
 */
@Singleton
class RemoteMetadataBackfiller @Inject constructor(
    private val dao: LibraryDao,
    private val bytes: AudioBytesReader,
) {

    private data class Patch(val before: SongEntity, val tags: AudioTags)

    suspend fun run(
        profile: ConnectionProfile,
        onProgress: (done: Int, total: Int) -> Unit = { _, _ -> },
    ): BackfillResult = withContext(Dispatchers.IO) {
        val startedAt = SystemClock.elapsedRealtime()
        val source = when (profile.protocol) {
            // Subsonic/Navidrome 的曲清单里本来就带时长、歌手、专辑、码率，没有可补的
            ProtocolType.Navidrome -> return@withContext BackfillResult(0, 0, 0, 0, 0L)
            ProtocolType.SMB -> SongSource.SMB.ordinal
            ProtocolType.WebDAV -> SongSource.WebDAV.ordinal
        }
        val rows = dao.remoteMissingMetadata(source)
        if (rows.isEmpty()) {
            return@withContext BackfillResult(0, 0, 0, 0, SystemClock.elapsedRealtime() - startedAt)
        }
        val total = rows.size
        // 两张正查表 + 两张反查表：正查给落库时「按名字要 id」，反查给取值时「按 id 还原当前文本」
        val artistNamesById = LinkedHashMap<Int, String>()
        val artistsByName = LinkedHashMap<String, Int>()
        dao.artistNames().forEach {
            artistNamesById.putIfAbsent(it.id, it.name)
            artistsByName.putIfAbsent(it.name, it.id)
        }
        val albumTitlesById = LinkedHashMap<Int, String>()
        val albumsByKey = LinkedHashMap<Pair<String, Int>, Int>()
        dao.albumKeys().forEach {
            albumTitlesById.putIfAbsent(it.id, it.title)
            albumsByKey.putIfAbsent(it.title to it.artistId, it.id)
        }
        val decodeTitle = profile.protocol == ProtocolType.WebDAV
        val done = AtomicInteger(0)

        val outcomes = coroutineScope {
            val workers = Dispatchers.IO.limitedParallelism(MetadataBackfill.WORKERS)
            rows.map { row ->
                async(workers) {
                    val result = runCatching {
                        fetch(row, artistNamesById, albumTitlesById, decodeTitle)
                    }
                    val n = done.incrementAndGet()
                    if (MetadataBackfill.reportsProgress(n, total)) onProgress(n, total)
                    result.fold(
                        onSuccess = { it to 0 },
                        onFailure = {
                            AppLog.w(TAG, "补全失败 ${row.filePath}: ${it.message}")
                            null to 1
                        },
                    )
                }
            }.awaitAll()
        }
        val patches = outcomes.map { it.first }.filterNotNull()
        val failed = outcomes.sumOf { it.second }

        var updated = 0
        for (batch in patches.chunked(MetadataBackfill.PERSIST_BATCH)) {
            updated += persist(batch, artistsByName, albumsByKey)
        }
        onProgress(total, total)
        AppLog.i(TAG, "补全元数据完成 total=$total updated=$updated skipped=${total - patches.size} failed=$failed")
        BackfillResult(
            total = total,
            updated = updated,
            skipped = total - patches.size,
            failed = failed,
            elapsedMs = SystemClock.elapsedRealtime() - startedAt,
        )
    }

    /** 取头部（必要时再取尾部）并解标签：拿不到、不是音频、或解不出有用信息都回 null（这一行不动） */
    private suspend fun fetch(
        row: SongEntity,
        artistNamesById: Map<Int, String>,
        albumTitlesById: Map<Int, String>,
        decodeTitle: Boolean,
    ): Patch? {
        val url = row.filePath
        if (url.isBlank()) return null
        // SQL 里已经写了同一条判据；这里再走一次纯规则，是为了让规则本身是承重的 ——
        // 哪天 WHERE 改了而规则没改，多进来的行会被安静地挡掉，而不是白下一轮头
        if (!MetadataBackfill.needsBackfill(row.durationSeconds, row.artistId)) return null

        var best: AudioTags? = null
        for (size in MetadataBackfill.headSizes(url)) {
            val head = bytes.head(url, size) ?: continue
            if (!MetadataBackfill.looksLikeAudio(head)) return null
            val parsed = AudioHeadTags.parse(head, row.fileSize)
            if (MetadataBackfill.hasUseful(parsed)) {
                return patch(row, parsed, artistNamesById, albumTitlesById, decodeTitle)
            }
            if (best == null) best = parsed
        }

        /**
         * 头部两次都没解出有效信息：非 faststart 的 M4A 就是这个形状 —— `moov`（全部标签与时长）
         * 在文件末尾，头部只有 ftyp + mdat。对齐 C# 的 `TagTailSize` 再 Range 一次尾部。
         * 这是整条链上唯一会下第二**段**内容的情形，所以判据收紧：只有 MP4 家族且文件大小已知才试。
         */
        val tailSize = MetadataBackfill.tailBytes(row.fileSize)
        if (tailSize > 0L && MetadataBackfill.mayHaveMoovAtTail(url)) {
            val tail = bytes.tail(url, tailSize, row.fileSize)
            if (tail != null) {
                val fromTail = AudioHeadTags.parseTail(tail)
                if (MetadataBackfill.hasUseful(fromTail)) {
                    return patch(row, fromTail, artistNamesById, albumTitlesById, decodeTitle)
                }
            }
        }

        val parsed = best?.takeIf { MetadataBackfill.hasUseful(it) } ?: return null
        return patch(row, parsed, artistNamesById, albumTitlesById, decodeTitle)
    }

    private fun patch(
        row: SongEntity,
        fetched: AudioTags,
        artistNamesById: Map<Int, String>,
        albumTitlesById: Map<Int, String>,
        decodeTitle: Boolean,
    ): Patch {
        val current = AudioTags(
            title = row.title,
            artist = artistNamesById[row.artistId],
            album = albumTitlesById[row.albumId].orEmpty(),
            genre = row.genre,
            durationMs = row.durationSeconds * 1000L,
            bitrateKbps = row.bitrate,
            trackNumber = row.trackNumber,
            year = row.year,
        )
        return Patch(row, MetadataBackfill.merge(current, fetched, decodeTitle))
    }

    /** 单线程侧：解析成 id、必要时新建，最后一批写回歌曲行与歌手关联 */
    private suspend fun persist(
        batch: List<Patch>,
        artistsByName: MutableMap<String, Int>,
        albumsByKey: MutableMap<Pair<String, Int>, Int>,
    ): Int {
        val songs = ArrayList<SongEntity>(batch.size)
        val links = ArrayList<SongArtistEntity>(batch.size)
        for (patch in batch) {
            val before = patch.before
            val tags = patch.tags
            val artistName = tags.artist?.trim().orEmpty()
            val albumName = tags.album.trim()
            val artistId = when {
                artistName.isEmpty() || artistName == MediaScanRules.UNKNOWN_ARTIST -> before.artistId
                else -> resolveArtist(dao, artistsByName, artistName)
            }
            val albumId = when {
                albumName.isEmpty() || albumName == MediaScanRules.UNKNOWN_ALBUM -> before.albumId
                artistId <= 0 -> before.albumId // 没有主歌手就归不出这张专辑
                else -> resolveAlbum(dao, albumsByKey, albumName, artistName, artistId, tags.year)
            }
            val after = before.copy(
                title = tags.title.ifBlank { before.title },
                artistId = artistId,
                albumId = albumId,
                durationSeconds = MediaScanRules.durationSeconds(tags.durationMs),
                bitrate = tags.bitrateKbps,
                trackNumber = tags.trackNumber,
                year = tags.year,
                genre = tags.genre,
            )
            if (after != before) songs += after
            if (artistId > 0 && artistId != before.artistId) {
                links += SongArtistEntity(songId = after.id, artistId = artistId)
            }
        }
        if (songs.isEmpty()) return 0
        dao.writeBackfill(songs, links)
        return songs.size
    }

    private suspend fun resolveArtist(dao: LibraryDao, cache: MutableMap<String, Int>, name: String): Int =
        cache[name] ?: dao.artistIdByName(name) ?: dao.insertArtist(ArtistEntity(name = name)).toInt()
            .also { cache[name] = it }

    private suspend fun resolveAlbum(
        dao: LibraryDao,
        cache: MutableMap<Pair<String, Int>, Int>,
        title: String,
        artistName: String,
        artistId: Int,
        year: Int,
    ): Int = cache[title to artistId]
        ?: dao.albumIdByTitle(title, artistId)
        ?: dao.insertAlbum(
            AlbumEntity(
                title = title,
                name = title,
                artist = artistName,
                year = year,
                artistId = artistId,
                releaseYear = year,
            ),
        ).toInt().also { cache[title to artistId] = it }

    private companion object {
        const val TAG = "CatClawBackfill"
    }
}
