package com.catclaw.music.kt.data.remote

import com.catclaw.music.kt.core.model.ConnectionProfile
import com.catclaw.music.kt.core.model.ProtocolType
import com.catclaw.music.kt.data.db.AlbumKey
import com.catclaw.music.kt.data.db.LibraryDao
import com.catclaw.music.kt.data.db.NameId
import com.catclaw.music.kt.data.db.entity.AlbumEntity
import com.catclaw.music.kt.data.db.entity.ArtistEntity
import com.catclaw.music.kt.data.db.entity.SongArtistEntity
import com.catclaw.music.kt.data.db.entity.SongEntity
import com.catclaw.music.kt.data.lyrics.TagFixtures
import com.catclaw.music.kt.data.media.HeadBytesReader
import java.lang.reflect.InvocationHandler
import java.lang.reflect.Method
import java.lang.reflect.Proxy
import java.nio.charset.StandardCharsets
import java.util.Collections
import kotlinx.coroutines.runBlocking
import org.junit.Assert.assertEquals
import org.junit.Assert.assertTrue
import org.junit.Test

/**
 * 远程补元数据的编排：选行、取头、合并、落库。
 *
 * `LibraryDao` 有 70 多个 suspend 方法，所以按项目已有做法用 `Proxy` 按方法名分派；
 * 取字节走假 [HeadBytesReader]，头部字节由 `TagFixtures` 按规范现拼 —— 解析器与测试各算各的。
 */
class RemoteMetadataBackfillerTest {

    private class FakeDao(
        val rows: List<SongEntity>,
        val knownArtists: List<NameId> = emptyList(),
        val knownAlbums: List<AlbumKey> = emptyList(),
    ) {
        val calls = mutableListOf<String>()
        val writtenSongs = mutableListOf<SongEntity>()
        val writtenLinks = mutableListOf<SongArtistEntity>()
        var insertedArtists = 0
        var insertedAlbums = 0

        fun build(): LibraryDao = Proxy.newProxyInstance(
            LibraryDao::class.java.classLoader,
            arrayOf(LibraryDao::class.java),
            InvocationHandler { _, method, args -> handle(method, args ?: emptyArray()) },
        ) as LibraryDao

        private fun handle(method: Method, a: Array<Any?>): Any? {
            calls += method.name
            return when (method.name) {
                "remoteMissingMetadata" -> rows
                "artistNames" -> knownArtists
                "albumKeys" -> knownAlbums
                "artistIdByName" -> knownArtists.firstOrNull { it.name == a[0] }?.id
                "albumIdByTitle" ->
                    knownAlbums.firstOrNull { it.title == a[0] && it.artistId == (a[1] as Number).toInt() }?.id
                // insertArtist / insertAlbum 回的是自增 id，装箱必须是 Long
                "insertArtist" -> { insertedArtists++; 900L + insertedArtists }
                "insertAlbum" -> { insertedAlbums++; 950L + insertedAlbums }
                "writeBackfill" -> {
                    @Suppress("UNCHECKED_CAST")
                    writtenSongs += a[0] as List<SongEntity>
                    @Suppress("UNCHECKED_CAST")
                    writtenLinks += a[1] as List<SongArtistEntity>
                    Unit
                }
                "toString" -> "FakeDao"
                "hashCode" -> System.identityHashCode(a[0])
                "equals" -> a[0] === a[0]
                else -> defaultFor(method.returnType)
            }
        }
    }

    private class FakeHead(responses: Map<String, ByteArray>) {
        val requests: MutableList<Pair<String, Int>> = Collections.synchronizedList(mutableListOf())
        private val table = responses.toMap()

        fun reader(): HeadBytesReader = HeadBytesReader { url, size ->
            requests += url to size
            val key = table.keys.firstOrNull { url.contains(it) } ?: throw IllegalStateException("没配这个 url: $url")
            table[key]
        }
    }

    private val webdav = ConnectionProfile(name = "NAS", protocol = ProtocolType.WebDAV, host = "192.0.2.1", port = 80)

    @Test
    fun smallHeadIsEnoughWhenItAlreadyHasTags() {
        val dao = FakeDao(listOf(row(id = 1, path = "/dav/a.mp3")))
        val head = FakeHead(mapOf("a.mp3" to id3Head(artist = "周杰伦", album = "十一月的萧邦")))
        val result = RemoteMetadataBackfiller(dao.build(), head.reader()).runNow(webdav)

        assertEquals(listOf("/dav/a.mp3" to 256 * 1024), requestsOf(head))
        assertEquals(1, result.updated)
        val written = dao.writtenSongs.single()
        // 库里没有「周杰伦」，所以歌手与专辑都要新建，id 用自增回来的那个
        assertEquals(901, written.artistId)
        assertEquals(951, written.albumId)
        assertEquals(1, dao.insertedArtists)
        assertEquals(1, dao.insertedAlbums)
        // 关联必须一起写，否则艺人下钻页看不到这首
        assertEquals(listOf(SongArtistEntity(songId = 1, artistId = 901)), dao.writtenLinks)
    }

    @Test
    fun knownArtistIsReusedInsteadOfDuplicated() {
        val dao = FakeDao(
            rows = listOf(row(id = 7, path = "/dav/b.mp3", artistId = 5, albumId = 8)),
            knownArtists = listOf(NameId(5, "周杰伦")),
            knownAlbums = listOf(AlbumKey(8, "十一月的萧邦", 5)),
        )
        val head = FakeHead(mapOf("b.mp3" to id3Head(artist = "周杰伦", album = "十一月的萧邦")))
        val result = RemoteMetadataBackfiller(dao.build(), head.reader()).runNow(webdav)

        assertEquals(0, result.updated) // 一行都没变 ⇒ 一次写都不做
        assertTrue(dao.writtenSongs.isEmpty())
        assertEquals(0, dao.insertedArtists)
        assertEquals(0, dao.insertedAlbums)
    }

    @Test
    fun largeTagFormatsRetryWithABiggerHead() {
        // FLAC 且小头里既没有字段也没有 STREAMINFO 时长 ⇒ 换 2 MB 再来一次
        val uselessFlac = TagFixtures.flac(TagFixtures.vorbisComment(emptyList()))
        val dao = FakeDao(listOf(row(id = 3, path = "/dav/c.flac")))
        val head = FakeHead(mapOf("c.flac" to uselessFlac))
        val result = RemoteMetadataBackfiller(dao.build(), head.reader()).runNow(webdav)

        assertEquals(listOf(256 * 1024, 2 * 1024 * 1024), head.requests.map { it.second })
        assertEquals(0, result.updated)
        assertEquals(1, result.skipped)
    }

    @Test
    fun nonAudioHeadStopsWithoutRetryingOrWriting() {
        val dao = FakeDao(listOf(row(id = 4, path = "/dav/d.mp3")))
        val head = FakeHead(mapOf("d.mp3" to "PKzipfilezip!!".toByteArray(StandardCharsets.ISO_8859_1)))
        val result = RemoteMetadataBackfiller(dao.build(), head.reader()).runNow(webdav)

        assertEquals(1, head.requests.size) // 不是音频就别再花一次大头
        assertEquals(0, result.updated)
        assertTrue(dao.writtenSongs.isEmpty())
    }

    @Test
    fun oneFailingSongDoesNotStopTheOthers() {
        val dao = FakeDao(listOf(row(id = 5, path = "/dav/boom.mp3"), row(id = 6, path = "/dav/good.mp3")))
        val head = FakeHead(mapOf("good.mp3" to id3Head(artist = "歌手")))
        val result = RemoteMetadataBackfiller(dao.build(), head.reader()).runNow(webdav)

        assertEquals(2, result.total)
        assertEquals(1, result.failed)
        assertEquals(1, result.updated)
        assertEquals(6, dao.writtenSongs.single().id)
    }

    @Test
    fun progressReportsEveryTenthAndFinishesFull() {
        val rows = (1..25).map { row(id = it, path = "/dav/s$it.mp3") }
        val dao = FakeDao(rows)
        val head = FakeHead(mapOf(".mp3" to id3Head(artist = "歌手")))
        val seen = Collections.synchronizedList(mutableListOf<Pair<Int, Int>>())
        RemoteMetadataBackfiller(dao.build(), head.reader()).runNow(webdav) { done, total -> seen += done to total }

        assertTrue("要按 10 的节奏报：$seen", seen.containsAll(listOf(10 to 25, 20 to 25, 25 to 25)))
        assertTrue(seen.all { it.second == 25 })
    }

    @Test
    fun subsonicProfilesAreLeftAlone() {
        val dao = FakeDao(listOf(row(id = 9, path = "http://host/rest/get")))
        val head = FakeHead(emptyMap())
        val result = RemoteMetadataBackfiller(dao.build(), head.reader())
            .runNow(ConnectionProfile(name = "Sub", protocol = ProtocolType.Navidrome))

        assertEquals(0, result.total)
        // 曲清单里本来就有时长与歌手，一条 SQL 都不该发、一个字节都不该下
        assertTrue(dao.calls.isEmpty())
        assertTrue(head.requests.isEmpty())
    }

    @Test
    fun percentEncodedWebDavTitlesAreDecodedButSmbTitlesAreNot() {
        val dao = FakeDao(listOf(row(id = 11, path = "/dav/%E5%A4%9C.mp3")))
        val head = FakeHead(mapOf("%" to id3Head(artist = "歌手", title = "夜曲%20-%20周杰伦")))
        RemoteMetadataBackfiller(dao.build(), head.reader()).runNow(webdav)
        assertEquals("夜曲 - 周杰伦", dao.writtenSongs.single().title)

        val smbDao = FakeDao(listOf(row(id = 12, path = "http://127.0.0.1:19876/smb/share/a.mp3")))
        val smbHead = FakeHead(mapOf("a.mp3" to id3Head(artist = "歌手", title = "夜曲%20-%20周杰伦")))
        RemoteMetadataBackfiller(smbDao.build(), smbHead.reader())
            .runNow(ConnectionProfile(name = "SMB", protocol = ProtocolType.SMB))
        assertEquals("夜曲%20-%20周杰伦", smbDao.writtenSongs.single().title)
    }

    /** suspend 的 run 包一层，测试里就能一行触发整轮回填 */
    private fun RemoteMetadataBackfiller.runNow(
        profile: ConnectionProfile,
        onProgress: (done: Int, total: Int) -> Unit = { _, _ -> },
    ): BackfillResult {
        val backfiller = this
        return runBlocking { backfiller.run(profile, onProgress) }
    }

    private fun requestsOf(head: FakeHead): List<Pair<String, Int>> = head.requests.toList()

    private fun row(id: Int, path: String, artistId: Int = 0, albumId: Int = 0, size: Long = 3_200_000L) = SongEntity(
        id = id,
        title = path.substringAfterLast('/'),
        filePath = path,
        fileSize = size,
        artistId = artistId,
        albumId = albumId,
    )

    /** 只带文本帧的 ID3v2.3 头：没有音频帧，所以时长一律 0，歌手或专辑必须有一个才算有用 */
    private fun id3Head(artist: String? = null, album: String? = null, title: String? = null): ByteArray {
        val frames = buildList {
            title?.let { add("TIT2" to utf8Frame(it)) }
            artist?.let { add("TPE1" to utf8Frame(it)) }
            album?.let { add("TALB" to utf8Frame(it)) }
        }
        return TagFixtures.id3v2(3, frames)
    }

    private fun utf8Frame(text: String): ByteArray = byteArrayOf(3) + text.toByteArray(StandardCharsets.UTF_8)

}

private fun defaultFor(type: Class<*>): Any? = when (type) {
    java.util.Optional::class.java -> null
    Int::class.java, Int::class.javaPrimitiveType -> 0
    Long::class.java, Long::class.javaPrimitiveType -> 0L
    Boolean::class.java, Boolean::class.javaPrimitiveType -> false
    Unit::class.java, Void::class.javaPrimitiveType -> Unit
    else -> if (List::class.java.isAssignableFrom(type)) emptyList<Any>() else null
}
