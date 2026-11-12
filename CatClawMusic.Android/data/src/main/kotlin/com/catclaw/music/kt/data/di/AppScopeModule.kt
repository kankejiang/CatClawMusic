package com.catclaw.music.kt.data.di

import dagger.Module
import dagger.Provides
import dagger.hilt.InstallIn
import dagger.hilt.components.SingletonComponent
import javax.inject.Qualifier
import javax.inject.Singleton
import kotlinx.coroutines.CoroutineDispatcher
import kotlinx.coroutines.Dispatchers

/**
 * 跨页状态作用域：Repository 的热流与后台任务用它，避免各自 new `CoroutineScope`。
 *
 * 由 `:app` 的 `PlaybackModule.appScope()` 提供实例（同一个播放器作用域），
 * 这里只放 qualifier 与 IO dispatcher：`:data` 不能反向依赖 `:app`。
 */
@Qualifier
@Retention(AnnotationRetention.BINARY)
annotation class AppScope

/** IO 调度器：Repository 里所有磁盘/数据库/文件遍历都必须显式走它 */
@Qualifier
@Retention(AnnotationRetention.BINARY)
annotation class IoDispatcher

@Module
@InstallIn(SingletonComponent::class)
object AppScopeModule {

    @Provides
    @Singleton
    @IoDispatcher
    fun ioDispatcher(): CoroutineDispatcher = Dispatchers.IO
}
