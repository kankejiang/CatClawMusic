package com.catclaw.music.kt.lyrico.search

import android.content.Context
import android.net.Uri
import android.os.ParcelFileDescriptor
import com.catclaw.music.kt.core.model.Song
import com.catclaw.music.kt.lyrico.search.LyricsTarget
import com.lonx.audiotag.model.AudioPicture
import com.lonx.audiotag.rw.AudioTagReader
import com.catclaw.music.kt.lyrico.write.SafeTagWriter
import com.lonx.audiotag.rw.AudioTagWriter
import java.io.File
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.async
import kotlinx.coroutines.flow.channelFlow
import kotlinx.coroutines.launch
import kotlinx.coroutines.coroutineScope
import kotlinx.coroutines.withTimeoutOrNull
import kotlinx.coroutines.withContext
import okhttp3.OkHttpClient
import okhttp3.Request

/** 一个封面候选。 */
data class CoverCandidate(
    val sourceId: String,
    val sourceName: String,
    val url: String,
    val title: String = "",
    val artist: String = "",
    val album: String = "",
    val score: Double = 0.0,
)

/** 封面写入结果。 */
sealed interface CoverWriteOutcome {
    data class Success(val bytes: Int) : CoverWriteOutcome
    data class Unsupported(val reason: String) : CoverWriteOutcome
    data class Failed(val message: String) : CoverWriteOutcome
}

/**
 * 封面搜索与写回。
 *
 * 搜索：复用插件的 `searchSongs`（返回的歌曲记录自带 `picUrl`），
 * 这样一次请求既能补歌词也能补封面，不用额外接口。
 *
 * 写回：下载图片 → 写进音频内嵌封面（Front Cover）→ 回读校验。
 * 与歌词一致，只支持本地/可写路径，远程曲目明确拒绝。
 */
class CoverSearchService(
    private val okHttpClient: OkHttpClient,
    private val pluginSources: LyricoPluginSources? = null,

    /** 单源超时：与歌词搜索一致，12s。超过基本是卡住了，继续等只会拖累用户 */
    private val perSourceTimeoutMs: Long = 12_000L,
) {

    /**
     * 封面搜索结果流：谁先回来先给谁。
     *
     * 与歌词搜索同一个理由：`awaitAll` 要等所有源（含卡住的），
     * 用户明明 10 秒就能看到封面，却要等 30 秒。
     */
    /**
     * 封面搜索结果流：谁先回来先给谁。
     *
     * 与歌词搜索同源的问题：第一版用 `flow{}` + 手写 Channel 做信号却忘了 close，
     * `for` 循环永远挂着，第一个源之后就没有第二次 emit 了。
     * 改用 `channelFlow`：子协程结束自动关闭 channel，不会漏。
     */
    fun searchStream(target: LyricsTarget): kotlinx.coroutines.flow.Flow<List<CoverCandidate>> =
        kotlinx.coroutines.flow.channelFlow {
            val pluginIds = pluginSources?.availablePlugins()?.map { it.id }.orEmpty()
            if (pluginIds.isEmpty()) return@channelFlow
            val collected = java.util.Collections.synchronizedList(mutableListOf<CoverCandidate>())
            pluginIds.forEach { pluginId ->
                // 同歌词搜索：必须显式切 IO，否则继承主线程上下文。
                launch(Dispatchers.IO) {
                    val items = runCatching {
                        withTimeoutOrNull(perSourceTimeoutMs) {
                            pluginSources!!.searchCovers(pluginId, target)
                        } ?: emptyList()
                    }.getOrElse { emptyList() }
                    if (items.isNotEmpty()) {
                        synchronized(collected) { collected.addAll(items) }
                        send(collected.toList().distinctBy { it.url }.sortedByDescending { it.score })
                    }
                }
            }
        }

    suspend fun search(target: LyricsTarget): List<CoverCandidate> = withContext(Dispatchers.IO) {
        val pluginIds = pluginSources?.availablePlugins()?.map { it.id }.orEmpty()
        val all = coroutineScope {
            pluginIds.map { pluginId ->
                async {
                    runCatching {
                        withTimeoutOrNull(perSourceTimeoutMs) {
                            pluginSources!!.searchCovers(pluginId, target)
                        } ?: emptyList()
                    }.getOrElse { emptyList() }
                }
            }.map { it.await() }.flatten()
        }
        // 同 URL 去重；不同源给了同一张图很常见
        all.distinctBy { it.url }
            .sortedByDescending { it.score }
    }

    suspend fun download(url: String): ByteArray? = withContext(Dispatchers.IO) {
        runCatching {
            val request = Request.Builder().url(url).header("User-Agent", USER_AGENT).build()
            okHttpClient.newCall(request).execute().use { resp ->
                if (!resp.isSuccessful) null else resp.body?.bytes()
            }
        }.getOrNull()
    }

    companion object {
        private const val USER_AGENT = "CatClawMusic/1.0"
    }
}

/** 把封面写进音频内嵌标签。 */
class CoverWriter(
    /**
     * 封面写入后要 bump 版本号，让所有显示封面的地方（播放页大图、列表行、
     * 雾面动态背景、通知栏 artwork）重新加载。不 bump 就只能杀进程重进。
     */
    private val coverVersion: com.catclaw.music.kt.data.media.CoverVersion,
    private val context: Context,
    private val coverSearch: CoverSearchService,
) {

    suspend fun write(song: Song, candidate: CoverCandidate): CoverWriteOutcome =
        withContext(Dispatchers.IO) {
            val path = song.filePath
            if (path.isBlank()) return@withContext CoverWriteOutcome.Unsupported("歌曲没有文件路径")
            val lower = path.lowercase()
            if (lower.startsWith("http://") || lower.startsWith("https://") ||
                lower.startsWith("smb://") || lower.startsWith("\\")
            ) {
                return@withContext CoverWriteOutcome.Unsupported("远程歌曲不支持写入封面")
            }

            val bytes = coverSearch.download(candidate.url)
                ?: return@withContext CoverWriteOutcome.Failed("封面下载失败")
            if (bytes.isEmpty()) return@withContext CoverWriteOutcome.Failed("封面内容为空")

            val mime = sniffImageMime(bytes) ?: "image/jpeg"
            val picture = AudioPicture(
                data = bytes,
                mimeType = mime,
                description = "CatClaw cover",
                pictureType = "Front Cover",
            )

            // 安全写入：先写临时副本再原子替换。
            // 直接改原文件会与正在播放的 ExoPlayer 抢同一个 inode，
            // 实测会导致播放卡死（需要切歌才恢复）。
            val wrote = SafeTagWriter.writeSafely(context, path) { pfd: ParcelFileDescriptor ->
                runCatching { AudioTagWriter.writePictures(pfd, listOf(picture)) }.getOrElse { false }
            }

            if (!wrote) return@withContext CoverWriteOutcome.Failed("封面写入失败（格式不支持或文件只读）")

            // 回读校验：确认真的嵌进去了
            val readBack: ByteArray? = SafeTagWriter.readSafely(context, path) { pfd: ParcelFileDescriptor ->
                runCatching {
                    AudioTagReader.readPicture(
                        pfd = pfd,
                        pictureType = com.lonx.audiotag.model.AudioPictureType.FrontCover,
                    )
                }.getOrNull()
            }
            if (readBack == null || readBack.isEmpty()) {
                return@withContext CoverWriteOutcome.Failed("写入后读回封面失败")
            }
            // 要告知所有展示封面的地方（播放页大图/列表行/雾面背景/通知栏）
            // 封面已变。项目用全局版本号做这件事，不 bump 就只能杀进程重进才看到新图。
            coverVersion.bump()
            CoverWriteOutcome.Success(bytes.size)
        }

    /** 只认常见图片头；识别不出就按 jpeg 处理（taglib 多数容器不校验） */
    private fun sniffImageMime(bytes: ByteArray): String? {
        if (bytes.size < 12) return null
        return when {
            bytes[0] == 0xFF.toByte() && bytes[1] == 0xD8.toByte() -> "image/jpeg"
            bytes[0] == 0x89.toByte() && bytes[1] == 0x50.toByte() -> "image/png"
            bytes[0] == 'G'.code.toByte() && bytes[1] == 'I'.code.toByte() -> "image/gif"
            bytes[0] == 'R'.code.toByte() && bytes[1] == 'I'.code.toByte() -> "image/webp"
            else -> null
        }
    }
}
