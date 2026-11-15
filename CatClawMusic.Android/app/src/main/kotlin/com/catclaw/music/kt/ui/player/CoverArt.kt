package com.catclaw.music.kt.ui.player

import android.content.Context
import android.graphics.Bitmap
import android.graphics.BitmapFactory
import android.media.MediaMetadataRetriever
import android.util.LruCache
import androidx.compose.foundation.Image
import androidx.compose.foundation.layout.Box
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.produceState
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.asImageBitmap
import androidx.compose.ui.layout.ContentScale
import androidx.compose.ui.platform.LocalContext
import com.catclaw.music.kt.data.media.RemoteImages
import java.io.File
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.withContext

/**
 * 封面取图的单一入口。
 *
 * 三条来源按代价从低到高试：DB 里存的本地图片文件 → 远程直链（Subsonic / 猫爪服务器给的就是 URL）
 * → 音频文件内嵌图。第三条要开 `MediaMetadataRetriever`，是本地曲唯一可能的来源：
 * 扫描器不往 `CoverArtPath` 里写东西（MAUI 是另有一台 CoverHelper 抽图落盘，本端没有那台引擎），
 * 所以不读内嵌图的话，本地曲就永远只有首字母。
 */
// 按字节限额（32MB，≈ 常用堆的 1/16）：之前按「96 条」计数，一张 1440px 全屏封面 8MB、
// 512px 行图 1MB，攒满 96 条能压近百 MB——滑动中 GC 一停顿就是几十毫秒的尖峰（gfxinfo 77~133ms 桶）
private val memoryCache = object : LruCache<String, Bitmap>(32 * 1024 * 1024) {
    override fun sizeOf(key: String, value: Bitmap): Int = value.byteCount
}

/** 只算数不碰 Android 类型的那半件，单独拆出来是为了能在 JVM 上测 */
internal fun inSampleSizeFor(width: Int, height: Int, maxPx: Int): Int {
    if (width <= 0 || height <= 0 || maxPx <= 0) return 1
    val longer = maxOf(width, height)
    if (longer <= maxPx) return 1
    var sample = 1
    while (longer / (sample * 2) >= maxPx) sample *= 2
    return sample
}

private fun decodeBytes(bytes: ByteArray, maxPx: Int): Bitmap? {
    val bounds = BitmapFactory.Options().apply { inJustDecodeBounds = true }
    BitmapFactory.decodeByteArray(bytes, 0, bytes.size, bounds)
    val opts = BitmapFactory.Options().apply { inSampleSize = inSampleSizeFor(bounds.outWidth, bounds.outHeight, maxPx) }
    return BitmapFactory.decodeByteArray(bytes, 0, bytes.size, opts)
}

private fun decodeFile(file: File, maxPx: Int): Bitmap? {
    val bounds = BitmapFactory.Options().apply { inJustDecodeBounds = true }
    BitmapFactory.decodeFile(file.absolutePath, bounds)
    val opts = BitmapFactory.Options().apply { inSampleSize = inSampleSizeFor(bounds.outWidth, bounds.outHeight, maxPx) }
    return BitmapFactory.decodeFile(file.absolutePath, opts)
}

/**
 * 取一张远程图。真正的 HTTP 细节（自签证书、URL 内嵌凭据）全在
 * [com.catclaw.music.kt.data.media.RemoteImages]，UI 层不再自己建客户端 ——
 * 之前正是「每条链路各建一个」让封面与侧车歌词各自漏掉了一整套 TLS 配置。
 */
private fun fetchUrl(url: String): ByteArray? = RemoteImages.fetchBytes(url)

/** 远程音频的**内嵌**封面：NAS 上没有侧车图时，图就在标签里（FLAC PICTURE / MP3 APIC） */
private fun remoteEmbeddedPicture(url: String): ByteArray? = RemoteImages.fetchEmbeddedCover(url)

/** 从音频文件里抠内嵌封面。content:// 与裸路径两种都要支持，因为本地曲两种都存在库里。 */
private fun embeddedPicture(context: Context, audioPath: String, maxPx: Int): Bitmap? = runCatching {
    val retriever = MediaMetadataRetriever()
    try {
        if (audioPath.startsWith("content://")) {
            retriever.setDataSource(context, android.net.Uri.parse(audioPath))
        } else {
            val file = File(audioPath)
            if (!file.isFile) return null
            retriever.setDataSource(file.absolutePath)
        }
        retriever.embeddedPicture?.let { decodeBytes(it, maxPx) }
    } finally {
        runCatching { retriever.release() }
    }
}.getOrNull()

/**
 * 远程音频的**内嵌**封面：只在 [audioPath] 是 http(s) 时有意义。
 *
 * 为什么单列一条：远程曲库入库时没有侧车图（实测某台 NAS 1227 个文件里图片扩展名为 0），
 * `CoverArtPath` 因此为空。但图就在音频标签里（FLAC 的 PICTURE / MP3 的 APIC）——
 * 定位与 Range 补取都在 [RemoteImages] 里，这里只负责解码成 Bitmap。
 */
private fun remoteEmbeddedPicture(url: String, maxPx: Int): Bitmap? =
    remoteEmbeddedPicture(url)?.let { decodeBytes(it, maxPx) }

private suspend fun load(
    context: Context,
    coverArtPath: String?,
    audioPath: String?,
    maxPx: Int,
): Bitmap? = withContext(Dispatchers.IO) {
    val key = "$coverArtPath|$audioPath|$maxPx"
    memoryCache.get(key)?.let { return@withContext it }
    val attempts = ArrayList<Bitmap?>(3)
    coverArtPath?.takeIf { it.isNotBlank() }?.let { path ->
        attempts += when {
            path.startsWith("http") -> fetchUrl(path)?.let { decodeBytes(it, maxPx) }
            File(path).isFile -> decodeFile(File(path), maxPx)
            else -> null
        }
    }
    audioPath?.takeIf { it.isNotBlank() }?.let { path ->
        attempts += if (path.startsWith("http")) {
            remoteEmbeddedPicture(path, maxPx)
        } else {
            embeddedPicture(context, path, maxPx)
        }
    }
    val bitmap = attempts.firstOrNull { it != null } ?: return@withContext null
    memoryCache.put(key, bitmap)
    bitmap
}

/**
 * 一首歌的封面位图，取不到就是 null（调用方保留首字母占位）。
 *
 * 换歌时不闪旧图：`produceState` 以两条路径为 key，新图解码完成前一直显示旧值——
 * 但旧值属于上一首歌，所以这里显式在 key 变化时先归 null。
 */
@Composable
fun rememberCover(coverArtPath: String?, audioPath: String?, maxPx: Int = 512): Bitmap? {
    val context = LocalContext.current
    val cover by produceState<Bitmap?>(null, coverArtPath, audioPath, maxPx) {
        value = load(context, coverArtPath, audioPath, maxPx)
    }
    return cover
}

/**
 * 列表行的小封面方块：有图就裁成方块铺满，没图回落到调用方给的占位内容（首字母或序号）。
 *
 * 外层的尺寸/圆角/底色由调用方通过 [modifier] 给 —— 各页的方块形状本来就不一样，
 * 统一在这里反而会把视觉语言拉平成一处。
 */
@Composable
fun CoverTile(
    coverArtPath: String?,
    audioPath: String?,
    modifier: Modifier = Modifier,
    maxPx: Int = 128,
    fallback: @Composable () -> Unit,
) {
    val cover = rememberCover(coverArtPath, audioPath, maxPx)
    Box(modifier = modifier, contentAlignment = Alignment.Center) {
        if (cover != null) {
            Image(
                bitmap = cover.asImageBitmap(),
                contentDescription = null,
                contentScale = ContentScale.Crop,
                modifier = Modifier.matchParentSize(),
            )
        } else {
            fallback()
        }
    }
}
