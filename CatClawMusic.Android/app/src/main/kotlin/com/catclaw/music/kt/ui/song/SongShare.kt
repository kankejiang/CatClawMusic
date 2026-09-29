package com.catclaw.music.kt.ui.song

import android.content.Context
import android.content.Intent
import android.net.Uri
import androidx.core.content.FileProvider
import java.io.File

/**
 * 把一首歌交给系统分享。
 *
 * 只认两种"对方真能读到"的地址：
 * - `content://`：本地曲库扫出来的就是 MediaStore 的 URI，天然可按文件授权；
 * - 落在本应用缓存目录里的文件：走 FileProvider（清单里只暴露 cache 根，见 `res/xml/file_paths.xml`）。
 *
 * 其余一律返回 null，让菜单**不画这一项**而不是点了再报错：
 * `/storage/...` 的裸 file:// 在 API 24+ 会抛 FileUriExposedException；
 * 远端直链（WebDAV/Subsonic）分享出去对方也下不到歌，那是个带签名的临时地址。
 * 宁可不给这一项，不给一个会崩或会骗人的项。
 */
internal fun shareUriFor(context: Context, path: String): Uri? {
    if (path.isBlank()) return null
    if (path.startsWith("content://")) return Uri.parse(path)
    if (path.startsWith("file://") || path.startsWith("http")) return null
    return runCatching {
        val file = File(path)
        val cacheRoot = context.cacheDir.canonicalPath + File.separator
        if (!file.canonicalPath.startsWith(cacheRoot) || !file.canRead()) {
            null
        } else {
            FileProvider.getUriForFile(context, "${context.packageName}.fileprovider", file)
        }
    }.getOrNull()
}

/** 组装 ACTION_SEND；[uri] 由 [shareUriFor] 给出，两者不各自判断一遍"能不能分享" */
internal fun shareIntent(uri: Uri, title: String, artist: String): Intent = Intent(Intent.ACTION_SEND).apply {
    type = "audio/*"
    putExtra(Intent.EXTRA_STREAM, uri)
    putExtra(Intent.EXTRA_TEXT, if (artist.isBlank()) title else "$title - $artist")
    addFlags(Intent.FLAG_GRANT_READ_URI_PERMISSION)
}

/** 没有可接收的应用时不弹空选择器，返回 false 让调用方给个提示 */
internal fun openShareSheet(context: Context, uri: Uri, title: String, artist: String): Boolean {
    val send = shareIntent(uri, title, artist)
    val chooser = Intent.createChooser(send, title)
    return runCatching { context.startActivity(chooser) }.isSuccess
}
