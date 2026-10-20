package com.catclaw.music.kt.core.ai

/** 快照里的一行艺人：名字 + 名下歌曲数 */
data class SnapshotArtist(val name: String, val songCount: Int)

/** 快照里的一行专辑 */
data class SnapshotAlbum(val title: String, val songCount: Int)

/** 快照里的一行歌（只要 id 用来去重） */
data class SnapshotSong(val id: Int, val title: String, val artist: String)

/**
 * 音乐库快照的文本格式 —— 对应 C# `Services/MusicLibrarySnapshotService.cs:20-95`。
 *
 * 这份文本会作为一条 system 消息注入对话前缀（`AgentService.awaitFixedPrefix` 里的 `[音乐库]`），
 * 所以它有两个硬约束：**紧凑**（每一行都占 token）与**只陈述事实**（模型据此判断库里有什么，
 * 编排错了它就会凭空报出根本不存在的歌）。抽取成纯函数是为了让这两点可测 ——
 * 排序、截断、去重全在算术里，不在 SQL 里。
 *
 * 与 C# 的两处口径差异，都是本端数据模型决定的：
 * - 艺人歌曲数按 `ArtistId` 统计（C# 是按歌曲行上的歌手**名字**分组），合作曲在两边落点不同；
 * - 专辑歌曲数取 `Albums.SongCount` 那一列（C# 现算），本端那张卡片一直用这一列。
 */
object LibrarySnapshotRules {

    const val TOP_ARTISTS = 10
    const val TOP_ALBUMS = 8
    const val TOP_SONGS = 20

    /** 「新增」最多几条。C# 先取最近 8 首、剔掉已进「常听」的，再取 5 首 */
    const val RECENT_TAKE = 5
    const val RECENT_CANDIDATES = 8

    /**
     * 拼快照文本。四段的顺序与 C# 一致：概览 / 艺人 / 专辑 / 常听 / 新增；
     * 某一段没有内容就整行不画（画一行「艺人:」后面空的，模型会以为库是空的）。
     *
     * 排序一律**稳定**降序：并列时保持传入顺序，与 C# `OrderByDescending` 的稳定性一致 ——
     * 否则同一份库两次生成的快照不同，服务端前缀缓存会白白失效。
     */
    fun build(
        songCount: Int,
        artistCount: Int,
        albumCount: Int,
        artists: List<SnapshotArtist>,
        albums: List<SnapshotAlbum>,
        topSongs: List<SnapshotSong>,
        recentSongs: List<SnapshotSong>,
    ): String {
        val sb = StringBuilder()
        sb.append("概览:").append(songCount).append("曲/").append(artistCount).append("艺人/")
            .append(albumCount).append("专辑").append('\n')

        val topArtistRows = artists
            .filter { it.name.isNotBlank() }
            .sortedByDescending { it.songCount }
            .take(TOP_ARTISTS)
        if (topArtistRows.isNotEmpty()) {
            sb.append("艺人:")
            sb.append(topArtistRows.joinToString(",") { "${it.name}(${it.songCount})" })
            sb.append('\n')
        }

        val topAlbumRows = albums
            .filter { it.title.isNotBlank() }
            .sortedByDescending { it.songCount }
            .take(TOP_ALBUMS)
        if (topAlbumRows.isNotEmpty()) {
            sb.append("专辑:")
            sb.append(topAlbumRows.joinToString(",") { "《${it.title}》(${it.songCount})" })
            sb.append('\n')
        }

        val songRows = topSongs.take(TOP_SONGS)
        if (songRows.isNotEmpty()) {
            sb.append("常听:")
            sb.append(songRows.joinToString(";") { "${it.title}-${it.artist}" })
            sb.append('\n')
        }

        // 「新增」要剔掉已经出现在「常听」里的：同一首在两段里各占一行，纯属浪费 token
        val topIds = songRows.map { it.id }.toSet()
        val fresh = recentSongs
            .filter { it.id !in topIds }
            .take(RECENT_TAKE)
        if (fresh.isNotEmpty()) {
            sb.append("新增:")
            sb.append(fresh.joinToString(";") { "${it.title}-${it.artist}" })
            sb.append('\n')
        }

        return sb.toString()
    }
}
