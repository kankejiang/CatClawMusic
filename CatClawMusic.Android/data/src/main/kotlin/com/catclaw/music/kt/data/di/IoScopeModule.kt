package com.catclaw.music.kt.data.di

import javax.inject.Qualifier
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.SupervisorJob
import dagger.Module
import dagger.Provides
import dagger.hilt.InstallIn
import dagger.hilt.components.SingletonComponent

/** 落库用的那条作用域（与后台计算分开，便于在测试里换掉） */
@Retention(AnnotationRetention.BINARY)
@Qualifier
annotation class IoScope

@Module
@InstallIn(SingletonComponent::class)
object IoScopeModule {
    /**
     * 单 worker 串行：`PlayHistoryRecorder` 的 `record` 是「先写库、再把会话 id 记进状态」两步，
     * 多 worker 会让后一首先写完、前一首后写完，状态里最终留下**前一首**的 id
     * ——之后 `sample()` 因为歌曲对不上一直早退，这一首的时长永远写不回库。
     */
    @Provides
    @IoScope
    fun ioScope(): CoroutineScope = CoroutineScope(SupervisorJob() + Dispatchers.IO.limitedParallelism(1))
}
