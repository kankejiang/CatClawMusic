package com.catclaw.music.kt.core.model

import org.junit.Assert.assertEquals
import org.junit.Test

/**
 * 详情页的格式化。钉的全是"缺数据被格式化成了数字"这一类：
 * 没轨号显示"0"、没码率显示"0 kbps"、年份显示"0 年" —— 看着像数据，其实是缺数据。
 */
class SongDetailFormatTest {

    @Test
    fun durationGrowsAnHourFieldOnlyWhenNeeded() {
        assertEquals("4:14", SongDetailFormat.duration(254))
        assertEquals("0:07", SongDetailFormat.duration(7))
        assertEquals("1:02:03", SongDetailFormat.duration(3723))
        assertEquals("没时长就该空着，不是 0:00", "", SongDetailFormat.duration(0))
        assertEquals("", SongDetailFormat.duration(-5))
    }

    @Test
    fun bytesScalesOnceAndNeverInventsZero() {
        assertEquals("512 B", SongDetailFormat.bytes(512))
        assertEquals("1.0 KB", SongDetailFormat.bytes(1024))
        assertEquals("12.3 MB", SongDetailFormat.bytes(12_897_480))
        assertEquals("0 字节是「没这个信息」", "", SongDetailFormat.bytes(0))
        assertEquals("", SongDetailFormat.bytes(-1))
    }

    @Test
    fun formatComesFromTheExtensionEvenForUrls() {
        assertEquals("MP3", SongDetailFormat.fileFormat("/storage/emulated/0/Music/a.MP3"))
        assertEquals("FLAC", SongDetailFormat.fileFormat("https://host/d/song.flac?token=abc"))
        assertEquals("没后缀就空着", "", SongDetailFormat.fileFormat("/music/song"))
        assertEquals("后缀过长说明那不是扩展名", "", SongDetailFormat.fileFormat("/music/reallylongname"))
        assertEquals("", SongDetailFormat.fileFormat(null))
    }

    @Test
    fun optionalNumbersShowBlankInsteadOfZero() {
        assertEquals("3", SongDetailFormat.track(3))
        assertEquals("", SongDetailFormat.track(0))
        assertEquals("2008", SongDetailFormat.year(2008))
        assertEquals("", SongDetailFormat.year(0))
        assertEquals("320 kbps", SongDetailFormat.bitrate(320))
        assertEquals("", SongDetailFormat.bitrate(0))
    }
}
