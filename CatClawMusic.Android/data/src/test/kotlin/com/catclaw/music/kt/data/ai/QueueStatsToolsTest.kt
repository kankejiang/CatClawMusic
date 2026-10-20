package com.catclaw.music.kt.data.ai

import com.catclaw.music.kt.core.model.Song
import com.catclaw.music.kt.core.playback.PlaybackUiState
import com.catclaw.music.kt.core.playback.PlayerApi
import com.catclaw.music.kt.core.playback.SessionLauncher
import com.catclaw.music.kt.data.db.LibraryDao
import com.catclaw.music.kt.data.db.PlayCountRow
import com.catclaw.music.kt.data.db.PlaylistCover
import com.catclaw.music.kt.data.db.FakePlaylistDao
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
 * 队列与统计两个工具。LibraryDao 用动态代理只答被用到的那两条查询 ——
 * 其余方法一旦被调到就抛，等于顺带钉住「这两个工具不该查别的」。
 */
class QueueStatsToolsTest {

    private class FakePlayer : PlayerApi {
        override val state = MutableStateFlow(PlaybackUiState())
        override var volume: Float = 1f
        val enqueued = mutableListOf<Pair<Int, Boolean>>()
        var clears = 0

        override fun playSongs(songs: List<Song>, startIndex: Int) = Unit
        override fun pause() = Unit
        override fun resume() = Unit
        override fun next() = Unit
        override fun previous() = Unit
        override fun seekTo(positionMs: Long) = Unit
        override fun currentPositionMs() = 0L
        override fun queueSongs() = emptyList<Song>()
        override fun clearQueue() { clears++ }
        override fun addToQueue(song: Song, nextUp: Boolean) { enqueued += song.id to nextUp }
    }

    private class FakeDao(
        val rows: List<SongListItem>,
        val top: List<PlayCountRow>,
    ) {
        var topPlayedLimit = -1

        val library: LibraryDao = Proxy.newProxyInstance(
            LibraryDao::class.java.classLoader,
            arrayOf(LibraryDao::class.java),
        ) { _, method, args ->
            when (method.name) {
                "localSongRows" -> rows
                "topPlayed" -> {
                    // suspend 函数的最后一个参数是 Continuation，limit 就是第 0 个；
                    // 这里真按 limit 截断，才像在 SQL 里那样验出"工具把参数传下去了"
                    val n = args?.firstOrNull() as? Int ?: -1
                    topPlayedLimit = n
                    top.take(n.coerceAtLeast(0))
                }
                else -> throw UnsupportedOperationException(method.name)
            }
        } as LibraryDao
    }

    private class Fixture(
        rows: List<SongListItem> = listOf(row(7, "春雪"), row(8, "偏爱")),
        top: List<PlayCountRow> = listOf(PlayCountRow(7, "春雪", "周深", 12), PlayCountRow(8, "偏爱", "张芸京", 3)),
    ) {
        val player = FakePlayer()
        val dao = FakeDao(rows, top)
        var ensured = 0
        val tools = MusicAgentTools(
            player = player,
            dao = dao.library,
            session = SessionLauncher { ensured++ },
            playlists = UnusedPlaylists,
            fetcher = FakeFetcher(mutableListOf()),
        )

        fun tool(name: String) = tools.all.first { it.name == name }
    }

    /**
     * 队列/统计工具都不碰歌单表 —— 共享替身的每个成员默认就抛，这里连覆写都不用写。
     * 以前这里是一份手抄的完整实现，`PlaylistDao` 每加一个方法就要来补一处。
     */
    private object UnusedPlaylists : FakePlaylistDao()

    @Test
    fun queueToolContractMatchesCSharp() {
        val f = Fixture()
        val add = f.tool("add_to_play_queue")
        assertEquals(listOf("song_id"), add.required)
        assertEquals(listOf("next", "end"), add.properties["position"]?.enumValues)
        assertTrue(!add.isReadOnly)
        val stats = f.tool("get_listening_stats")
        assertTrue("排行是只读的，要能和别的只读工具并行", stats.isReadOnly)
        assertEquals(emptyList<String>(), stats.required)
    }

    @Test
    fun addToQueueRejectsInvalidAndUnknownSongIds() = runTest {
        val f = Fixture()
        assertTrue(f.tool("add_to_play_queue").execute("""{"song_id":0}""").contains("请提供有效的歌曲 ID"))
        assertTrue(f.tool("add_to_play_queue").execute("""{"song_id":999}""").contains("未找到该歌曲"))
        assertTrue(f.player.enqueued.isEmpty())
    }

    @Test
    fun addToQueueDefaultsToNextUp() = runTest {
        val f = Fixture()
        val result = f.tool("add_to_play_queue").execute("""{"song_id":7}""")
        assertEquals(listOf(7 to true), f.player.enqueued)
        assertTrue(result.contains("已将「春雪」设为下一首播放"))
    }

    @Test
    fun addToQueueEndPositionSwitchesNextUpOff() = runTest {
        val f = Fixture()
        val result = f.tool("add_to_play_queue").execute("""{"song_id":8,"position":"end"}""")
        assertEquals(listOf(8 to false), f.player.enqueued)
        assertTrue(result.contains("已将「偏爱」添加到播放队列末尾"))
    }

    @Test
    fun statsDefaultToTopTenAndSumOnlyRankedSongs() = runTest {
        val f = Fixture()
        val result = f.tool("get_listening_stats").execute("{}")
        assertTrue(result.contains("\"top_n\":10"))
        // C# 的 total_plays 就是榜上几首之和（不是全库播放总数），这里保持同形
        assertTrue(result.contains("\"total_plays\":15"))
        assertTrue(result.contains("7|春雪|周深|12"))
    }

    @Test
    fun statsHonourTopN() = runTest {
        val f = Fixture()
        val result = f.tool("get_listening_stats").execute("""{"top_n":1}""")
        assertTrue(result.contains("\"top_n\":1"))
        assertTrue(result.contains("\"total_plays\":12"))
        assertEquals("limit 要真的传到查询里", 1, f.dao.topPlayedLimit)
    }
}

/** 队列/统计工具用到的最小曲库行 */
private fun row(id: Int, title: String) =
    SongListItem(id, title, "A", "AL", 100, "/m/$id.mp3", null, 0L, 0, 0)
