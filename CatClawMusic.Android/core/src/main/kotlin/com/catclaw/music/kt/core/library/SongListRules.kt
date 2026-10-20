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
 * 每一档的默认方向 —— 对应 C# `AllSongsViewModel.ToggleSort` 那句
 * `SortAscending = key != "added" && key != "plays"`（时间/次数默认降序）。
 *
 * 与 C# 的唯一差异是 `Duration`：它那边默认升序，本端一直是「长的在前」。
 * 这一档现在可以按方向键翻过来，所以保留本端的默认值、不去对齐一个用户随时能改的初值。
 */
val SongSortKey.defaultDescending: Boolean
    get() = when (this) {
        SongSortKey.Duration, SongSortKey.DateAdded -> true
        else -> false
    }
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
    /** 方向。默认取该档自己的默认方向；点同一颗 chip 就是把它翻过来 */
    descending: Boolean = key.defaultDescending,
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
    // 先按「升序」写一条比较器，再整体翻面 —— 这样每档都天然支持方向，
    // 也不会出现「只翻了主键、并列的那几条还用旧方向」的半截结果。
    // 每档都带第二键：只按标题排，同名歌的相对顺序就取决于数据库给的回读顺序；
    // 「入库时间」更是如此 —— 一次扫描入库的歌 DateAdded 完全相同，没有第二键等于每次看都可能换个样。
    val ascending: Comparator<T> = when (key) {
        SongSortKey.Title -> compareBy({ fieldsOf(it).title.lowercase() }, { fieldsOf(it).id })
        SongSortKey.Artist ->
            compareBy({ fieldsOf(it).artist.lowercase() }, { fieldsOf(it).title.lowercase() })
        SongSortKey.Album ->
            compareBy({ fieldsOf(it).album.lowercase() }, { fieldsOf(it).title.lowercase() })
        SongSortKey.Duration ->
            compareBy({ fieldsOf(it).durationSeconds }, { fieldsOf(it).id })
        SongSortKey.DateAdded ->
            compareBy({ fieldsOf(it).dateAdded }, { fieldsOf(it).id })
    }
    return filtered.sortedWith(if (descending) ascending.reversed() else ascending)
}

/** 「全部 / 本地 / 网络」来源筛选三档 —— 与 MAUI `AllSongsViewModel.FilterMode` 同集合、同语义 */
enum class SongSourceFilter { All, Local, Network }

/**
 * 判据只看 `Source` 的 ordinal 契约：Local=0、WebDAV=1、SMB=2、Cache=3
 * （由 `SourceOrdinalParityTest` 钉住）。
 *
 * 「网络」写成 `!= 0` 而不是枚举某个来源：以后再加一种远程协议（URL 直下之类）不用回来改这里，
 * 反过来写成 `source == 1 || source == 2` 就会让新来源静默落进"本地"那一档。
 */
fun SongSourceFilter.accepts(source: Int): Boolean = when (this) {
    SongSourceFilter.All -> true
    SongSourceFilter.Local -> source == 0
    SongSourceFilter.Network -> source != 0
}

/** 泛型与 [filterAndSortSongs] 同理：`:core` 不认 `:data` 的行类型，由调用方给投影 */
fun <T> filterBySource(rows: List<T>, filter: SongSourceFilter, sourceOf: (T) -> Int): List<T> =
    if (filter == SongSourceFilter.All) rows else rows.filter { filter.accepts(sourceOf(it)) }
