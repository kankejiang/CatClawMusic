package com.catclaw.music.kt.lyrico.db

import androidx.room.Dao
import androidx.room.Insert
import androidx.room.OnConflictStrategy
import androidx.room.Query
import androidx.room.Upsert

@Dao
interface LyricoSongDao {

    @Upsert
    suspend fun upsert(song: LyricoSongEntity): Long

    @Upsert
    suspend fun upsertAll(songs: List<LyricoSongEntity>)

    @Query("SELECT * FROM lyrico_song WHERE filePath = :filePath LIMIT 1")
    suspend fun byPath(filePath: String): LyricoSongEntity?

    @Query("SELECT * FROM lyrico_song WHERE id = :id")
    suspend fun byId(id: Long): LyricoSongEntity?

    @Query("SELECT * FROM lyrico_song ORDER BY title COLLATE NOCASE LIMIT :limit OFFSET :offset")
    suspend fun page(limit: Int, offset: Int): List<LyricoSongEntity>

    @Query("SELECT COUNT(*) FROM lyrico_song")
    suspend fun count(): Int

    @Query(
        "SELECT * FROM lyrico_song WHERE title LIKE '%' || :q || '%' " +
            "OR artist LIKE '%' || :q || '%' OR album LIKE '%' || :q || '%' " +
            "ORDER BY title COLLATE NOCASE LIMIT :limit",
    )
    suspend fun search(q: String, limit: Int): List<LyricoSongEntity>

    @Query("UPDATE lyrico_song SET lyricsOrigin = :origin, lyricsUpdatedAt = :at WHERE id = :id")
    suspend fun markLyricsUpdated(id: Long, origin: String?, at: Long)

    @Query("DELETE FROM lyrico_song WHERE filePath = :filePath")
    suspend fun deleteByPath(filePath: String)
}

@Dao
interface LyricoFolderDao {

    @Upsert
    suspend fun upsert(folder: LyricoFolderEntity): Long

    @Query("SELECT * FROM lyrico_folder ORDER BY addedAt")
    suspend fun all(): List<LyricoFolderEntity>

    @Query("SELECT * FROM lyrico_folder WHERE path = :path LIMIT 1")
    suspend fun byPath(path: String): LyricoFolderEntity?

    @Query("DELETE FROM lyrico_folder WHERE id = :id")
    suspend fun delete(id: Long)
}

@Dao
interface LyricoSourcePluginDao {

    @Upsert
    suspend fun upsert(plugin: LyricoSourcePluginEntity): Long

    @Query("SELECT * FROM lyrico_source_plugin ORDER BY pluginId")
    suspend fun all(): List<LyricoSourcePluginEntity>

    @Query("SELECT * FROM lyrico_source_plugin WHERE pluginId = :pluginId LIMIT 1")
    suspend fun byPluginId(pluginId: String): LyricoSourcePluginEntity?

    @Query("SELECT * FROM lyrico_source_plugin WHERE enabled = 1 ORDER BY pluginId")
    suspend fun enabled(): List<LyricoSourcePluginEntity>

    @Query("UPDATE lyrico_source_plugin SET enabled = :enabled WHERE pluginId = :pluginId")
    suspend fun setEnabled(pluginId: String, enabled: Boolean)

    @Query("DELETE FROM lyrico_source_plugin WHERE pluginId = :pluginId")
    suspend fun delete(pluginId: String)
}

@Dao
interface LyricoPluginSubscriptionDao {

    @Upsert
    suspend fun upsert(subscription: LyricoPluginSubscriptionEntity): Long

    @Query("SELECT * FROM lyrico_plugin_subscription ORDER BY id")
    suspend fun all(): List<LyricoPluginSubscriptionEntity>

    @Query("DELETE FROM lyrico_plugin_subscription WHERE id = :id")
    suspend fun delete(id: Long)
}

@Dao
interface LyricoAppLogDao {

    @Insert(onConflict = OnConflictStrategy.REPLACE)
    suspend fun insert(log: LyricoAppLogEntity)

    @Query("SELECT * FROM lyrico_app_log ORDER BY timestamp DESC LIMIT :limit")
    suspend fun recent(limit: Int = 500): List<LyricoAppLogEntity>

    @Query("DELETE FROM lyrico_app_log WHERE timestamp < :before")
    suspend fun deleteBefore(before: Long): Int

    @Query("DELETE FROM lyrico_app_log")
    suspend fun clear()
}
