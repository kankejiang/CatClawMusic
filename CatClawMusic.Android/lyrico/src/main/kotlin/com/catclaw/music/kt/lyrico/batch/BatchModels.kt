package com.catclaw.music.kt.lyrico.batch

import com.catclaw.music.kt.lyrico.lyrics.LyricoLyricsFormat
import kotlinx.serialization.json.Json
import kotlinx.serialization.json.JsonObject
import kotlinx.serialization.json.booleanOrNull
import kotlinx.serialization.json.jsonPrimitive

/** 批量操作类型（对齐 Lyrico 的 7 个 processor）。 */
enum class BatchOperation(val key: String, val label: String) {
    MATCH_LYRICS("matchLyrics", "匹配歌词"),
    MATCH_COVER("matchCover", "匹配封面"),
    MATCH_METADATA("matchMetadata", "匹配元数据"),
    EDIT_TAGS("editTags", "批量改标签"),
    LYRICS_FORMAT("lyricsFormat", "歌词格式转换"),
    RENAME_FILES("renameFiles", "重命名文件"),
    /** 需要 ReplayGain 扫描（ffmpeg + ebur128），当前未移植 DSP，执行时明确跳过而非静默失败 */
    REPLAY_GAIN("replayGain", "ReplayGain"),
    ;

    companion object {
        fun fromKey(key: String): BatchOperation? = entries.firstOrNull { it.key == key }
    }
}

/**
 * 批量任务参数。
 *
 * 用 JSON 存（`BatchTaskEntity.optionsJson`）：7 种操作参数差异太大，
 * 给每种操作加列会让表变成一堆 nullable。解析失败一律退回默认值 —— 批量任务
 * 不该因为一个参数格式不对就整个跑不起来。
 */
data class BatchOptions(
    /** 已有歌词/封面时是否覆盖 */
    val overwrite: Boolean = false,
    /** 目标歌词格式 */
    val lyricsFormat: LyricoLyricsFormat = LyricoLyricsFormat.PLAIN,
    /** 重命名模板，占位符：{artist} {title} {album} {track} {year} */
    val renamePattern: String = "{artist} - {title}",
    /** 批量改标签：要设置的字段 */
    val setFields: Map<String, String> = emptyMap(),
    /** 批量改标签：要清空的字段（键名同 setFields） */
    val clearFields: List<String> = emptyList(),
    /** 匹配元数据时只补空字段（默认 true，避免覆盖用户已整理的信息） */
    val onlyFillMissing: Boolean = true,
)

object BatchOptionsCodec {

    private val json = Json { ignoreUnknownKeys = true; isLenient = true }

    fun encode(options: BatchOptions): String = buildString {
        append("{\"overwrite\":").append(options.overwrite)
        append(",\"lyricsFormat\":\"").append(options.lyricsFormat.name).append('"')
        append(",\"renamePattern\":\"").append(escape(options.renamePattern)).append('"')
        append(",\"onlyFillMissing\":").append(options.onlyFillMissing)
        append(",\"setFields\":{")
        options.setFields.entries.forEachIndexed { i, (k, v) ->
            if (i > 0) append(',')
            append('"').append(escape(k)).append("\":\"").append(escape(v)).append('"')
        }
        append("},\"clearFields\":[")
        options.clearFields.forEachIndexed { i, f ->
            if (i > 0) append(',')
            append('"').append(escape(f)).append('"')
        }
        append("]}")
    }

    fun decode(raw: String): BatchOptions {
        if (raw.isBlank()) return BatchOptions()
        val obj = runCatching { json.parseToJsonElement(raw) as JsonObject }.getOrNull()
            ?: return BatchOptions()
        val format = runCatching {
            LyricoLyricsFormat.valueOf(
                obj["lyricsFormat"]?.jsonPrimitive?.content ?: "PLAIN",
            )
        }.getOrDefault(LyricoLyricsFormat.PLAIN)
        val setFields = (obj["setFields"] as? JsonObject)?.entries
            ?.mapNotNull { (k, v) -> runCatching { k to v.jsonPrimitive.content }.getOrNull() }
            ?.toMap()
            ?: emptyMap()
        val clearFields = (obj["clearFields"] as? kotlinx.serialization.json.JsonArray)
            ?.mapNotNull { runCatching { it.jsonPrimitive.content }.getOrNull() }
            ?: emptyList()
        return BatchOptions(
            overwrite = obj["overwrite"]?.jsonPrimitive?.booleanOrNull ?: false,
            lyricsFormat = format,
            renamePattern = obj["renamePattern"]?.jsonPrimitive?.content ?: "{artist} - {title}",
            setFields = setFields,
            clearFields = clearFields,
            onlyFillMissing = obj["onlyFillMissing"]?.jsonPrimitive?.booleanOrNull ?: true,
        )
    }

    private fun escape(s: String): String = buildString(s.length + 8) {
        s.forEach { c ->
            when (c) {
                '"' -> append("\\\"")
                '\\' -> append("\\\\")
                '\n' -> append("\\n")
                '\r' -> append("\\r")
                '\t' -> append("\\t")
                else -> append(c)
            }
        }
    }
}

/** 处理器对单个条目的结论。 */
sealed interface BatchItemResult {
    data class Success(val message: String) : BatchItemResult
    data class Skipped(val reason: String) : BatchItemResult
    data class Failed(val message: String) : BatchItemResult
}

/** 处理器拿到的输入。 */
data class BatchItemContext(
    val songId: Int,
    val title: String,
    val artist: String,
    val album: String,
    val trackNumber: Int,
    val year: Int,
    val filePath: String,
    val durationMs: Long,
    val options: BatchOptions,
)

/** 一个批量处理器。实现必须自己吞掉可预期的异常，返回 Failed 而不是抛。 */
interface BatchProcessor {
    val operation: BatchOperation
    suspend fun process(item: BatchItemContext): BatchItemResult
}
