package com.catclaw.music.kt.core.download

import java.io.File
import java.io.IOException
import java.io.InputStream
import java.net.HttpURLConnection
import com.catclaw.music.kt.core.net.UrlCredentials
import java.net.URL
import java.util.concurrent.Semaphore
import kotlinx.coroutines.currentCoroutineContext
import kotlinx.coroutines.ensureActive
import kotlinx.coroutines.withContext
import kotlinx.coroutines.Dispatchers

/** 一次取流的结果：状态码、总字节数（未知为 -1）、响应体 */
data class DownloadStream(val statusCode: Int, val totalBytes: Long, val body: InputStream)

/** 可注入的取流口子：单测里换成假服务器，真机上走 [UrlRangeTransport] */
fun interface DownloadTransport {
    @Throws(IOException::class)
    fun open(url: String, rangeFrom: Long): DownloadStream
}

/** Range 续传的真机实现。GET 是标准方法，HttpURLConnection 够用（不像 PROPFIND 那样被拒） */
object UrlRangeTransport : DownloadTransport {
    override fun open(url: String, rangeFrom: Long): DownloadStream {
        val (cleanUrl, authorization) = UrlCredentials.split(url)
        val conn = (URL(cleanUrl).openConnection() as HttpURLConnection).apply {
            connectTimeout = 15_000
            readTimeout = 30_000
            instanceFollowRedirects = true
            requestMethod = "GET"
            if (rangeFrom > 0) setRequestProperty("Range", "bytes=$rangeFrom-")
            authorization?.let { setRequestProperty("Authorization", it) }
        }
        val status = conn.responseCode
        if (status !in 200..299) {
            // 非 2xx 不去碰 inputStream（JDK 对 4xx 直接抛 FileNotFoundException），
            // 让调用方按状态码给出明确错误
            return DownloadStream(status, -1L, java.io.ByteArrayInputStream(ByteArray(0)))
        }
        val total = if (status == 206) {
            // Content-Range: bytes 100-299/300 → 取 '/' 后面的全长
            conn.getHeaderField("Content-Range")?.substringAfterLast('/', "")?.toLongOrNull()
                ?: conn.contentLengthLong.coerceAtLeast(rangeFrom)
        } else {
            conn.contentLengthLong
        }
        return DownloadStream(status, total, conn.inputStream)
    }
}

/**
 * 断点续传下载器，语义逐条对齐 C# `DownloadManager.DownloadFromUrlAsync`：
 * 临时文件 `<LocalPath>.part`；有 `.part` 就带 `Range: bytes=<len>-` 续；
 * 206 → 追加写，非 206（服务器不认 Range）→ 删掉 `.part` 从头下；
 * 取消/出错时**保留** `.part` 供下次续传，只有正常完成才改名落盘。
 */
class RangeDownloader(
    private val directory: File,
    private val transport: DownloadTransport = UrlRangeTransport,
    private val concurrentLimit: Int = DEFAULT_CONCURRENT,
) {
    data class Outcome(val file: File, val totalBytes: Long, val resumedFrom: Long)

    init {
        require(concurrentLimit in MIN_CONCURRENT..MAX_CONCURRENT) { "并发数要在 $MIN_CONCURRENT..$MAX_CONCURRENT 内" }
    }

    private val slots = Semaphore(concurrentLimit)

    fun partFile(fileName: String): File = File(directory, "$fileName.part")

    fun targetFile(fileName: String): File = File(directory, fileName)

    fun discardPart(fileName: String) {
        runCatching { partFile(fileName).delete() }
    }

    /** 批量下载：并发受 [concurrentLimit] 限制，逐个失败不影响其余 */
    suspend fun downloadAll(requests: List<Pair<String, String>>): List<Result<Outcome>> =
        requests.map { (name, url) ->
            slots.acquire()
            try {
                Result.runCatching { download(name, url) }
            } finally {
                slots.release()
            }
        }

    suspend fun download(
        fileName: String,
        url: String,
        onProgress: (downloaded: Long, total: Long) -> Unit = { _, _ -> },
    ): Outcome = withContext(Dispatchers.IO) {
        if (!directory.exists()) directory.mkdirs()
        val part = partFile(fileName)
        var resumeFrom = if (part.exists()) part.length().coerceAtLeast(0L) else 0L
        if (resumeFrom <= 0) runCatching { part.delete() }

        val stream = transport.open(url, resumeFrom)

        val resumed: Boolean
        val total: Long
        if (stream.statusCode == 206 && resumeFrom > 0) {
            resumed = true
            total = if (stream.totalBytes > 0) stream.totalBytes else resumeFrom
        } else {
            // 服务器不支持 Range：从头下，遗留的 .part 必须先清掉，否则拼出坏文件
            runCatching { part.delete() }
            resumed = false
            total = stream.totalBytes
        }
        if (stream.statusCode !in 200..299) {
            stream.body.close()
            throw IOException("HTTP ${stream.statusCode}")
        }

        var written = if (resumed) resumeFrom else 0L
        try {
        // 续传时必须以追加方式打开：用 outputStream()（截断模式）会把已下的段落覆盖掉
        java.io.FileOutputStream(part, resumed).use { out ->
                val buffer = ByteArray(BUFFER_BYTES)
                var lastReportAt = 0L
                while (true) {
                    // 取消/暂停时直接抛出，.part 原样留着，下次接着传
                    currentCoroutineContext().ensureActive()
                    val read = stream.body.read(buffer)
                    if (read <= 0) break
                    out.write(buffer, 0, read)
                    written += read
                    val now = System.currentTimeMillis()
                    if (now - lastReportAt >= PROGRESS_INTERVAL_MS) {
                        lastReportAt = now
                        onProgress(written, total)
                    }
                }
            }
        } catch (e: IOException) {
            onProgress(written, total)
            throw e
        } finally {
            runCatching { stream.body.close() }
        }

        val expected = total
        if (expected > 0 && written != expected) {
            // 服务器/代理提前干净地收尾时，光看 EOF 会把截断的文件当成功落盘；
            // 这里按总长校验，不足就保留 .part 等下次续传
            onProgress(written, expected)
            throw IOException("下载不完整: $written/$expected")
        }

        val target = targetFile(fileName)
        if (target.exists() && !target.delete()) throw IOException("无法覆盖已存在文件: ${target.name}")
        if (!part.renameTo(target)) throw IOException("临时文件落盘失败")
        onProgress(target.length(), total.coerceAtLeast(target.length()))
        Outcome(target, total.coerceAtLeast(target.length()), if (resumed) resumeFrom else 0L)
    }

    companion object {
        const val DEFAULT_CONCURRENT = 2
        const val MIN_CONCURRENT = 1
        const val MAX_CONCURRENT = 5
        private const val BUFFER_BYTES = 81_920
        private const val PROGRESS_INTERVAL_MS = 500L
    }
}
