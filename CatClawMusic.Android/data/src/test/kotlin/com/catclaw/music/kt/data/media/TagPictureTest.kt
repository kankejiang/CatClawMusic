package com.catclaw.music.kt.data.media

import org.junit.Assert.assertEquals
import org.junit.Assert.assertNotNull
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Test

/**
 * 内嵌封面解析的字节级测试。
 *
 * 这个解析器的坏法全是静默的：偏移算错一位 → 解出一堆垃圾字节 → `BitmapFactory`
 * 返回 null → UI 画首字母。没有异常、没有日志，只有「封面还是不出来」。
 * 所以每个容器的布局都在这里用构造字节钉住。
 */
class TagPictureTest {

    // ── 构造工具：按规范拼出最小可解的数据 ───────────────────────────────

    private fun be32(value: Int): ByteArray = byteArrayOf(
        (value ushr 24).toByte(), (value ushr 16).toByte(), (value ushr 8).toByte(), value.toByte(),
    )

    private fun synchsafe(value: Int): ByteArray = byteArrayOf(
        ((value ushr 21) and 0x7F).toByte(), ((value ushr 14) and 0x7F).toByte(),
        ((value ushr 7) and 0x7F).toByte(), (value and 0x7F).toByte(),
    )

    /** 一张能过 [TagPicture] 校验的最小 JPEG：长度 > 64 字节、以 FFD8FF 开头 */
    private fun jpeg(size: Int = 200): ByteArray =
        ByteArray(size) { i ->
            when (i) {
                0 -> 0xFF.toByte()
                1 -> 0xD8.toByte()
                2 -> 0xFF.toByte()
                else -> (i % 251).toByte()
            }
        }

    private fun png(size: Int = 200): ByteArray =
        ByteArray(size) { i ->
            val head = byteArrayOf(
                0x89.toByte(), 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A,
            )
            if (i < head.size) head[i] else (i % 241).toByte()
        }

    // ── ID3v2 APIC ────────────────────────────────────────────────────────

    private fun id3WithApic(mime: String, picture: ByteArray, desc: String = ""): ByteArray {
        val mimeBytes = mime.toByteArray(Charsets.ISO_8859_1)
        // 编码(0=ISO-8859-1) + MIME + NUL + 类型(3=封面正面) + 描述 + NUL + 图
        val body = byteArrayOf(0) + mimeBytes + byteArrayOf(0) + byteArrayOf(3) +
            desc.toByteArray(Charsets.ISO_8859_1) + byteArrayOf(0) + picture
        // v2.3 的帧长度是普通大端；末尾的 2 字节是帧标志
        val frameHeader = "APIC".toByteArray() + be32(body.size) + byteArrayOf(0, 0)
        val frames = frameHeader + body
        val header = "ID3".toByteArray() + byteArrayOf(3, 0, 0) + synchsafe(frames.size)
        return header + frames
    }

    @Test
    fun `ID3v2_3 的 APIC 能解出 MIME 与原图字节`() {
        val picture = jpeg()
        val tags = id3WithApic("image/jpeg", picture)

        val extracted = TagPicture.extract(tags)

        assertNotNull("APIC 没解出来", extracted)
        assertEquals("image/jpeg", extracted!!.mime)
        assertTrue("图字节不一致", extracted.bytes.contentEquals(picture))
    }

    @Test
    fun `ID3v2 的 locate 给出图在文件里的绝对偏移`() {
        val picture = jpeg(300)
        val tags = id3WithApic("image/jpeg", picture)

        val location = TagPicture.locate(tags)

        assertNotNull(location)
        // 偏移处必须正好是 JPEG 魔数 —— 差一位就是"解出垃圾"
        assertEquals(0xFF.toByte(), tags[location!!.offset.toInt()])
        assertEquals(0xD8.toByte(), tags[location.offset.toInt() + 1])
        assertEquals(picture.size.toLong(), location.length)
    }

    @Test
    fun `ID3v2 的 PNG 识别成 image-png`() {
        val picture = png()
        val tags = id3WithApic("image/png", picture)

        val extracted = TagPicture.extract(tags)

        assertEquals("image/png", extracted?.mime)
        assertEquals("png", extracted?.extension)
    }

    @Test
    fun `窗口只到图中间时 extract 回 null 但 locate 仍给出正确终点`() {
        val picture = jpeg(5000)
        val tags = id3WithApic("image/jpeg", picture)
        // 只给前 1 KB：帧头与 MIME 在里面，图没解全
        val window = tags.copyOfRange(0, 1024)

        assertNull("窗口没覆盖整张图就不该返回半张", TagPicture.extract(window))
        val location = TagPicture.locate(window)
        assertNotNull("定位不该依赖图是否完整", location)
        assertEquals(tags.size.toLong(), location!!.end)
    }

    // ── FLAC PICTURE ──────────────────────────────────────────────────────

    private fun flacWithPicture(mime: String, picture: ByteArray): ByteArray {
        val mimeBytes = mime.toByteArray(Charsets.ISO_8859_1)
        val blockBody = be32(3) + be32(mimeBytes.size) + mimeBytes +
            be32(0) + // 描述长度 0
            be32(1400) + be32(1400) + be32(24) + be32(0) + // 宽高深色
            be32(picture.size) + picture
        // 块头：最后一块标志 + type=6，24 位长度
        val blockHeader = byteArrayOf(
            (0x80 or 6).toByte(),
            ((blockBody.size ushr 16) and 0xFF).toByte(),
            ((blockBody.size ushr 8) and 0xFF).toByte(),
            (blockBody.size and 0xFF).toByte(),
        )
        return "fLaC".toByteArray() + blockHeader + blockBody
    }

    @Test
    fun `FLAC 的 PICTURE 块能解出封面`() {
        val picture = jpeg(1000)
        val flac = flacWithPicture("image/jpeg", picture)

        val extracted = TagPicture.extract(flac)

        assertNotNull("PICTURE 块没解出来", extracted)
        assertEquals("image/jpeg", extracted!!.mime)
        assertTrue(extracted.bytes.contentEquals(picture))
    }

    @Test
    fun `FLAC 的 locate 偏移指向图体而不是块头`() {
        val picture = jpeg(600)
        val flac = flacWithPicture("image/jpeg", picture)

        val location = TagPicture.locate(flac)

        assertNotNull(location)
        assertEquals(picture.size.toLong(), location!!.length)
        assertEquals(0xFF.toByte(), flac[location.offset.toInt()])
    }

    @Test
    fun `FLAC 没有 PICTURE 块时两种入口都回 null`() {
        // 只有 STREAMINFO（type=0）的最小 FLAC
        val streamInfo = ByteArray(34)
        val header = byteArrayOf(0x80.toByte(), 0, 0, 34)
        val flac = "fLaC".toByteArray() + header + streamInfo

        assertNull(TagPicture.locate(flac))
        assertNull(TagPicture.extract(flac))
    }

    // ── 边界 ──────────────────────────────────────────────────────────────

    @Test
    fun `空字节与不认识的容器都回 null`() {
        assertNull(TagPicture.extract(ByteArray(0)))
        assertNull(TagPicture.locate(ByteArray(0)))
        assertNull(TagPicture.extract("RIFF....WAVE".toByteArray()))
        assertNull(TagPicture.locate("RIFF....WAVE".toByteArray()))
    }

    @Test
    fun `被截断的 FLAC 块头不会抛异常也不会瞎猜`() {
        val truncated = "fLaC".toByteArray() + byteArrayOf(0x86.toByte(), 0x00)
        assertNull(TagPicture.extract(truncated))
        assertNull(TagPicture.locate(truncated))
    }

    @Test
    fun `图片扩展名映射覆盖常见 MIME`() {
        fun ext(mime: String) = TagPicture.Picture(mime, ByteArray(100)).extension
        assertEquals("jpg", ext("image/jpeg"))
        assertEquals("jpg", ext("image/jpg"))
        assertEquals("png", ext("image/png"))
        assertEquals("webp", ext("image/webp"))
        assertEquals("gif", ext("image/gif"))
        assertEquals("img", ext("application/octet-stream"))
    }
}
