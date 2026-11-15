package com.catclaw.music.kt.data.media

import android.content.Context
import com.catclaw.music.kt.core.diagnostic.AppLog
import com.catclaw.music.kt.core.library.MetadataBackfill
import com.catclaw.music.kt.data.db.LibraryDao
import com.catclaw.music.kt.data.db.entity.SongEntity
import dagger.hilt.android.qualifiers.ApplicationContext
import java.io.File
import java.util.concurrent.atomic.AtomicInteger
import javax.inject.Inject
import javax.inject.Singleton
import kotlin.coroutines.cancellation.CancellationException
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.async
import kotlinx.coroutines.awaitAll
import kotlinx.coroutines.coroutineScope
import kotlinx.coroutines.withContext

/**
 * 远程曲**封面**回填：把音频标签里的内嵌图抽出来落盘，再把文件路径写回 `CoverArtPath`。
 *
 * ## 为什么需要独立一轮
 *
 * 远程曲库入库时只有目录清单，封面两条来源都断了：
 * - **侧车图**：实测某台 NAS 1227 个文件里 `.jpg/.png` 为 0，`WebDavImporter` 配对不到就是空；
 * - **标签**：图其实就在文件里（FLAC 的 `PICTURE` 块 598 KB JPEG、MP3 的 `APIC` 帧），
 *   但入库时只取了文件名，从没读过图。
 *
 * 结果就是 `Songs.CoverArtPath` 全空 → 列表画首字母 → 通知栏/锁屏也没有 `artworkUri`。
 *
 * ## 为什么不能复用元数据回填那张查询
 *
 * 那张的判据是「时长或歌手缺」（`MetadataBackfill.needsBackfill`），补完之后这些行就不再被选中。
 * 而封面与「时长/歌手」无关：元数据早已齐全的歌照样可能没封面 —— 你这套库里 613 首
 * 有 385 首时长已填、封面仍全空。合成一张查询会让绝大多数行被漏掉。
 *
 * ## 为什么落盘而不是存 URL
 *
 * 内嵌图没有可直链的地址，只能取出来存文件。目录 `filesDir/remote_covers/`、按歌曲 Id 命名：
 * Id 稳定、天然去重，`CoverTile` 的本地文件分支直接能读，备份与清理也好定位。
 */
@Singleton
class RemoteCoverBackfiller @Inject constructor(
    @ApplicationContext private val context: Context,
    private val dao: LibraryDao,
    private val bytes: AudioBytesReader,
) {

    data class Result(val total: Int, val updated: Int, val skipped: Int, val failed: Int, val elapsedMs: Long)

    private val coverDir: File
        get() = File(context.filesDir, DIR_NAME).apply { if (!exists()) mkdirs() }

    /**
     * @param onProgress (已完成, 总数)；总数在开始时一次算准，便于画确定进度
     */
    suspend fun run(
        source: Int,
        onProgress: (done: Int, total: Int) -> Unit = { _, _ -> },
    ): Result = withContext(Dispatchers.IO) {
        val startedAt = System.currentTimeMillis()
        val rows = dao.remoteMissingCover(source)
        if (rows.isEmpty()) return@withContext Result(0, 0, 0, 0, 0L)
        val total = rows.size
        onProgress(0, total)
        val done = AtomicInteger(0)
        val saved = AtomicInteger(0)
        val skipped = AtomicInteger(0)
        val failed = AtomicInteger(0)

        coroutineScope {
            val workers = Dispatchers.IO.limitedParallelism(WORKERS)
            rows.map { row ->
                async(workers) {
                    try {
                        if (fetchAndSave(row)) saved.incrementAndGet() else skipped.incrementAndGet()
                    } catch (e: CancellationException) {
                        throw e
                    } catch (@Suppress("TooGenericExceptionCaught") e: Exception) {
                        failed.incrementAndGet()
                        AppLog.w(TAG, "封面失败 ${row.title}: ${e.javaClass.simpleName} ${e.message}")
                    }
                    val n = done.incrementAndGet()
                    // 每 10 首或最后一首报一次：613 首就是 61 次重组，别按每首刷
                    if (n % 10 == 0 || n == total) onProgress(n, total)
                }
            }.awaitAll()
        }

        val elapsed = System.currentTimeMillis() - startedAt
        AppLog.i(
            TAG,
            "封面回填完成 total=$total saved=${saved.get()} noCover=${skipped.get()} failed=${failed.get()} ${elapsed}ms",
        )
        Result(total, saved.get(), skipped.get(), failed.get(), elapsed)
    }

    /** @return true = 存下了封面并回写库；false = 这个文件没有内嵌图（不是错误） */
    private suspend fun fetchAndSave(row: SongEntity): Boolean {
        val url = row.filePath
        if (url.isBlank()) return false

        // 1) 头部窗口：够覆盖 ID3v2 整表与 FLAC 前几个块，也够定位 PICTURE/APIC
        val head = bytes.head(url, TagPicture.HEAD_WINDOW) ?: return false
        if (!MetadataBackfill.looksLikeAudio(head)) return false

        val picture = extractPicture(url, head, row.fileSize) ?: return false

        // 2) 落盘：先写临时文件再改名，避免 UI 读到半张图
        val target = File(coverDir, "${row.id}.${picture.extension}")
        val tmp = File(coverDir, "${row.id}.tmp")
        try {
            tmp.writeBytes(picture.bytes)
            if (!tmp.renameTo(target)) {
                target.writeBytes(picture.bytes)
                tmp.delete()
            }
        } catch (e: Exception) {
            runCatching { tmp.delete() }
            throw e
        }

        // 3) 只回写封面列，不整行 upsert（避免覆盖别处刚补好的字段）
        dao.setCoverArtPath(row.id, target.absolutePath)
        return true
    }

    /**
     * 先按窗口定位，窗口不够才补一次精确 Range。
     *
     * M4A 的 `moov` 在文件尾时头部窗口里没有任何图，走尾部窗口（与标签回填同一条判据与同一个上限）。
     */
    private suspend fun extractPicture(url: String, head: ByteArray, fileSize: Long): TagPicture.Picture? {
        val location = TagPicture.locate(head)
        if (location != null) {
            if (location.end <= head.size) return TagPicture.extract(head)
            if (location.length <= 0 || location.length > TagPicture.MAX_COVER_BYTES) return null
            val missing = (location.end - head.size).toInt()
            val tail = bytes.range(url, head.size.toLong(), missing) ?: return null
            return TagPicture.extract(head + tail)
        }
        // Ogg 的图是头部窗口里注释包的 base64，无需再取
        TagPicture.extract(head)?.let { return it }
        // 非 faststart 的 M4A：moov（含 ilst/covr）在文件尾
        if (fileSize > 0 && MetadataBackfill.mayHaveMoovAtTail(url)) {
            val tailSize = MetadataBackfill.tailBytes(fileSize)
            val tail = bytes.tail(url, tailSize, fileSize) ?: return null
            return TagPicture.extractFromMp4Tail(tail)
        }
        return null
    }

    private companion object {
        const val TAG = "CatClawCover"
        const val DIR_NAME = "remote_covers"

        /** 与元数据回填同值：4 个 worker 时远程才不会开始排队限流 */
        const val WORKERS = 4
    }
}
