package com.catclaw.music.kt.core.audio

import java.io.File
import java.security.MessageDigest
import java.util.concurrent.ConcurrentHashMap
import kotlinx.coroutines.CoroutineDispatcher
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.sync.Mutex
import kotlinx.coroutines.sync.withLock
import kotlinx.coroutines.withContext

/**
 * 网络音频本地缓存，行为对齐 C# Maui/Services/AudioCacheService.cs：
 * - 键 = URL 的 SHA256；目录 = cacheDir/music_cache
 * - 流式写 `.part` 临时文件，成功后**原子改名**提交（全程不把整首歌驻留内存）
 * - single-flight：同一 URL 并发请求只下载一次（`getOrAdd` 原子占位）
 * - 超上限按 LRU（最后访问时间最早）淘汰；容量夹在 100–15000 MB
 *
 * 下载动作通过 [Fetcher] 注入，便于单测覆盖键/淘汰/原子改名/single-flight。
 */
class AudioCache(
    private val dir: File,
    private val fetcher: Fetcher = UrlFetcher,
    private val ioDispatcher: CoroutineDispatcher = Dispatchers.IO,
    maxSizeMb: Int = DEFAULT_CACHE_SIZE_MB,
) {
    private val inFlight = ConcurrentHashMap<String, Mutex>()

    var maxSizeBytes: Long = clampMb(maxSizeMb).toLong() * 1024L * 1024L
        set(value) {
            field = value
            evictIfNeeded()
        }

    fun interface Fetcher {
        /** 把 url 的内容写入 [target]（已存在的字节数可作为 Range 续传起点）；返回异常表示失败 */
        suspend fun fetch(url: String, target: File)
    }

    init {
        dir.mkdirs()
    }

    /** 键与 C# AudioCacheService.GetCachePath 一致：SHA256 前 16 位十六进制 + 原扩展名（无/过长则 `.bin`） */
    fun cacheKey(url: String): String = MessageDigest.getInstance("SHA-256")
        .digest(url.toByteArray())
        .joinToString("") { "%02x".format(it) }
        .substring(0, 16) + extensionOf(url)

    fun cachedFile(url: String): File? {
        val f = File(dir, cacheKey(url))
        // 命中即刷新修改时间，作为 LRU 的访问依据（Android 上 atime 不可靠）
        if (f.exists()) f.setLastModified(System.currentTimeMillis())
        return if (f.exists()) f else null
    }

    /** 取本地缓存，没有就下载。同一 URL 的并发调用只会真正下载一次。 */
    suspend fun getOrCreate(url: String): File {
        cachedFile(url)?.let { return it }
        val key = cacheKey(url)
        val mutex = inFlight.computeIfAbsent(key) { Mutex() }
        return mutex.withLock {
            try {
                val part = File(dir, "$key.part")
                val target = File(dir, key)
                if (target.exists()) {
                    target.setLastModified(System.currentTimeMillis())
                    return@withLock target
                }
                withContext(ioDispatcher) { fetcher.fetch(url, part) }
                // 原子提交：覆盖旧缓存时旧文件已被同长度替换，不会虚高统计
                if (!part.renameTo(target)) {
                    part.copyTo(target, overwrite = true)
                    part.delete()
                }
                evictIfNeeded()
                target
            } finally {
                inFlight.remove(key)
            }
        }
    }

    fun evictIfNeeded() {
        val files = dir.listFiles()?.filter { it.isFile && !it.name.endsWith(".part") } ?: return
        var total = files.sumOf { it.length() }
        if (total <= maxSizeBytes) return
        files.sortedBy { it.lastModified() }.forEach { f ->
            if (total <= maxSizeBytes) return
            val len = f.length()
            if (f.delete()) total -= len
        }
    }

    fun totalBytes(): Long = dir.listFiles()?.filter { it.isFile }?.sumOf { it.length() } ?: 0L

    fun clear() {
        dir.deleteRecursively()
        dir.mkdirs()
    }

    companion object {
        const val DEFAULT_CACHE_SIZE_MB = 500
        const val PRE_BUFFER_SECONDS = 10

        fun clampMb(mb: Int): Int = mb.coerceIn(100, 15000)

        private fun extensionOf(url: String): String {
            val path = url.substringBefore('?').substringBefore('#')
            val dot = path.lastIndexOf('.')
            if (dot < 0 || dot < path.lastIndexOf('/')) return BIN
            val ext = path.substring(dot + 1).lowercase()
            return if (ext.length in 1..10 && ext.all { it.isLetterOrDigit() }) ".$ext" else BIN
        }

        private const val BIN = ".bin"
    }
}

/** 默认实现：HttpURLConnection + Range 续传（`.part` 已有长度作为起点）。 */
object UrlFetcher : AudioCache.Fetcher {
    override suspend fun fetch(url: String, target: File) {
        val existing = if (target.exists()) target.length() else 0L
        val conn = (java.net.URL(url).openConnection() as java.net.HttpURLConnection).apply {
            connectTimeout = 15_000
            readTimeout = 30_000
            instanceFollowRedirects = true
            if (existing > 0) setRequestProperty("Range", "bytes=$existing-")
        }
        try {
            val status = conn.responseCode
            if (status !in 200..299) throw java.io.IOException("HTTP $status for $url")
            val resumeAt = if (status == 206 && existing > 0) existing else {
                target.delete()
                0L
            }
            conn.inputStream.use { input ->
                java.io.RandomAccessFile(target, "rw").use { raf ->
                    raf.seek(resumeAt)
                    val buffer = ByteArray(64 * 1024)
                    while (true) {
                        val read = input.read(buffer)
                        if (read <= 0) break
                        raf.write(buffer, 0, read)
                    }
                }
            }
        } finally {
            conn.disconnect()
        }
    }
}
