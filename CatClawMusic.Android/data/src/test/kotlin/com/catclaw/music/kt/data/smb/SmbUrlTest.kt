package com.catclaw.music.kt.data.smb

import com.catclaw.music.kt.core.net.UrlCredentials
import org.junit.Assert.assertEquals
import org.junit.Assert.assertNotNull
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Test

/** `smb://` 解析：逐条对齐 C# `SmbStreamProxy.TryParseSmbUrl` */
class SmbUrlTest {

    @Test
    fun basicShareAndPath() {
        val t = parseSmbUrl("smb://nas/Music/Album/song.mp3")!!
        assertEquals("nas", t.host)
        assertEquals(445, t.port)
        assertEquals("Music", t.share)
        assertEquals("\\Album\\song.mp3", t.remotePath)
        assertEquals("", t.user)
    }

    @Test
    fun nonDefaultPortOnlyOverrides() {
        assertEquals(4445, parseSmbUrl("smb://nas:4445/Music/a.mp3")!!.port)
        assertEquals(445, parseSmbUrl("smb://nas:445/Music/a.mp3")!!.port)
    }

    @Test
    fun userInfoSplitsOnFirstColonOnly() {
        val t = parseSmbUrl("smb://alice:s3cr3t@nas/Music/a.mp3")!!
        assertEquals("alice", t.user)
        assertEquals("s3cr3t", t.password)

        val withColon = parseSmbUrl("smb://alice:a:b@nas/Music/a.mp3")!!
        assertEquals("alice", withColon.user)
        assertEquals("a:b", withColon.password)

        val userOnly = parseSmbUrl("smb://alice@nas/Music/a.mp3")!!
        assertEquals("alice", userOnly.user)
        assertEquals("", userOnly.password)
    }

    @Test
    fun plusInPasswordSurvives() {
        val t = parseSmbUrl("smb://alice:p+q@nas/Music/a.mp3")!!
        assertEquals("p+q", t.password)
    }

    @Test
    fun percentEncodedPathIsDecoded() {
        val t = parseSmbUrl("smb://nas/Music/my%20song.mp3")!!
        assertEquals("\\my song.mp3", t.remotePath)
    }

    /** java.net.URI 会直接对裸中文抛异常，这里必须能吃下 —— .NET 的 Uri 是宽松的 */
    @Test
    fun bareNonAsciiIsAccepted() {
        val t = parseSmbUrl("smb://nas/音乐/夜曲.mp3")!!
        assertEquals("音乐", t.share)
        assertEquals("\\夜曲.mp3", t.remotePath)
    }

    @Test
    fun shareOnlyYieldsRootRemotePath() {
        val t = parseSmbUrl("smb://nas/Music")!!
        assertEquals("Music", t.share)
        assertEquals("\\", t.remotePath)
    }

    @Test
    fun rejectsNonSmbAndIncompleteUrls() {
        assertNull(parseSmbUrl("http://nas/Music/a.mp3"))
        assertNull(parseSmbUrl("smb:///Music/a.mp3"))
        assertNull(parseSmbUrl("smb://nas"))
        assertNull(parseSmbUrl("smb://nas/"))
        assertNull(parseSmbUrl(""))
    }

    @Test
    fun serviceKeyMatchesCsharpShape() {
        val t = parseSmbUrl("smb://alice:pw@nas:4445/Music/a.mp3")!!
        assertEquals("nas|4445|Music|alice", t.serviceKey)
    }
}
