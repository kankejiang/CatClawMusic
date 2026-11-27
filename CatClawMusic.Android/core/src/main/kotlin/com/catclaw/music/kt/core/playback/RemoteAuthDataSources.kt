package com.catclaw.music.kt.core.playback

import android.content.Context
import android.net.Uri
import androidx.media3.datasource.BaseDataSource
import androidx.media3.datasource.DataSource
import androidx.media3.datasource.DataSpec
import androidx.media3.datasource.DefaultDataSource
import androidx.media3.datasource.okhttp.OkHttpDataSource
import androidx.media3.datasource.ResolvingDataSource
import androidx.media3.datasource.cache.CacheDataSource
import androidx.media3.datasource.cache.LeastRecentlyUsedCacheEvictor
import androidx.media3.datasource.cache.SimpleCache
import androidx.media3.database.StandaloneDatabaseProvider
import com.catclaw.music.kt.core.net.RemoteHttp
import com.catclaw.music.kt.core.net.UrlCredentials
import java.io.File

/**
 * 远程音源的取流工厂。
 *
 * WebDAV 的播放地址是 `http://user:pass@host/dav/…` 形态（与 C# `BuildStreamUrl` 保持一致），
 * 但 `HttpURLConnection` 不会把 URL 里的 userinfo 变成 `Authorization` 头 ——
 * 真机上的表现是每一次 GET 都吃 401（曲库导入正常、就是不出声）。
 * 这里在解析 DataSpec 时把凭据摘出来，改发标准 Basic 头，URL 本身退回无凭据形态。
 *
 * 2026-11-12 起，http(s) 流走 media3 **CacheDataSource**：
 * ExoPlayer 直接流式起播（边下边播），同时把数据写进磁盘缓存 ——
 * 命中的歌秒开，重复播放不重复下载。此前 `moveToCurrent` 要求
 * `AudioCache.getOrCreate` 把**整首歌**下载完才允许 `playWhenReady`，
 * 这正是「网易云切歌慢」的主因；而且 ExoPlayer 并不读那份缓存，
 * 同一首歌被下载两遍。缓存 key 用 `MediaItem.customCacheKey`
 * （`netease:<id>`），网易云直链每次签名不同也能稳定命中。
 */
object RemoteAuthDataSources {

    const val CACHE_DIR_NAME = "player_cache"
    private const val DEFAULT_CACHE_MAX_BYTES = 1024L * 1024 * 1024 // 1GB，LRU 淘汰

    /** 播放缓存上限：跟随设置页的「缓存上限」，App 启动时写入（见 configureLimitMb） */
    @Volatile
    private var cacheMaxBytes = DEFAULT_CACHE_MAX_BYTES

    private var playerCache: SimpleCache? = null

    /**
     * 设置播放缓存上限。
     *
     * SimpleCache 的淘汰器只能在构造时给定，所以必须在**第一次播放之前**调用（App.onCreate）。
     * 已有实例时只记下新值，对当前进程不生效；调小后的旧数据由 [trimIfOverLimit] 在下次启动清掉。
     */
    fun configureLimitMb(mb: Int) {
        cacheMaxBytes = mb.coerceAtLeast(64).toLong() * 1024L * 1024L
    }

    /**
     * 启动时调用：上限调小后，目录里可能还留着上一轮（固定 1GB 时代）的旧缓存。
     * 此时 SimpleCache 还没创建、没有索引与锁，整目录删掉是安全的。
     */
    fun trimIfOverLimit(context: Context) {
        val dir = File(context.cacheDir, CACHE_DIR_NAME)
        if (!dir.isDirectory) return
        val used = dir.walkTopDown().filter { it.isFile }.sumOf { it.length() }
        if (used > cacheMaxBytes) runCatching { dir.deleteRecursively() }
    }

    /**
     * 清空播放缓存。
     *
     * 不能直接删目录：SimpleCache 是进程内单例、还被 CacheDataSource 引用着，
     * 删掉 `.exo` 文件而索引仍在，后续播放会把「有长度、没内容」的坏数据当命中。
     * `removeResource` 是它自带的安全删除，正在读的 span 也能处理。
     */
    fun clearCache() {
        val cache = synchronized(this) { playerCache } ?: return
        runCatching { cache.keys.toList().forEach { cache.removeResource(it) } }
    }

    /**
     * 播放取流的 TLS 与凭据都来自 [RemoteHttp] —— 全项目远程音乐的唯一 HTTP 入口。
     *
     * 为什么不直接 `DefaultHttpDataSource`：它走 `HttpURLConnection`，塞不进自定义
     * TrustManager，而 NAS 的自签证书在系统信任库里校验必失败（群晖/QNAP/Trim 全都一样）。
     * 所以这里用 [OkHttpDataSource] 承接 [RemoteHttp.client]。
     */
    private val remoteClient get() = RemoteHttp.client

    fun factory(context: Context): DataSource.Factory {
        val http = OkHttpDataSource.Factory(remoteClient)
        // 本地 file:// 与 content:// 也得能开：DefaultDataSource 按 scheme 分派，
        // 直接把解析器挂在 Http 工厂上会让本地曲全部播不出来
        val switching = DefaultDataSource.Factory(context, http)
        val resolving = ResolvingDataSource.Factory(switching) { dataSpec -> withBasicAuth(dataSpec) }
        val cached = CacheDataSource.Factory()
            .setCache(playerCache(context))
            .setUpstreamDataSourceFactory(resolving)
            // 出错时绕开缓存直连上游，失败不阻塞播放
            .setFlags(CacheDataSource.FLAG_IGNORE_CACHE_ON_ERROR or CacheDataSource.FLAG_BLOCK_ON_CACHE)
        return DataSource.Factory { SwitchingDataSource(context, cached, switching) }
    }

    /**
     * 不要缓存的取流工厂：分享导出这种「一次性整文件读取」用它。
     *
     * 不能复用 [factory]：播放器正在流式播放时，SimpleCache 里同一个 key 的 span 写锁
     * 被 CacheWriter 持有着，再开一条 CacheDataSource 去读会一直等锁（真机上表现为
     * 分享进度卡在已缓存的那几个百分点）。这里直接走鉴权后的上游，不碰缓存。
     */
    fun upstreamFactory(context: Context): DataSource.Factory {
        val http = OkHttpDataSource.Factory(remoteClient)
        val switching = DefaultDataSource.Factory(context, http)
        return ResolvingDataSource.Factory(switching) { dataSpec -> withBasicAuth(dataSpec) }
    }

    /** SimpleCache 必须全局唯一（目录有锁），这里用 object 属性兜住 */
    private fun playerCache(context: Context): SimpleCache =
        playerCache ?: synchronized(this) {
            playerCache ?: SimpleCache(
                File(context.cacheDir, CACHE_DIR_NAME),
                LeastRecentlyUsedCacheEvictor(cacheMaxBytes),
                StandaloneDatabaseProvider(context),
            ).also { playerCache = it }
        }

    private fun withBasicAuth(dataSpec: DataSpec): DataSpec {
        val (cleanUrl, header) = UrlCredentials.split(dataSpec.uri.toString())
        if (header == null) return dataSpec
        val builder = dataSpec.buildUpon()
        builder.setUri(Uri.parse(cleanUrl))
        builder.setHttpRequestHeaders(mapOf("Authorization" to header))
        return builder.build()
    }

    /** http(s) 走缓存流（边下边播），file/content 直读不进缓存 */
    private class SwitchingDataSource(
        context: Context,
        cachedFactory: DataSource.Factory,
        baseFactory: DataSource.Factory,
    ) : BaseDataSource(/* usesTag = */ false) {

        private val cachedSource = cachedFactory.createDataSource()
        private val baseSource = baseFactory.createDataSource()
        private var active: DataSource? = null

        override fun open(dataSpec: DataSpec): Long {
            val ds = when (dataSpec.uri.scheme?.lowercase()) {
                "http", "https" -> cachedSource
                else -> baseSource
            }.also { active = it }
            return ds.open(dataSpec)
        }

        override fun read(buffer: ByteArray, offset: Int, length: Int): Int =
            active?.read(buffer, offset, length) ?: -1

        override fun getUri(): Uri? = active?.uri

        override fun getResponseHeaders(): MutableMap<String, MutableList<String>> =
            active?.responseHeaders ?: mutableMapOf()

        override fun close() {
            active?.close()
            active = null
        }
    }
}
