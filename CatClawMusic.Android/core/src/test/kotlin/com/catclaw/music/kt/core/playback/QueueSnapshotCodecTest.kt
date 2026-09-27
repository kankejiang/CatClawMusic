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
                Song(id = 8, title = "", filePath = "https://x/y?sign=1", source = SongSource.WebDAV),
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
}
