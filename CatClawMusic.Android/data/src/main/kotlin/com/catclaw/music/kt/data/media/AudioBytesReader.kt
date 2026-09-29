package com.catclaw.music.kt.data.media

import com.catclaw.music.kt.data.remote.authorizedRequest
import java.util.concurrent.TimeUnit
import javax.inject.Inject
import javax.inject.Singleton
import kotlin.coroutines.cancellation.CancellationException
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.withContext
import okhttp3.OkHttpClient

/**
 * 按窗口取远程文件的字节。远程补元数据的全部网络面都收在这个口子上，
 * 于是解析、合并、落库能在 JVM 里拿假字节测到底。
 */
interface AudioBytesReader {

    /** 前 [size] 个字节；一个字节都拿不到回 null */
    suspend fun head(url: String, size: Int): ByteArray?

    /**
     * 末尾 [bytes] 个字节（[fileSize] 是已知文件大小）。
     *
     * 只有非 faststart 的 M4A 需要它：那一族的 `moov`（全部标签 + 时长）在文件末尾，
     * 头部窗口再大也读不到。默认回 null 表示"这条通道不取尾"，调用方据此跳过第二次请求。
     */
    suspend fun tail(url: String, bytes: Long, fileSize: Long): ByteArray? = null
}

/**
 * HTTP Range 取窗口：WebDAV 与 SMB 都走这里 —— SMB 那批行的 `FilePath` 存的本来就是
 * [com.catclaw.music.kt.data.smb.SmbStreamProxy] 的 127.0.0.1 回环地址，而它自己就实现了 Range。
 */
@Singleton
class HttpAudioBytesReader @Inject constructor() : AudioBytesReader {

    private val client by lazy {
        OkHttpClient.Builder()
            .connectTimeout(8, TimeUnit.SECONDS)
            .readTimeout(20, TimeUnit.SECONDS)
            .build()
    }

    override suspend fun head(url: String, size: Int): ByteArray? = readRange(url, 0L, size)

    override suspend fun tail(url: String, bytes: Long, fileSize: Long): ByteArray? {
        if (fileSize <= 0 || bytes <= 0) return null
        val take = minOf(bytes, fileSize)
        return readRange(url, fileSize - take, take.toInt())
    }

    private suspend fun readRange(url: String, offset: Long, size: Int): ByteArray? {
        if (size <= 0) return null
        return withContext(Dispatchers.IO) {
            try {
                val request = authorizedRequest(url).newBuilder()
                    .header("Range", "bytes=$offset-${offset + size - 1}")
                    .build()
                client.newCall(request).execute().use { response ->
                    if (!response.isSuccessful) return@use null
                    val stream = response.body?.byteStream() ?: return@use null
                    // 服务器不理 Range 时会从 0 开始把整首发过来：读够 size 字节就停，绝不把整首歌灌进内存
                    val buffer = ByteArray(size)
                    var filled = 0
                    while (filled < size) {
                        val n = stream.read(buffer, filled, size - filled)
                        if (n <= 0) break
                        filled += n
                    }
                    if (filled == 0) null else buffer.copyOf(filled)
                }
            } catch (e: CancellationException) {
                throw e
            } catch (@Suppress("TooGenericExceptionCaught") e: Exception) {
                null // 一首取不到不该打断整轮回填，调用方按「这一行没补上」处理
            }
        }
    }
}
