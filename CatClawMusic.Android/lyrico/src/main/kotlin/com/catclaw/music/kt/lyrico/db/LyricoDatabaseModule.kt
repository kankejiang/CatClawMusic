package com.catclaw.music.kt.lyrico.db

import android.content.Context
import androidx.room.Room
import dagger.Module
import dagger.Provides
import dagger.hilt.InstallIn
import dagger.hilt.android.qualifiers.ApplicationContext
import dagger.hilt.components.SingletonComponent
import javax.inject.Singleton

/** Lyrico 独立数据库（`lyrico.db`）的 Hilt 装配。 */
@Module
@InstallIn(SingletonComponent::class)
object LyricoDatabaseModule {

    @Provides
    @Singleton
    fun database(@ApplicationContext context: Context): LyricoDatabase =
        Room.databaseBuilder(context, LyricoDatabase::class.java, LyricoDatabase.NAME)
            // 派生数据，开发期允许重建；发版前冻结 schema 并补迁移
            .fallbackToDestructiveMigration(true)
            .build()

    @Provides
    fun songDao(db: LyricoDatabase): LyricoSongDao = db.songDao()

    @Provides
    fun folderDao(db: LyricoDatabase): LyricoFolderDao = db.folderDao()

    @Provides
    fun sourcePluginDao(db: LyricoDatabase): LyricoSourcePluginDao = db.sourcePluginDao()

    @Provides
    fun pluginSubscriptionDao(db: LyricoDatabase): LyricoPluginSubscriptionDao =
        db.pluginSubscriptionDao()

    @Provides
    fun appLogDao(db: LyricoDatabase): LyricoAppLogDao = db.appLogDao()
}
