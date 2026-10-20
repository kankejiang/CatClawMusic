package com.catclaw.music.kt.core.ai

import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Test

/**
 * 快照文本的格式与取舍。这份文本会进 system 前缀，格式错了模型就会误判库里有什么；
 * 而它又长又碎，靠人在真机上读一眼是验不出来的。
 */
class LibrarySnapshotRulesTest {

    private fun song(id: Int, title: String, artist: String = "某歌手") = SnapshotSong(id, title, artist)

    @Test
    fun overviewAlwaysComesFirstAndCarriesAllThreeCounts() {
        val text = LibrarySnapshotRules.build(
            songCount = 1234, artistCount = 56, albumCount = 78,
            artists = emptyList(), albums = emptyList(), topSongs = emptyList(), recentSongs = emptyList(),
        )
        assertEquals("概览:1234曲/56艺人/78专辑\n", text)
    }

    @Test
    fun emptySectionsAreOmittedInsteadOfPrintedAsBareLabels() {
        val text = LibrarySnapshotRules.build(
            songCount = 0, artistCount = 0, albumCount = 0,
            artists = emptyList(), albums = emptyList(), topSongs = emptyList(), recentSongs = emptyList(),
        )
        assertEquals("库里什么都没有时只剩概览那一行", "概览:0曲/0艺人/0专辑\n", text)
        assertFalse("空段不能留下一个光秃秃的标签", text.contains("艺人:"))
    }

    @Test
    fun sectionsAreTruncatedToTheirLimitsAndSortedByCount() {
        val artists = (1..15).map { SnapshotArtist("艺人$it", it) }
        val albums = (1..12).map { SnapshotAlbum("专辑$it", it) }
        val text = LibrarySnapshotRules.build(
            songCount = 100, artistCount = 15, albumCount = 12,
            artists = artists, albums = albums,
            topSongs = (1..30).map { song(it, "歌$it") },
            recentSongs = emptyList(),
        )
        val byLine = text.lines().associateBy { it.substringBefore(':') }
        assertEquals("艺人取 10 个，按歌曲数降序", 10, byLine.getValue("艺人").removePrefix("艺人:").split(",").size)
        assertTrue(byLine.getValue("艺人").startsWith("艺人:艺人15(15)"))
        assertEquals(8, byLine.getValue("专辑").removePrefix("专辑:").split(",").size)
        assertEquals(20, byLine.getValue("常听").removePrefix("常听:").split(";").size)
    }

    @Test
    fun blankArtistNamesAndSilentDecorativeText() {
        val text = LibrarySnapshotRules.build(
            songCount = 2, artistCount = 2, albumCount = 0,
            artists = listOf(SnapshotArtist("", 5), SnapshotArtist("真名", 1)),
            albums = listOf(SnapshotAlbum("  ", 3)),
            topSongs = listOf(song(1, "A"), song(2, "B")),
            recentSongs = emptyList(),
        )
        assertTrue("没有名字的艺人不进快照", text.contains("艺人:真名(1)"))
        assertFalse("空白专辑名同样不进", text.contains("专辑:"))
    }

    @Test
    fun recentLineSkipsSongsAlreadyListedAsTopPlayed() {
        val text = LibrarySnapshotRules.build(
            songCount = 5, artistCount = 1, albumCount = 1,
            artists = emptyList(),
            albums = emptyList(),
            topSongs = listOf(song(1, "老歌"), song(2, "也老")),
            // 最近入库的 8 首里有 2 首已经在「常听」里，剩下 6 首取 5 首
            recentSongs = listOf(song(1, "老歌"), song(2, "也老")) + (3..8).map { song(it, "新$it") },
        )
        val fresh = text.lines().first { it.startsWith("新增:") }.removePrefix("新增:")
        assertEquals("剔掉重复后只取 5 首", 5, fresh.split(";").size)
        assertFalse("已经在「常听」里的不许在「新增」里再来一次", fresh.contains("老歌"))
        assertEquals("新3-某歌手;新4-某歌手;新5-某歌手;新6-某歌手;新7-某歌手", fresh)
    }
}
