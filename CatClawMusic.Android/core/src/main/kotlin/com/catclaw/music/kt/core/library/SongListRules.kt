package com.catclaw.music.kt.core.library

import com.catclaw.music.kt.core.model.ConnectionProfile

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

/**
 * 「已停用的连接，它的歌不显示」。
 *
 * 语义逐条对齐 MAUI `MusicDatabase.FilterByEnabledProtocols`：
 * - 本地曲（Source=0）与缓存曲（Source=Cache）**永远显示** —— 它们不依赖任何连接；
 * - 其余远程曲看**协议**：该协议下只要还有一条启用的连接就显示，全停用才隐藏。
 *
 * ## 为什么按协议、不按连接
 *
 * `Songs` 表没有「来自哪条连接」的列（它的 schema 必须与 MAUI 线上库逐列一致，
 * 有 `SchemaParityTest` 钉着）。按连接过滤就得加列，而 MAUI 的选择是协议级粒度 ——
 * 这也是它能做到「零 schema 变更」的原因。
 *
 * 代价要说清：同一个协议配了**两条**连接时，关掉其中一条，另一条的曲目仍会显示，
 * 且无法区分「这首歌属于被关掉的那条」。要精确到连接必须加列，那是另一个决定。
 *
 * ## 为什么不做跨连接去重
 *
 * MAUI 明确不去重（`SearchAsync` 注释：「合并结果：本地+网络，不去重，保留同歌名不同版本」）。
 * 同一份文件经 WebDAV 与 Navidrome 各导一遍会出现两条，这是已知且被接受的行为 ——
 * 「万一用户就是要两个版本」的代价，小于「误判成重复把真版本藏掉」。
 */

/**
 * 「已停用的连接，它的歌不显示」。
 *
 * 语义逐条对齐 MAUI `MusicDatabase.FilterByEnabledProtocols`：
 * - 本地曲（`Source=Local`）与缓存曲（`Source=Cache`）**永远显示** —— 它们不依赖任何连接；
 * - 其余远程曲看**协议**：该协议下只要还有一条启用的连接就显示，全停用才隐藏。
 *
 * ## 为什么按协议、不按连接
 *
 * `Songs` 表没有「来自哪条连接」的列 —— 它的 schema 必须与 MAUI 线上库逐列一致
 * （`SchemaParityTest` 钉着 17 张表的每一列），加列就破。按连接过滤必须加列，
 * 而 MAUI 的选择是协议级粒度，这也是它能零 schema 变更的原因。
 *
 * 代价说清：同一协议配了**两条**连接时，关掉其中一条，另一条的曲目仍显示，
 * 且无法区分「这首歌属于被关掉的那条」。要精确到连接得加列，那是另一个决定。
 *
 * ## 为什么不做跨连接去重
 *
 * MAUI 明确不去重（`MusicLibraryService.SearchAsync` 的注释：
 * 「合并结果：本地+网络（不去重，保留同歌名不同版本）」）。
 * 同一份文件经 WebDAV 与 Navidrome 各导一遍会出现两条，这是已知且被接受的行为：
 * 宁可多一条，也不要误判成重复、把真版本藏掉。
 */
object ConnectionFilter {

    /**
     * @param enabledProtocols 至少有一条**启用**连接的协议集合（`ProtocolType.ordinal`）
     */
    fun <T> filterByEnabledProtocols(
        rows: List<T>,
        enabledProtocols: Set<Int>,
        sourceOf: (T) -> Int,
        protocolOf: (T) -> Int,
    ): List<T> {
        if (rows.isEmpty()) return rows
        return rows.filter { row ->
            if (isConnectionFree(sourceOf(row))) return@filter true
            enabledProtocols.contains(protocolOf(row))
        }
    }

    /**
     * 本地曲与缓存曲不依赖连接，永远显示。
     *
     * 判据是 `SongSource` 的 ordinal 契约（Local=0、WebDAV=1、SMB=2、Cache=3，
     * 由 `SourceOrdinalParityTest` 钉住）。**不要**写成「远程就是 1 或 2」——
     * 以后加一种远程协议就会静默落进"本地"那一档。
     */
    fun isConnectionFree(source: Int): Boolean = source == SOURCE_LOCAL || source == SOURCE_CACHE

    private const val SOURCE_LOCAL = 0
    private const val SOURCE_CACHE = 3

    /**
     * 从连接列表算出启用的协议集合。
     *
     * 放在 `:core` 而不是各调用方：规则改一次要全项目一致，
     * 漏一处就是「这个页面歌消失了、那个页面还在」。
     */
    fun enabledProtocols(profiles: List<ConnectionProfile>): Set<Int> {
        val out = HashSet<Int>()
        for (p in profiles) if (p.isEnabled) out += p.protocol.ordinal
        return out
    }
}

/**
 * 「按网络服务来源筛选」—— 只看某一类协议（WebDAV / Navidrome / SMB）的曲目。
 *
 * 和 [SongSourceFilter]（全部/本地/网络）是两个维度：
 * - `SongSourceFilter` 分**本地 vs 网络**；
 * - 这个再细分**网络里是哪一种服务**。
 *
 * ## 为什么按协议而不是按连接
 *
 * `Songs` 表只有 `Protocol`（协议类型），没有「来自哪条连接」的列 —— 那张表必须与
 * MAUI 线上库逐列一致（`SchemaParityTest` 钉着 17 张表的每一列）。所以粒度只能是协议级：
 * 同一协议配了两条连接时，这个筛选会把它们**一起**显示。这与「停用连接」的过滤是同一档粒度，
 * 两个筛选叠在一起不会互相矛盾。
 */
enum class NetworkProtocolFilter(
    /** 对应的 `ProtocolType.ordinal`；null 表示"不限" */
    val protocolOrdinal: Int?,
) {
    All(null),
    WebDAV(0),
    Navidrome(1),
    SMB(2),
    ;

    /**
     * @param protocol `ProtocolType.ordinal`
     * @param source `SongSource.ordinal`（本地/缓存没有网络服务归属，选具体协议时一律不显示）
     */
    fun accepts(protocol: Int, source: Int): Boolean {
        val want = protocolOrdinal ?: return true
        if (ConnectionFilter.isConnectionFree(source)) return false
        return protocol == want
    }

    companion object {
        /**
         * 由 `ProtocolType.ordinal` 反查筛选档，用于「当前列表里有哪些协议」这类场景。
         *
         * 这里**显式按 ordinal 匹配**而不是靠枚举相邻：`ProtocolType` 与
         * [NetworkProtocolFilter] 是两套枚举，靠 `ordinal ± 1` 对齐太脆
         * （任一边插一项就静默错位）。`NetworkProtocolFilterTest` 钉住这层对应。
         */
        fun of(protocolOrdinal: Int): NetworkProtocolFilter =
            entries.firstOrNull { it.protocolOrdinal == protocolOrdinal } ?: All
    }
}

/** 泛型版：调用方给投影，`:core` 不认 `:data` 的行类型 */
fun <T> filterByNetworkProtocol(
    rows: List<T>,
    filter: NetworkProtocolFilter,
    protocolOf: (T) -> Int,
    sourceOf: (T) -> Int,
): List<T> = if (filter == NetworkProtocolFilter.All) {
    rows
} else {
    rows.filter { filter.accepts(protocolOf(it), sourceOf(it)) }
}
