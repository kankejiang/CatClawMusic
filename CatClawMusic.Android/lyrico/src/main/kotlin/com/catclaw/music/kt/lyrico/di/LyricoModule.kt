package com.catclaw.music.kt.lyrico.di

import android.content.Context
import com.catclaw.music.kt.data.lyrics.LyricsReader
import com.catclaw.music.kt.data.db.LibraryDao
import com.catclaw.music.kt.lyrico.batch.BatchProcessors
import com.catclaw.music.kt.lyrico.batch.BatchRunner
import com.catclaw.music.kt.lyrico.db.BatchItemDao
import com.catclaw.music.kt.lyrico.db.BatchTaskDao
import com.catclaw.music.kt.lyrico.db.LyricoSourcePluginDao
import com.catclaw.music.kt.lyrico.plugin.PluginCatalogClient
import com.catclaw.music.kt.lyrico.settings.LyricoSettingsStore
import com.catclaw.music.kt.lyrico.plugin.PluginRegistry
import com.catclaw.music.kt.data.lyrics.LyricsRefreshBus
import com.catclaw.music.kt.lyrico.search.LrclibClient
import com.catclaw.music.kt.lyrico.search.CoverSearchService
import com.catclaw.music.kt.lyrico.search.CoverWriter
import com.catclaw.music.kt.lyrico.search.LyricsSearchService
import com.catclaw.music.kt.lyrico.search.LyricoPluginSources
import com.catclaw.music.kt.lyrico.write.LyricsWriter
import com.catclaw.music.kt.lyrico.write.TagEditor
import com.catclaw.music.kt.lyrico.write.TagSyncService
import dagger.Module
import dagger.Provides
import dagger.hilt.InstallIn
import dagger.hilt.android.qualifiers.ApplicationContext
import dagger.hilt.components.SingletonComponent
import java.util.concurrent.TimeUnit
import javax.inject.Qualifier
import javax.inject.Singleton
import okhttp3.OkHttpClient

/** 源插件 / LRCLIB 用的 HTTP 客户端（与 `RemoteHttp` 的信任自签证书无关）。 */
@Qualifier
@Retention(AnnotationRetention.BINARY)
annotation class LyricoHttpClient

@Module
@InstallIn(SingletonComponent::class)
object LyricoModule {

    /**
     * 公网 API 专用 client。
     *
     * **刻意不复用 `RemoteHttp.client`**：那个 client 信任所有证书，只该用于用户自己
     * 配置的私有服务器；Lyrico 源与 LRCLIB 走公网，必须保留正常校验，否则等于给中间人开门。
     *
     * 超时给得比较宽：插件内部是「搜索 → 逐条详情 → 取词」多跳串行请求。
     */
    @Provides
    @Singleton
    @LyricoHttpClient
    fun provideLyricoHttpClient(): OkHttpClient = OkHttpClient.Builder()
        // callTimeout 是**整次调用**的总预算，覆盖 DNS 解析、连接、读写。
        // 只设 connect/read 挡不住「DNS 被黑洞」这种情况——实测 lrclib.net 解析得出但
        // 数据包全丢，没有 callTimeout 时一次请求能挂很久，批量任务直接卡住。
        .callTimeout(45, TimeUnit.SECONDS)
        .connectTimeout(15, TimeUnit.SECONDS)
        .readTimeout(30, TimeUnit.SECONDS)
        .writeTimeout(30, TimeUnit.SECONDS)
        .retryOnConnectionFailure(true)
        .build()

    @Provides
    @Singleton
    fun provideLrclibClient(@LyricoHttpClient client: OkHttpClient): LrclibClient =
        LrclibClient(client)

    @Provides
    @Singleton
    fun providePluginSources(
        @ApplicationContext context: Context,
        @LyricoHttpClient client: OkHttpClient,
        registry: PluginRegistry,
    ): LyricoPluginSources = LyricoPluginSources(
        context = context,
        okHttpClient = client,
        // 用户在插件页关掉的源不该继续参与搜索
        enabledIdsProvider = { registry.enabledPluginIds() },
        locale = {
            // 跟随系统语言；插件文案按 zh-Hans / en 回退
            val tag = java.util.Locale.getDefault().toLanguageTag()
            when {
                tag.startsWith("zh") -> "zh-Hans"
                tag.startsWith("en") -> "en"
                else -> tag
            }
        },
        logSink = { level, tag, msg ->
            // 同时进 logcat 与 AppLog 环形缓冲：用户在「诊断日志」页能直接看到插件报错，
            // 不必连电脑抓 logcat
            val fullTag = "Lyrico:$tag"
            when (level) {
                "error" -> com.catclaw.music.kt.core.diagnostic.AppLog.e(fullTag, msg)
                "warn" -> com.catclaw.music.kt.core.diagnostic.AppLog.w(fullTag, msg)
                else -> com.catclaw.music.kt.core.diagnostic.AppLog.d(fullTag, msg)
            }
        },
    )

    @Provides
    @Singleton
    fun provideSearchService(
        lrclib: LrclibClient,
        pluginSources: LyricoPluginSources,
    ): LyricsSearchService = LyricsSearchService(lrclib, pluginSources)

    @Provides
    @Singleton
    fun provideLyricsWriter(
        @ApplicationContext context: Context,
        lyricsReader: LyricsReader,
        refreshBus: LyricsRefreshBus,
    ): LyricsWriter = LyricsWriter(context, lyricsReader, refreshBus)
    @Provides
    @Singleton
    fun provideCoverSearchService(
        @LyricoHttpClient client: OkHttpClient,
        pluginSources: LyricoPluginSources,
    ): CoverSearchService = CoverSearchService(client, pluginSources)

    @Provides
    @Singleton
    fun provideCoverWriter(
        @ApplicationContext context: Context,
        coverSearch: CoverSearchService,
        coverVersion: com.catclaw.music.kt.data.media.CoverVersion,
        dao: LibraryDao,
    ): CoverWriter = CoverWriter(coverVersion, context, coverSearch, dao)

    @Provides
    @Singleton
    fun provideTagEditor(@ApplicationContext context: Context): TagEditor = TagEditor(context)

    @Provides
    @Singleton
    fun provideTagSyncService(
        dao: LibraryDao,
        refreshBus: com.catclaw.music.kt.lyrico.write.SongMetadataRefreshBus,
    ): TagSyncService = TagSyncService(dao, refreshBus)

    @Provides
    @Singleton
    fun provideBatchProcessors(
        searchService: LyricsSearchService,
        lyricsWriter: LyricsWriter,
        coverSearch: CoverSearchService,
        coverWriter: CoverWriter,
        tagEditor: TagEditor,
        tagSync: TagSyncService,
        pluginSources: LyricoPluginSources,
    ): BatchProcessors = BatchProcessors(
        lyricsSearch = searchService,
        lyricsWriter = lyricsWriter,
        coverSearch = coverSearch,
        coverWriter = coverWriter,
        tagEditor = tagEditor,
        tagSync = tagSync,
        pluginSources = pluginSources,
    )

    @Provides
    @Singleton
    fun provideBatchRunner(
        taskDao: BatchTaskDao,
        itemDao: BatchItemDao,
        libraryDao: LibraryDao,
        processors: BatchProcessors,
    ): BatchRunner = BatchRunner(taskDao, itemDao, libraryDao, processors)

    @Provides
    @Singleton
    fun providePluginRegistry(
        @ApplicationContext context: Context,
        sourcePluginDao: LyricoSourcePluginDao,
    ): PluginRegistry = PluginRegistry(
        context = context,
        dao = sourcePluginDao,
        locale = {
            val tag = java.util.Locale.getDefault().toLanguageTag()
            when {
                tag.startsWith("zh") -> "zh-Hans"
                tag.startsWith("en") -> "en"
                else -> tag
            }
        },
        logSink = { _, tag, msg ->
            com.catclaw.music.kt.core.diagnostic.AppLog.w("Lyrico:$tag", msg)
        },
    )

    @Provides
    @Singleton
    fun providePluginCatalogClient(
        @LyricoHttpClient client: OkHttpClient,
    ): PluginCatalogClient = PluginCatalogClient(client)

    @Provides
    @Singleton
    fun provideLyricoSettingsStore(
        @ApplicationContext context: Context,
    ): LyricoSettingsStore = LyricoSettingsStore(context)
}