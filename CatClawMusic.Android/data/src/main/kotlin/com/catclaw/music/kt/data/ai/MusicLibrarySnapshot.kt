package com.catclaw.music.kt.data.ai

import com.catclaw.music.kt.core.ai.LibrarySnapshotRules
import com.catclaw.music.kt.core.ai.SnapshotAlbum
import com.catclaw.music.kt.core.ai.SnapshotArtist
import com.catclaw.music.kt.core.ai.SnapshotSong
import com.catclaw.music.kt.data.db.LibraryDao
import javax.inject.Inject
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.withContext

/**
 * 音乐库快照：把库压成一段紧凑文本，供 AI 作为 system 消息读取（格式见 [LibrarySnapshotRules]）。
 *
 * C# 那边是「扫描后写一个 music_library.md，AI 读文件」（`MusicLibrarySnapshotService`），
 * 快照新鲜不新鲜取决于上一次扫描跑没跑。本端直接查库：组前缀时（每次对话开头一次）算一遍，
 * 代价是几条聚合查询，换来的是永远不用管「快照过期了没有」。
 *
 * 「常听」用 `frequentSongs`（按 `PlayHistory` 累计），与 C# 取 `Songs.PlayCount` 存量列不同 ——
 * 本端「常听」这套一直是按播放历史算的，快照没理由另立一个口径。
 */
class MusicLibrarySnapshot @Inject constructor(private val dao: LibraryDao) {

    suspend fun text(): String = withContext(Dispatchers.IO) {
        val artists = dao.artistsOnce()
        val albums = dao.albumsOnce()
        LibrarySnapshotRules.build(
            songCount = dao.songCount(),
            // 计数取自这两张列表本身：快照里的「N 艺人 / N 专辑」与页面上数的必须是同一批
            artistCount = artists.size,
            albumCount = albums.size,
            artists = artists.map { SnapshotArtist(it.name, it.songCount) },
            albums = albums.map { SnapshotAlbum(it.title, it.songCount) },
            // 常听取满上限（规则再截断），新增多取几首让规则剔重后还够 5 首 —— 与 C# 的两次 Take 一致
            topSongs = dao.frequentSongs(LibrarySnapshotRules.TOP_SONGS)
                .map { SnapshotSong(it.id, it.title, it.artist) },
            recentSongs = dao.newArrivals(LibrarySnapshotRules.RECENT_CANDIDATES)
                .map { SnapshotSong(it.id, it.title, it.artist) },
        )
    }
}
