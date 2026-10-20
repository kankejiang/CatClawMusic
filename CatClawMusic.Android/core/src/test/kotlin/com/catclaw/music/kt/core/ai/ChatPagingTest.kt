package com.catclaw.music.kt.core.ai

import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Test

class ChatPagingTest {

    @Test
    fun loadsWhenTheOldestVisibleItemEntersThePrefetchWindow() {
        // 一页 30 条：看到第 27 条（倒数第 3）就该开始预取，第 26 条还不用
        assertFalse(ChatPaging.shouldLoadOlder(lastVisibleIndex = 26, itemCount = 30))
        assertTrue(ChatPaging.shouldLoadOlder(lastVisibleIndex = 27, itemCount = 30))
        assertTrue(ChatPaging.shouldLoadOlder(lastVisibleIndex = 29, itemCount = 30))
    }

    @Test
    fun anUnlaidOutListNeverTriggersPaging() {
        // 首帧 layoutInfo 还是空的（lastVisibleIndex = -1）：此刻触发只会白查一次数据库
        assertFalse(ChatPaging.shouldLoadOlder(lastVisibleIndex = -1, itemCount = 30))
        assertFalse(ChatPaging.shouldLoadOlder(lastVisibleIndex = 0, itemCount = 0))
    }

    @Test
    fun shortHistoriesLoadImmediatelyWhenTheTailIsVisible() {
        // 历史不满一屏（2 条）时末项天然可见 —— 该继续翻，别让用户以为就这么点
        assertTrue(ChatPaging.shouldLoadOlder(lastVisibleIndex = 1, itemCount = 2))
    }
}
