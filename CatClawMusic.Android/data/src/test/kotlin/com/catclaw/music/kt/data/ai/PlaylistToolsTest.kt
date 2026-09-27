package com.catclaw.music.kt.data.ai

import com.catclaw.music.kt.core.model.Song
import com.catclaw.music.kt.core.playback.PlaybackUiState
import com.catclaw.music.kt.core.playback.PlayerApi
import com.catclaw.music.kt.core.playback.SessionLauncher
import com.catclaw.music.kt.data.db.LibraryDao
import com.catclaw.music.kt.data.db.PlaylistDao
import com.catclaw.music.kt.data.db.SongListItem
import com.catclaw.music.kt.data.db.entity.PlaylistEntity
import java.lang.reflect.Proxy
import kotlinx.coroutines.flow.Flow
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.test.runTest
import org.junit.Assert.assertEquals
import org.junit.Assert.assertTrue
import org.junit.Test

/**
 * 歌单类工具的**工具层**行为：参数校验消息、调用哪个 DAO 方法、结果 JSON 形状。
 *
 * ⚠ 覆盖边界要说清楚：`delete` 跳过 IsSystem 歌单、`insertItem` 的 OR IGNORE 去重、
 * `Position` 排序这些都是 SQL 里的行为，假 DAO 测不到，得等 Room 侧的仪器化测试。
 */
class PlaylistToolsTest {

    private class FakePlaylists : PlaylistDao {
        val created = mutableListOf<PlaylistEntity>()
        val items = linkedMapOf<Int, MutableList<Int>>()
        val deleted = mutableListOf<Int>()
        val removed = mutableListOf<Pair<Int, Int>>()
        val touched = mutableListOf<Pair<Int, Int>>()
        private var nextId = 100

        override suspend fun byId(id: Int) = created.firstOrNull { it.id == id }
        override suspend fun byName(name: String) = created.firstOrNull { it.name == name }

        override suspend fun upsert(playlist: PlaylistEntity): Long {
            val row = playlist.copy(id = nextId++)
            created += row
            items.getOrPut(row.id) { mutableListOf() }
            return row.id.toLong()
        }

        override suspend fun delete(id: Int) {
            deleted += id
            items.remove(id)
        }

        override suspend fun clearItems(id: Int) { items[id]?.clear() }

        override suspend fun insertItem(playlistId: Int, songId: Int, position: Int) {
            items.getOrPut(playlistId) { mutableListOf() }.add(position.coerceAtMost(items[playlistId]?.size ?: 0), songId)
        }

        override suspend fun removeItem(playlistId: Int, songId: Int) {
            removed += playlistId to songId
            items[playlistId]?.remove(songId)
        }

        override suspend fun songIdsInOrder(playlistId: Int) = items[playlistId]?.toList() ?: emptyList()

        override fun observePlaylists(): Flow<List<PlaylistEntity>> = MutableStateFlow(created.toList())

        override fun observeItems(playlistId: Int): Flow<List<SongListItem>> = MutableStateFlow(emptyList())

        override suspend fun touch(id: Int, count: Int, now: Long) { touched += id to count }

        override suspend fun reorder(playlistId: Int, orderedSongIds: List<Int>) {
            items[playlistId] = orderedSongIds.toMutableList()
        }

        override suspend fun updatePosition(playlistId: Int, songId: Int, position: Int) = Unit

        override suspend fun appendAll(playlistId: Int, songIds: List<Int>) {
            val list = items.getOrPut(playlistId) { mutableListOf() }
            list += songIds
            touch(playlistId, list.size, 0L)
        }

        override suspend fun maxPosition(playlistId: Int) = (items[playlistId]?.size ?: 0) - 1
    }

    private class FakePlayer : PlayerApi {
        override val state = MutableStateFlow(PlaybackUiState())
        override var volume: Float = 1f
        val played = mutableListOf<List<Song>>()
        val log = mutableListOf<String>()

        override fun playSongs(songs: List<Song>, startIndex: Int) { played += songs }
        override fun pause() { log += "pause" }
        override fun resume() { log += "resume" }
        override fun next() { log += "next" }
        override fun previous() { log += "previous" }
        override fun seekTo(positionMs: Long) { log += "seek:$positionMs" }
        override fun currentPositionMs() = 0L
        override fun queueSongs() = emptyList<Song>()
        override fun clearQueue() { log += "clear" }
        override fun addToQueue(song: Song, nextUp: Boolean) { log += "enqueue" }
    }

    private class Fixture {
        val playlists = FakePlaylists()
        val player = FakePlayer()
        var ensured = 0
        val tools = MusicAgentTools(
            player = player,
            dao = unusedLibraryDao,
            session = SessionLauncher { ensured++ },
            playlists = playlists,
            fetcher = FakeFetcher(mutableListOf()),
        )

        fun tool(name: String) = tools.all.first { it.name == name }
    }

    @Test
    fun registryExposesThePlaylistContract() {
        val f = Fixture()
        val expected = listOf(
            "create_playlist", "add_song_to_playlist", "remove_song_from_playlist",
            "list_playlists", "get_playlist_songs", "delete_playlist",
        )
        assertEquals(expected, expected.map { f.tool(it).name })
        // 只读标记决定 Agent 能否并行分派，抄错会让写操作和读操作一起跑
        assertTrue(f.tool("list_playlists").isReadOnly)
        assertTrue(f.tool("get_playlist_songs").isReadOnly)
        assertTrue(!f.tool("create_playlist").isReadOnly)
        assertTrue(!f.tool("add_song_to_playlist").isReadOnly)
        assertTrue(!f.tool("delete_playlist").isReadOnly)
        assertEquals(listOf("playlist_id", "song_id"), f.tool("add_song_to_playlist").required)
        assertEquals(listOf("name"), f.tool("create_playlist").required)
        assertEquals(emptyList<String>(), f.tool("list_playlists").required)
    }

    @Test
    fun createWritesRowAndReportsItsId() = runTest {
        val f = Fixture()
        val result = f.tool("create_playlist").execute("""{"name":"深夜"}""")
        assertTrue(result.contains("歌单「深夜」已创建"))
        val row = f.playlists.created.single()
        assertEquals("深夜", row.name)
        assertTrue("创建时间必须真的写进去", row.createdAt > 0)
        assertTrue(result.contains("\"playlistId\":${row.id}"))
    }

    @Test
    fun createRejectsBlankNameWithoutTouchingTheDao() = runTest {
        val f = Fixture()
        assertTrue(f.tool("create_playlist").execute("""{"name":"   "}""").contains("请提供歌单名称"))
        assertTrue(f.playlists.created.isEmpty())
    }

    @Test
    fun addSongAppendsAndRefreshesCount() = runTest {
        val f = Fixture()
        val id = f.playlists.upsert(PlaylistEntity(name = "深夜")).toInt()
        val result = f.tool("add_song_to_playlist").execute("""{"playlist_id":$id,"song_id":7}""")
        assertTrue(result.contains("歌曲已添加到歌单"))
        assertEquals(listOf(7), f.playlists.items.getValue(id))
        assertEquals(id to 1, f.playlists.touched.last())
    }

    @Test
    fun addSongValidatesBothIdsSeparately() = runTest {
        val f = Fixture()
        assertTrue(f.tool("add_song_to_playlist").execute("""{"playlist_id":0,"song_id":7}""").contains("请提供有效的歌单 ID"))
        assertTrue(f.tool("add_song_to_playlist").execute("""{"playlist_id":3,"song_id":0}""").contains("请提供有效的歌曲 ID"))
        assertTrue(f.playlists.touched.isEmpty())
    }

    @Test
    fun removeSongRewritesCount() = runTest {
        val f = Fixture()
        val id = f.playlists.upsert(PlaylistEntity(name = "深夜")).toInt()
        f.playlists.items.getValue(id).addAll(listOf(7, 8))
        val result = f.tool("remove_song_from_playlist").execute("""{"playlist_id":$id,"song_id":7}""")
        assertTrue(result.contains("歌曲已从歌单中移除"))
        assertEquals(listOf(id to 7), f.playlists.removed)
        assertEquals(id to 1, f.playlists.touched.last())
    }

    @Test
    fun removeSongRejectsEitherInvalidId() = runTest {
        val f = Fixture()
        assertTrue(
            f.tool("remove_song_from_playlist").execute("""{"playlist_id":1,"song_id":-2}""")
                .contains("请提供有效的歌单 ID 和歌曲 ID"),
        )
    }

    @Test
    fun listRendersIdNameCountRows() = runTest {
        val f = Fixture()
        val a = f.playlists.upsert(PlaylistEntity(name = "晨跑", songCount = 12))
        val b = f.playlists.upsert(PlaylistEntity(name = "深夜", songCount = 0))
        val result = f.tool("list_playlists").execute("{}")
        assertTrue(result.contains("\"count\":2"))
        assertTrue(result.contains("$a|晨跑|12"))
        assertTrue(result.contains("$b|深夜|0"))
    }

    @Test
    fun emptyPlaylistListStillReportsCountZero() = runTest {
        val f = Fixture()
        assertEquals("""{"count":0,"playlists":[]}""", f.tool("list_playlists").execute("{}"))
    }

    @Test
    fun getPlaylistSongsRejectsInvalidIdBeforeQuerying() = runTest {
        val f = Fixture()
        assertTrue(f.tool("get_playlist_songs").execute("""{"playlist_id":0}""").contains("请提供有效的歌单 ID"))
        val ok = f.tool("get_playlist_songs").execute("""{"playlist_id":5}""")
        assertTrue(ok.contains("\"count\":0"))
    }

    @Test
    fun deleteRemovesRowAndItsItems() = runTest {
        val f = Fixture()
        val id = f.playlists.upsert(PlaylistEntity(name = "深夜")).toInt()
        f.playlists.items.getValue(id).addAll(listOf(7, 8))
        val result = f.tool("delete_playlist").execute("""{"playlist_id":$id}""")
        assertTrue(result.contains("歌单已删除"))
        assertEquals(listOf(id), f.playlists.deleted)
        assertTrue("删单后不该再留条目", f.playlists.items[id].isNullOrEmpty())
    }
}

/** 歌单类工具一条曲库查询都不发，所以这里用「被调到就是 bug」的代理占位，不必手写 30 个方法 */
private val unusedLibraryDao = Proxy.newProxyInstance(
    LibraryDao::class.java.classLoader,
    arrayOf(LibraryDao::class.java),
) { _, method, _ -> throw UnsupportedOperationException(method.name) } as LibraryDao
