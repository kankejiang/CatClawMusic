package com.catclaw.music.kt.data.media

import android.content.Context
import android.net.Uri
import android.provider.OpenableColumns
import java.io.File

/**
 * 封面容器能力判定。
 *
 * WAV/AIFF 的封面只存在 RIFF 的 `ID3 ` 块里：
 * - Android 的 MediaMetadataRetriever 不解析这个块 → **读**封面必须靠 TagLib；
 * - TagLib 的 WAV `tag()` 是 TagUnion，对没有 ID3v2 块的文件 PICTURE 复杂属性写不进去。
 *
 * 本地曲库的 `Songs.FilePath` 存的是 MediaStore 的 `content://` URI（不是后缀名），
 * 所以判定必须同时支持「路径后缀」和「ContentResolver MIME」。
 */
object CoverFormats {

    private val RIFF_ID3_EXTS = setOf("wav", "aif", "aiff", "aifc")

    private val RIFF_ID3_MIMES = setOf(
        "audio/wav", "audio/x-wav", "audio/wave", "audio/vnd.wave", "audio/x-pn-wav",
        "audio/aiff", "audio/x-aiff", "audio/x-aifc",
    )

    /** 封面存在 RIFF 的 ID3v2 块里、系统读写都不可靠的容器（WAV/AIFF） */
    fun isRiffId3Format(context: Context, path: String): Boolean {
        if (!path.startsWith("content://")) {
            return File(path).extension.lowercase() in RIFF_ID3_EXTS
        }
        val uri = Uri.parse(path)
        val mime = runCatching { context.contentResolver.getType(uri) }
            .getOrNull()
            ?.lowercase()
            .orEmpty()
        if (mime.isNotEmpty()) return mime in RIFF_ID3_MIMES
        // 个别 provider 不给 MIME，退回查显示名后缀
        val name = runCatching {
            context.contentResolver.query(uri, arrayOf(OpenableColumns.DISPLAY_NAME), null, null, null)
                ?.use { c -> if (c.moveToFirst()) c.getString(0) else null }
        }.getOrNull()
        return File(name.orEmpty()).extension.lowercase() in RIFF_ID3_EXTS
    }
}
