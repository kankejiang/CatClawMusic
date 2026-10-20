package com.catclaw.music.kt.core.library

import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Test

/**
 * 侧车配对规则的边界。
 *
 * 这里钉的是**远程**那一套（`.lrc`/`.ttml`，与 C# 远程扫描一致），不是本地那套
 * （本地走 `LyricsFileLocator`，集合更宽）。两者混用不会报错，只会让某一类歌词静默不显示。
 */
class SidecarRulesTest {

    @Test
    fun `歌词侧车只认 lrc 与 ttml`() {
        assertTrue(SidecarRules.isLyricsSidecar("song.lrc"))
        assertTrue(SidecarRules.isLyricsSidecar("song.ttml"))
        assertTrue("扩展名大小写不敏感（NAS 上到处是 .LRC）", SidecarRules.isLyricsSidecar("Song.LRC"))
        assertFalse("本地那套才认 .krc，远程不认", SidecarRules.isLyricsSidecar("song.krc"))
        assertFalse(SidecarRules.isLyricsSidecar("song.xml"))
        assertFalse(SidecarRules.isLyricsSidecar("lrc"))
        assertFalse(SidecarRules.isLyricsSidecar("song"))
    }

    @Test
    fun `图片侧车认常见五种`() {
        listOf("cover.jpg", "cover.JPEG", "cover.png", "cover.webp", "cover.bmp").forEach {
            assertTrue(it, SidecarRules.isImageSidecar(it))
        }
        assertFalse(SidecarRules.isImageSidecar("cover.gif"))
        assertFalse(SidecarRules.isImageSidecar("cover"))
    }

    @Test
    fun `配对键带父目录，避免跨专辑误配`() {
        assertEquals("/music/A/song", SidecarRules.sidecarKey("/music/A", "song.flac"))
        assertTrue(
            "两个目录下的同名文件必须给出不同的键",
            SidecarRules.sidecarKey("/music/A", "song.flac") != SidecarRules.sidecarKey("/music/B", "song.flac"),
        )
    }
}
