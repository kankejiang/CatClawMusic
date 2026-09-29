package com.catclaw.music.kt.core.library

/**
 * 歌曲列表页的过滤 + 排序 —— 对应 MAUI `AllSongsViewModel` / `PlaylistDetailViewModel` 的 SortOptions。
 *
 * 放在内存里做而不是发 SQL，是因为列表页本来就把整张表读在手上（头部要显示总数、
 * 点一行要"从该位置起播这份列表"）。为排序再查一遍会让"看到的顺序"和"点下去播的顺序"来自两处，
 * 而这两处一旦不一致，用户看到的就是"明明排过了，播起来却是另一个顺序"。
 */
/**
 * 五档，与 MAUI 的两页 SortOptions 的并集对齐。
 * 没有「按播放次数」那一档：列表行里不带 PlayCount，为它给每条列表 SQL 加一列不划算，
 * 而摆一颗排不动的 chip 比少摆一档更糟。
 */
enum class SongSortKey { Title, Artist, Album, Duration, DateAdded }
/**
 * 参与比较的那几列。各页面的行类型不同（`:data` 的 `SongListItem`、歌单页的同一类型），
 * 而 `:core` 不依赖 `:data`，所以这里只声明字段、不认行类型 —— 由调用方给投影函数。
 */
data class SongSortFields(
    val title: String,
    val artist: String,
    val album: String,
    val durationSeconds: Int,
    val dateAdded: Long,
    val id: Int,
)

/**
 * 先按关键词过滤（标题 / 歌手 / 专辑任一含之，忽略大小写），再按档位排。
 *
 * 两处值得说明：
 * 1. 比较前一律 `lowercase()`：不折叠大小写时 "call" 与 "Call" 会分成两段，看着就像排错了。
 * 2. 每一档都带第二键。只按标题排，同名歌的相对顺序就取决于数据库给的回读顺序；
 *    「入库时间」更是如此 —— 一次扫描入库的歌 `DateAdded` 完全相同，没有第二键等于每次看都可能换个样。
 */
fun <T> filterAndSortSongs(
    rows: List<T>,
    key: SongSortKey,
    query: String,
    fieldsOf: (T) -> SongSortFields,
): List<T> {
    val q = query.trim()
    val filtered = if (q.isEmpty()) {
        rows
    } else {
        rows.filter {
            val f = fieldsOf(it)
            f.title.contains(q, ignoreCase = true) ||
                f.artist.contains(q, ignoreCase = true) ||
                f.album.contains(q, ignoreCase = true)
        }
    }
    return when (key) {
        SongSortKey.Title -> filtered.sortedWith(
            compareBy<T>({ fieldsOf(it).title.lowercase() }, { fieldsOf(it).id }),
        )

        SongSortKey.Artist -> filtered.sortedWith(
            compareBy<T>({ fieldsOf(it).artist.lowercase() }, { fieldsOf(it).title.lowercase() }),
        )

        SongSortKey.Album -> filtered.sortedWith(
            compareBy<T>({ fieldsOf(it).album.lowercase() }, { fieldsOf(it).title.lowercase() }),
        )

        // 长的在前 / 新的在前，第二键一律取 id 倒序：并列时给一个不会随回读顺序漂移的定序
        SongSortKey.Duration -> filtered.sortedWith(
            compareByDescending<T> { fieldsOf(it).durationSeconds }.thenByDescending { fieldsOf(it).id },
        )

        SongSortKey.DateAdded -> filtered.sortedWith(
            compareByDescending<T> { fieldsOf(it).dateAdded }.thenByDescending { fieldsOf(it).id },
        )
    }
}
