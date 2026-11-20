package com.catclaw.music.kt.lyrico.di

import android.content.Context
import com.catclaw.music.kt.data.lyrics.LyricsReader
import com.catclaw.music.kt.data.lyrics.LyricsRefreshBus
import com.catclaw.music.kt.lyrico.search.LrclibClient
import com.catclaw.music.kt.lyrico.search.CoverSearchService
import com.catclaw.music.kt.lyrico.search.CoverWriter
import com.catclaw.music.kt.lyrico.search.LyricsSearchService
import com.catclaw.music.kt.lyrico.search.LyricoPluginSources
import com.catclaw.music.kt.lyrico.write.LyricsWriter
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
    ): LyricoPluginSources = LyricoPluginSources(
        context = context,
        okHttpClient = client,
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
            when (level) {
                "error", "warn" -> android.util.Log.w("Lyrico:$tag", msg)
                else -> android.util.Log.d("Lyrico:$tag", msg)
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
    ): CoverWriter = CoverWriter(context, coverSearch)
}