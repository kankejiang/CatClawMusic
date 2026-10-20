package com.catclaw.music.kt.core.library

import org.junit.Assert.assertEquals
import org.junit.Test

/**
 * 列表页的过滤 + 排序。行类型在这里换成一个最小结构，为的是让断言只说字段本身，
 * 不跟着 `:data` 的行类型走 —— 页面接线由真机取证那一路负责。
 *
 * 四条数据是故意挑的：两首标题只差大小写与后半段（call Me / Call You）、一对同歌手同专辑
 * 但时长与入库时间都不同的（Call You / Zebra）、一首哪一列都跟别人不同的（Apple）。
 */
class SongListRulesTest {

    private data class Row(
        val id: Int,
        val title: String,
        val artist: String,
        val album: String,
        val duration: Int,
        val added: Long,
        /** Source ordinal：0=本地，非 0 都是网络来源（见 SongSourceFilter.accepts） */
        val source: Int = 0,
    )

    private fun fields(row: Row) = SongSortFields(
        title = row.title,
        artist = row.artist,
        album = row.album,
        durationSeconds = row.duration,
        dateAdded = row.added,
        id = row.id,
    )

    private fun run(rows: List<Row>, key: SongSortKey, query: String, descending: Boolean = key.defaultDescending) =
        filterAndSortSongs(rows, key, query, descending = descending, fieldsOf = ::fields).map { it.title }

    private val rows = listOf(
        Row(1, "call Me", "A-Huge", "Zed", 100, 500),
        Row(2, "Call You", "small", "Alpha", 300, 500),
        Row(3, "Zebra", "small", "Alpha", 300, 900),
        Row(4, "Apple", "A-Huge", "Mid", 60, 100),
    )

    @Test
    fun queryMatchesTitleArtistOrAlbumIgnoringCase() {
        assertEquals("标题命中", listOf("call Me", "Call You"), run(rows, SongSortKey.Title, "call"))
        assertEquals("歌手命中，且忽略大小写", listOf("Apple", "call Me"), run(rows, SongSortKey.Title, "A-HUGE"))
        assertEquals("专辑命中", listOf("Call You", "Zebra"), run(rows, SongSortKey.Title, "alpha"))
        assertEquals("关键词两端的空白不算字", listOf("call Me", "Call You"), run(rows, SongSortKey.Title, "  call  "))
        assertEquals("空关键词不过滤", 4, run(rows, SongSortKey.Title, "").size)
        assertEquals("搜不到就是空列表", emptyList<String>(), run(rows, SongSortKey.Title, "nope"))
    }

    @Test
    fun everyKeyOrdersByItsOwnColumnThenATieBreaker() {
        assertEquals(
            "标题档要先折叠大小写，否则 Apple 会掉到 call Me 之后",
            listOf("Apple", "call Me", "Call You", "Zebra"),
            run(rows, SongSortKey.Title, ""),
        )
        assertEquals(
            "同歌手内按标题续排",
            listOf("Apple", "call Me", "Call You", "Zebra"),
            run(rows, SongSortKey.Artist, ""),
        )
        assertEquals(
            "专辑档看的是专辑：Alpha 两张在前，Zed 那张最后",
            listOf("Call You", "Zebra", "Apple", "call Me"),
            run(rows, SongSortKey.Album, ""),
        )
        assertEquals(
            "时长降序、并列时 id 倒序",
            listOf("Zebra", "Call You", "call Me", "Apple"),
            run(rows, SongSortKey.Duration, ""),
        )
        assertEquals(
            "入库时间新的在前、同秒时 id 倒序",
            listOf("Zebra", "Call You", "call Me", "Apple"),
            run(rows, SongSortKey.DateAdded, ""),
        )
    }

    @Test
    fun sameTimestampStillOrdersStably() {
        // 一次扫描入库的整库 DateAdded 都相同 —— 这一档若没有第二键，顺序就只剩"看运气"
        val sameSecond = (1..6).map { Row(it, "s$it", "a", "b", 10, 42L) }
        assertEquals(listOf("s6", "s5", "s4", "s3", "s2", "s1"), run(sameSecond, SongSortKey.DateAdded, ""))
        assertEquals(listOf("s1", "s2", "s3", "s4", "s5", "s6"), run(sameSecond.shuffled(), SongSortKey.Title, ""))
    }

    // ── 来源筛选（全部 / 本地 / 网络）────────────────────────────

    private fun sources(rows: List<Row>, filter: SongSourceFilter) =
        filterBySource(rows, filter) { it.source }

    private val mixed = listOf(
        Row(1, "本地的一首", "a", "b", 10, 1L, source = 0),
        Row(2, "WebDAV 的一首", "a", "b", 10, 2L, source = 1),
        Row(3, "SMB 的一首", "a", "b", 10, 3L, source = 2),
        Row(4, "缓存的一首", "a", "b", 10, 4L, source = 3),
    )

    @Test
    fun sourceFilterSplitsLocalFromEverythingElse() {
        assertEquals("「全部」不该动任何一行", listOf(1, 2, 3, 4), sources(mixed, SongSourceFilter.All).map { it.id })
        assertEquals("「本地」只看 Source = 0", listOf(1), sources(mixed, SongSourceFilter.Local).map { it.id })
        assertEquals(
            "「网络」是 != 0 —— 新增的远程协议必须自动落进这一档，而不是被算成本地",
            listOf(2, 3, 4), sources(mixed, SongSourceFilter.Network).map { it.id },
        )
    }

    @Test
    fun eachKeyHasItsOwnDefaultDirection() {
        // 与 C# `ToggleSort` 那句 `key != "added"` 同源：时间默认降序；
        // 时长本端一直是「长的在前」（C# 那边默认升序，但用户一拍就能翻，不值得为初值对齐）
        assertEquals(false, SongSortKey.Title.defaultDescending)
        assertEquals(false, SongSortKey.Artist.defaultDescending)
        assertEquals(false, SongSortKey.Album.defaultDescending)
        assertEquals(true, SongSortKey.Duration.defaultDescending)
        assertEquals(true, SongSortKey.DateAdded.defaultDescending)
    }

    @Test
    fun flippingTheDirectionReversesTheWholeOrder() {
        val up = run(rows, SongSortKey.Title, query = "", descending = false)
        val down = run(rows, SongSortKey.Title, query = "", descending = true)
        assertEquals("降序就是升序的整条倒过来 —— 并列那几条也要跟着倒，不能只翻主键", up.reversed(), down)
    }

    @Test
    fun theSameDirectionTwiceGivesTheSameList() {
        // 界面上的「点两次回到原样」全靠这条：排序不能带随机性
        assertEquals(
            run(rows, SongSortKey.Duration, query = "", descending = false),
            run(rows, SongSortKey.Duration, query = "", descending = false),
        )
    }

    @Test
    fun directionAlsoAppliesWhenAQueryIsActive() {
        val filtered = run(rows, SongSortKey.Title, query = "call", descending = true)
        assertEquals("过滤与方向是两件事：过滤结果同样要按方向排", listOf("Call You", "call Me"), filtered)
    }

    @Test
    fun sourceFilterAgreesWithTheSourceOrdinalContract() {
        // SourceOrdinalParityTest 钉住 Local=0 / WebDAV=1 / SMB=2 / Cache=3；
        // 这里只用 ordinal 的"是不是 0"这一条，避免和那边的枚举值重复声明
        assertEquals(true, SongSourceFilter.Local.accepts(0))
        assertEquals(false, SongSourceFilter.Local.accepts(1))
        assertEquals(true, SongSourceFilter.Network.accepts(99))
        assertEquals(true, SongSourceFilter.All.accepts(-1))
    }
}
