package com.catclaw.music.kt.data.media

import android.content.Context
import android.net.Uri
import android.provider.MediaStore
import dagger.hilt.android.qualifiers.ApplicationContext
import javax.inject.Inject
import javax.inject.Singleton

/**
 * 把 `content://` 还原成真实文件路径。
 *
 * 本地曲入库时存的是 MediaStore 的 content URI（`LibraryScanner` 里 `MediaScanRules.contentUri`），
 * 因为直接存 `_data` 在分区存储下不一定还读得到。代价是 **URI 里没有文件名与后缀**，
 * 所以凡是要给用户看"格式 / 文件名 / 侧车歌词路径"的地方，都得先解一次真实路径。
 */
@Singleton
class MediaPathResolver @Inject constructor(
    @ApplicationContext private val context: Context,
) {

    /** 非 content URI 原样返回；解不出来也原样返回（调用方按"没有这个信息"处理） */
    fun resolve(path: String): String {
        if (!path.startsWith("content://")) return path
        return runCatching {
            context.contentResolver.query(Uri.parse(path), arrayOf(MediaStore.Audio.Media.DATA), null, null, null)
                ?.use { cursor -> if (cursor.moveToFirst()) cursor.getString(0)?.takeIf { it.isNotBlank() } else null }
        }.getOrNull() ?: path
    }
}
