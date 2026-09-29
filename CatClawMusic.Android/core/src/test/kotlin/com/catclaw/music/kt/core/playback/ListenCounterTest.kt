package com.catclaw.music.kt.core.playback

import org.junit.Assert.assertEquals
import org.junit.Assert.assertNull
import org.junit.Test

/**
 * 「一次聆听」的判定。
 *
 * 这条规则两个失败方向都不报错：挂太松 → 暂停/恢复把计数刷爆（C# 侧真出过这事故，
 * 事后写了 `RecalibratePlayCountsAsync` 来校正）；挂太紧 → 自动切歌全漏记。
 * 所以钉死在这里。
 */
class ListenCounterTest {

    @Test
    fun countsOnceWhenASongActuallyStarts() {
        val c = ListenCounter()
        assertEquals(7, c.onPublish(songId = 7, playing = true))
        assertNull("同一首继续播，后续的 publish 不再算新聆听", c.onPublish(songId = 7, playing = true))
    }

    @Test
    fun pauseAndResumeOfTheSameSongIsNotANewListen() {
        val c = ListenCounter()
        assertEquals(7, c.onPublish(7, playing = true))
        assertNull("暂停不算", c.onPublish(7, playing = false))
        assertNull("恢复同一首也不算", c.onPublish(7, playing = true))
    }

    @Test
    fun switchingSongsCountsAgain() {
        val c = ListenCounter()
        assertEquals(7, c.onPublish(7, playing = true))
        assertEquals(8, c.onPublish(8, playing = true))
    }

    @Test
    fun nothingCountsWhileIdleOrWithAnEmptyQueue() {
        val c = ListenCounter()
        assertNull("队列空", c.onPublish(0, playing = true))
        assertNull("没在播", c.onPublish(9, playing = false))
        assertEquals(9, c.onPublish(9, playing = true))
    }

    @Test
    fun replayingTheSameSongAfterTheQueueIsClearedCountsAgain() {
        val c = ListenCounter()
        assertEquals(9, c.onPublish(9, playing = true))
        c.onQueueCleared()
        assertEquals(9, c.onPublish(9, playing = true))
    }
}
