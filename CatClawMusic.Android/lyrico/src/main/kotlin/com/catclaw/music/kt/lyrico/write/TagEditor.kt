package com.catclaw.music.kt.lyrico.write

import android.content.Context
import android.net.Uri
import android.os.ParcelFileDescriptor
import com.lonx.audiotag.model.AudioTagData
import com.lonx.audiotag.rw.AudioTagReader
import com.lonx.audiotag.rw.AudioTagWriter
import java.io.File
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.withContext

/**
 * 可编辑的标签字段集合。
 *
 * 只收「用户真的会改」的那些：技术字段（码率/采样率/时长）由文件决定，不该给编辑入口。
 */
data class EditableTags(
    val title: String = "",
    val artist: String = "",
    val album: String = "",
    val albumArtist: String = "",
    val genre: String = "",
    /** 发行日期/年份，字符串以兼容 `2026` 与 `2026-09-24` 两种写法 */
    val date: String = "",
    val trackNumber: String = "",
    val discNumber: String = "",
    val composer: String = "",
    val lyricist: String = "",
    val comment: String = "",
) {
    fun isEmpty(): Boolean = this == EditableTags()
}

sealed interface TagReadOutcome {
    data class Success(val tags: EditableTags) : TagReadOutcome
    data class Unsupported(val reason: String) : TagReadOutcome
    data class Failed(val message: String) : TagReadOutcome
}

sealed interface TagWriteOutcome {
    data class Success(val changed: List<String>) : TagWriteOutcome
    data class Unsupported(val reason: String) : TagWriteOutcome
    data class Failed(val message: String) : TagWriteOutcome
}

/**
 * 读取/写入音频文件的文本标签。
 *
 * 走 `:lyrico-audiotag`（taglib）而不是自研解析：容器差异（ID3v2 / Vorbis / MP4 atoms /
 * APEv2）与字符编码的历史包袱都由 taglib 兜住，这正是移植它的原因。
 */
class TagEditor(private val context: Context) {

    suspend fun read(filePath: String): TagReadOutcome = withContext(Dispatchers.IO) {
        if (filePath.isBlank()) return@withContext TagReadOutcome.Unsupported("歌曲没有文件路径")
        val pfd = openReadOnly(filePath)
            ?: return@withContext TagReadOutcome.Unsupported("无法读取该文件")
        val data = pfd.use { descriptor ->
            runCatching { AudioTagReader.read(descriptor, readPictures = false) }.getOrNull()
        } ?: return@withContext TagReadOutcome.Failed("标签解析失败（可能不是音频文件）")
        TagReadOutcome.Success(data.toEditable())
    }

    /**
     * 写标签。只写**有变化**的字段：taglib 每次重写整个标签块，
     * 无关字段来回改会带来不必要的文件改动与失败面。
     */
    suspend fun write(filePath: String, original: EditableTags, edited: EditableTags): TagWriteOutcome =
        withContext(Dispatchers.IO) {
            if (filePath.isBlank()) return@withContext TagWriteOutcome.Unsupported("歌曲没有文件路径")
            val updates = mutableMapOf<String, String>()
            fun diff(key: String, old: String, new: String) {
                if (old != new) updates[key] = new
            }
            diff("TITLE", original.title, edited.title)
            diff("ARTIST", original.artist, edited.artist)
            diff("ALBUM", original.album, edited.album)
            diff("ALBUMARTIST", original.albumArtist, edited.albumArtist)
            diff("GENRE", original.genre, edited.genre)
            diff("DATE", original.date, edited.date)
            diff("TRACKNUMBER", original.trackNumber, edited.trackNumber)
            diff("DISCNUMBER", original.discNumber, edited.discNumber)
            diff("COMPOSER", original.composer, edited.composer)
            diff("LYRICIST", original.lyricist, edited.lyricist)
            diff("COMMENT", original.comment, edited.comment)

            if (updates.isEmpty()) return@withContext TagWriteOutcome.Success(emptyList())

            val pfd = openReadWrite(filePath)
                ?: return@withContext TagWriteOutcome.Unsupported("无法写入该文件（只读或不存在）")
            val ok = pfd.use { descriptor ->
                runCatching {
                    AudioTagWriter.writeTags(descriptor, updates, preserveOldTags = true)
                }.getOrElse { false }
            }
            if (!ok) return@withContext TagWriteOutcome.Failed("标签写入失败（格式不支持或文件只读）")

            // 回读校验：确认关键字段真的落盘
            val readBack = openReadOnly(filePath)?.use { descriptor ->
                runCatching { AudioTagReader.read(descriptor, readPictures = false) }.getOrNull()
            }
            if (readBack == null) {
                return@withContext TagWriteOutcome.Failed("写入后读回失败")
            }
            val mismatched = updates.filter { (key, value) ->
                val actual = when (key) {
                    "TITLE" -> readBack.title
                    "ARTIST" -> readBack.artist
                    "ALBUM" -> readBack.album
                    "ALBUMARTIST" -> readBack.albumArtist
                    "GENRE" -> readBack.genre
                    "DATE" -> readBack.date
                    "TRACKNUMBER" -> readBack.trackNumber
                    "DISCNUMBER" -> readBack.discNumber?.toString()
                    "COMPOSER" -> readBack.composer
                    "LYRICIST" -> readBack.lyricist
                    "COMMENT" -> readBack.comment
                    else -> null
                }
                if (value.isEmpty()) return@filter false
                // 数字字段要按数值比：taglib 会把 "07" 规范化成 "7"，
                // 拿原串比会把一次成功写入判成失败（实测踩到）。
                if (key == "TRACKNUMBER" || key == "DISCNUMBER") {
                    val expectedNum = value.trim().toIntOrNull()
                    val actualNum = actual?.trim()?.toIntOrNull()
                    return@filter expectedNum != null && expectedNum != actualNum
                }
                actual != value
            }.keys
            if (mismatched.isNotEmpty()) {
                return@withContext TagWriteOutcome.Failed("写入后校验不一致：${mismatched.joinToString()}")
            }
            TagWriteOutcome.Success(updates.keys.toList())
        }

    /** 文件里是否已有内嵌封面（批量匹配时据此决定跳过）。 */
    suspend fun hasEmbeddedPicture(filePath: String): Boolean = withContext(Dispatchers.IO) {
        if (filePath.isBlank()) return@withContext false
        openReadOnly(filePath)?.use { descriptor ->
            runCatching {
                AudioTagReader.read(descriptor, readPictures = true).pictures.isNotEmpty()
            }.getOrDefault(false)
        } ?: false
    }
    /** 读文件里已有的内嵌歌词（判断「是否已有歌词」、格式转换都要用）。 */
    suspend fun readEmbeddedLyrics(filePath: String): String? = withContext(Dispatchers.IO) {
        if (filePath.isBlank()) return@withContext null
        openReadOnly(filePath)?.use { descriptor ->
            runCatching { AudioTagReader.read(descriptor, readPictures = false).lyrics }.getOrNull()
        }?.takeIf { it.isNotBlank() }
    }

    /** 直接写歌词文本（格式转换场景用：已有文本、不经过搜索）。 */
    suspend fun writeRawLyrics(filePath: String, text: String): Boolean = withContext(Dispatchers.IO) {
        if (text.isBlank()) return@withContext false
        val pfd = openReadWrite(filePath) ?: return@withContext false
        pfd.use { descriptor ->
            runCatching {
                AudioTagWriter.writeTags(descriptor, mapOf("LYRICS" to text), preserveOldTags = true)
            }.getOrElse { false }
        }
    }
    private fun AudioTagData.toEditable(): EditableTags = EditableTags(
        title = title.orEmpty(),
        artist = artist.orEmpty(),
        album = album.orEmpty(),
        albumArtist = albumArtist.orEmpty(),
        genre = genre.orEmpty(),
        date = date.orEmpty(),
        trackNumber = trackNumber.orEmpty(),
        discNumber = discNumber?.toString().orEmpty(),
        composer = composer.orEmpty(),
        lyricist = lyricist.orEmpty(),
        comment = comment.orEmpty(),
    )

    private fun openReadOnly(path: String): ParcelFileDescriptor? = runCatching {
        if (path.startsWith("content://")) {
            context.contentResolver.openFileDescriptor(Uri.parse(path), "r")
        } else {
            val file = File(path)
            if (!file.isFile) return null
            ParcelFileDescriptor.open(file, ParcelFileDescriptor.MODE_READ_ONLY)
        }
    }.getOrNull()

    private fun openReadWrite(path: String): ParcelFileDescriptor? = runCatching {
        if (path.startsWith("content://")) {
            context.contentResolver.openFileDescriptor(Uri.parse(path), "rw")
        } else {
            val file = File(path)
            if (!file.isFile) return null
            ParcelFileDescriptor.open(file, ParcelFileDescriptor.MODE_READ_WRITE)
        }
    }.getOrNull()
}
