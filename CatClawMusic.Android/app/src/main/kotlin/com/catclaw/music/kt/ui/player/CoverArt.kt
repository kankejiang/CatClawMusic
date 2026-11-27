package com.catclaw.music.kt.ui.player

import android.content.Context
import android.graphics.Bitmap
import android.graphics.BitmapFactory
import android.media.MediaMetadataRetriever
import android.os.ParcelFileDescriptor
import android.util.LruCache
import androidx.compose.foundation.Image
import androidx.compose.foundation.layout.Box
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.produceState
import androidx.compose.runtime.remember
import androidx.compose.runtime.rememberUpdatedState
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.asImageBitmap
import androidx.compose.ui.layout.ContentScale
import androidx.compose.ui.platform.LocalContext
import androidx.lifecycle.Lifecycle
import androidx.lifecycle.compose.LifecycleEventEffect
import androidx.lifecycle.compose.collectAsStateWithLifecycle
import com.catclaw.music.kt.data.media.CoverFormats
import com.catclaw.music.kt.data.media.CoverVersion
import com.catclaw.music.kt.data.media.RemoteImages
import com.catclaw.music.kt.lyrico.write.SafeTagWriter
import com.lonx.audiotag.rw.AudioTagReader
import dagger.hilt.EntryPoint
import dagger.hilt.InstallIn
import dagger.hilt.android.EntryPointAccessors
import dagger.hilt.components.SingletonComponent
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

/**
 * 从音频文件里抠内嵌封面。content:// 与裸路径两种都要支持，因为本地曲两种都存在库里。
 *
 * WAV/AIFF 的封面存在 RIFF 的 `ID3 ` 块里，Android 的解析器（MediaMetadataRetriever）
 * 不认这个块，系统读不到不代表文件里没有 —— 这类容器改用 TagLib 兜底读，
 * 否则封面匹配明明写成功了，界面却永远只有首字母。
 */
private suspend fun embeddedPicture(context: Context, audioPath: String, maxPx: Int): Bitmap? {
    val fromSystem = runCatching {
        val retriever = MediaMetadataRetriever()
        try {
            if (audioPath.startsWith("content://")) {
                retriever.setDataSource(context, android.net.Uri.parse(audioPath))
            } else {
                val file = File(audioPath)
                if (!file.isFile) return@runCatching null
                retriever.setDataSource(file.absolutePath)
            }
            retriever.embeddedPicture?.let { decodeBytes(it, maxPx) }
        } finally {
            runCatching { retriever.release() }
        }
    }.getOrNull()
    if (fromSystem != null) return fromSystem

    // 本地曲的 FilePath 是 MediaStore 的 content:// URI，不能只看后缀 —— 统一交给 CoverFormats 判定
    if (!CoverFormats.isRiffId3Format(context, audioPath)) return null
    val bytes = SafeTagWriter.readSafely(context, audioPath) { pfd: ParcelFileDescriptor ->
        AudioTagReader.readPicture(pfd)
    } ?: return null
    if (bytes.isEmpty()) return null
    return decodeBytes(bytes, maxPx)
}

/**
 * 远程音频的**内嵌**封面：只在 [audioPath] 是 http(s) 时有意义。
 *
 * 为什么单列一条：远程曲库入库时没有侧车图（实测某台 NAS 1227 个文件里图片扩展名为 0），
 * `CoverArtPath` 因此为空。但图就在音频标签里（FLAC 的 PICTURE / MP3 的 APIC）——
 * 定位与 Range 补取都在 [RemoteImages] 里，这里只负责解码成 Bitmap。
 */
private fun remoteEmbeddedPicture(url: String, maxPx: Int): Bitmap? =
    remoteEmbeddedPicture(url)?.let { decodeBytes(it, maxPx) }

/**
 * 远程曲封面补全的落盘目录名。与 `RemoteCoverBackfiller.DIR_NAME` 必须一致 ——
 * 那里写、这里读，改一处漏一处就会「补完了还是不出图」。
 */
private const val REMOTE_COVER_DIR = "remote_covers"

/** 该目录下按歌曲 id 命名的封面文件；没有就回 null */
private fun cachedCoverFile(context: Context, songId: Int): File? {
    if (songId <= 0) return null
    val dir = File(context.filesDir, REMOTE_COVER_DIR)
    val names = dir.list() ?: return null
    val prefix = "$songId."
    val hit = names.firstOrNull { it.startsWith(prefix) && !it.endsWith(".tmp") } ?: return null
    return File(dir, hit).takeIf { it.isFile && it.length() > 0 }
}

private suspend fun load(
    context: Context,
    coverArtPath: String?,
    audioPath: String?,
    songId: Int,
    maxPx: Int,
    version: Int,
): Bitmap? = withContext(Dispatchers.IO) {
    // 版本号必须进 key：封面匹配/补全写的是音频内嵌图，路径字符串没变，
    // 只 bump 版本号的情况下若沿用旧 key，内存 LRU 会直接命中写入前的那张图 ——
    // 表现就是「封面写进去了但界面不变，重启才生效」。带上版本号才会真的重读文件。
    val key = "$coverArtPath|$audioPath|$songId|$maxPx|v$version"
    memoryCache.get(key)?.let { return@withContext it }
    val attempts = ArrayList<Bitmap?>(3)
    coverArtPath?.takeIf { it.isNotBlank() }?.let { path ->
        attempts += when {
            path.startsWith("http") -> fetchUrl(path)?.let { decodeBytes(it, maxPx) }
            File(path).isFile -> decodeFile(File(path), maxPx)
            else -> null
        }
    }
    // 第二级：封面补全**落过盘**的图。
    // 队列页与播放页读的是播放器里的 Song 快照，补全只改了数据库，
    // 快照里的 coverArtPath 仍是空 —— 不查这一级，补完了这张图也永远读不到。
    cachedCoverFile(context, songId)?.let { attempts += decodeFile(it, maxPx) }
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
 * 非 Compose 场景（分享导出）取封面：与 [rememberCover] 同一套来源与解码顺序。
 *
 * 不订阅封面版本号 —— 导出是一次性的快照，用到的是「此刻能读到哪张图」。
 */
internal suspend fun loadCoverBitmap(
    context: Context,
    coverArtPath: String?,
    audioPath: String?,
    songId: Int,
    maxPx: Int = 512,
): Bitmap? = load(context, coverArtPath, audioPath, songId, maxPx, version = 0)

/**
 * 订阅「封面可能变了」的版本号。
 *
 * 这样 [rememberCover] 的调用方（队列行、播放页大图、迷你条、全屏歌词……）**都不用改**：
 * 版本一变，key 变，`produceState` 重跑。之所以做成 Composable 内部订阅而不是让每个
 * 调用方自己注入，是因为封面显示点有 7 处，漏掉任何一处就又变成「有的页面刷新、有的不刷新」。
 */
@Composable
private fun coverVersion(): Int {
    val appContext = LocalContext.current.applicationContext
    val entry = remember(appContext) {
        runCatching {
            EntryPointAccessors.fromApplication(appContext, CoverVersionEntryPoint::class.java).coverVersion()
        }.getOrNull()
    } ?: return 0
    val version by entry.version.collectAsStateWithLifecycle()
    return version
}

/** Hilt 入口点：Composable 里没有注入点，只能这样从应用容器取单例 */
@EntryPoint
@InstallIn(SingletonComponent::class)
internal interface CoverVersionEntryPoint {
    fun coverVersion(): CoverVersion
}

/**
 * 一首歌的封面位图，取不到就是 null（调用方保留首字母占位）。
 *
 * 换歌时不闪旧图：`produceState` 以两条路径为 key，新图解码完成前一直显示旧值——
 * 但旧值属于上一首歌，所以这里显式在 key 变化时先归 null。
 *
 * [songId] 用于查「封面补全落盘的那张图」。必须传：补全只写数据库，而队列页/播放页
 * 手里是播放器的 `Song` 快照，其 `coverArtPath` 在补全前后都是空，光靠路径找不到图。
 *
 * 版本号（[coverVersion]）参与 key：补全完成后它变化，`produceState` 重跑，
 * 这时第二级回落就能读到刚落盘的图 —— 不用杀掉重进。
 *
 * [retryWhenMissing]：**回到前台时若这张图还没取到就重试一次**。
 * 在线曲封面走网络，切后台瞬间网络休眠/Doze 会让请求超时（日志里的 SocketTimeoutException），
 * `produceState` 跑完拿到 null 就结束了 —— key 不变它不会自己再试，于是用户看到
 * 「切回来还是占位图，去别的页面转一圈才出图」（离开页面等于组合重建，才会重跑）。
 * 只在「当前曲大图 / 迷你条」这类单点调用方开启；列表行不开，避免回前台时
 * 几十上百张图同时重发请求。
 */
@Composable
fun rememberCover(
    coverArtPath: String?,
    audioPath: String?,
    maxPx: Int = 512,
    songId: Int = 0,
    retryWhenMissing: Boolean = false,
): Bitmap? {
    val context = LocalContext.current
    val version = coverVersion()
    // 回到前台的重试计数，作为 produceState 的 key
    var resumeTick by remember { mutableStateOf(0) }
    val cover by produceState<Bitmap?>(null, coverArtPath, audioPath, maxPx, songId, version, resumeTick) {
        value = load(context, coverArtPath, audioPath, songId, maxPx, version)
    }
    if (retryWhenMissing) {
        // 首帧组合时 lifecycle 已 RESUMED，addObserver 会补发一次 ON_RESUME；
        // 用 seenResume 跳过这次，否则会和首轮加载撞车、白跑两遍请求
        val latest by rememberUpdatedState(cover)
        var seenResume by remember { mutableStateOf(false) }
        LifecycleEventEffect(Lifecycle.Event.ON_RESUME) {
            if (seenResume && latest == null) resumeTick++
            seenResume = true
        }
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
    songId: Int = 0,
    fallback: @Composable () -> Unit,
) {
    val cover = rememberCover(coverArtPath, audioPath, maxPx, songId)
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
