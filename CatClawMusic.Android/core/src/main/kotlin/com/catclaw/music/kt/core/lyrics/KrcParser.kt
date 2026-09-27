package com.catclaw.music.kt.core.lyrics

import java.io.ByteArrayOutputStream
import java.util.Base64
import java.util.zip.Inflater
import kotlinx.serialization.json.jsonArray
import kotlinx.serialization.json.jsonObject
import kotlinx.serialization.json.jsonPrimitive
import kotlinx.serialization.json.longOrNull

/**
 * 酷狗 KRC / QQ QRC 二进制歌词，逐条对齐 C# `LyricsService.Krc.cs`。
 *
 * 注意两处**照抄的不一致**（不是笔误，移植要保真）：
 * - KRC 分支不做排序、不做对唱合并、不做译文合并；
 * - QRC 分支里字词 `p` 直接当**绝对**时间用，不像 KRC 那样加行起始。
 */
object KrcParser {

    private val KRC_KEY = byteArrayOf(
        0x40, 0x47, 0x61, 0x77, 0x5e, 0x32, 0x74, 0x47, 0x51, 0x36, 0x31, 0x2d,
        0xce.toByte(), 0xd2.toByte(), 0x6e, 0x69,
    )

    private val QRC_KEY = byteArrayOf(
        0x3b, 0x44, 0x51, 0x62, 0x3e, 0x58, 0x31, 0x52, 0x34, 0x55, 0x3f, 0x73, 0x42, 0x67, 0x35, 0x6d,
        0x30, 0x6d, 0x36, 0x70, 0x3e, 0x59, 0x31, 0x4c, 0x33, 0x4e, 0x31, 0x52, 0x35, 0x6e, 0x30, 0x6a,
        0x30, 0x71, 0x36, 0x73, 0x3f, 0x5b, 0x31, 0x45, 0x3c, 0x6c, 0x33, 0x55, 0x33, 0x64, 0x30, 0x70,
        0x31, 0x71, 0x36, 0x73, 0x3f, 0x5b, 0x31, 0x45, 0x3c, 0x6c, 0x33, 0x55, 0x33, 0x64, 0x30, 0x70,
    )

    private val LINE_BLOCK = Regex("""\[(\d+),(\d+)]""")
    private val WORD = Regex("""<(\d+),(\d+)(?:,\d+)?>([^<]*)""")
    private val LANGUAGE = Regex("""\[language:([\w=+/\\]+)]""")

    /** 解析酷狗 KRC（需要完整文件字节，含 krc1 魔数） */
    fun parse(data: ByteArray?): Lyrics? {
        if (data == null || data.size < 8) return null
        if (String(data, 0, 4, Charsets.ISO_8859_1) != "krc1") return null
        val text = runCatching { decryptAndInflate(data, 4, KRC_KEY, 16) }.getOrNull() ?: return null
        return parseKrcText(text)
    }

    internal fun parseKrcText(text: String): Lyrics? {
        val translationByIndex = parseLanguageStream(text)
        val lines = mutableListOf<LyricLine>()
        var lineIndex = 0

        for (rawLine in text.replace("\r\n", "\n").replace("\r", "\n").split("\n")) {
            val line = rawLine.trim()
            if (line.isEmpty() || line.startsWith("[id:") || line.startsWith("[language:")) continue

            val blocks = LINE_BLOCK.findAll(line).toList()
            if (blocks.isEmpty()) continue

            for ((i, block) in blocks.withIndex()) {
                val lineStartMs = block.groupValues[1].toLongOrNull() ?: continue
                val blockEnd = if (i + 1 < blocks.size) blocks[i + 1].range.first else line.length
                val blockText = line.substring(block.range.last + 1, blockEnd)

                val words = WORD.findAll(blockText).mapNotNull { m ->
                    val rel = m.groupValues[1].toLongOrNull() ?: return@mapNotNull null
                    val dur = m.groupValues[2].toLongOrNull() ?: 0L
                    val word = m.groupValues[3]
                    if (word.trim().isEmpty()) return@mapNotNull null
                    WordTimestamp(word, lineStartMs + rel, dur)
                }.toList()

                val (text2, timestamps) = if (words.isNotEmpty()) {
                    words.joinToString("") { it.word } to words
                } else {
                    val plain = blockText.trim()
                    if (plain.isEmpty()) continue else plain to null
                }

                lines += LyricLine(
                    startMs = lineStartMs,
                    text = text2,
                    wordTimestamps = timestamps,
                    translation = translationByIndex?.translations?.getOrNull(lineIndex),
                    roma = translationByIndex?.roma?.getOrNull(lineIndex),
                )
                lineIndex++
            }
        }

        if (lines.isEmpty()) return null
        return Lyrics().apply { this.lines = lines }
    }

    /** 解析 QQ 音乐 QRC（64 字节 XOR + zlib + XML） */
    fun parseQrc(data: ByteArray?): Lyrics? {
        if (data == null || data.size < 8) return null
        if (String(data, 0, 4, Charsets.ISO_8859_1) != "QRC1") return null
        val xml = runCatching { decryptAndInflate(data, 4, QRC_KEY, 64) }.getOrNull() ?: return null

        return try {
            val lines = mutableListOf<LyricLine>()
            val doc = XmlSupport.parse(xml) ?: return null
            val paragraphs = doc.getElementsByTagName("line")
            for (i in 0 until paragraphs.length) {
                val node = paragraphs.item(i)
                if (node !is org.w3c.dom.Element) continue
                val startMs = node.getAttribute("t").toLongOrNull() ?: continue
                val spans = node.getElementsByTagName("s")
                val words = mutableListOf<WordTimestamp>()
                for (j in 0 until spans.length) {
                    val s = spans.item(j) as? org.w3c.dom.Element ?: continue
                    val word = s.getAttribute("w")
                    if (word.isBlank()) continue
                    // QRC 的 p 直接当绝对时间用（与 KRC 分支不同，照抄）
                    val p = s.getAttribute("p").toLongOrNull() ?: 0L
                    val d = s.getAttribute("t").toLongOrNull() ?: 0L
                    words += WordTimestamp(word, p, d)
                }
                val text = if (words.isNotEmpty()) words.joinToString("") { it.word }
                else node.textContent?.trim().orEmpty()
                if (text.isEmpty()) continue
                lines += LyricLine(startMs = startMs, text = text, wordTimestamps = words.ifEmpty { null })
            }
            if (lines.isEmpty()) return null
            Lyrics().apply { this.lines = lines }
        } catch (_: Exception) {
            null
        }
    }

    private class LanguageStreams(val roma: List<String?>, val translations: List<String?>)

    /** `[language:base64]` → JSON.content[]，type==0 罗马音、type==1 译文 */
    private fun parseLanguageStream(text: String): LanguageStreams? {
        val match = LANGUAGE.find(text) ?: return null
        return try {
            val json = String(Base64.getDecoder().decode(match.groupValues[1]), Charsets.UTF_8)
            val root = kotlinx.serialization.json.Json.parseToJsonElement(json).jsonObject
            val items = root["content"]?.jsonArray ?: return null
            val roma = mutableListOf<String?>()
            val trans = mutableListOf<String?>()
            for (item in items) {
                val obj = runCatching { item.jsonObject }.getOrNull() ?: continue
                val type = obj["type"]?.jsonPrimitive?.longOrNull
                val content = obj["lyricContent"]?.jsonArray?.joinToString("") { element ->
                    runCatching { element.jsonPrimitive.content }
                        .getOrElse { runCatching { element.jsonArray.joinToString("") { it.jsonPrimitive.content } }.getOrDefault("") }
                } ?: ""
                when (type) {
                    0L -> roma += content
                    1L -> trans += content
                    else -> Unit
                }
            }
            LanguageStreams(roma, trans)
        } catch (_: Exception) {
            null
        }
    }

    private fun decryptAndInflate(data: ByteArray, offset: Int, key: ByteArray, keySize: Int): String {
        val decrypted = ByteArray(data.size - offset)
        for (i in decrypted.indices) {
            decrypted[i] = (data[offset + i].toInt() xor key[i % keySize].toInt()).toByte()
        }
        val inflater = Inflater()
        inflater.setInput(decrypted)
        val out = ByteArrayOutputStream()
        val buffer = ByteArray(8192)
        try {
            while (!inflater.finished()) {
                val read = inflater.inflate(buffer)
                if (read <= 0) break
                out.write(buffer, 0, read)
            }
        } finally {
            inflater.end()
        }
        return out.toString("UTF-8")
    }
}
