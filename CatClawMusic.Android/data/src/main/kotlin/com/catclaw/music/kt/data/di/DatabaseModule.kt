package com.catclaw.music.kt.data.di

import android.content.Context
import com.catclaw.music.kt.data.db.CatClawDatabase
import com.catclaw.music.kt.data.db.ChatDao
import com.catclaw.music.kt.data.db.ConnectionDao
import com.catclaw.music.kt.data.db.LibraryDao
import com.catclaw.music.kt.data.db.PlaylistDao
import dagger.Module
import dagger.Provides
import dagger.hilt.InstallIn
import dagger.hilt.android.qualifiers.ApplicationContext
import dagger.hilt.components.SingletonComponent
import javax.inject.Singleton

@Module
@InstallIn(SingletonComponent::class)
object DatabaseModule {

    @Provides
    @Singleton
    fun database(@ApplicationContext context: Context): CatClawDatabase = CatClawDatabase.create(context)

    @Provides
    @Singleton
    fun libraryDao(db: CatClawDatabase): LibraryDao = db.libraryDao()

    @Provides
    @Singleton
    fun playlistDao(db: CatClawDatabase): PlaylistDao = db.playlistDao()

    @Provides
    @Singleton
    fun connectionDao(db: CatClawDatabase): ConnectionDao = db.connectionDao()

    @Provides
    @Singleton
    fun chatDao(db: CatClawDatabase): ChatDao = db.chatDao()
}
