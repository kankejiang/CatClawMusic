package com.catclaw.music.kt.data.di

import android.content.Context
import com.catclaw.music.kt.core.ai.MusicSourceRegistry
import com.catclaw.music.kt.core.ai.SourceHttp
import com.catclaw.music.kt.core.ai.SourceStorage
import com.catclaw.music.kt.data.ai.OkHttpPageFetcher
import com.catclaw.music.kt.data.ai.SqliteWordSource
import com.catclaw.music.kt.data.ai.OkHttpSourceHttp
import com.catclaw.music.kt.data.ai.OkHttpWebGateway
import com.catclaw.music.kt.data.ai.PageFetcher
import com.catclaw.music.kt.data.ai.PrefsSourceStorage
import com.catclaw.music.kt.data.download.SourceDownloadManager
import dagger.Module
import dagger.Provides
import dagger.hilt.InstallIn
import dagger.hilt.android.qualifiers.ApplicationContext
import dagger.hilt.components.SingletonComponent
import javax.inject.Singleton

/**
 * `fetch_web_page` 的抓取实现绑定。
 * 接口 + @Inject 实现不会自动连起来 —— Dagger 要一条 @Provides/@Binds，
 * 否则报错点是 app 的 Hilt 聚合期，离真正缺绑定的地方很远，不好查。
 */
@Module
@InstallIn(SingletonComponent::class)
object AiTransportModule {

    @Provides
    @Singleton
    fun pageFetcher(): PageFetcher = OkHttpPageFetcher()

    @Provides
    @Singleton
    fun sourceHttp(): SourceHttp = OkHttpSourceHttp()

    @Provides
    @Singleton
    fun sourceStorage(@ApplicationContext context: Context): SourceStorage = PrefsSourceStorage(context)

    @Provides
    @Singleton
    fun musicSourceRegistry(storage: SourceStorage, http: SourceHttp): MusicSourceRegistry =
        MusicSourceRegistry(storage, http)

    @Provides
    @Singleton
    fun downloadEnqueuer(impl: SourceDownloadManager): com.catclaw.music.kt.core.ai.DownloadEnqueuer = impl

    @Provides
    @Singleton
    fun webFetchGateway(impl: OkHttpWebGateway): com.catclaw.music.kt.core.ai.WebFetchGateway = impl

    @Provides
    @Singleton
    fun wordSource(@ApplicationContext context: Context): com.catclaw.music.kt.core.ai.WordSource =
        SqliteWordSource(context)

    @Provides
    @Singleton
    fun yukiWordLibrary(source: com.catclaw.music.kt.core.ai.WordSource): com.catclaw.music.kt.core.ai.YukiWordLibrary =
        com.catclaw.music.kt.core.ai.YukiWordLibrary(source)

    @Provides
    @Singleton
    fun browserNavigator(impl: com.catclaw.music.kt.data.ai.WebViewBrowserNavigator):
        com.catclaw.music.kt.core.ai.BrowserNavigator = impl
}
