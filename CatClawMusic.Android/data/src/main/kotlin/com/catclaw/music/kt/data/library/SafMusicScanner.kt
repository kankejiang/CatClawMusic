package com.catclaw.music.kt.data.library

import android.content.Context
import android.database.Cursor
import android.media.MediaMetadataRetriever
import android.net.Uri
import android.provider.DocumentsContract
import com.catclaw.music.kt.core.diagnostic.AppLog
import com.catclaw.music.kt.core.library.MediaScanRules
import dagger.hilt.android.qualifiers.ApplicationContext
import javax.inject.Inject
import javax.inject.Singleton

/**
 * 一次扫描看到的一首音频（MediaStore 与 SAF 两条路都收敛到这里）。
 *
 * `path` 就是入库的 FilePath，也是**去重与裁剪的唯一 key**：MediaStore 那侧是
 * `content://media/.../<id>`，SAF 那侧是 `content://<provider>/tree/.../document/...`。
 * 两者形状不同但都稳定，所以同一首歌从两条路各进来会是两行 —— 与 C# 侧同口径
 * （那边也按路径去重，不做跨来源合并）。
 */
data class ScanTrack(
    val path: String,
    val mediaStoreId: Long,
    val title: String,
    val artistRaw: String?,
    val album: String,
    val durationMs: Long,
    val size: Long,
    val bitrateKbps: Int,
    val trackNumber: Int,
    val year: Int,
    val genre: String?,
    val dateModified: Long,
)

/**
 * SAF（用户手动挑的音乐文件夹）扫描 —— 对应 C# `LocalScanService` 的 SAF stage。
 *
 * 走 `DocumentsContract` 而不是 `DocumentFile`：后者每进一个目录都要再查一次
 * `document/{id}` 来确认它是不是目录，树遍历的往返次数直接翻倍。
 * 递归是自己写的显式栈 —— C# 侧的递归是写死在 `ScanFolderRecursive` 里的，
 * 那个 `ScanSubfolders` 开关从来没有消费者。
 */
@Singleton
class SafMusicScanner @Inject constructor(
    @ApplicationContext private val context: Context,
) {

    /** 一首 SAF 音频在文档树里的位置 + 目录列表就能给全的那几项 */
    private data class Child(
        val documentId: String,
        val displayName: String,
        val size: Long,
        val lastModifiedSec: Long,
        val isDirectory: Boolean,
    )

    /** 标签：SAF 只保证有文件名，其余全靠现读，读不动就是空值 */
    private data class Tags(
        val title: String = "",
        val artist: String? = null,
        val album: String = "",
        val durationMs: Long = 0L,
        val bitrateKbps: Int = 0,
        val trackNumber: Int = 0,
        val year: Int = 0,
        val genre: String? = null,
    )

    fun scan(treeUris: List<String>): List<ScanTrack> {
        val out = mutableListOf<ScanTrack>()
        for (raw in treeUris) {
            val tree = runCatching { Uri.parse(raw) }.getOrNull() ?: continue
            // 一个文件夹读不到（用户撤了授权、SD 卡拔了）不该让整次扫描失败，
            // 但也不能悄悄吃掉：MediaStore 那侧此时可能一条都没返回，
            // 于是"吃掉"就变成"把整库判成已消失"——裁剪的护栏在 LibraryScanner 里
            runCatching { walkTree(tree, out) }
                .onFailure { AppLog.w(TAG, "SAF 扫描失败 $raw: ${it.message}") }
        }
        return out
    }

    private fun walkTree(tree: Uri, out: MutableList<ScanTrack>) {
        val rootId = DocumentsContract.getTreeDocumentId(tree)
        val queue = ArrayDeque(listOf(DocumentsContract.buildDocumentUriUsingTree(tree, rootId)))
        var visited = 0
        while (queue.isNotEmpty()) {
            // 用户挑一个整个存储根是完全可能的：给遍历一个上限，
            // 而不是让一次扫描变成跑不完的查询风暴
            if (++visited > MAX_DIRECTORIES) {
                AppLog.w(TAG, "SAF 目录数超过 $MAX_DIRECTORIES，其余不再展开：$tree")
                return
            }
            for (child in children(tree, queue.removeFirst())) {
                if (child.isDirectory) {
                    queue.addLast(DocumentsContract.buildDocumentUriUsingTree(tree, child.documentId))
                } else if (MediaScanRules.isAudioFile(child.displayName)) {
                    out.add(toTrack(tree, child))
                }
            }
        }
    }

    private fun children(tree: Uri, dir: Uri): List<Child> {
        val childrenUri = DocumentsContract.buildChildDocumentsUriUsingTree(tree, DocumentsContract.getDocumentId(dir))
        val projection = arrayOf(
            DocumentsContract.Document.COLUMN_DOCUMENT_ID,
            DocumentsContract.Document.COLUMN_DISPLAY_NAME,
            DocumentsContract.Document.COLUMN_MIME_TYPE,
            DocumentsContract.Document.COLUMN_SIZE,
            DocumentsContract.Document.COLUMN_LAST_MODIFIED,
        )
        val out = mutableListOf<Child>()
        context.contentResolver.query(childrenUri, projection, null, null, null)?.use { c ->
            while (c.moveToNext()) {
                val id = c.text(DocumentsContract.Document.COLUMN_DOCUMENT_ID) ?: continue
                out.add(
                    Child(
                        documentId = id,
                        displayName = c.text(DocumentsContract.Document.COLUMN_DISPLAY_NAME).orEmpty(),
                        size = c.number(DocumentsContract.Document.COLUMN_SIZE),
                        lastModifiedSec = c.number(DocumentsContract.Document.COLUMN_LAST_MODIFIED),
                        isDirectory = c.text(DocumentsContract.Document.COLUMN_MIME_TYPE) ==
                            DocumentsContract.Document.MIME_TYPE_DIR,
                    ),
                )
            }
        }
        return out
    }

    private fun toTrack(tree: Uri, child: Child): ScanTrack {
        val uri = DocumentsContract.buildDocumentUriUsingTree(tree, child.documentId)
        val tags = readTags(uri)
        return ScanTrack(
            path = uri.toString(),
            mediaStoreId = 0L,
            // 标签读不出来（或这首根本没写标签）就退回文件名，与 C# 侧远程曲库的取法一致
            title = tags.title.ifBlank { MediaScanRules.titleFromFileName(child.displayName) },
            artistRaw = tags.artist,
            album = tags.album,
            durationMs = tags.durationMs,
            size = child.size,
            bitrateKbps = tags.bitrateKbps,
            trackNumber = tags.trackNumber,
            year = tags.year,
            genre = tags.genre,
            dateModified = child.lastModifiedSec,
        )
    }

    /** 现读一次标签。打开失败（授权没了、文件坏了）给空值而不是抛 */
    private fun readTags(uri: Uri): Tags {
        val retriever = MediaMetadataRetriever()
        return try {
            // 必须是 (Context, Uri) 这一支：`setDataSource(String)` 走的是文件路径解析，
            // 喂它 content URI 读不到任何东西 —— 真机上表现成 SAF 导入的歌时长一律 0
            retriever.setDataSource(context, uri)
            Tags(
                title = retriever.tag(MediaMetadataRetriever.METADATA_KEY_TITLE).orEmpty(),
                artist = retriever.tag(MediaMetadataRetriever.METADATA_KEY_ARTIST),
                album = retriever.tag(MediaMetadataRetriever.METADATA_KEY_ALBUM).orEmpty(),
                durationMs = retriever.tag(MediaMetadataRetriever.METADATA_KEY_DURATION)?.toLongOrNull() ?: 0L,
                bitrateKbps = MediaScanRules.bitrateKbps(retriever.tag(MediaMetadataRetriever.METADATA_KEY_BITRATE)?.toIntOrNull() ?: 0),
                // CD_TRACK_NUMBER 的形状是 "3/12"（本张第几首/共几首），只要前半个；
                // NUM_TRACKS 是整张专辑的首数，拿它当曲目号会把每首都写成同一个数
                trackNumber = retriever.tag(MediaMetadataRetriever.METADATA_KEY_CD_TRACK_NUMBER)
                    ?.substringBefore('/')?.toIntOrNull() ?: 0,
                year = retriever.tag(MediaMetadataRetriever.METADATA_KEY_YEAR)?.take(4)?.toIntOrNull() ?: 0,
                genre = retriever.tag(MediaMetadataRetriever.METADATA_KEY_GENRE),
            )
        } catch (@Suppress("TooGenericExceptionCaught") e: Exception) {
            Tags()
        } finally {
            runCatching { retriever.release() }
        }
    }

    /** 列不存在（不同 DocumentsProvider 的投影不完全一样）就当没有，而不是抛 */
    private fun Cursor.text(column: String): String? {
        val index = getColumnIndex(column)
        return if (index < 0 || isNull(index)) null else getString(index)
    }

    private fun Cursor.number(column: String): Long {
        val index = getColumnIndex(column)
        return if (index < 0 || isNull(index)) 0L else getLong(index)
    }

    private fun MediaMetadataRetriever.tag(key: Int): String? = runCatching { extractMetadata(key) }.getOrNull()

    private companion object {
        const val TAG = "CatClawSafScan"

        /** 单次 SAF 扫描最多展开多少个目录 */
        const val MAX_DIRECTORIES = 2_000
    }
}
