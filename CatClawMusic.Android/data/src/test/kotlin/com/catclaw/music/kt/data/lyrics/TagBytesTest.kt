package com.catclaw.music.kt.data.lyrics

import org.junit.Assert.assertEquals
import org.junit.Test

/**
 * 字节读取的位拼接。
 *
 * 起因：`be32` 与 `synchsafe` 原先写成 `a shl 24 or b shl 16 or …`，而 Kotlin 的 `shl` 和 `or`
 * 是同级中缀、**从左往右**结合，于是实际算的是 `((a shl 24 or b) shl 16 or c) shl 8 or d`。
 * 只要高位字节是 0（小尺寸、小偏移），两种算法碰巧一样，所以问题藏了很久。
 *
 * 真会坏的场景：ID3v2.3 里一张 300 KB 的内嵌封面，帧长度 `00 04 B0 00` 会被读成 67 171 328，
 * 于是走帧循环时判定「帧越界」直接 break，**排在封面之后的 USLT 歌词整段读不到**。
 */
class TagBytesTest {

    @Test
    fun be32ReadsAllFourBytes() {
        assertEquals(0x01020304, TagBytes.be32(byteArrayOf(0x01, 0x02, 0x03, 0x04), 0))
        assertEquals(0x00_30_BE_BC, TagBytes.be32(byteArrayOf(0x00, 0x30, 0xBE.toByte(), 0xBC.toByte()), 0))
        assertEquals(0x7FFF_FFFF, TagBytes.be32(byteArrayOf(0x7F, 0xFF.toByte(), 0xFF.toByte(), 0xFF.toByte()), 0))
    }

    @Test
    fun synchsafeKeepsSevenBitsPerByte() {
        // 四个字节各取低 7 位，合成 28 位：最大就是 0x0FFF_FFFF
        assertEquals(0x0FFF_FFFF, TagBytes.synchsafe(byteArrayOf(0x7F, 0x7F, 0x7F, 0x7F), 0))
        assertEquals((1 shl 21) or (2 shl 14) or (3 shl 7) or 4, TagBytes.synchsafe(byteArrayOf(0x01, 0x02, 0x03, 0x04), 0))
        // 高位被丢弃，不能把第 8 位算进数值
        assertEquals(0, TagBytes.synchsafe(byteArrayOf(0x80.toByte(), 0, 0, 0), 0))
    }

    @Test
    fun otherWidthsStayCorrect() {
        assertEquals(0x1234, TagBytes.be16(byteArrayOf(0x12, 0x34), 0))
        assertEquals(0x04030201, TagBytes.le32(byteArrayOf(0x01, 0x02, 0x03, 0x04), 0))
        assertEquals(0x0102030405060708L, TagBytes.be64(byteArrayOf(1, 2, 3, 4, 5, 6, 7, 8), 0))
    }

    @Test
    fun lyricsSurviveAHeaderFullOfCoverArt() {
        // 70 KB 的 APIC：帧长度的第二个字节不再是 0，旧的位拼接会把整段标签算坏
        val cover = ByteArray(70_000) { (it % 251).toByte() }
        val tag = TagFixtures.id3v2(
            3,
            listOf(
                "APIC" to cover,
                // 编码 3 = UTF-8：0 是 ISO-8859-1，中文会被写成问号
                "USLT" to TagFixtures.usltValue(3, "第一行歌词"),
            ),
        )
        assertEquals("第一行歌词", Id3v2Tag.lyrics(tag))
    }
}
