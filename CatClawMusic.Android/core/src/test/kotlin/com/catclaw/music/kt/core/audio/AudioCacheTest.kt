package com.catclaw.music.kt.core.audio

import java.io.File
import java.nio.file.Files
import kotlinx.coroutines.async
import kotlinx.coroutines.awaitAll
import kotlinx.coroutines.test.runTest
import org.junit.Assert.assertEquals
import org.junit.Assert.assertNotNull
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Test

class AudioCacheTest {

    private fun tempDir(): File = Files.createTempDirectory("ccache").toFile()

    /** 记录每个 URL 被真正拉取了几次 */
    private class CountingFetcher : AudioCache.Fetcher {
        val calls = HashMap<String, Int>()
        override suspend fun fetch(url: String, target: File) {
            calls[url] = (calls[url] ?: 0) + 1
            Thread.sleep(30)
            target.writeBytes(ByteArray(1024) { 1 })
        }
    }

    @Test
    fun cacheKeyMatchesCsharpShape() {
        val cache = AudioCache(tempDir())
        val key = cache.cacheKey("https://host/a/b/song.mp3?sign=xyz&exp=1")
        assertEquals("SHA256 前 16 位 + 原扩展名", 16 + 4, key.length)
        assertTrue(key.substring(0, 16).matches(Regex("[0-9a-f]{16}")))
        assertEquals(".mp3", key.substring(16))
        assertEquals(".bin", cache.cacheKey("https://host/noext").substring(16))
        assertEquals(".bin", cache.cacheKey("https://host/toolongextension.abcdefghijk").substring(16))
    }

    @Test
    fun singleFlightDownloadsOnce() = runTest {
        val fetcher = CountingFetcher()
        val cache = AudioCache(tempDir(), fetcher = fetcher)
        val url = "https://h/x/track.flac"

        val files = (1..10).map { async { cache.getOrCreate(url) } }.awaitAll()

        assertEquals("并发同一 URL 只应下载一次", 1, fetcher.calls[url])
        assertEquals(1, files.map { it.absolutePath }.distinct().size)
        assertTrue(files.first().exists())
    }

    @Test
    fun commitsAtomicallyAndLeavesNoPartFile() = runTest {
        val dir = tempDir()
        val cache = AudioCache(dir, fetcher = CountingFetcher())
        val f = cache.getOrCreate("https://h/a.wav")
        assertTrue(f.exists())
        assertEquals("不应留下 .part 临时文件", 0, dir.listFiles { _, n -> n.endsWith(".part") }!!.size)
    }

    @Test
    fun hitRefreshesForLruAndEvictionKeepsNewest() = runTest {
        val dir = tempDir()
        // 上限 100 MB 但用极小值触发淘汰：maxSizeMb 会被夹到 100，故这里直接压低字节数
        val cache = AudioCache(dir, fetcher = CountingFetcher())
        cache.maxSizeBytes = 2500L
        val a = cache.getOrCreate("https://h/1.mp3")
        val b = cache.getOrCreate("https://h/2.mp3")
        Thread.sleep(20)
        // 触碰 a 使其成为最近使用，随后写入 c 应淘汰 b
        assertNotNull(cache.cachedFile("https://h/1.mp3"))
        val c = cache.getOrCreate("https://h/3.mp3")

        assertTrue("新写入的文件必须在", c.exists())
        assertTrue("被触碰过的文件必须在", a.exists())
        assertTrue("最久未用的应被淘汰", !b.exists())
        assertTrue(cache.totalBytes() <= 2500L + 1024)
    }

    @Test
    fun clearEmptiesDirectory() = runTest {
        val dir = tempDir()
        val cache = AudioCache(dir, fetcher = CountingFetcher())
        cache.getOrCreate("https://h/z.mp3")
        cache.clear()
        assertEquals(0L, cache.totalBytes())
        assertNull(cache.cachedFile("https://h/z.mp3"))
    }

    @Test
    fun sizeLimitIsClampedToAllowedRange() {
        assertEquals(100, AudioCache.clampMb(1))
        assertEquals(15000, AudioCache.clampMb(999_999))
        assertEquals(500, AudioCache.DEFAULT_CACHE_SIZE_MB)
        assertEquals(10, AudioCache.PRE_BUFFER_SECONDS)
    }
}
