package com.catclaw.music.kt.core.lyrics

import java.nio.ByteBuffer
import java.nio.charset.CharacterCodingException
import java.nio.charset.CodingErrorAction
import java.nio.charset.StandardCharsets

/** 歌词文件解码与格式路由，逐条对齐 C# `LyricsService.FileReader.cs` 的 ReadLyricsFileFallback 顺序 */
object LyricsEncoding {

    const val MAX_LYRICS_FILE_BYTES = 2_000_000
    const val MAX_PARSE_BYTES = 1_000_000

    private val NUL: Char = 0.toChar()

    fun decode(bytes: ByteArray): String? {
        if (bytes.isEmpty()) return null
        if (bytes.size > MAX_LYRICS_FILE_BYTES) return null

        // ① BOM 优先
        if (bytes.size >= 3 && bytes[0] == 0xEF.toByte() && bytes[1] == 0xBB.toByte() && bytes[2] == 0xBF.toByte()) {
            return String(bytes, 3, bytes.size - 3, StandardCharsets.UTF_8)
        }
        if (bytes.size >= 2 && bytes[0] == 0xFF.toByte() && bytes[1] == 0xFE.toByte()) {
            return String(bytes, 2, bytes.size - 2, StandardCharsets.UTF_16LE)
        }
        if (bytes.size >= 2 && bytes[0] == 0xFE.toByte() && bytes[1] == 0xFF.toByte()) {
            return String(bytes, 2, bytes.size - 2, StandardCharsets.UTF_16BE)
        }

        // ② 严格 UTF-8
        strictUtf8(bytes)?.let { return it }

        // ③ 无 BOM 的 UTF-16 启发式：长度为偶数 + 前 4096 里 NUL 占比 > 1/8 + 解出来含尖括号
        if (bytes.size % 2 == 0 && denseNulls(bytes)) {
            val asUtf16 = String(bytes, StandardCharsets.UTF_16LE)
            if (asUtf16.contains('<') && asUtf16.contains('>')) return asUtf16
        }

        // ④ GBK → ⑤ GB2312 → ⑥ 宽松 UTF-8
        runCatching { String(bytes, charset("GBK")) }.getOrNull()?.takeIf { looksLikeText(it) }?.let { return it }
        runCatching { String(bytes, charset("GB2312")) }.getOrNull()?.takeIf { looksLikeText(it) }?.let { return it }
        return String(bytes, StandardCharsets.UTF_8)
    }

    /**
     * 按嗅探规则选解析器：JSON → AMLL，含 &lt;tt → TTML，其余走 LRC。
     * 解出来带 NUL 的（严格 UTF-8 尾部漏字符）先按 UTF-16 重解一次，与 C# 的 TryReinterpretAsUtf16 同规则。
     */
    fun parseContent(content: String?): Lyrics? {
        if (content.isNullOrBlank()) return null
        val text = if (content.indexOf(NUL) >= 0) reinterpretAsUtf16(content) ?: return null else content

        AmllParser.parse(text).let { if (it != null) return it }
        if (text.contains("<tt") || text.contains("xmlns=\"http://www.w3.org/ns/ttml")) {
            TtmlParser.parse(text)?.let { return it }
            // C# 原话「TTML 专用路径：绝不回退到 PlainText」—— 解不动就是解不动，别把 XML 源码当歌词刷出去
            return null
        }
        LrcParser.parse(text)?.let { return it }
        if (looksLikeMarkup(text)) return null // 错误页 / JSON 响应不是歌词
        return plainTextLyrics(text)
    }

    private fun reinterpretAsUtf16(text: String): String? = try {
        val bytes = text.toByteArray(StandardCharsets.ISO_8859_1)
        if (bytes.size < 2) return null
        String(bytes, StandardCharsets.UTF_16LE).takeIf { it.indexOf(NUL) < 0 }
    } catch (_: Exception) {
        null
    }

    private fun strictUtf8(bytes: ByteArray): String? = try {
        StandardCharsets.UTF_8.newDecoder()
            .onMalformedInput(CodingErrorAction.REPORT)
            .onUnmappableCharacter(CodingErrorAction.REPORT)
            .decode(ByteBuffer.wrap(bytes))
            .toString()
    } catch (_: CharacterCodingException) {
        null
    }

    private fun denseNulls(bytes: ByteArray): Boolean {
        val window = minOf(bytes.size, 4096)
        var zeros = 0
        for (i in 0 until window) if (bytes[i] == 0.toByte()) zeros++
        return zeros * 8 > window
    }

    private fun looksLikeText(s: String): Boolean = s.isNotEmpty() && s.indexOf(NUL) < 0
}
