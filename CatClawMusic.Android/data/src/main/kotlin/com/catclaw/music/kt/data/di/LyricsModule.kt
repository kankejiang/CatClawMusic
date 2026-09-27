package com.catclaw.music.kt.data.di

import com.catclaw.music.kt.core.lyrics.RemoteBytesFetcher
import com.catclaw.music.kt.core.lyrics.RemoteLyrics
import com.catclaw.music.kt.data.lyrics.OkHttpBytesFetcher
import dagger.Module
import dagger.Provides
import dagger.hilt.InstallIn
import dagger.hilt.components.SingletonComponent
import javax.inject.Singleton

/**
 * 远程侧车歌词这条链的绑定。接口在 :core（这样它能在 JVM 上被测），实现在 :data。
 * Dagger 不会自动把 @Inject 实现接到接口上 —— 少一条 @Provides，报错点会跑到 app 的聚合期。
 */
@Module
@InstallIn(SingletonComponent::class)
object LyricsModule {

    @Provides
    @Singleton
    fun remoteBytesFetcher(impl: OkHttpBytesFetcher): RemoteBytesFetcher = impl

    @Provides
    @Singleton
    fun remoteLyrics(fetcher: RemoteBytesFetcher): RemoteLyrics = RemoteLyrics(fetcher)
}
