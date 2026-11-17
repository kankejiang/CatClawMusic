package com.catclaw.music.kt.data.smb

import org.junit.Assert.assertEquals
import org.junit.Test

/**
 * 从各种路径写法里推断 SMB 共享名。
 *
 * 用途：用户在「基础路径」里可能粘进完整 URL、Windows 路径或相对路径，
 * 此时共享名不必手填 —— 第一段就是共享名。推断失败返回空串，走探测兜底。
 */
class SmbWebGuessShareTest {

    @Test
    fun `完整 smb URL`() {
        assertEquals("music", SmbBrowser.guessShare("smb://nas.local/music"))
        assertEquals("music", SmbBrowser.guessShare("smb://nas.local/music/专辑"))
        assertEquals("music", SmbBrowser.guessShare("smb://user:pass@nas.local:445/music/专辑"))
    }

    @Test
    fun `Windows UNC 路径`() {
        assertEquals("music", SmbBrowser.guessShare("\\\\nas\\music"))
        assertEquals("music", SmbBrowser.guessShare("\\\\nas\\music\\专辑"))
    }

    @Test
    fun `共享内相对路径`() {
        // 用户可能只填了目录名，第一段当作共享名
        assertEquals("music", SmbBrowser.guessShare("music"))
        assertEquals("music", SmbBrowser.guessShare("music/专辑"))
        assertEquals("专辑", SmbBrowser.guessShare("\\专辑"))
    }

    @Test
    fun `只有主机名时推不出共享`() {
        assertEquals("", SmbBrowser.guessShare("smb://nas.local"))
        assertEquals("", SmbBrowser.guessShare("smb://nas.local/"))
        assertEquals("", SmbBrowser.guessShare("\\\\nas"))
    }

    @Test
    fun `空路径返回空`() {
        assertEquals("", SmbBrowser.guessShare(""))
        assertEquals("", SmbBrowser.guessShare("   "))
    }

    @Test
    fun `路径末尾多余分隔符不影响`() {
        assertEquals("music", SmbBrowser.guessShare("smb://nas.local/music/"))
        assertEquals("music", SmbBrowser.guessShare("music/"))
    }
}
