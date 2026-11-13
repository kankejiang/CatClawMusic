package com.catclaw.music.kt.data.library

import com.catclaw.music.kt.core.diagnostic.AppLog
import com.catclaw.music.kt.core.library.AudioTags
import com.catclaw.music.kt.core.library.MediaScanRules
import com.catclaw.music.kt.data.media.AudioHeadTags
import java.io.File
import javax.inject.Inject
import javax.inject.Singleton
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.async
import kotlinx.coroutines.awaitAll
import kotlinx.coroutines.coroutineScope

/**
 * 自研文件夹管理器配套的扫描器：**真实文件路径**遍历（不再走 SAF 树 URI）。
 *
 * 为什么弃用 SAF：
 * - SAF 只给 `content://…/tree/…` 文档 URI，改标签必须整文件重写，随机写/保留原文件元数据都不可控；
 * - 想修改歌曲元数据（写 ID3）必须拿到 `File`，于是文件夹选择也要回到真实路径。
 *
 * 需要 `MANAGE_EXTERNAL_STORAGE`（清单已声明），未授权时 [scan] 会返回空并打日志；
 * UI 负责先引导用户去系统设置里打开「所有文件访问」。
 *
 * 标签读取与 SAF 版同一套：先流式读 128KB 头（[AudioHeadTags]），失败再回退 MMR，按块并发。
 */
@Singleton
class FileMusicScanner @Inject constructor() {

    private val headBytes = 128 * 1024
    private val chunkSize = 8

    suspend fun scan(roots: List<String>): List<ScanTrack> = coroutineScope {
        val files = mutableListOf<File>()
        for (root in roots) {
            // 旧版本存的是 SAF 树 URI，这里直接跳过（UI 会提示移除）
            if (!root.startsWith("/")) continue
            val dir = runCatching { File(root) }.getOrNull() ?: continue
            if (!dir.isDirectory) {
                AppLog.w(TAG, "跳过不存在的文件夹 $root")
                continue
            }
            runCatching {
                dir.walkTopDown()
                    .filter { it.isFile && MediaScanRules.isAudioFile(it.name) }
                    .forEach { files += it }
            }.onFailure { AppLog.w(TAG, "遍历失败 $root: ${it.message}") }
        }
        files.chunked(chunkSize).map { chunk ->
            async(Dispatchers.IO) { chunk.map { toTrack(it) } }
        }.awaitAll().flatten()
    }

    private fun toTrack(file: File): ScanTrack {
        val tags = readTags(file)
        return ScanTrack(
            path = file.absolutePath,
            mediaStoreId = 0L,
            title = tags.title.ifBlank { MediaScanRules.titleFromFileName(file.name) },
            artistRaw = tags.artist,
            album = tags.album,
            durationMs = tags.durationMs,
            size = file.length(),
            bitrateKbps = tags.bitrateKbps,
            trackNumber = tags.trackNumber,
            year = tags.year,
            genre = tags.genre,
            dateModified = file.lastModified(),
        )
    }

    private fun readTags(file: File): AudioTags {
        val head = runCatching {
            file.inputStream().use { input ->
                val buffer = ByteArray(headBytes)
                var read = 0
                while (read < buffer.size) {
                    val n = input.read(buffer, read, buffer.size - read)
                    if (n <= 0) break
                    read += n
                }
                buffer.copyOf(read)
            }
        }.getOrNull()
        if (head != null && head.isNotEmpty()) {
            val fast = runCatching { AudioHeadTags.parse(head, file.length()) }.getOrNull()
            if (fast != null && (fast.title.isNotBlank() || fast.artist != null || fast.durationMs > 0)) {
                return fast
            }
        }
        return readTagsSlow(file)
    }

    /** 兜底：MMR 支持更全的容器（wav/ogg/aac），真实路径下可以直接 setDataSource(path) */
    private fun readTagsSlow(file: File): AudioTags {
        val retriever = android.media.MediaMetadataRetriever()
        return try {
            retriever.setDataSource(file.absolutePath)
            AudioTags(
                title = retriever.extractMetadata(android.media.MediaMetadataRetriever.METADATA_KEY_TITLE).orEmpty(),
                artist = retriever.extractMetadata(android.media.MediaMetadataRetriever.METADATA_KEY_ARTIST),
                album = retriever.extractMetadata(android.media.MediaMetadataRetriever.METADATA_KEY_ALBUM).orEmpty(),
                durationMs = retriever.extractMetadata(android.media.MediaMetadataRetriever.METADATA_KEY_DURATION)?.toLongOrNull() ?: 0L,
                bitrateKbps = MediaScanRules.bitrateKbps(
                    retriever.extractMetadata(android.media.MediaMetadataRetriever.METADATA_KEY_BITRATE)?.toIntOrNull() ?: 0,
                ),
                trackNumber = retriever.extractMetadata(android.media.MediaMetadataRetriever.METADATA_KEY_CD_TRACK_NUMBER)
                    ?.substringBefore('/')?.toIntOrNull() ?: 0,
                year = retriever.extractMetadata(android.media.MediaMetadataRetriever.METADATA_KEY_YEAR)?.take(4)?.toIntOrNull() ?: 0,
                genre = retriever.extractMetadata(android.media.MediaMetadataRetriever.METADATA_KEY_GENRE),
            )
        } catch (@Suppress("TooGenericExceptionCaught") e: Exception) {
            AudioTags()
        } finally {
            runCatching { retriever.release() }
        }
    }

    private companion object {
        const val TAG = "FileMusicScan"
    }
}
