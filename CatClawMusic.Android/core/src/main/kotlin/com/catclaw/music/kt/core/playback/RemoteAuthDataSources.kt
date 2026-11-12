package com.catclaw.music.kt.core.playback

import android.content.Context
import android.net.Uri
import androidx.media3.datasource.BaseDataSource
import androidx.media3.datasource.DataSource
import androidx.media3.datasource.DataSpec
import androidx.media3.datasource.DefaultDataSource
import androidx.media3.datasource.DefaultHttpDataSource
import androidx.media3.datasource.ResolvingDataSource
import androidx.media3.datasource.cache.CacheDataSource
import androidx.media3.datasource.cache.LeastRecentlyUsedCacheEvictor
import androidx.media3.datasource.cache.SimpleCache
import androidx.media3.database.StandaloneDatabaseProvider
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

    private const val CACHE_DIR = "player_cache"
    private const val CACHE_MAX_BYTES = 1024L * 1024 * 1024 // 1GB，LRU 淘汰

    private var playerCache: SimpleCache? = null

    fun factory(context: Context): DataSource.Factory {
        val http = DefaultHttpDataSource.Factory()
            .setAllowCrossProtocolRedirects(true)
            .setConnectTimeoutMs(10_000)
            .setReadTimeoutMs(30_000)
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

    /** SimpleCache 必须全局唯一（目录有锁），这里用 object 属性兜住 */
    private fun playerCache(context: Context): SimpleCache =
        playerCache ?: synchronized(this) {
            playerCache ?: SimpleCache(
                File(context.cacheDir, CACHE_DIR),
                LeastRecentlyUsedCacheEvictor(CACHE_MAX_BYTES),
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
