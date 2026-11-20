package com.catclaw.music.kt.lyrico.write

import android.content.Context
import android.net.Uri
import android.os.ParcelFileDescriptor
import com.catclaw.music.kt.core.lyrics.Lyrics
import com.catclaw.music.kt.core.model.Song
import com.catclaw.music.kt.data.lyrics.LyricsReader
import com.catclaw.music.kt.lyrico.lyrics.LyricoLyricsEncoder
import com.catclaw.music.kt.lyrico.lyrics.LyricoLyricsFormat
import com.lonx.audiotag.rw.AudioTagReader
import com.lonx.audiotag.rw.AudioTagWriter
import java.io.File
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.withContext

/** 写回结果。 */
sealed interface LyricsWriteOutcome {
    /** 成功：[text] 是实际写入的歌词文本，[format] 是编码格式 */
    data class Success(val text: String, val format: LyricoLyricsFormat) : LyricsWriteOutcome

    /** 该歌曲不允许写（远程曲目 / 只读文档 / 不支持的路径形态） */
    data class Unsupported(val reason: String) : LyricsWriteOutcome

    /** 写失败（IO / 标签库报错） */
    data class Failed(val message: String) : LyricsWriteOutcome
}

/**
 * 把歌词写进音频文件内嵌标签。
 *
 * ## 为什么写完还要更新 DB 缓存
 *
 * `LyricsReader.loadFor()` 在「自动」档会**先查 `Lyrics.Content` 缓存**，命中就直接返回。
 * 如果只改文件不更新缓存，播放页拿到的还是旧歌词 —— 表现为「写完了但界面没变」。
 * 所以成功后立刻 `saveText(songId, text)`，让文件与缓存一致。
 *
 * ## 支持范围
 *
 * - 本地文件路径：直接以读写方式打开
 * - `content://` 文档：走 ContentResolver 拿可写 fd（SAF 场景）
 * - 远程曲目（http/https、smb://）：**不支持**。写回 NAS 需要协议层支持，
 *   而且用户通常也不希望改远端文件；这里明确返回 Unsupported，由界面提示。
 */
class LyricsWriter(
    private val context: Context,
    private val lyricsReader: LyricsReader,
    /** 写入成功后广播，让正在播放的那首歌立刻重载歌词 */
    private val refreshBus: com.catclaw.music.kt.data.lyrics.LyricsRefreshBus,
) {

    suspend fun write(
        song: Song,
        lyrics: Lyrics,
        format: LyricoLyricsFormat = LyricoLyricsFormat.PLAIN,
    ): LyricsWriteOutcome = withContext(Dispatchers.IO) {
        if (lyrics.lines.isEmpty()) {
            return@withContext LyricsWriteOutcome.Failed("没有可写入的歌词")
        }
        val path = song.filePath
        if (path.isBlank()) return@withContext LyricsWriteOutcome.Unsupported("歌曲没有文件路径")

        val lower = path.lowercase()
        if (lower.startsWith("http://") || lower.startsWith("https://") ||
            lower.startsWith("smb://") || lower.startsWith("\\")
        ) {
            return@withContext LyricsWriteOutcome.Unsupported("远程歌曲不支持写入标签")
        }

        val text = LyricoLyricsEncoder.encode(lyrics, format)
        if (text.isBlank()) return@withContext LyricsWriteOutcome.Failed("编码后的歌词为空")

        val wrote = openReadWrite(path)?.use { pfd ->
            runCatching {
                AudioTagWriter.writeTags(pfd, mapOf(KEY_LYRICS to text), preserveOldTags = true)
            }.getOrElse { false }
        } ?: return@withContext LyricsWriteOutcome.Unsupported("无法打开该文件（只读或不存在）")

        if (!wrote) return@withContext LyricsWriteOutcome.Failed("标签写入失败（文件可能只读或格式不支持）")

        // 校验：读回内嵌歌词，确认真的落盘了（避免"报成功但其实没写"）
        val readBack = openReadWrite(path)?.use { pfd ->
            runCatching { AudioTagReader.read(pfd, readPictures = false).lyrics }.getOrNull()
        }
        if (readBack.isNullOrBlank() || !readBack.contains(text.lineSequence().first().take(16).trim())) {
            return@withContext LyricsWriteOutcome.Failed("写入后读回校验失败")
        }

        // 同步 DB 缓存，保证播放页立即用新歌词
        runCatching { lyricsReader.saveText(song.id, text) }
        // 再广播一次：播放侧 ViewModel 只在切歌时重载，同 id 需要外部推它一把
        refreshBus.notifyChanged(song.id)

        LyricsWriteOutcome.Success(text, format)
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

    private companion object {
        /** taglib 侧对「歌词」的统一键名；各格式会映射到 USLT / LYRICS / ©lyr */
        const val KEY_LYRICS = "LYRICS"
    }
}
