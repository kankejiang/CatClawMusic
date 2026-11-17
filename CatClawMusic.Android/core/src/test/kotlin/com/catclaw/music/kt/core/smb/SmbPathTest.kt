package com.catclaw.music.kt.core.smb

import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Test

/**
 * SMB 共享内路径的层级运算。
 *
 * 这些判断直接决定「浏览目录」能不能用：写错的表现是「上一级点不动」
 * 「根目录还亮着上一级按钮」「选中的路径多一个反斜杠」——
 * 都要靠真机逐层点才发现，所以逐条钉住。
 */
class SmbPathTest {

    @Test
    fun `normalize 统一反斜杠`() {
        assertEquals("\\专辑", SmbPath.normalize("\\专辑"))
        assertEquals("\\专辑", SmbPath.normalize("/专辑"))
        assertEquals("\\a\\b", SmbPath.normalize("a/b"))
        assertEquals("\\a\\b", SmbPath.normalize("\\a\\b\\"))
    }

    @Test
    fun `normalize 空串与纯分隔符归为根`() {
        assertEquals(SmbPath.ROOT, SmbPath.normalize(""))
        assertEquals(SmbPath.ROOT, SmbPath.normalize("/"))
        assertEquals(SmbPath.ROOT, SmbPath.normalize("\\"))
        assertEquals(SmbPath.ROOT, SmbPath.normalize("\\\\"))
    }

    @Test
    fun `isRoot 认得各种根写法`() {
        assertTrue(SmbPath.isRoot(""))
        assertTrue(SmbPath.isRoot("/"))
        assertTrue(SmbPath.isRoot("\\"))
        assertFalse(SmbPath.isRoot("\\专辑"))
    }

    @Test
    fun `parent 逐级上升`() {
        assertEquals("\\a", SmbPath.parent("\\a\\b"))
        assertEquals("\\a\\b", SmbPath.parent("\\a\\b\\c"))
        assertEquals(SmbPath.ROOT, SmbPath.parent("\\a"))
    }

    @Test
    fun `parent 在根目录返回根，不会变负`() {
        assertEquals(SmbPath.ROOT, SmbPath.parent(SmbPath.ROOT))
        assertEquals(SmbPath.ROOT, SmbPath.parent(""))
        assertEquals(SmbPath.ROOT, SmbPath.parent("/"))
        // 幂等：反复上升仍停在根
        assertEquals(SmbPath.ROOT, SmbPath.parent(SmbPath.parent(SmbPath.ROOT)))
    }

    @Test
    fun `parent 对混用分隔符的输入也正确`() {
        // 用户手填过 "a/b/c" → 规范成 \a\b\c，上一级是 \a\b（不是 \a）
        assertEquals("\\a\\b", SmbPath.parent("a/b/c"))
        assertEquals("\\a", SmbPath.parent("a/b"))
        assertEquals(SmbPath.ROOT, SmbPath.parent("a"))
    }

    @Test
    fun `name 取当前层`() {
        assertEquals("b", SmbPath.name("\\a\\b"))
        assertEquals("a", SmbPath.name("\\a"))
        assertEquals(SmbPath.ROOT, SmbPath.name(SmbPath.ROOT))
    }

    @Test
    fun `反斜杠不是斜杠的子串，两套 path 函数不能混用`() {
        // 这正是当初把 SmbPath 单独立一类的理由：
        // 对 "\a\b" 用 trimEnd('/') 或 substringBeforeLast('/') 会原样返回，
        // 于是「上一级」变成空操作 —— 静默失效。
        val smb = "\\a\\b"
        assertEquals(smb, smb.trimEnd('/'))            // 未按预期切掉任何东西
        assertEquals("", smb.substringBeforeLast('/', "")) // 也没有分隔
        // 而 SmbPath 能正确处理
        assertEquals("\\a", SmbPath.parent(smb))
    }
}
