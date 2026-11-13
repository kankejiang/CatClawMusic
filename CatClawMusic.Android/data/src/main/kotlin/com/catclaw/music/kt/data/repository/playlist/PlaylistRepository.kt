package com.catclaw.music.kt.data.repository.playlist

import com.catclaw.music.kt.data.db.PlaylistCover
import com.catclaw.music.kt.data.db.PlaylistDao
import com.catclaw.music.kt.data.db.SongListItem
import com.catclaw.music.kt.data.db.entity.PlaylistEntity
import javax.inject.Inject
import javax.inject.Singleton
import kotlinx.coroutines.flow.Flow
import kotlinx.coroutines.flow.flowOf

/**
 * 本地歌单唯一数据入口（MVVM B3）：列表页与详情页共用。
 *
 * 只做聚合与转发；排序/搜索属于页面态，留在 ViewModel。
 */
@Singleton
class PlaylistRepository @Inject constructor(
    private val dao: PlaylistDao,
) {
    val playlists: Flow<List<PlaylistEntity>> = dao.observePlaylists()

    val covers: Flow<List<PlaylistCover>> = dao.observePlaylistCovers()

    /** 只订阅当前打开歌单的曲目；未打开时给空流 */
    fun items(playlistId: Int?): Flow<List<SongListItem>> =
        if (playlistId == null) flowOf(emptyList()) else dao.observeItems(playlistId)

    suspend fun byName(name: String): PlaylistEntity? = dao.byName(name)

    suspend fun create(name: String) {
        val now = System.currentTimeMillis()
        dao.upsert(PlaylistEntity(name = name, createdAt = now, updatedAt = now))
    }

    suspend fun rename(id: Int, name: String) {
        dao.rename(id, name, System.currentTimeMillis())
    }

    suspend fun delete(id: Int) {
        dao.clearItems(id)
        dao.delete(id)
    }

    suspend fun appendSongs(playlistId: Int, songIds: List<Int>) {
        if (songIds.isEmpty()) return
        dao.appendAll(playlistId, songIds)
    }

    suspend fun removeSong(playlistId: Int, songId: Int) {
        dao.removeFromPlaylist(playlistId, songId)
    }

    suspend fun reorder(orderedIds: List<Int>) {
        if (orderedIds.isEmpty()) return
        dao.reorderPlaylists(orderedIds)
    }
}
