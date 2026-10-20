package com.catclaw.music.kt.data.db

import com.catclaw.music.kt.data.db.entity.PlaylistEntity
import kotlinx.coroutines.flow.Flow

/**
 * `PlaylistDao` 的共享测试替身：**每个成员默认抛异常**，用得到哪个就在测试里覆写哪个。
 *
 * 为什么要有这一层：在此之前每个测试各写一份完整实现（工具测试一份、队列统计一份），
 * 于是每次给 `PlaylistDao` 加方法，都要手动去补两处 —— 已经因此编译失败过两次，
 * 而失败信息指向的是测试文件「没有实现抽象成员」，和真正改动的 DAO 隔着一层。
 *
 * 默认抛而不是默认返回空值，是刻意选的：默认返回 `emptyList()` 会让「这个测试其实走到了
 * 一条它没准备过的路径」表现成安静的通过，而抛异常会当场指出来。
 */
open class FakePlaylistDao : PlaylistDao {

    protected fun nope(what: String): Nothing =
        throw UnsupportedOperationException("这个测试不该用到 PlaylistDao.$what")

    // 接口里**带默认实现**的那几个（removeFromPlaylist / reorder / reorderPlaylists / appendAll）
    // 这里故意不覆写：它们的默认体本来就是真行为（内部再调 removeItem / touch / updatePosition 这些原语），
    // 而原语在上面已经默认抛了。第一版把它们也改成抛，当场弄红了一条依赖默认体的测试
    // （`removeSongRewritesCount`：它要验的正是 `removeFromPlaylist` 顺手改 SongCount 那段）。

    override fun observePlaylists(): Flow<List<PlaylistEntity>> = nope("observePlaylists")
    override suspend fun byId(id: Int): PlaylistEntity? = nope("byId")
    override suspend fun byName(name: String): PlaylistEntity? = nope("byName")
    override suspend fun upsert(playlist: PlaylistEntity): Long = nope("upsert")
    override suspend fun delete(id: Int): Unit = nope("delete")
    override suspend fun rename(id: Int, name: String, now: Long): Int = nope("rename")
    override suspend fun updateSortOrder(id: Int, order: Int): Unit = nope("updateSortOrder")
    override suspend fun clearItems(id: Int): Unit = nope("clearItems")
    override suspend fun insertItem(playlistId: Int, songId: Int, position: Int): Unit = nope("insertItem")
    override suspend fun removeItem(playlistId: Int, songId: Int): Unit = nope("removeItem")

    override suspend fun songIdsInOrder(playlistId: Int): List<Int> = nope("songIdsInOrder")
    override fun observeItems(playlistId: Int): Flow<List<SongListItem>> = nope("observeItems")
    override suspend fun touch(id: Int, count: Int, now: Long): Unit = nope("touch")

    override suspend fun updatePosition(playlistId: Int, songId: Int, position: Int): Unit = nope("updatePosition")

    override suspend fun maxPosition(playlistId: Int): Int? = nope("maxPosition")
    override fun observePlaylistCovers(): Flow<List<PlaylistCover>> = nope("observePlaylistCovers")
}
