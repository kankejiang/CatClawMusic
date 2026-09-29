package com.catclaw.music.kt.data.lyrics

import android.content.Context
import android.provider.MediaStore
import com.catclaw.music.kt.core.diagnostic.AppLog
import com.catclaw.music.kt.core.lyrics.KrcParser
import com.catclaw.music.kt.core.lyrics.LrcParser
import com.catclaw.music.kt.core.lyrics.Lyrics
import com.catclaw.music.kt.core.lyrics.LyricsEncoding
import com.catclaw.music.kt.core.lyrics.LyricsFileLocator
import com.catclaw.music.kt.core.lyrics.RemoteLyrics
import com.catclaw.music.kt.core.model.Song
import com.catclaw.music.kt.data.db.LibraryDao
import com.catclaw.music.kt.data.db.entity.LyricsEntity
import dagger.hilt.android.qualifiers.ApplicationContext
import java.io.File
import javax.inject.Inject
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.withContext

/**
 * 歌词读取：先查 DB 缓存，再找侧车文件（.lrc → .ttml → .xml → .krc → .qrc，再同名前缀模糊匹配）。
 *
 * content:// 曲目先经 MediaStore 的 DATA 列还原真实路径，找不到就没有侧车可查——
 * 与 MAUI 侧「SAF 歌曲存 content:// URI，用时再解析 _data」的做法一致。
 * `sourceMode` 对应 C# 的「自动 / 仅内嵌 / 仅外挂」三档；在线歌词尚未接入（属 M5/M7）。
 */
class LyricsReader @Inject constructor(
    @ApplicationContext private val context: Context,
    private val dao: LibraryDao,
    private val remoteLyrics: RemoteLyrics,
) {

    suspend fun loadFor(song: Song, sourceMode: LyricsSourceMode = LyricsSourceMode.Auto): Lyrics? =
        withContext(Dispatchers.IO) {
            if (sourceMode != LyricsSourceMode.Embedded) {
                dao.lyricsContent(song.id)?.let { cached ->
                    LrcParser.parse(cached) ?: LyricsEncoding.parseContent(cached)
                }?.let { return@withContext it }

                val path = realPathOf(song) ?: return@withContext null
                if (RemoteLyrics.isRemoteUrl(path)) {
                    // 远程曲目没有本地目录可枚举，侧车只能走入库时配好的远程地址；
                    // 它排在内嵌之前，与 C# 同一理由：小文件直读，省一次 2 MB 的头请求。
                    return@withContext remoteLyrics.load(song.lyricsPath)
                }
                readSidecar(path)?.also { found ->
                    // 命中后回写 LrcPath，下次免去目录枚举
                    runCatching { dao.setLyricsPath(song.id, found) }
                }?.let { parseFile(it) }?.let { return@withContext it }

                if (sourceMode == LyricsSourceMode.External) return@withContext null
                parseEmbedded(path)
            } else {
                val path = song.filePath
                if (path.startsWith("content://") || path.startsWith("http")) null else parseEmbedded(path)
            }
        }

    /**
     * 侧车没有才看内嵌标签 —— 与 C# Auto 模式的次序一致（`song.LyricsPath` 侧车优先，
     * 之后才 `TagReader.ReadEmbeddedLyrics`）。内嵌文本走同一套解析分派：
     * TTML / AMLL / 带时间戳的 LRC / 纯文本兜底。
     */
    private fun parseEmbedded(audioPath: String): Lyrics? {
        if (audioPath.startsWith("content://") || audioPath.startsWith("http")) return null
        val file = File(audioPath)
        val text = runCatching { AudioTagLyrics.read(audioPath) }.getOrNull() ?: return null
        return LyricsEncoding.parseContent(text)?.also {
            AppLog.i(TAG, "embedded lyrics ${file.name} lines=${it.lines.size}")
        }
    }

    /** 把在线歌词等文本落到 DB，供离线复用 */
    suspend fun saveText(songId: Int, content: String, lrcPath: String? = null) = withContext(Dispatchers.IO) {
        dao.upsertLyrics(LyricsEntity(songId = songId, lrcPath = lrcPath, content = content))
    }

    private fun realPathOf(song: Song): String? {
        val path = song.filePath
        if (!path.startsWith("content://")) return path
        return runCatching {
            context.contentResolver.query(
                android.net.Uri.parse(path),
                arrayOf(MediaStore.Audio.Media.DATA),
                null, null, null,
            )?.use { c ->
                if (c.moveToFirst()) c.getString(0)?.takeIf { it.isNotBlank() } else null
            }
        }.getOrNull()
    }

    private fun readSidecar(audioPath: String): String? {
        for (candidate in LyricsFileLocator.candidates(audioPath)) {
            val file = File(candidate)
            if (!file.isFile) continue
            if (file.length() > LyricsEncoding.MAX_LYRICS_FILE_BYTES) continue
            return candidate
        }
        val dir = File(LyricsFileLocator.directoryOf(audioPath) ?: return null)
        val names = dir.listFiles()?.map { it.name } ?: return null
        val picked = LyricsFileLocator.fuzzyPick(names, LyricsFileLocator.baseName(audioPath)) ?: return null
        return File(dir, picked).absolutePath
    }

    private fun parseFile(path: String): Lyrics? {
        val file = File(path)
        if (!file.isFile) return null
        return runCatching {
            if (LyricsFileLocator.isBinaryLyrics(file)) {
                KrcParser.parse(file.readBytes()) ?: KrcParser.parseQrc(file.readBytes())
            } else {
                LyricsEncoding.parseContent(LyricsEncoding.decode(file.readBytes()))
            }
        }.onFailure { AppLog.w(TAG, "歌词解析失败 $path", it) }.getOrNull()
    }

    private companion object {
        const val TAG = "CatClawLyrics"
    }
}
