package com.catclaw.music.kt.data.settings

import org.junit.Assert.assertEquals
import org.junit.Test

/**
 * 启动页选项 → ViewPager 页号。
 * C# 侧是 `MapStartupIndexToTabIndex`（MainPage.xaml.cs:215-224）再 +1 落到
 * `[FullLyrics 0, NowPlaying 1, Search 2, Playlist 3, Library 4]`，
 * 所以「歌单」这一档在启动页里根本到不了 —— 这条测试钉住映射，别哪天被"顺手补全"。
 */
class AppearanceStartupMappingTest {

    @Test
    fun libraryStartsOnTheLibraryPage() {
        assertEquals(4, AppearanceSettings.startupToPagerPage(0))
    }

    @Test
    fun exploreStartsOnTheSearchPage() {
        assertEquals(2, AppearanceSettings.startupToPagerPage(1))
    }

    @Test
    fun playingIsTheDefaultAndStartsOnNowPlaying() {
        assertEquals(2, AppearanceSettings.DEFAULT_STARTUP)
        assertEquals(1, AppearanceSettings.startupToPagerPage(AppearanceSettings.DEFAULT_STARTUP))
    }

    @Test
    fun unknownIndexFallsBackToNowPlaying() {
        assertEquals(1, AppearanceSettings.startupToPagerPage(7))
        assertEquals(1, AppearanceSettings.startupToPagerPage(-3))
    }

    @Test
    fun noIndexCanLandOnThePlaylistPage() {
        // MAUI 到不了页 3（歌单）；这里显式钉住，避免有人以为漏了入口
        assertEquals(false, (0..2).map { AppearanceSettings.startupToPagerPage(it) }.contains(3))
    }
}
