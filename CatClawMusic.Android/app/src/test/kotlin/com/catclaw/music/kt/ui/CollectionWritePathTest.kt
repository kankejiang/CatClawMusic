package com.catclaw.music.kt.ui

import java.io.File
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Test

/**
 * 「有读必有写」闸门。
 *
 * 这条规矩是被收藏坑出来的：`Favorites` 表、`observeFavorites()`、音乐库的「我喜欢的」统计格、
 * 点一行就能播放的列表——全都齐了，唯独没有任何地方往里写。
 * 于是那张表永远为空，用户点进去永远看到空状态，而编译与全部单测都是绿的。
 *
 * 所以规则只看一件事：**app 层源码里必须存在对收藏的写调用**。
 * （只看 :app 是因为 DAO 接口本身必然声明这些方法，扫到 :data 会永远为真。）
 */
class CollectionWritePathTest {

    private val appUiRoot by lazy {
        File("src/main/kotlin/com/catclaw/music/kt/ui").takeIf { it.isDirectory }
            ?: File("app/src/main/kotlin/com/catclaw/music/kt/ui").takeIf { it.isDirectory }
            ?: error("找不到 ui 源码目录（测试工作目录应为 :app 模块根）")
    }

    private val appSources: Map<String, String> by lazy {
        appUiRoot.walkTopDown().filter { it.extension == "kt" }
            .associate { it.name to it.readText() }
            .also { assertTrue("只读到 ${it.size} 个文件，八成路径错了", it.size >= 10) }
    }

    /** 规则写成纯函数，好同时配正证与反证 */
    private fun favoritesAreWritable(sources: Map<String, String>): List<String> =
        sources.filter { (_, body) -> body.contains("upsertFavorite(") || body.contains("removeFavorite(") }
            .keys.toList().sorted()

    @Test
    fun favoriteTableHasAWritePathInTheAppLayer() {
        val writers = favoritesAreWritable(appSources)
        assertEquals("收藏表在 app 层没有写入口——「我喜欢的」会是永久空页", listOf("LibraryViewModel.kt"), writers)
    }

    @Test
    fun writePathRuleActuallyFailsWhenTheWriterIsGone() {
        // 反证：把写调用删掉，规则必须变红。规则若恒真，它就不是闸门。
        val withoutWriter = appSources.filterKeys { it != "LibraryViewModel.kt" }
        assertFalse("闸门抓不到「只剩读」的写法", favoritesAreWritable(withoutWriter).isNotEmpty())
    }

    @Test
    fun theFavoriteWriterIsReachedFromASongRow() {
        // 写入口存在还不够，得真的挂在行上：长按 → 菜单 → toggleFavorite
        assertTrue(
            "歌曲行没有长按手势，菜单就是死代码",
            appSources.values.any { it.contains("combinedClickable(") },
        )
        assertTrue(
            "SongActionMenu 没有被任何页面渲染",
            appSources.values.any { it.contains("SongActionMenu(") },
        )
        assertTrue(
            "菜单的收藏项没接到写调用上",
            appSources.values.any { it.contains("onToggleFavorite =") },
        )
    }
}
