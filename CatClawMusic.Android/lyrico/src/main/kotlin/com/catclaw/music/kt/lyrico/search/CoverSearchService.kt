package com.catclaw.music.kt.lyrico.search

import android.content.Context
import android.net.Uri
import android.os.ParcelFileDescriptor
import com.catclaw.music.kt.core.model.Song
import com.catclaw.music.kt.lyrico.search.LyricsTarget
import com.lonx.audiotag.model.AudioPicture
import com.lonx.audiotag.rw.AudioTagReader
import com.lonx.audiotag.rw.AudioTagWriter
import java.io.File
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.async
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
    private val perSourceTimeoutMs: Long = 35_000L,
) {

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

            val wrote = openReadWrite(path)?.use { pfd ->
                runCatching { AudioTagWriter.writePictures(pfd, listOf(picture)) }.getOrElse { false }
            } ?: return@withContext CoverWriteOutcome.Unsupported("无法打开该文件（只读或不存在）")

            if (!wrote) return@withContext CoverWriteOutcome.Failed("封面写入失败（格式不支持或文件只读）")

            // 回读校验：确认真的嵌进去了
            val readBack = openReadWrite(path)?.use { pfd ->
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
            CoverWriteOutcome.Success(bytes.size)
        }

    private fun openReadWrite(path: String): ParcelFileDescriptor? = runCatching {
        if (path.startsWith("content://")) {
            context.contentResolver.openFileDescriptor(Uri.parse(path), "rw")
        } else {
            val file = File(path)
            if (!file.isFile) return null
            ParcelFileDescriptor.open(file, ParcelFileDescriptor.MODE_READ_WRITE)
        }
    }.getOrNull()

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
