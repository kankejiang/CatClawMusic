package com.catclaw.music.kt.lyrico.lyrics

import kotlinx.serialization.json.Json
import kotlinx.serialization.json.JsonArray
import kotlinx.serialization.json.JsonElement
import kotlinx.serialization.json.JsonObject
import kotlinx.serialization.json.JsonPrimitive
import kotlinx.serialization.json.jsonArray
import kotlinx.serialization.json.jsonObject

/**
 * 解析 Lyrico 插件的 `getLyrics` 返回值。
 *
 * 输入是候选数组的 JSON；每个元素按 `type` 分流到结构化或 raw 形态。
 * 容错优先：单个候选坏掉不影响其它候选（插件生态里字段名有历史变体）。
 */
object LyricoLyricsParser {

    private val json = Json { ignoreUnknownKeys = true; isLenient = true }

    /** @return 解析出的候选；无法解析时返回空列表 */
    fun parseCandidates(valueJson: String?): List<LyricoLyrics> {
        if (valueJson.isNullOrBlank()) return emptyList()
        val arr = runCatching { json.parseToJsonElement(valueJson).jsonArray }.getOrNull()
            ?: return parseSingle(valueJson).let { if (it != null) listOf(it) else emptyList() }
        return arr.mapNotNull { el ->
            runCatching { parseOne(el.jsonObject) }.getOrNull()
        }
    }

    private fun parseSingle(valueJson: String): LyricoLyrics? =
        runCatching { parseOne(json.parseToJsonElement(valueJson).jsonObject) }.getOrNull()

    private fun parseOne(obj: JsonObject): LyricoLyrics {
        val typeRaw = obj.string("type")
        val type = LyricoPayloadType.fromWire(typeRaw) ?: LyricoPayloadType.STRUCTURED
        val tags = (obj["tags"] as? JsonObject)?.entries
            ?.mapNotNull { (k, v) -> (v as? JsonPrimitive)?.content?.let { k to it } }
            ?.toMap()
            ?: emptyMap()

        if (type != LyricoPayloadType.STRUCTURED) {
            return LyricoLyrics(
                tags = tags,
                payloadType = type,
                rawPlainLrc = obj.string("rawPlainLrc", "plainLrc", "originalLrc", "original_lrc")
                    ?: obj.string("original")?.takeIf { !it.trimStart().startsWith("[") }.orEmpty(),
                rawVerbatimLrc = obj.string("rawVerbatimLrc", "raw_verbatim_lrc").orEmpty(),
                rawEnhancedLrc = obj.string("rawEnhancedLrc", "raw_enhanced_lrc").orEmpty(),
                rawTtml = obj.string("rawTtml", "raw_ttml").orEmpty(),
                rawMultiPersonEnhancedLrc = obj.string(
                    "rawMultiPersonEnhancedLrc",
                    "raw_multi_person_enhanced_lrc",
                ).orEmpty(),
            )
        }

        val original = obj.arrayOf("original", "lines").mapNotNull(::parseLine)
        val translated = obj.arrayOf("translated").mapNotNull(::parseLine).takeIf { it.isNotEmpty() }
        val romanization = obj.arrayOf("romanization").mapNotNull(::parseLine).takeIf { it.isNotEmpty() }
        return LyricoLyrics(
            tags = tags,
            payloadType = LyricoPayloadType.STRUCTURED,
            original = original,
            translated = translated,
            romanization = romanization,
            // 有的插件在 structured 里也带一份 raw 兜底
            rawPlainLrc = obj.string("rawPlainLrc", "originalLrc").orEmpty(),
            rawTtml = obj.string("rawTtml").orEmpty(),
        )
    }

    /**
     * 解析一行：`[start, end, words]`，words 为 `[[ws, we, text], ...]`。
     * 也容忍 `{start,end,words}` 的对象形态。
     */
    private fun parseLine(el: JsonElement): LyricoLine? {
        when (el) {
            is JsonArray -> {
                if (el.size < 3) return null
                val start = el[0].asLong() ?: return null
                val end = el[1].asLong() ?: return null
                val wordsEl = el[2] as? JsonArray ?: return null
                val words = wordsEl.mapNotNull(::parseWord)
                if (words.isEmpty()) return null
                return LyricoLine(start = start, end = end, words = words)
            }
            is JsonObject -> {
                val start = el.numberLong("start", "startTime", "time") ?: return null
                val end = el.numberLong("end", "endTime") ?: start
                val words = (el["words"] as? JsonArray)?.mapNotNull(::parseWord).orEmpty()
                if (words.isNotEmpty()) return LyricoLine(start, end, words)
                // 行对象只给了文本
                val text = el.string("text") ?: return null
                return LyricoLine(start, end, listOf(LyricoWord(start, end, text)))
            }
            else -> return null
        }
    }

    /** 解析一个词：`[start, end, text]` 或 `{start,end,text}`。 */
    private fun parseWord(el: JsonElement): LyricoWord? {
        when (el) {
            is JsonArray -> {
                if (el.size < 3) return null
                val start = el[0].asLong() ?: return null
                val end = el[1].asLong() ?: start
                val text = (el[2] as? JsonPrimitive)?.content ?: return null
                return LyricoWord(start, end, text)
            }
            is JsonObject -> {
                val start = el.numberLong("start", "startTime") ?: return null
                val end = el.numberLong("end", "endTime") ?: start
                val text = el.string("text") ?: return null
                return LyricoWord(start, end, text)
            }
            is JsonPrimitive -> {
                // 极简形态：只有文本，时间未知 → 用 -1 表示未知
                return LyricoWord(-1, -1, el.content)
            }
            else -> return null
        }
    }

    private fun JsonObject.string(vararg keys: String): String? {
        keys.forEach { k ->
            val v = this[k] as? JsonPrimitive ?: return@forEach
            if (v.isString) return v.content
        }
        return null
    }

    private fun JsonObject.numberLong(vararg keys: String): Long? {
        keys.forEach { k ->
            val v = this[k] as? JsonPrimitive ?: return@forEach
            v.content.toLongOrNull()?.let { return it }
            v.content.toDoubleOrNull()?.let { return it.toLong() }
        }
        return null
    }

    private fun JsonObject.arrayOf(vararg keys: String): List<JsonElement> {
        keys.forEach { k ->
            (this[k] as? JsonArray)?.let { return it }
        }
        return emptyList()
    }

    private fun JsonElement.asLong(): Long? {
        val p = this as? JsonPrimitive ?: return null
        return p.content.toLongOrNull() ?: p.content.toDoubleOrNull()?.toLong()
    }
}
