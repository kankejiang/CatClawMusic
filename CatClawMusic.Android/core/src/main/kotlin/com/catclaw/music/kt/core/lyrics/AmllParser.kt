package com.catclaw.music.kt.core.lyrics

import kotlinx.serialization.json.Json
import kotlinx.serialization.json.JsonArray
import kotlinx.serialization.json.JsonElement
import kotlinx.serialization.json.JsonObject
import kotlinx.serialization.json.jsonArray
import kotlinx.serialization.json.jsonObject
import kotlinx.serialization.json.jsonPrimitive

/**
 * AMLL（Anni Music Lyrics Library）JSON 逐字歌词，常见于网易云 / QQ 音乐下载的文件。
 * 逐条对齐 C# `LyricsService.Amll.cs`：
 * - 行 `startTime` 是**绝对**毫秒；行 `endTime` 不读；
 * - 字词 `words[].startTime` 是**相对行首**的偏移，必须加行起始转绝对——
 *   否则绝对 position 永远大于词时间戳，逐字进度会瞬间变 1（"一行只闪一下"）；
 * - 文本键优先 `content` 再 `lyric`；空文本行照样产出；
 * - 时间字段解析失败即整份歌词作废（all-or-nothing），与 C# 的整段 try/catch 一致。
 */
object AmllParser {

    private const val DEFAULT_WORD_MS = 200L

    fun parse(content: String?): Lyrics? {
        if (content.isNullOrBlank()) return null
        return try {
            val root = Json.parseToJsonElement(content).jsonObject
            val payload = (root["lyrics"] ?: root["data"])?.jsonObject ?: return null
            val lineArray = payload["lines"]?.jsonArray ?: return null

            val lines = lineArray.map { element ->
                val obj = element.jsonObject
                val startMs = obj["startTime"].asLongOrNull()
                    ?: throw IllegalArgumentException("AMLL 行缺 startTime")
                val text = (obj["content"] ?: obj["lyric"])?.jsonPrimitive?.content ?: ""
                val role = obj["role"]?.jsonPrimitive?.content
                val singer = obj["singer"]?.jsonPrimitive?.content
                val (alignment, backing) = LyricMerging.inferRoleAlignment(role)

                LyricLine(
                    startMs = startMs,
                    text = text,
                    wordTimestamps = parseWords(obj["words"]?.jsonArray, startMs),
                    alignment = alignment,
                    isBackingVocal = backing,
                    role = singer ?: role,
                    singerName = singer ?: role,
                )
            }.toMutableList()

            if (lines.isEmpty()) return null
            Lyrics().apply {
                this.lines = lines
                LyricMerging.finalize(this)
            }
        } catch (_: Exception) {
            null
        }
    }

    /** 内容嗅探：AMLL 是 JSON，API 报错载荷（含 code/message）也要路由到这里并被丢弃 */
    fun looksLike(content: String): Boolean {
        val trimmed = content.trimStart()
        if (!trimmed.startsWith("{")) return false
        return listOf("\"lyrics\"", "\"lines\"", "\"role\"", "\"code\"", "\"message\"", "\"data\"")
            .any { trimmed.contains(it) }
    }

    private fun parseWords(array: JsonArray?, lineStartMs: Long): List<WordTimestamp>? {
        if (array == null || array.isEmpty()) return null
        val words = mutableListOf<WordTimestamp>()
        for (element in array) {
            val obj: JsonObject = runCatching { element.jsonObject }.getOrNull() ?: continue
            val word = obj["word"]?.jsonPrimitive?.content ?: continue
            val relStart = obj["startTime"].asLongOrNull() ?: continue
            val startMs = lineStartMs + relStart
            val endMs = obj["endTime"].asLongOrNull()
            val durationMs = when {
                endMs != null -> endMs - relStart
                else -> obj["duration"].asLongOrNull() ?: DEFAULT_WORD_MS
            }
            words += WordTimestamp(word, startMs, if (durationMs <= 0) DEFAULT_WORD_MS else durationMs)
        }
        return words.ifEmpty { null }
    }

    /** 数字或字符串两种写法都要能吃下（AMLL 导出里两者都常见） */
    private fun JsonElement?.asLongOrNull(): Long? =
        this?.jsonPrimitive?.content?.trim()?.toLongOrNull()
}
