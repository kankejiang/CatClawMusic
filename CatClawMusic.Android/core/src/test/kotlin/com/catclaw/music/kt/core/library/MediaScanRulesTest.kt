package com.catclaw.music.kt.core.library

import com.catclaw.music.kt.core.library.MediaScanRules as Rules
import org.junit.Assert.assertEquals
import org.junit.Assert.assertNull
import org.junit.Test

class MediaScanRulesTest {

    @Test
    fun artistNormalization() {
        assertEquals(Rules.UNKNOWN_ARTIST, Rules.normalizeArtist(null))
        assertEquals(Rules.UNKNOWN_ARTIST, Rules.normalizeArtist("   "))
        assertEquals(Rules.UNKNOWN_ARTIST, Rules.normalizeArtist("<unknown>"))
        assertEquals("Train", Rules.normalizeArtist("  Train  "))
    }

    @Test
    fun contentUriIsStableDedupKey() {
        assertEquals("content://media/external/audio/media/42", Rules.contentUri(42))
        assertEquals(Rules.contentUri(7), Rules.dedupKey(" " + Rules.contentUri(7) + " "))
    }

    @Test
    fun durationContractIsSeconds() {
        assertEquals(251, Rules.durationSeconds(251_400))
        assertEquals(0, Rules.durationSeconds(-5))
    }

    @Test
    fun titleFallback() {
        // 侧车规则搬去了 SidecarRulesTest（原来这里测的那条只认 .lrc，已删）
        assertEquals("Track 01", Rules.titleFromPath("/storage/emulated/0/Music/Track 01.mp3"))
    }

    @Test
    fun bitrateConvertedToKbps() {
        assertEquals(320, Rules.bitrateKbps(320_000))
        assertEquals(0, Rules.bitrateKbps(0))
    }

    @Test
    fun folderListRoundTripsThroughOneString() {
        val trees = listOf(
            "content://com.android.externalstorage.documents/tree/primary%3AMusic",
            "content://com.android.externalstorage.documents/tree/primary%3AMusic%2F%E6%94%B6%E8%97%8F",
        )
        assertEquals(trees, Rules.decodeFolders(Rules.encodeFolders(trees)))
        assertEquals("", Rules.encodeFolders(emptyList()))
        assertEquals(emptyList<String>(), Rules.decodeFolders(""))
        // 去重与去空白：清单既是存储值也是列表页的顺序，重复一行会显示成两个一样的文件夹
        assertEquals(
            trees,
            Rules.decodeFolders(Rules.encodeFolders(trees + trees[0] + "  " + trees[1] + " ")),
        )
        // URI 里带逗号与冒号是常态，所以分隔符只能是 `|`（与 C# FolderPicker 同一条约定）
        assertEquals(
            2,
            Rules.decodeFolders("content://x/tree/a%3Ab,1|content://x/tree/c%3Ad,2").size,
        )
    }
}
