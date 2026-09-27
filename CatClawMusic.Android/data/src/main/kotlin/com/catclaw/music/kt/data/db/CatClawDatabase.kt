package com.catclaw.music.kt.data.db

import android.content.Context
import androidx.room.Database
import androidx.room.Room
import androidx.room.RoomDatabase
import com.catclaw.music.kt.data.db.entity.AlbumEntity
import com.catclaw.music.kt.data.db.entity.ArtistEntity
import com.catclaw.music.kt.data.db.entity.CachedSongEntity
import com.catclaw.music.kt.data.db.entity.ChatMessageEntity
import com.catclaw.music.kt.data.db.entity.ConnectionProfileEntity
import com.catclaw.music.kt.data.db.entity.DailyPlayStatEntity
import com.catclaw.music.kt.data.db.entity.DailySongStatEntity
import com.catclaw.music.kt.data.db.entity.FavoriteEntity
import com.catclaw.music.kt.data.db.entity.HourlyPlayStatEntity
import com.catclaw.music.kt.data.db.entity.LyricsEntity
import com.catclaw.music.kt.data.db.entity.MigrationFlagEntity
import com.catclaw.music.kt.data.db.entity.PlayHistoryEntity
import com.catclaw.music.kt.data.db.entity.PlaySessionEntity
import com.catclaw.music.kt.data.db.entity.PlaylistEntity
import com.catclaw.music.kt.data.db.entity.PlaylistSongEntity
import com.catclaw.music.kt.data.db.entity.SongArtistEntity
import com.catclaw.music.kt.data.db.entity.SongEntity

/**
 * 17 张业务表，与 MAUI 线上库 `catclaw.db` 的真实 schema 一一对应
 * （参考 `docs-schema/catclaw.reference.sqlite.sql`；`:data` 的 SchemaParityTest 会把
 * Room 导出的 schema JSON 与该文件逐表逐列比对）。
 */
@Database(
    entities = [
        SongEntity::class,
        ArtistEntity::class,
        AlbumEntity::class,
        SongArtistEntity::class,
        PlaylistEntity::class,
        PlaylistSongEntity::class,
        FavoriteEntity::class,
        LyricsEntity::class,
        PlayHistoryEntity::class,
        PlaySessionEntity::class,
        DailyPlayStatEntity::class,
        DailySongStatEntity::class,
        HourlyPlayStatEntity::class,
        ConnectionProfileEntity::class,
        CachedSongEntity::class,
        ChatMessageEntity::class,
        MigrationFlagEntity::class,
    ],
    version = 1,
    exportSchema = true,
)
abstract class CatClawDatabase : RoomDatabase() {

    abstract fun libraryDao(): LibraryDao

    abstract fun playlistDao(): PlaylistDao

    abstract fun connectionDao(): ConnectionDao

    abstract fun chatDao(): ChatDao

    companion object {
        const val NAME = "catclaw.db"

        fun create(context: Context): CatClawDatabase =
            Room.databaseBuilder(context.applicationContext, CatClawDatabase::class.java, NAME)
                .build()
    }
}
