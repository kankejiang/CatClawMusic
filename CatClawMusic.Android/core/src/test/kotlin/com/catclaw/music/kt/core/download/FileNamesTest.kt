package com.catclaw.music.kt.core.download

import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Test

/** 下载目录唯一的路径守卫。规则错一条，写出去的文件就在目录外面 */
class FileNamesTest {

    private fun ch(code: Int) = String(Character.toChars(code))

    @Test
    fun `分隔符全部换掉，名字出不了这一层目录`() {
        assertEquals("a_b_c.mp3", FileNames.sanitize("a/b/c.mp3"))
        assertEquals("_etc_passwd", FileNames.sanitize("/etc/passwd"))
        assertEquals("win_path", FileNames.sanitize("win" + ch(92) + "path"))
    }

    @Test
    fun `开头点全部去掉，旧写法把两点洗成一点就是目录本身`() {
        // removePrefix(".") 只去一个点：两点会留下一个点、三点会留下两点，
        // 而 File(dir, ".") 是下载目录本身、File(dir, "..") 直接跑到上一级。
        // 这两个用例就是钉那个洞的。
        assertEquals(FileNames.FALLBACK, FileNames.sanitize(".."))
        assertEquals(FileNames.FALLBACK, FileNames.sanitize("..."))
        assertEquals("hidden.mp3", FileNames.sanitize(".hidden.mp3"))
    }

    @Test
    fun `控制字符与 NUL 不留，空名字给兜底`() {
        assertEquals("a_b", FileNames.sanitize("a" + ch(0) + "b"))
        assertEquals("name_.mp3", FileNames.sanitize("name" + ch(9) + ".mp3"))
        assertEquals(FileNames.FALLBACK, FileNames.sanitize(""))
        assertEquals(FileNames.FALLBACK, FileNames.sanitize(null))
        assertEquals(FileNames.FALLBACK, FileNames.sanitize("   "))
    }

    @Test
    fun `普通歌名原样保留，超长才截`() {
        assertEquals("晴天.mp3", FileNames.sanitize("晴天.mp3"))
        val out = FileNames.sanitize("a".repeat(400) + ".mp3")
        assertEquals(FileNames.MAX_LENGTH, out.length)
        assertTrue(out.startsWith("aaaa"))
    }

    @Test
    fun `洗完永远是单段可写文件名`() {
        listOf("../x", "..", "...", "/", "\\", "  ", ch(0), "a/b").forEach { raw ->
            val out = FileNames.sanitize(raw)
            assertFalse("洗完还带分隔符: " + out, out.contains('/') || out.contains(ch(92)))
            assertFalse("洗完以点开头: " + out, out.startsWith("."))
            assertTrue("洗完是空的", out.isNotBlank())
        }
    }
}
