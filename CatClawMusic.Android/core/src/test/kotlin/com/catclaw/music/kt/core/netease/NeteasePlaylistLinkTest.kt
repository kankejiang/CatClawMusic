package com.catclaw.music.kt.core.netease

import org.junit.Assert.assertEquals
import org.junit.Assert.assertNull
import org.junit.Test

class NeteasePlaylistLinkTest {

    @Test
    fun `纯数字直接当歌单 ID`() {
        assertEquals("3778678", NeteasePlaylistLink.parse("3778678"))
        assertEquals("3778678", NeteasePlaylistLink.parse("  3778678  "))
    }

    @Test
    fun `网页链接里取 playlist 的 id，而不是 userid`() {
        assertEquals(
            "24381616",
            NeteasePlaylistLink.parse("https://music.163.com/playlist?id=24381616&userid=329532145"),
        )
    }

    @Test
    fun `移动端分享链接`() {
        assertEquals(
            "7451234567",
            NeteasePlaylistLink.parse("https://y.music.163.com/m/playlist?id=7451234567&userid=1&from=qr"),
        )
    }

    @Test
    fun `分享文案整段粘进来也能认`() {
        val text = "分享歌单《晚上写代码听的》: https://music.163.com/playlist?id=2884035&userid=42 (来自@网易云音乐)"
        assertEquals("2884035", NeteasePlaylistLink.parse(text))
    }

    @Test
    fun `路径式链接`() {
        assertEquals("3778678", NeteasePlaylistLink.parse("https://music.163.com/#/playlist/3778678"))
    }

    @Test
    fun `只有用户或歌曲 id 时不认`() {
        assertNull(NeteasePlaylistLink.parse("https://music.163.com/user?id=1234567"))
        assertNull(NeteasePlaylistLink.parse("https://music.163.com/song?id=1234567"))
        assertNull(NeteasePlaylistLink.parse(""))
        assertNull(NeteasePlaylistLink.parse("随便一段没有链接的文字"))
        // 3 位数字太短，不像歌单 id
        assertNull(NeteasePlaylistLink.parse("123"))
    }
}
