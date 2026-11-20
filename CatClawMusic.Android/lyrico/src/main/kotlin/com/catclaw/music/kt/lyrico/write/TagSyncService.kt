package com.catclaw.music.kt.lyrico.write

import com.catclaw.music.kt.data.db.LibraryDao
import com.catclaw.music.kt.data.db.entity.AlbumEntity
import com.catclaw.music.kt.data.db.entity.ArtistEntity
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.withContext

/**
 * 标签写回后同步曲库数据库。
 *
 * ## 为什么要同步
 *
 * 改标签只改了文件，而列表/搜索读的是 `catclaw.db` 的 `Songs` 表 —— 不同步的话，
 * 用户改完标题回到列表，看到的还是旧标题（要等下次扫描才变），会以为是写失败。
 *
 * ## 关系表怎么处理
 *
 * `Songs` 里的艺人/专辑是外键：歌手名改了要落到 `Artists` 行、专辑还要挂到对应艺人下。
 * 这里复用导入流程已有的「按名查、没有就插入」逻辑，保证与扫描入库的结果一致，
 * 而不是在 `Songs` 里塞一个对不上的名字。
 *
 * 注意**不改 `catclaw.db` 的表结构**（`SchemaParityTest` 锁着），只做既有表的数据更新。
 */
class TagSyncService(private val dao: LibraryDao) {

    /** @return 是否真的改动了数据库 */
    suspend fun sync(songId: Int, edited: EditableTags, currentGenre: String?, currentYear: Int, currentTrack: Int): Boolean =
        withContext(Dispatchers.IO) {
            val song = dao.songById(songId) ?: return@withContext false

            // 艺人：名改了才动关系
            var artistId = song.artistId
            if (edited.artist.isNotBlank()) {
                val existingId = dao.artistIdByName(edited.artist)
                artistId = existingId ?: dao.insertArtist(
                    ArtistEntity(name = edited.artist),
                ).toInt()
            }

            // 专辑：同名同艺人视为同一张，否则新建
            var albumId = song.albumId
            val albumTitle = edited.album.ifBlank { song.title }
            if (edited.album.isNotBlank() || edited.artist.isNotBlank()) {
                val existingAlbum = dao.albumIdByTitle(albumTitle, artistId)
                albumId = existingAlbum ?: dao.insertAlbum(
                    AlbumEntity(
                        title = albumTitle,
                        name = albumTitle,
                        artist = edited.artist,
                        year = yearOf(edited.date),
                        artistId = artistId,
                    ),
                ).toInt()
            }

            val newYear = yearOf(edited.date).takeIf { it > 0 } ?: currentYear
            val newTrack = edited.trackNumber.trim().toIntOrNull() ?: currentTrack
            val newGenre = edited.genre.takeIf { it.isNotBlank() } ?: currentGenre

            val changed = song.copy(
                title = edited.title.ifBlank { song.title },
                artistId = artistId,
                albumId = albumId,
                genre = newGenre,
                year = newYear,
                trackNumber = newTrack,
                dateModified = System.currentTimeMillis() / 1000,
            )
            if (changed == song) return@withContext false
            dao.upsertSong(changed)
            true
        }

    /** 从 `2026` / `2026-09-24` 里取年份；取不到返回 0 */
    private fun yearOf(date: String): Int =
        date.trim().take(4).toIntOrNull() ?: 0
}
