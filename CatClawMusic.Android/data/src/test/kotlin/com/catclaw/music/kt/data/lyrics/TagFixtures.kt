package com.catclaw.music.kt.data.lyrics

import java.io.ByteArrayOutputStream
import java.nio.charset.Charset
import java.nio.charset.StandardCharsets

/**
 * 测试用的标签构造器：按规范自己拼字节，独立于被测解析器。
 * 这样「解析对了」才是真证据，而不是拿同一个实现自证。
 */
internal object TagFixtures {

    fun synchsafe(value: Int): ByteArray = byteArrayOf(
        ((value shr 21) and 0x7F).toByte(), ((value shr 14) and 0x7F).toByte(),
        ((value shr 7) and 0x7F).toByte(), (value and 0x7F).toByte(),
    )

    fun be32(value: Int): ByteArray = byteArrayOf(
        (value ushr 24).toByte(), (value ushr 16).toByte(), (value ushr 8).toByte(), value.toByte(),
    )

    fun le32(value: Int): ByteArray = byteArrayOf(
        value.toByte(), (value ushr 8).toByte(), (value ushr 16).toByte(), (value ushr 24).toByte(),
    )

    /** USLT 帧值 = 编码 + "eng" + 描述(按编码终止) + 正文 */
    fun usltValue(encoding: Int, text: String, descriptor: String = ""): ByteArray {
        val out = ByteArrayOutputStream()
        out.write(encoding)
        out.write("eng".toByteArray(StandardCharsets.ISO_8859_1))
        out.write(terminated(descriptor, encoding))
        out.write(encode(text, encoding))
        return out.toByteArray()
    }

    private fun terminated(text: String, encoding: Int): ByteArray {
        val body = encode(text, encoding)
        return if (encoding == 1 || encoding == 2) body + byteArrayOf(0, 0) else body + byteArrayOf(0)
    }

    fun encode(text: String, encoding: Int): ByteArray = when (encoding) {
        0 -> text.toByteArray(StandardCharsets.ISO_8859_1)
        1 -> text.toByteArray(StandardCharsets.UTF_16LE)
        2 -> text.toByteArray(StandardCharsets.UTF_16BE)
        3 -> text.toByteArray(StandardCharsets.UTF_8)
        else -> text.toByteArray(StandardCharsets.ISO_8859_1)
    }

    /** 组一个 ID3v2 标签；version 2 用 3+3 帧头，3 用 4+4+2，4 用 4+4(synchsafe)+2 */
    fun id3v2(version: Int, frames: List<Pair<String, ByteArray>>, unsync: Boolean = false, extHeader: ByteArray? = null): ByteArray {
        val body = ByteArrayOutputStream()
        if (version >= 3 && extHeader != null) body.write(extHeader)
        frames.forEach { (id, value) -> body.write(frame(version, id, if (unsync) syncUp(value) else value)) }
        val payload = body.toByteArray()
        val out = ByteArrayOutputStream()
        out.write("ID3".toByteArray(StandardCharsets.ISO_8859_1))
        val flags = (if (unsync) 0x80 else 0) or (if (extHeader != null) 0x40 else 0)
        out.write(byteArrayOf(version.toByte(), 0, flags.toByte()))
        out.write(synchsafe(payload.size + if (version == 2) 0 else 0))
        out.write(payload)
        return out.toByteArray()
    }

    private fun frame(version: Int, id: String, value: ByteArray): ByteArray {
        val out = ByteArrayOutputStream()
        if (version == 2) {
            out.write(id.toByteArray(StandardCharsets.ISO_8859_1))
            out.write(byteArrayOf((value.size shr 16).toByte(), (value.size shr 8).toByte(), value.size.toByte()))
        } else {
            out.write(id.toByteArray(StandardCharsets.ISO_8859_1))
            out.write(if (version == 4) synchsafe(value.size) else be32(value.size))
            out.write(byteArrayOf(0, 0))
        }
        out.write(value)
        return out.toByteArray()
    }

    /** 写侧的「加 unsync」：0xFF 后插 0x00，用来构造被同步化的标签 */
    private fun syncUp(raw: ByteArray): ByteArray {
        val out = ByteArrayOutputStream(raw.size + 8)
        var i = 0
        while (i < raw.size) {
            // 每个跟在 0xFF 后面的 0x00 前面再补一个 0x00（原字节仍照常写出）
            out.write(raw[i].toInt())
            if (raw[i] == 0xFF.toByte() && i + 1 < raw.size && raw[i + 1] == 0x00.toByte()) out.write(0)
            i++
        }
        return out.toByteArray()
    }

    fun vorbisComment(fields: List<Pair<String, String>>, vendor: String = "ref"): ByteArray {
        val out = ByteArrayOutputStream()
        val vendorBytes = vendor.toByteArray(StandardCharsets.UTF_8)
        out.write(le32(vendorBytes.size)); out.write(vendorBytes)
        out.write(le32(fields.size))
        fields.forEach { (key, value) ->
            val entry = "$key=$value".toByteArray(StandardCharsets.UTF_8)
            out.write(le32(entry.size)); out.write(entry)
        }
        return out.toByteArray()
    }

    fun flac(comment: ByteArray, withPaddingFirst: Boolean = true): ByteArray {
        val out = java.io.ByteArrayOutputStream()
        out.write("fLaC".toByteArray(Charsets.ISO_8859_1))
        if (withPaddingFirst) {
            out.write(byteArrayOf(0, 0, 0, 4))
            out.write(ByteArray(4))
        }
        val lastHeader = byteArrayOf(0x84.toByte(), (comment.size shr 16).toByte(), (comment.size shr 8).toByte(), comment.size.toByte())
        out.write(lastHeader)
        out.write(comment)
        return out.toByteArray()
    }

    fun ogg(comment: ByteArray): ByteArray {
        val out = java.io.ByteArrayOutputStream()
        out.write("OggS".toByteArray(Charsets.ISO_8859_1))
        out.write(ByteArray(20))
        out.write(byteArrayOf(3, 'v'.code.toByte(), 'o'.code.toByte(), 'r'.code.toByte(), 'b'.code.toByte(), 'i'.code.toByte(), 's'.code.toByte()))
        out.write(comment)
        return out.toByteArray()
    }

    fun atom(type: String, body: ByteArray): ByteArray =
        be32(body.size + 8) + type.toByteArray(Charsets.ISO_8859_1) + body

    fun mp4(lyrics: String, lyricType: String = "\u00a9lyr"): ByteArray {
        val text = lyrics.toByteArray(Charsets.UTF_8)
        val dataAtom = atom("data", ByteArray(8) + text)
        val ilst = atom("ilst", atom(lyricType, dataAtom))
        val meta = atom("meta", ByteArray(4) + ilst)
        val udta = atom("udta", meta)
        val moov = atom("moov", udta)
        val ftyp = atom("ftyp", "M4A ".toByteArray(Charsets.ISO_8859_1) + ByteArray(4) + "M4A ".toByteArray(Charsets.ISO_8859_1))
        return ftyp + moov
    }

    /** APEv2：只写 footer（C# 侧 TagLib 两种都吃），条目按规范排 in-tag 区 */
    fun apev2(items: List<Pair<String, ByteArray>>, withHeader: Boolean = false): ByteArray =
        apev2WithFlags(items, 0, withHeader)

    /** flags 低 2 位是值类型，bit29-30 是文本编码：0=UTF8、1=UTF16LE、2=二进制 */
    fun apev2WithFlags(items: List<Pair<String, ByteArray>>, valueFlags: Int, withHeader: Boolean = false): ByteArray {
        val body = java.io.ByteArrayOutputStream()
        items.forEach { (key, value) ->
            body.write(le32(value.size))
            body.write(le32(valueFlags))
            body.write(key.toByteArray(Charsets.UTF_8))
            body.write(0)
            body.write(value)
        }
        val values = body.toByteArray()
        val tagSize = values.size + 32 + if (withHeader) 32 else 0
        val out = java.io.ByteArrayOutputStream()
        if (withHeader) {
            out.write("APETAGEX".toByteArray(Charsets.ISO_8859_1))
            out.write(le32(2000)); out.write(le32(tagSize)); out.write(le32(items.size))
            out.write(le32(0x80000003.toInt()))
            out.write(ByteArray(8))
        }
        out.write(values)
        out.write("APETAGEX".toByteArray(Charsets.ISO_8859_1))
        out.write(le32(2000)); out.write(le32(tagSize)); out.write(le32(items.size))
        out.write(le32(if (withHeader) 0x80000003.toInt() else 0x00000003))
        out.write(ByteArray(8))
        return out.toByteArray()
    }
}
