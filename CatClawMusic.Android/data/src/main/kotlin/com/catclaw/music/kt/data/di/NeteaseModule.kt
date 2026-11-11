package com.catclaw.music.kt.data.di

import com.catclaw.music.kt.data.netease.NeteaseEapi
import com.catclaw.music.kt.data.netease.NeteaseEapiChannel
import dagger.Binds
import dagger.Module
import dagger.hilt.InstallIn
import dagger.hilt.components.SingletonComponent

/**
 * eapi 通道的绑定。接口 + @Inject 实现不会自动连起来，
 * 缺这条绑定报错会出现在 app 的 Hilt 聚合期，离真正缺绑定的地方很远。
 */
@Module
@InstallIn(SingletonComponent::class)
abstract class NeteaseModule {

    @Binds
    abstract fun bindNeteaseEapi(impl: NeteaseEapi): NeteaseEapiChannel
}
