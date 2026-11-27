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

    /**
     * 没能写进音频标签（典型：WAV/AIFF 没有 ID3v2 块，TagLib 的 `tag()` 是 TagUnion，
     * 对 PICTURE 复杂属性只能落到默认实现返回 false），退而落盘成应用内封面。
     * 应用内所有封面位与通知栏 artwork 都读 [Song.coverArtPath]，效果与内嵌一致。
     */
    data class AppOnly(val bytes: Int, val reason: String) : CoverWriteOutcome
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

/** 把封面写进音频内嵌标签；写不进（WAV/AIFF 等）就落盘成应用内封面。 */
class CoverWriter(
    /**
     * 封面写入后要 bump 版本号，让所有显示封面的地方（播放页大图、列表行、
     * 雾面动态背景、通知栏 artwork）重新加载。不 bump 就只能杀进程重进。
     */
    private val coverVersion: com.catclaw.music.kt.data.media.CoverVersion,
    private val context: Context,
    private val coverSearch: CoverSearchService,
    private val dao: com.catclaw.music.kt.data.db.LibraryDao,
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

            // WAV/AIFF 这类容器在 TagLib 里 `tag()` 是 TagUnion，PICTURE 这个复杂属性
            // 没有实现（默认返回 false），而 JNI 又只把 file->save() 的结果当成功，
            // 所以「写了但没写进去」。再加上 SafeTagWriter 会整份复制源文件
            // （75MB 的 WAV 就是 75MB 无谓拷贝），不如直接落盘成应用内封面。
            // 本地曲的 FilePath 是 content:// URI，格式判定不能看后缀，统一走 CoverFormats。
            if (com.catclaw.music.kt.data.media.CoverFormats.isRiffId3Format(context, path)) {
                return@withContext saveAppCover(song.id, bytes, mime, "该格式不支持内嵌封面")
                    ?: CoverWriteOutcome.Failed("保存应用内封面失败")
            }

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

            // 回读校验：确认真嵌进去了（`wrote` 只代表标签保存成功，不代表图进去了）
            val readBack: ByteArray? = if (wrote) {
                SafeTagWriter.readSafely(context, path) { pfd: ParcelFileDescriptor ->
                    runCatching {
                        AudioTagReader.readPicture(
                            pfd = pfd,
                            pictureType = com.lonx.audiotag.model.AudioPictureType.FrontCover,
                        )
                    }.getOrNull()
                }
            } else {
                null
            }
            if (readBack != null && readBack.isNotEmpty()) {
                // 要告知所有展示封面的地方（播放页大图/列表行/雾面背景/通知栏）
                // 封面已变。项目用全局版本号做这件事，不 bump 就只能杀进程重进才看到新图。
                coverVersion.bump()
                return@withContext CoverWriteOutcome.Success(bytes.size)
            }

            // 其他格式也可能因文件只读等原因写不进去：同样退到应用内封面，至少界面能立刻用上
            val reason = if (wrote) "音频标签不支持内嵌封面" else "文件写不进音频标签"
            saveAppCover(song.id, bytes, mime, reason)
                ?: CoverWriteOutcome.Failed(if (!wrote) "封面写入失败（格式不支持或文件只读）" else "写入后读回封面失败")
        }

    /**
     * 把封面存成应用内封面：`filesDir/remote_covers/<歌曲id>.<扩展名>`（与
     * `RemoteCoverBackfiller` 同一目录），并把路径回写 `Songs.CoverArtPath`。
     * 成功后 bump 版本号 —— 数据库路径会让播放器快照与通知栏 artwork 一起换图。
     */
    private suspend fun saveAppCover(
        songId: Int,
        bytes: ByteArray,
        mime: String,
        reason: String,
    ): CoverWriteOutcome.AppOnly? {
        if (songId <= 0) return null
        return runCatching {
            val dir = File(context.filesDir, APP_COVER_DIR).apply { if (!exists()) mkdirs() }
            val ext = when (mime) {
                "image/png" -> "png"
                "image/gif" -> "gif"
                "image/webp" -> "webp"
                else -> "jpg"
            }
            // 同一首歌只留一张：CoverArt 取 `$id.` 开头的第一个文件，
            // 残留的旧扩展名可能让它读到上一张图
            dir.listFiles()?.forEach { f ->
                if (f.name.startsWith("$songId.") && !f.name.endsWith(".$ext")) runCatching { f.delete() }
            }
            val target = File(dir, "$songId.$ext")
            val tmp = File(dir, "$songId.tmp")
            tmp.writeBytes(bytes)
            if (!tmp.renameTo(target)) {
                target.writeBytes(bytes)
                tmp.delete()
            }
            dao.setCoverArtPath(songId, target.absolutePath)
            coverVersion.bump()
            CoverWriteOutcome.AppOnly(bytes.size, reason)
        }.getOrNull()
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

    private companion object {
        /** 与应用侧 `CoverArt.REMOTE_COVER_DIR` / `RemoteCoverBackfiller.DIR_NAME` 保持一致 */
        const val APP_COVER_DIR = "remote_covers"
    }
}
