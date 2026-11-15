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
import com.catclaw.music.kt.core.net.RemoteHttp
import com.catclaw.music.kt.data.db.LibraryDao
import com.catclaw.music.kt.data.db.entity.LyricsEntity
import com.catclaw.music.kt.data.media.TagPicture
import dagger.hilt.android.qualifiers.ApplicationContext
import java.io.File
import javax.inject.Inject
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.withContext

/**
 * 歌词读取：先查 DB 缓存，再按来源档取词 ——
 * **自动**：内嵌标签 → 侧车文件（.lrc → .ttml → .xml → .krc → .qrc，再同名前缀模糊匹配）；
 * **仅内嵌**：只读音频标签；**仅外挂**：只读侧车。
 * （2026-11-04 用户要求把「自动」改成内嵌优先，原来与 C# 一样是侧车优先。）
 *
 * content:// 曲目先经 MediaStore 的 DATA 列还原真实路径，找不到就没有侧车可查——
 * 与 MAUI 侧「SAF 歌曲存 content:// URI，用时再解析 _data」的做法一致。
 * `sourceMode` 对应 C# 的「自动 / 仅内嵌 / 仅外挂」三档；在线歌词尚未接入（属 M5/M7）。
 */
class LyricsReader @Inject constructor(
    @ApplicationContext private val context: Context,
    private val dao: LibraryDao,
    private val remoteLyrics: RemoteLyrics,
    /** 在线音源歌词（网易云等）：本地文件与侧车都取不到时最后试一次 */
    private val onlineLyrics: OnlineLyricsSource,
) {

    suspend fun loadFor(song: Song, sourceMode: LyricsSourceMode = LyricsSourceMode.Auto): Lyrics? =
        withContext(Dispatchers.IO) {
            AppLog.d(TAG, "load '${song.title}' id=${song.id} remote=${song.isRemote} rid=${song.remoteId}")
            if (sourceMode != LyricsSourceMode.Embedded) {
                dao.lyricsContent(song.id)?.let { cached ->
                    LrcParser.parse(cached) ?: LyricsEncoding.parseContent(cached)
                }?.let { return@withContext it }

                // 在线曲没有本地文件可读：先问平台歌词源，拿到就顺手落库（下次离线可复用）
                if (song.isRemote) {
                    val fetched = runCatching { onlineLyrics.lyricsFor(song) }
                        .onFailure { AppLog.w(TAG, "online lyrics failed", it) }
                        .getOrNull()
                    AppLog.d(TAG, "online fetched=${fetched?.length ?: -1}")
                    if (!fetched.isNullOrBlank()) {
                        saveText(song.id, fetched)
                        (LrcParser.parse(fetched) ?: LyricsEncoding.parseContent(fetched))
                            ?.let { return@withContext it }
                    }
                }

                val path = realPathOf(song) ?: return@withContext null
                if (RemoteLyrics.isRemoteUrl(path)) {
                    // 远程曲目：先读**侧车** .lrc（入库时配对好、地址在 LyricsPath 里），
                    // 侧车没有再回落到**音频标签里的内嵌歌词** —— 有些库没整理侧车，
                    // 但 FLAC 的 LYRICS / MP3 的 USLT 一直跟着文件走。
                    remoteLyrics.load(song.lyricsPath)?.let { return@withContext it }
                    AppLog.d(TAG, "remote sidecar miss, trying embedded tag of ${song.title}")
                    fetchRemoteEmbeddedLyrics(path)?.let { text ->
                        saveText(song.id, text)
                        return@withContext (LrcParser.parse(text) ?: LyricsEncoding.parseContent(text))
                    }
                    return@withContext null
                }
                // 自动模式：**内嵌标签优先于外挂侧车**（2026-11-04 用户要求，原来与 C# 一样是侧车优先）。
                // 理由：内嵌的是跟着文件走的、与整理出的 .lrc 可能不同步；侧车作为兜底。
                // 「仅外挂」档跳过这一段直接看侧车。
                if (sourceMode != LyricsSourceMode.External) {
                    parseEmbedded(path)?.let { return@withContext it }
                }
                readSidecar(path)?.also { found ->
                    // 命中后回写 LrcPath，下次免去目录枚举
                    runCatching { dao.setLyricsPath(song.id, found) }
                }?.let { parseFile(it) }?.let { return@withContext it }
                null
            } else {
                // 「仅内嵌」也要先还原真实路径：本地曲库里 content://（SAF 导入）很常见，
                // 直接拿 URI 进不了标签解析，表现就是「内嵌歌词永远读不到」。
                val path = realPathOf(song)
                if (path == null || path.startsWith("http")) null else parseEmbedded(path)
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

    /**
     * 远程音频的**内嵌歌词**：取头部窗口，按容器解 USLT / LYRICS 标签。
     *
     * 为什么需要它：`LyricsPath` 只有入库时配对到的侧车 `.lrc`。没整理侧车的那批歌，
     * 库里这一列是空的，而歌词其实就在标签里（实测 FLAC 带 `LYRICS`、MP3 带 `USLT`）。
     * 此前远程分支只读侧车，于是「本地有词、同一首在远程没词」。
     *
     * 不复用 `RemoteLyrics`：那个只把 URL 当文本取回来，不碰二进制标签。
     */
    private suspend fun fetchRemoteEmbeddedLyrics(url: String): String? {
        val head = RemoteHttp.client
            .newCall(RemoteHttp.request(url, TagPicture.HEAD_WINDOW))
            .execute().use { response ->
                if (!response.isSuccessful) null else response.body?.bytes()
            } ?: return null
        return AudioTagLyrics.readWindow(head)
            ?.takeIf { it.isNotBlank() }
            ?.also { AppLog.i(TAG, "remote embedded lyrics ${it.lineSequence().count()} lines") }
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
