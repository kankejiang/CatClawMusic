package com.catclaw.music.kt.core.update

import org.junit.Assert.assertEquals
import org.junit.Assert.assertNull
import org.junit.Test

/**
 * 版本比较与 release 解析的语义闸门 —— 逐条对齐 C# `UpdateService`。
 * 这几条判错的表现是"永远提示有更新"或"永远不提示"，都不会崩，只能靠测试钉。
 */
class UpdateRulesTest {

    @Test
    fun `版本号只比前三段，缺段与非数字段按零算`() {
        assertEquals(listOf(1, 8, 13), UpdateRules.parseVersion("1.8.13").toList())
        assertEquals(listOf(1, 8, 0), UpdateRules.parseVersion("1.8").toList())
        assertEquals(listOf(0, 0, 0), UpdateRules.parseVersion(null).toList())
        // 第四段被忽略是 C# 的原语义（1.8.13.4 不算比 1.8.13 新）
        assertEquals(0, UpdateRules.compareVersion("1.8.13.4", "1.8.13"))
        assertEquals(1, UpdateRules.compareVersion("1.8.14", "1.8.13"))
        assertEquals(1, UpdateRules.compareVersion("1.9.0", "1.8.99"))
        assertEquals(-1, UpdateRules.compareVersion("1.8.13", "1.8.14"))
        assertEquals(0, UpdateRules.compareVersion("1.8", "1.8.0"))
        // 脏数据不能抛，也不能被判成"有更新"
        assertEquals(0, UpdateRules.compareVersion("abc", "0.0.0"))
    }

    @Test
    fun `tag_name 前缀 v 剥掉，取不到就回 null 而不是空串`() {
        assertEquals("1.8.13", UpdateRules.latestVersionFrom("""{"tag_name":"v1.8.13"}"""))
        assertEquals("1.8.13", UpdateRules.latestVersionFrom("""{"tag_name":"1.8.13","name":"x"}"""))
        assertNull(UpdateRules.latestVersionFrom("""{"message":"Not Found"}"""))
        assertNull(UpdateRules.latestVersionFrom("""{"tag_name":""}"""))
        assertNull(UpdateRules.latestVersionFrom("""{"tag_name":"v"}"""))
        // 数字形态的 tag（GitHub 不会给，但解析器不该崩）与整段非 JSON 都算"没问到"
        assertNull(UpdateRules.latestVersionFrom("""{"tag_name":123}"""))
        assertNull(UpdateRules.latestVersionFrom("not json at all"))
        assertNull(UpdateRules.latestVersionFrom(null))
    }

    @Test
    fun `正控制：把比较改成恒真时这条必须红`() {
        // 这条本身是绿的；它保证上面两条断言的不是"恒等式"
        val newer = UpdateRules.compareVersion("1.8.14", "1.8.13")
        val older = UpdateRules.compareVersion("1.8.12", "1.8.13")
        assertEquals(1, newer)
        assertEquals(-1, older)
    }
}
