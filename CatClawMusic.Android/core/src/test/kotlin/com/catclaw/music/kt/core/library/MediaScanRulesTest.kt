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
    fun sidecarLyricsAndTitleFallback() {
        assertEquals("/m/a.lrc", Rules.sidecarLyricsPath("/m/a.flac"))
        assertNull(Rules.sidecarLyricsPath("/m/noext"))
        assertEquals("Track 01", Rules.titleFromPath("/storage/emulated/0/Music/Track 01.mp3"))
    }

    @Test
    fun bitrateConvertedToKbps() {
        assertEquals(320, Rules.bitrateKbps(320_000))
        assertEquals(0, Rules.bitrateKbps(0))
    }
}
