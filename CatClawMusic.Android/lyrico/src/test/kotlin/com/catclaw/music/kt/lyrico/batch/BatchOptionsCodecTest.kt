package com.catclaw.music.kt.lyrico.batch

import com.catclaw.music.kt.lyrico.lyrics.LyricoLyricsFormat
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Test

/** 批量参数编解码的单测：参数存进 DB 再读回来必须等价，否则任务跑起来用的是错配置。 */
class BatchOptionsCodecTest {

    @Test
    fun roundTripsAllFields() {
        val options = BatchOptions(
            overwrite = true,
            lyricsFormat = LyricoLyricsFormat.ENHANCED,
            renamePattern = "{track} {artist} - {title}",
            setFields = mapOf("GENRE" to "流行", "COMMENT" to "批量整理"),
            clearFields = listOf("LYRICIST"),
            onlyFillMissing = false,
        )
        val decoded = BatchOptionsCodec.decode(BatchOptionsCodec.encode(options))
        assertEquals(options, decoded)
    }

    @Test
    fun escapesQuotesAndNewlines() {
        val options = BatchOptions(
            setFields = mapOf("COMMENT" to "含\"引号\"\n与换行"),
            renamePattern = "{artist} - {title}",
        )
        val decoded = BatchOptionsCodec.decode(BatchOptionsCodec.encode(options))
        assertEquals("含\"引号\"\n与换行", decoded.setFields["COMMENT"])
    }

    @Test
    fun defaultsWhenEmptyOrGarbage() {
        val defaults = BatchOptions()
        assertEquals(defaults, BatchOptionsCodec.decode(""))
        assertEquals(defaults, BatchOptionsCodec.decode("not json"))
        assertEquals(defaults, BatchOptionsCodec.decode("{}"))
        // 未知的格式枚举要退回默认，而不是抛
        assertEquals(
            LyricoLyricsFormat.PLAIN,
            BatchOptionsCodec.decode("""{"lyricsFormat":"TOTALLY_UNKNOWN"}""").lyricsFormat,
        )
    }

    @Test
    fun default_doesNotOverwrite() {
        assertFalse("默认不应覆盖已有内容", BatchOptions().overwrite)
        assertTrue("默认只补空字段", BatchOptions().onlyFillMissing)
        assertEquals("{artist} - {title}", BatchOptions().renamePattern)
    }
}
