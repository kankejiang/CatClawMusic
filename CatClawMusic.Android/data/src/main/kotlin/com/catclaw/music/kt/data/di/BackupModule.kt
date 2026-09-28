package com.catclaw.music.kt.data.di

import android.content.Context
import com.catclaw.music.kt.data.backup.BackupService
import com.catclaw.music.kt.data.backup.RoomBackupStore
import dagger.Module
import dagger.Provides
import dagger.hilt.InstallIn
import dagger.hilt.android.qualifiers.ApplicationContext
import dagger.hilt.components.SingletonComponent
import java.io.File

@Module
@InstallIn(SingletonComponent::class)
object BackupModule {

    /**
     * 封面缓存目录跟着 store 走，两处必须同一个路径，否则备份出来的 covers 与 App 读的 covers 不是同一堆。
     * 服务本身无状态，单例只为省掉重复构造。
     */
    @Provides
    fun backupService(store: RoomBackupStore, @ApplicationContext context: Context): BackupService =
        BackupService(store, File(context.filesDir, COVER_DIR))

    const val COVER_DIR = "artist_covers"
}
