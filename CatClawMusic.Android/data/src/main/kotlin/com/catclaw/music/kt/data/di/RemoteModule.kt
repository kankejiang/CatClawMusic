package com.catclaw.music.kt.data.di

import com.catclaw.music.kt.data.media.AudioBytesReader
import com.catclaw.music.kt.data.media.HttpAudioBytesReader
import dagger.Module
import dagger.Provides
import dagger.hilt.InstallIn
import dagger.hilt.components.SingletonComponent
import javax.inject.Singleton

/**
 * 远程取头部字节的绑定。接口留在 `:data` 但实现分开，是因为
 * `RemoteMetadataBackfiller` 的选行、大小头重试、合并、建歌手专辑、批量落库
 * 全都能在 JVM 里用假实现测到底 —— 不需要网络，也不需要设备。
 */
@Module
@InstallIn(SingletonComponent::class)
object RemoteModule {

    @Provides
    @Singleton
    fun audioBytesReader(impl: HttpAudioBytesReader): AudioBytesReader = impl
}
