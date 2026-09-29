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
 * 只取远程文件的前若干字节。补元数据的全部网络面都收在这个口子上，
 * 于是解析与落库能在 JVM 里拿假字节测到底。
 */
fun interface HeadBytesReader {
    /** @return 拿到的字节（可能少于 size，文件本来就短）；一个字节都拿不到回 null */
    suspend fun read(url: String, size: Int): ByteArray?
}

/**
 * HTTP Range 取头部：WebDAV 与 SMB 都走这里 —— SMB 那批行的 `FilePath` 存的本来就是
 * [com.catclaw.music.kt.data.smb.SmbStreamProxy] 的 127.0.0.1 回环地址，而它自己就实现了 Range。
 */
@Singleton
class HttpHeadBytesReader @Inject constructor() : HeadBytesReader {

    private val client by lazy {
        OkHttpClient.Builder()
            .connectTimeout(8, TimeUnit.SECONDS)
            .readTimeout(20, TimeUnit.SECONDS)
            .build()
    }

    override suspend fun read(url: String, size: Int): ByteArray? {
        if (size <= 0) return null
        return withContext(Dispatchers.IO) {
            try {
                client.newCall(authorizedRequest(url, rangeBytes = size)).execute().use { response ->
                    if (!response.isSuccessful) return@use null
                    val stream = response.body?.byteStream() ?: return@use null
                    // 服务器不理 Range 时会把整首发过来：自己只读 size 字节，绝不把整首歌灌进内存
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
            } catch (e: Exception) {
                null // 一首取不到不该打断整轮回填，调用方按「这一行没补上」处理
            }
        }
    }
}
