package com.catclaw.music.kt.core.playback

import com.catclaw.music.kt.core.model.Song
import com.catclaw.music.kt.core.model.SongSource
import org.junit.Assert.assertEquals
import org.junit.Assert.assertNull
import org.junit.Test

class QueueSnapshotCodecTest {

    private val store = object : KeyValueStore {
        val map = HashMap<String, String>()
        override fun put(key: String, value: String) {
            map[key] = value
        }

        override fun get(key: String): String? = map[key]
    }

    @Test
    fun roundTripsEveryFieldIncludingNastyStrings() {
        val snapshot = PlayQueue.Snapshot(
            songs = listOf(
                Song(
                    id = 7,
                    title = "有\t制表符\n和换行 \\ 反斜杠 中文名",
                    artist = "周杰伦 / 林俊杰",
                    album = "哎呦，不错哦",
                    durationSeconds = 251,
                    filePath = "content://media/external/audio/media/7",
                    source = SongSource.Local,
                    coverArtPath = null,
                ),
                Song(
                    id = 8,
                    title = "",
                    filePath = "https://x/y?sign=1",
                    source = SongSource.WebDAV,
                    remoteId = "netease:1234567",
                ),
            ),
            currentIndex = 1,
            mode = PlayMode.Shuffle,
        )

        val decoded = QueueSnapshotCodec.decode(QueueSnapshotCodec.encode(snapshot))

        assertEquals(2, decoded.songs.size)
        assertEquals(1, decoded.currentIndex)
        assertEquals(PlayMode.Shuffle, decoded.mode)
        assertEquals(snapshot.songs[0].title, decoded.songs[0].title)
        assertEquals(snapshot.songs[0].artist, decoded.songs[0].artist)
        assertEquals(251, decoded.songs[0].durationSeconds)
        assertEquals("content://media/external/audio/media/7", decoded.songs[0].filePath)
        assertEquals("https://x/y?sign=1", decoded.songs[1].filePath)
        assertEquals(SongSource.WebDAV, decoded.songs[1].source)
        // 在线曲的 remoteId 必须往返保留：丢了它，恢复队列后歌词/取链找不到原曲
        assertEquals("netease:1234567", decoded.songs[1].remoteId)
        assertNull(decoded.songs[0].coverArtPath)
    }

    @Test
    fun corruptOrForeignPayloadDegradesToEmpty() {
        assertEquals(0, QueueSnapshotCodec.decode("").songs.size)
        assertEquals(0, QueueSnapshotCodec.decode("not-a-queue\n1\tx").songs.size)
        assertEquals(-1, QueueSnapshotCodec.decode("catclaw-queue-v1\t-1\tListRepeat").currentIndex)
    }

    @Test
    fun restoreFromStoreKeepsShuffleOrder() {
        val q = PlayQueue().apply {
            setSongs((1..6).map { Song(id = it, title = "T$it", filePath = "/m/$it") })
            selectSong(3)
            playMode = PlayMode.Shuffle
        }
        val order = q.songs().map { it.id }
        store.put("k", QueueSnapshotCodec.encode(q.snapshot()))

        val restored = PlayQueue()
        val decoded = QueueSnapshotCodec.decode(store.get("k")!!)
        restored.restorePersisted(decoded.songs, decoded.currentIndex, decoded.mode)
        assertEquals(order, restored.songs().map { it.id })
        assertEquals(3, restored.currentSong?.id)
    }

    @Test
    fun `网易云在线曲快照不落过期直链`() {
        val online = Song(
            id = -123,
            title = "有形的翅膀",
            filePath = "https://m801.music.126.net/old-trial-30s.mp3",
            source = SongSource.WebDAV,
            remoteId = "netease:1366052804",
        )
        val local = Song(id = 7, title = "本地曲", filePath = "/storage/emulated/0/Music/a.mp3")
        val snapshot = PlayQueue.Snapshot(listOf(online, local), 0, PlayMode.ListRepeat)
        val decoded = QueueSnapshotCodec.decode(QueueSnapshotCodec.encode(snapshot)).songs
        // 恢复后必须是 outer 占位，PlayerController 才会实时取链
        org.junit.Assert.assertTrue(decoded[0].filePath.contains("outer/url?id=1366052804.mp3"))
        assertEquals("netease:1366052804", decoded[0].remoteId)
        // 本地曲不受影响
        assertEquals("/storage/emulated/0/Music/a.mp3", decoded[1].filePath)
    }

}
