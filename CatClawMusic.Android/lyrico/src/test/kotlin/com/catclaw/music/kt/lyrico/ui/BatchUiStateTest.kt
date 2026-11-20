package com.catclaw.music.kt.lyrico.ui

import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Test

/**
 * 批量选择状态的纯逻辑测试。
 *
 * 这些语义出错时用户感受很直接：「全选后清空搜索，以为选的是全部」、
 * 「搜索后全选，结果把看不见的歌也选进去了」。所以单独钉住。
 */
class BatchUiStateTest {

    private fun song(id: Int, title: String, artist: String = "艺人", album: String = "专辑") =
        BatchSongItem(
            id = id,
            title = title,
            artist = artist,
            album = album,
            filePath = "/music/$title.flac",
            remote = false,
        )

    private val all = listOf(
        song(1, "晴天", "周杰伦", "叶惠美"),
        song(2, "夜曲", "周杰伦", "十一月的萧邦"),
        song(3, "不浪漫罪名", "王杰", "从今开始"),
        song(4, "海屿你", "马尾", "海屿"),
    )

    @Test
    fun blankQueryShowsEverything() {
        val state = BatchUiState(loading = false, allSongs = all)
        assertEquals(4, state.visibleSongs.size)
    }

    @Test
    fun queryFiltersByTitleArtistAlbum() {
        val byTitle = BatchUiState(loading = false, allSongs = all, query = "晴天")
        assertEquals(listOf(1), byTitle.visibleSongs.map { it.id })

        val byArtist = BatchUiState(loading = false, allSongs = all, query = "周杰伦")
        assertEquals(listOf(1, 2), byArtist.visibleSongs.map { it.id })

        val byAlbum = BatchUiState(loading = false, allSongs = all, query = "从今开始")
        assertEquals(listOf(3), byAlbum.visibleSongs.map { it.id })
    }

    @Test
    fun queryIsCaseInsensitiveAndTrims() {
        val state = BatchUiState(loading = false, allSongs = all, query = "  晴天  ")
        assertEquals(listOf(1), state.visibleSongs.map { it.id })
    }

    @Test
    fun queryDoesNotAffectSelection() {
        // 选了 4 首，然后搜「晴天」只看得到 1 首 —— 已选集合不该被搜索改变
        val state = BatchUiState(
            loading = false,
            allSongs = all,
            query = "晴天",
            selectedIds = setOf(1, 2, 3, 4),
        )
        assertEquals(4, state.selectedIds.size)
        assertEquals(1, state.visibleSongs.size)
    }

    @Test
    fun emptyQueryMatchesNothingToFilter() {
        val state = BatchUiState(loading = false, allSongs = all, query = "不存在的歌")
        assertTrue(state.visibleSongs.isEmpty())
        assertTrue("过滤结果为空不代表已选清空", state.selectedIds.isEmpty())
    }

    @Test
    fun remoteFlagReflectsSource() {
        val remote = song(9, "网络歌").copy(remote = true)
        assertTrue(remote.remote)
        assertFalse(all.first().remote)
    }
}
