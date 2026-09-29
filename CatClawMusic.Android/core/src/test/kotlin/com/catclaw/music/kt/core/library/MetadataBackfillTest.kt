package com.catclaw.music.kt.core.library

import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Test

/**
 * 远程曲补元数据的取值规则。每条断言都对着 C# `NetworkMusicService.Metadata` 的原文写，
 * 红了就说明两边又漂了。
 */
class MetadataBackfillTest {

    private val skeleton = AudioTags(title = "夜曲", artist = "", album = "", genre = null)

    @Test
    fun needsBackfillCoversBothMissingColumns() {
        // 时长没填、艺术家没建关联，任一成立就是骨架行
        assertTrue(MetadataBackfill.needsBackfill(durationSeconds = 0, artistId = 5))
        assertTrue(MetadataBackfill.needsBackfill(durationSeconds = 180, artistId = 0))
        assertTrue(MetadataBackfill.needsBackfill(durationSeconds = -1, artistId = 0))
        // 正控制：两列都齐了就不该再被选中，否则每次补全都会重跑整库
        assertFalse(MetadataBackfill.needsBackfill(durationSeconds = 180, artistId = 5))
    }

    @Test
    fun placeholdersDoNotCountAsUseful() {
        assertFalse(MetadataBackfill.hasUseful(skeleton.copy(artist = "未知艺术家", album = "未知专辑")))
        assertFalse(MetadataBackfill.hasUseful(skeleton))
        // 三者任取其一就够，不必再花一次大头请求
        assertTrue(MetadataBackfill.hasUseful(skeleton.copy(artist = "周杰伦")))
        assertTrue(MetadataBackfill.hasUseful(skeleton.copy(album = "十一月的萧邦")))
        assertTrue(MetadataBackfill.hasUseful(skeleton.copy(durationMs = 213_000)))
    }

    @Test
    fun onlyLargeTagFormatsGetASecondHead() {
        assertEquals(
            listOf(MetadataBackfill.TAG_HEAD_BYTES),
            MetadataBackfill.headSizes("/music/夜曲.mp3"),
        )
        for (ext in listOf("flac", "ape", "wav", "aiff", "aif", "dsf", "wv")) {
            assertEquals(
                ".$ext 应该排大头重试",
                listOf(MetadataBackfill.TAG_HEAD_BYTES, MetadataBackfill.TAG_HEAD_LARGE_BYTES),
                MetadataBackfill.headSizes("/music/album.$ext"),
            )
        }
        // OpenList 的直链带 ?sign=，扩展名判定不能被查询串带走
        assertTrue(MetadataBackfill.mayHaveLargeTagHeader("/d/a.flac?sign=AbCd&type=download"))
        assertTrue(MetadataBackfill.mayHaveLargeTagHeader("/d/a.FLAC"))
        assertFalse(MetadataBackfill.mayHaveLargeTagHeader("/d/a.mp3?sign=x"))
        assertFalse(MetadataBackfill.mayHaveLargeTagHeader("/d/no-extension"))
        assertFalse(MetadataBackfill.mayHaveLargeTagHeader("/d/trailing-dot."))
    }

    @Test
    fun audioHeaderTable() {
        assertTrue(MetadataBackfill.looksLikeAudio(head(0 to "ID3")))
        assertTrue(MetadataBackfill.looksLikeAudio(head(0 to "fLaC")))
        assertTrue(MetadataBackfill.looksLikeAudio(head(0 to "OggS")))
        assertTrue(MetadataBackfill.looksLikeAudio(head(4 to "ftyp")))
        assertTrue(MetadataBackfill.looksLikeAudio(head(0 to "RIFF", 8 to "WAVE")))
        assertTrue(MetadataBackfill.looksLikeAudio(head(0 to "MAC ")))
        assertTrue(MetadataBackfill.looksLikeAudio(head(0 to "wvpk")))
        assertTrue(MetadataBackfill.looksLikeAudio(head(0 to "FORM", 8 to "AIFF")))
        assertTrue(MetadataBackfill.looksLikeAudio(head(0 to "DSD ")))
        assertTrue(MetadataBackfill.looksLikeAudio(head(0 to "MPCK")))
        // MP3 裸帧同步是 11 位全 1，所以第二字节的高 3 位必须都是 1
        assertTrue(MetadataBackfill.looksLikeAudio(syncFrame(0xFF, 0xE0)))
        assertTrue(MetadataBackfill.looksLikeAudio(syncFrame(0xFF, 0xFB)))
    }

    @Test
    fun nonAudioHeadersAreRejected() {
        assertFalse(MetadataBackfill.looksLikeAudio(head(0 to "PK\u0003\u0004")))
        assertFalse(MetadataBackfill.looksLikeAudio(ByteArray(16)))
        // 正控制：只对齐了第一个 0xFF 不算同步字（半截下载就是这个形状）
        assertFalse(MetadataBackfill.looksLikeAudio(syncFrame(0xFF, 0x00)))
        assertFalse(MetadataBackfill.looksLikeAudio(byteArrayOf('I'.code.toByte(), 'D'.code.toByte(), '3'.code.toByte())))
        // ftyp 在第 4 字节，偏移写错就查不到
        assertFalse(MetadataBackfill.looksLikeAudio(head(0 to "ftyp")))
    }

    @Test
    fun emptyTagsLeaveTheRowAloneButGenreIsStillAssigned() {
        val current = AudioTags(
            title = "夜曲", artist = "周杰伦", album = "十一月的萧邦", genre = "流行",
            durationMs = 213_000, bitrateKbps = 320, trackNumber = 3, year = 2005,
        )
        val merged = MetadataBackfill.merge(current, skeleton)
        assertEquals(current.copy(genre = null), merged)
        // 这条钉的是 C# 的原样行为：Genre 是无条件赋值，没解到就清掉。
        // 看着像 bug，但它是「标签为准」的一致性，改成保留会让用户改过的类型永远盖不回来。
        assertNull(merged.genre)
    }

    @Test
    fun fetchedValuesWinOnlyWhenTheyAreReal() {
        val current = AudioTags(title = "夜曲", artist = "未知艺术家", album = "未知专辑", durationMs = 0)
        val merged = MetadataBackfill.merge(
            current,
            AudioTags(
                title = "Encore", artist = "周杰伦", album = "十一月的萧邦", genre = "流行",
                durationMs = 213_000, bitrateKbps = 320, trackNumber = 4, year = 2005,
            ),
        )
        assertEquals("周杰伦", merged.artist)
        assertEquals("十一月的萧邦", merged.album)
        assertEquals(213_000L, merged.durationMs)
        assertEquals(320, merged.bitrateKbps)
        assertEquals(4, merged.trackNumber)
        assertEquals(2005, merged.year)
        assertEquals("Encore", merged.title)
        // 占位值不许顶掉真值：第二次补全如果只解出「未知艺术家」，歌手列不能被写坏
        val second = MetadataBackfill.merge(
            merged,
            AudioTags(artist = "未知艺术家", album = "未知专辑", durationMs = 0, bitrateKbps = 0, year = 0, trackNumber = 0),
        )
        assertEquals("周杰伦", second.artist)
        assertEquals("十一月的萧邦", second.album)
        assertEquals(213_000L, second.durationMs)
    }

    @Test
    fun titleIsBlankProtectedAndOnlyWebDavDecodes() {
        val current = AudioTags(title = "夜曲")
        assertEquals("夜曲", MetadataBackfill.merge(current, AudioTags(title = "   ")).title)
        assertEquals("夜曲", MetadataBackfill.merge(current, AudioTags(title = "夜曲")).title)
        assertEquals(
            "夜曲 - 周杰伦",
            MetadataBackfill.merge(current, AudioTags(title = "夜曲%20-%20周杰伦"), decodeTitle = true).title,
        )
        // SMB 那一支不解码：百分号是标题本身的字符
        assertEquals(
            "夜曲%20-%20周杰伦",
            MetadataBackfill.merge(current, AudioTags(title = "夜曲%20-%20周杰伦")).title,
        )
    }

    @Test
    fun percentDecodingLeavesPlusAlone() {
        // URLDecoder 会把 + 当空格，Asia++ 这类艺名会被改坏 —— 这里必须原样留着
        val merged = MetadataBackfill.merge(
            AudioTags(title = "旧名"),
            AudioTags(title = "Asia++%20Live"),
            decodeTitle = true,
        )
        assertEquals("Asia++ Live", merged.title)
    }

    @Test
    fun onlyTheMp4FamilyIsWorthATailRead() {
        assertTrue(MetadataBackfill.mayHaveMoovAtTail("/dav/a.m4a"))
        assertTrue(MetadataBackfill.mayHaveMoovAtTail("/dav/a.MP4"))
        assertTrue(MetadataBackfill.mayHaveMoovAtTail("/d/a.m4b?sign=xx"))
        assertFalse(MetadataBackfill.mayHaveMoovAtTail("/dav/a.flac"))
        assertFalse(MetadataBackfill.mayHaveMoovAtTail("/dav/no-extension"))

        // 尾部窗口 = min(8 MB, 文件大小)；大小未知就是 0 —— 不知道在哪结束就不该去要
        assertEquals(8L * 1024 * 1024, MetadataBackfill.tailBytes(20L * 1024 * 1024))
        assertEquals(3_200_000L, MetadataBackfill.tailBytes(3_200_000L))
        assertEquals(0L, MetadataBackfill.tailBytes(0L))
        assertEquals(0L, MetadataBackfill.tailBytes(-1L))
    }

    @Test
    fun progressReportsEveryTenthAndAlwaysTheLast() {
        assertFalse(MetadataBackfill.reportsProgress(5, 50))
        assertTrue(MetadataBackfill.reportsProgress(10, 50))
        assertFalse(MetadataBackfill.reportsProgress(36, 50))
        assertFalse(MetadataBackfill.reportsProgress(47, 50))
        // 最后一首必须报，不然进度条会停在 40/50
        assertTrue(MetadataBackfill.reportsProgress(50, 50))
        assertTrue(MetadataBackfill.reportsProgress(41, 41))
    }

    /** 造一个 16 字节的头：把若干 ISO-8859-1 片段写到指定偏移 */
    private fun head(vararg at: Pair<Int, String>): ByteArray {
        val bytes = ByteArray(16)
        for ((offset, text) in at) text.toByteArray(Charsets.ISO_8859_1).copyInto(bytes, offset)
        return bytes
    }

    private fun syncFrame(first: Int, second: Int): ByteArray =
        ByteArray(16).also {
            it[0] = first.toByte()
            it[1] = second.toByte()
        }
}
