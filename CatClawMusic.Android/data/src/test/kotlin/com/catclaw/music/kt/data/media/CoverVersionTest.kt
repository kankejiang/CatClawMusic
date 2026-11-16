package com.catclaw.music.kt.data.media

import org.junit.Assert.assertEquals
import org.junit.Test

/**
 * 封面版本号的语义测试。
 *
 * 这个类本身只有「递增」一个行为，但它承载的是一个**曾被漏掉的正确性要求**：
 * 封面补全只写数据库，而队列页/播放页读的是播放器快照 —— 快照里的 `coverArtPath`
 * 在补全前后完全一样，`produceState` 的 key 不变就不会重新加载。没有这个版本号，
 * 用户看到的就是「图已经存好了，界面还是首字母，要杀掉重进才显示」。
 */
class CoverVersionTest {

    @Test
    fun `初始版本是 0`() {
        assertEquals(0, CoverVersion().version.value)
    }

    @Test
    fun `每次 bump 递增 1`() {
        val v = CoverVersion()
        v.bump()
        assertEquals(1, v.version.value)
        v.bump()
        assertEquals(2, v.version.value)
    }

    @Test
    fun `bump 后的值能被观察到（StateFlow 语义）`() {
        val v = CoverVersion()
        val seen = mutableListOf<Int>()
        seen += v.version.value
        v.bump()
        seen += v.version.value
        v.bump()
        seen += v.version.value
        assertEquals(listOf(0, 1, 2), seen)
    }
}
