package com.catclaw.music.kt.core.playback

import com.catclaw.music.kt.core.model.Song
import org.junit.Assert.assertEquals
import org.junit.Assert.assertNull
import org.junit.Assert.assertSame
import org.junit.Test

/** 逐条对齐 C# Core/Services/PlayQueue.cs 的行为契约 */
class PlayQueueTest {

    private fun songs(n: Int) = (1..n).map { Song(id = it, title = "T$it", filePath = "/m/$it.mp3") }

    @Test
    fun defaultModeIsListRepeatAndWrapsToHead() {
        val q = PlayQueue().apply { setSongs(songs(3)) }
        assertEquals(PlayMode.ListRepeat, q.playMode)
        q.selectSong(3)
        assertEquals(1, q.next()?.id)
    }

    @Test
    fun sequentialStopsAtEnd() {
        val q = PlayQueue().apply {
            setSongs(songs(2))
            playMode = PlayMode.Sequential
        }
        q.selectSong(2)
        assertNull(q.next())
    }

    @Test
    fun singleRepeatKeepsSameSong() {
        val q = PlayQueue().apply {
            setSongs(songs(3))
            playMode = PlayMode.SingleRepeat
        }
        q.selectSong(2)
        assertEquals(2, q.next()?.id)
        assertEquals(2, q.next()?.id)
    }

    @Test
    fun previousWalksHistoryStackNotIndexMinusOne() {
        val q = PlayQueue().apply { setSongs(songs(5)) }
        q.selectSong(4)
        q.next() // → 5，历史里有 4
        q.selectSong(2) // → 历史 4,5
        assertEquals(2, q.currentSong?.id)
        assertEquals(5, q.previous()?.id)
        assertEquals(4, q.previous()?.id)
    }

    @Test
    fun enteringShufflePreservesCurrentlyPlayingSong() {
        val q = PlayQueue().apply { setSongs(songs(20)) }
        q.selectSong(7)
        q.playMode = PlayMode.Shuffle
        assertEquals(7, q.currentSong?.id)
    }

    @Test
    fun shuffleLoopsOverTheSameOrderUntilReshuffle() {
        val q = PlayQueue().apply {
            setSongs(songs(8))
            playMode = PlayMode.Shuffle
        }
        val firstPass = (0 until 8).map { q.next()?.id }
        val secondPass = (0 until 8).map { q.next()?.id }
        assertEquals("随机模式必须复用同一份洗牌序循环", firstPass, secondPass)
    }

    @Test
    fun enableShuffleRearrangesOrder() {
        // 30 首歌两次 Fisher-Yates 撞同序的概率约 1/30!，可视为确定性差异
        val q = PlayQueue().apply {
            setSongs(songs(30))
            playMode = PlayMode.Shuffle
        }
        val before = q.songs().map { it.id }
        q.enableShuffle()
        org.junit.Assert.assertNotEquals(before, q.songs().map { it.id })
    }

    @Test
    fun leavingShuffleRepositionsIntoOriginalOrder() {
        val q = PlayQueue().apply { setSongs(songs(10)) }
        q.selectSong(6)
        q.playMode = PlayMode.Shuffle
        q.playMode = PlayMode.ListRepeat
        assertEquals((1..10).toList(), q.songs().map { it.id })
        assertEquals(6, q.currentSong?.id)
        assertEquals(7, q.next()?.id)
    }

    @Test
    fun addNextInsertsRightAfterCurrent() {
        val q = PlayQueue().apply { setSongs(songs(3)) }
        q.selectSong(1)
        q.addNext(Song(id = 99, title = "N", filePath = "/m/99.mp3"))
        assertEquals(listOf(1, 99, 2, 3), q.songs().map { it.id })
        assertEquals(99, q.next()?.id)
    }

    @Test
    fun removingEarlierSongShiftsIndexAndRemovingCurrentAdvances() {
        val q = PlayQueue().apply { setSongs(songs(4)) }
        q.selectSong(3)
        q.removeSong(1)
        assertEquals(3, q.currentSong?.id)

        q.removeSong(3)
        assertEquals("移除当前曲后索引指向同位置的下一首", 4, q.currentSong?.id)
    }

    @Test
    fun restorePersistedKeepsSavedOrderAndIndex() {
        val saved = songs(5).shuffled()
        val q = PlayQueue()
        q.restorePersisted(saved, currentIndex = 2, mode = PlayMode.Shuffle)
        assertEquals(saved.map { it.id }, q.songs().map { it.id })
        assertEquals(saved[2].id, q.currentSong?.id)
    }

    @Test
    fun upcomingDoesNotMovePosition() {
        val q = PlayQueue().apply { setSongs(songs(4)) }
        q.selectSong(3)
        val before = q.currentSong
        assertEquals(listOf(4, 1, 2), q.upcomingSongs(3).map { it.id })
        assertSame(before, q.currentSong)
    }

    @Test
    fun emptyQueueIsHandled() {
        val q = PlayQueue()
        assertNull(q.next())
        assertNull(q.currentSong)
        q.setSongs(songs(3))
        assertEquals(3, q.songs().size)
    }
}
