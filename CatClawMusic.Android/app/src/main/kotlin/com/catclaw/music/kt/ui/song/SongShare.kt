package com.catclaw.music.kt.ui.song

import android.content.Context
import android.content.Intent
import androidx.core.content.FileProvider
import java.io.File

/**
 * 分享导出的落点。
 *
 * `res/xml/file_paths.xml` 只暴露 cache 根目录，所以图片/视频必须写在这里，
 * `FileProvider.getUriForFile` 才能按文件授权给接收方。
 */
internal fun shareDir(context: Context): File =
    File(context.cacheDir, "share").apply { mkdirs() }

/**
 * 把一个已经落盘的图片/视频交给系统分享。
 *
 * 与旧的音频分享不同，这里不再判断「对方能不能读到原文件」—— 产物是我们自己生成的，
 * 一定在 cache 里，直接走 FileProvider。返回 false 表示没有可接收的应用。
 */
internal fun shareFile(
    context: Context,
    file: File,
    mime: String,
    text: String,
    chooserTitle: String,
): Boolean = runCatching {
    val uri = FileProvider.getUriForFile(context, "${context.packageName}.fileprovider", file)
    val send = Intent(Intent.ACTION_SEND).apply {
        type = mime
        putExtra(Intent.EXTRA_STREAM, uri)
        if (text.isNotBlank()) putExtra(Intent.EXTRA_TEXT, text)
        addFlags(Intent.FLAG_GRANT_READ_URI_PERMISSION)
    }
    context.startActivity(Intent.createChooser(send, chooserTitle))
}.isSuccess
