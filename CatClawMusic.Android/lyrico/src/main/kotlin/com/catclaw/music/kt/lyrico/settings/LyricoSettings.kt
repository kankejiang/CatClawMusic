package com.catclaw.music.kt.lyrico.settings

import kotlinx.serialization.json.Json
import kotlinx.serialization.json.JsonArray
import kotlinx.serialization.json.JsonObject
import kotlinx.serialization.json.JsonPrimitive
import kotlinx.serialization.json.booleanOrNull
import kotlinx.serialization.json.contentOrNull

/**
 * 标签编辑时展示哪些字段。
 *
 * 为什么可配：不同人对「标签」的关注点差别很大 —— 有人只要歌名/歌手/专辑，
 * 有人要作曲作词注释。全展示会让编辑页太长（手机上要滚好几屏）。
 */
data class EditFieldVisibility(
    val albumArtist: Boolean = true,
    val genre: Boolean = true,
    val date: Boolean = true,
    val trackNumber: Boolean = true,
    val discNumber: Boolean = false,
    val composer: Boolean = false,
    val lyricist: Boolean = false,
    val comment: Boolean = false,
) {
    /** 必显字段不受此配置影响（标题/歌手/专辑） */
    fun isVisible(key: String): Boolean = when (key) {
        "albumArtist" -> albumArtist
        "genre" -> genre
        "date" -> date
        "trackNumber" -> trackNumber
        "discNumber" -> discNumber
        "composer" -> composer
        "lyricist" -> lyricist
        "comment" -> comment
        else -> true
    }

    fun toggle(key: String, enabled: Boolean): EditFieldVisibility = when (key) {
        "albumArtist" -> copy(albumArtist = enabled)
        "genre" -> copy(genre = enabled)
        "date" -> copy(date = enabled)
        "trackNumber" -> copy(trackNumber = enabled)
        "discNumber" -> copy(discNumber = enabled)
        "composer" -> copy(composer = enabled)
        "lyricist" -> copy(lyricist = enabled)
        "comment" -> copy(comment = enabled)
        else -> this
    }
}

/**
 * 歌词清理规则（写入标签前套用）。
 *
 * 这些规则对应「拿到一份网络歌词后常见的脏数据」：
 * 制作人员行、空行、尾部空白、首尾多余空行。
 */
data class LyricsCleanupRules(
    /** 去掉「作词：xxx」「作曲：xxx」这类制作人员行 */
    val removeCreditLines: Boolean = false,
    /** 去掉空行 */
    val removeEmptyLines: Boolean = true,
    /** 去掉每行首尾空白 */
    val trimLines: Boolean = true,
    /** 去掉 `[ti:...]` 之类的全局标签行 */
    val removeMetadataTags: Boolean = false,
    /** 去掉行内的 `[00:12.34]` 重复时间戳（一行多个时间戳时只留第一个） */
    val dedupeTimestamps: Boolean = true,
)

/** 艺人拆分：`A/B`、`A & B`、`A、B` 都拆成多个艺人。 */
data class ArtistSplitConfig(
    val enabled: Boolean = false,
    /** 作为分隔符的字符（不含空格，空格太容易误拆） */
    val separators: List<String> = listOf("/", "&", "、", "；", ";", "feat.", "ft."),
    /** 拆分后是否用统一分隔符重新拼接 */
    val joinSeparator: String = "/",
)

/** 字符映射：把某些写法统一（常见于日文/繁简/全角）。 */
data class CharacterMapping(
    val enabled: Boolean = false,
    /** 源 → 目标 */
    val pairs: Map<String, String> = emptyMap(),
) {
    /** 按最长键优先替换，避免短键把长键拆坏 */
    fun convert(text: String): String {
        if (!enabled || pairs.isEmpty()) return text
        var result = text
        pairs.entries
            .sortedByDescending { it.key.length }
            .forEach { (from, to) ->
                if (from.isNotEmpty()) result = result.replace(from, to)
            }
        return result
    }
}

/**
 * 全部 Lyrico 侧设置。
 *
 * 闭包在一个 data class 里而不是散成一堆单个开关：DAO 只存一行 JSON，
 * 加字段时不必改表结构（`lyrico.db` 的 schema 已被测试冻结）。
 */
data class LyricoSettings(
    val editFields: EditFieldVisibility = EditFieldVisibility(),
    val cleanup: LyricsCleanupRules = LyricsCleanupRules(),
    val artistSplit: ArtistSplitConfig = ArtistSplitConfig(),
    val characterMapping: CharacterMapping = CharacterMapping(),
    /** 默认歌词格式（歌词匹配页与批量任务的初始值） */
    val defaultLyricsFormat: String = "PLAIN",
    /** 批量任务默认是否覆盖已有歌词/封面 */
    val defaultOverwrite: Boolean = false,
)

/** `LyricoSettings` 的 JSON 序列化。手写而不用反射，避免再引一个序列化器。 */
object LyricoSettingsCodec {

    private val json = Json { ignoreUnknownKeys = true; isLenient = true }

    fun encode(s: LyricoSettings): String = buildString {
        append("{\"editFields\":{")
        append("\"albumArtist\":").append(s.editFields.albumArtist).append(',')
        append("\"genre\":").append(s.editFields.genre).append(',')
        append("\"date\":").append(s.editFields.date).append(',')
        append("\"trackNumber\":").append(s.editFields.trackNumber).append(',')
        append("\"discNumber\":").append(s.editFields.discNumber).append(',')
        append("\"composer\":").append(s.editFields.composer).append(',')
        append("\"lyricist\":").append(s.editFields.lyricist).append(',')
        append("\"comment\":").append(s.editFields.comment)
        append("},\"cleanup\":{")
        append("\"removeCreditLines\":").append(s.cleanup.removeCreditLines).append(',')
        append("\"removeEmptyLines\":").append(s.cleanup.removeEmptyLines).append(',')
        append("\"trimLines\":").append(s.cleanup.trimLines).append(',')
        append("\"removeMetadataTags\":").append(s.cleanup.removeMetadataTags).append(',')
        append("\"dedupeTimestamps\":").append(s.cleanup.dedupeTimestamps)
        append("},\"artistSplit\":{")
        append("\"enabled\":").append(s.artistSplit.enabled).append(',')
        append("\"separators\":[")
        s.artistSplit.separators.forEachIndexed { i, v ->
            if (i > 0) append(',')
            append('"').append(escape(v)).append('"')
        }
        append("],\"joinSeparator\":\"").append(escape(s.artistSplit.joinSeparator)).append('"')
        append("},\"characterMapping\":{")
        append("\"enabled\":").append(s.characterMapping.enabled).append(',')
        append("\"pairs\":{")
        s.characterMapping.pairs.entries.forEachIndexed { i, (k, v) ->
            if (i > 0) append(',')
            append('"').append(escape(k)).append("\":\"").append(escape(v)).append('"')
        }
        append("}},\"defaultLyricsFormat\":\"").append(escape(s.defaultLyricsFormat)).append('"')
        append(",\"defaultOverwrite\":").append(s.defaultOverwrite)
        append('}')
    }

    fun decode(raw: String): LyricoSettings {
        if (raw.isBlank()) return LyricoSettings()
        val root = runCatching { json.parseToJsonElement(raw) as? JsonObject }.getOrNull()
            ?: return LyricoSettings()
        val d = LyricoSettings()

        val ef = root["editFields"] as? JsonObject
        val editFields = EditFieldVisibility(
            albumArtist = ef.bool("albumArtist") ?: d.editFields.albumArtist,
            genre = ef.bool("genre") ?: d.editFields.genre,
            date = ef.bool("date") ?: d.editFields.date,
            trackNumber = ef.bool("trackNumber") ?: d.editFields.trackNumber,
            discNumber = ef.bool("discNumber") ?: d.editFields.discNumber,
            composer = ef.bool("composer") ?: d.editFields.composer,
            lyricist = ef.bool("lyricist") ?: d.editFields.lyricist,
            comment = ef.bool("comment") ?: d.editFields.comment,
        )

        val cu = root["cleanup"] as? JsonObject
        val cleanup = LyricsCleanupRules(
            removeCreditLines = cu.bool("removeCreditLines") ?: d.cleanup.removeCreditLines,
            removeEmptyLines = cu.bool("removeEmptyLines") ?: d.cleanup.removeEmptyLines,
            trimLines = cu.bool("trimLines") ?: d.cleanup.trimLines,
            removeMetadataTags = cu.bool("removeMetadataTags") ?: d.cleanup.removeMetadataTags,
            dedupeTimestamps = cu.bool("dedupeTimestamps") ?: d.cleanup.dedupeTimestamps,
        )

        val asr = root["artistSplit"] as? JsonObject
        val artistSplit = ArtistSplitConfig(
            enabled = asr.bool("enabled") ?: d.artistSplit.enabled,
            separators = (asr?.get("separators") as? JsonArray)
                ?.mapNotNull { (it as? JsonPrimitive)?.contentOrNull }
                ?.takeIf { it.isNotEmpty() }
                ?: d.artistSplit.separators,
            joinSeparator = asr.str("joinSeparator") ?: d.artistSplit.joinSeparator,
        )

        val cm = root["characterMapping"] as? JsonObject
        val characterMapping = CharacterMapping(
            enabled = cm.bool("enabled") ?: d.characterMapping.enabled,
            pairs = (cm?.get("pairs") as? JsonObject)?.entries
                ?.mapNotNull { (k, v) -> (v as? JsonPrimitive)?.contentOrNull?.let { k to it } }
                ?.toMap()
                ?: d.characterMapping.pairs,
        )

        return LyricoSettings(
            editFields = editFields,
            cleanup = cleanup,
            artistSplit = artistSplit,
            characterMapping = characterMapping,
            defaultLyricsFormat = root.str("defaultLyricsFormat") ?: d.defaultLyricsFormat,
            defaultOverwrite = root.bool("defaultOverwrite") ?: d.defaultOverwrite,
        )
    }

    private fun JsonObject?.bool(key: String): Boolean? =
        (this?.get(key) as? JsonPrimitive)?.booleanOrNull

    private fun JsonObject?.str(key: String): String? =
        (this?.get(key) as? JsonPrimitive)?.contentOrNull

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
