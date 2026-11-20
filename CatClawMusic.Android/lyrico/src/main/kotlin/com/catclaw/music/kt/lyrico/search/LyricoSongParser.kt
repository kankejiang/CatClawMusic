package com.catclaw.music.kt.lyrico.search

import kotlinx.serialization.json.Json
import kotlinx.serialization.json.JsonArray
import kotlinx.serialization.json.JsonObject
import kotlinx.serialization.json.JsonPrimitive

/**
 * 插件 `searchSongs` 返回的一条歌曲记录。
 *
 * 必须保留 [json] 原文：`getLyrics` 要的就是这条记录（含各插件塞在
 * `internal` 里的真实 id），挑字段重建会丢信息。
 */
data class LyricoSongHit(
    val id: String,
    val title: String,
    val artist: String,
    val album: String,
    val durationMs: Long,
    /** 插件给的封面地址（searchSongs 里就有，封面搜索直接用这个，省一次请求） */
    val picUrl: String,
    val json: String,
)

/** 解析 `searchSongs` 返回值。 */
object LyricoSongParser {

    private val json = Json { ignoreUnknownKeys = true; isLenient = true }

    fun parse(valueJson: String?): List<LyricoSongHit> {
        if (valueJson.isNullOrBlank()) return emptyList()
        val arr = runCatching { json.parseToJsonElement(valueJson) as? JsonArray }.getOrNull()
            ?: return emptyList()
        return arr.mapNotNull { el ->
            val obj = el as? JsonObject ?: return@mapNotNull null
            val id = obj.str("id")
            val title = obj.str("title").orEmpty()
            if (id.isNullOrBlank() && title.isBlank()) return@mapNotNull null
            LyricoSongHit(
                id = id.orEmpty(),
                title = title,
                artist = obj.str("artist").orEmpty(),
                album = obj.str("album").orEmpty(),
                // 插件给的 duration 有秒也有毫秒，统一到毫秒：小于 10000 视为秒
                durationMs = parseDuration(obj.str("duration")),
                picUrl = obj.str("picUrl").orEmpty(),
                json = el.toString(),
            )
        }
    }

    private fun parseDuration(raw: String?): Long {
        val v = raw?.toDoubleOrNull() ?: return 0L
        if (v <= 0) return 0L
        return if (v < 10_000) (v * 1000).toLong() else v.toLong()
    }

    private fun JsonObject.str(key: String): String? {
        val p = this[key] as? JsonPrimitive ?: return null
        return if (p.isString) p.content else p.content
    }
}

/**
 * 在插件返回的歌曲列表里挑出与目标最接近的一首。
 *
 * 只做「够用」的判断：歌名高权重、艺人次之、时长兜底；分数太低（< 0.5）就不选，
 * 免得把同名不同歌的歌词写进去。
 */
object LyricoSongMatcher {

    fun pickBest(target: LyricsTarget, hits: List<LyricoSongHit>): LyricoSongHit? =
        hits.map { it to score(target, it) }
            .filter { it.second >= 0.5 }
            .maxByOrNull { it.second }
            ?.first

    fun score(target: LyricsTarget, hit: LyricoSongHit): Double {
        val t = normalize(target.title)
        val h = normalize(hit.title)
        val titleScore = when {
            t.isEmpty() || h.isEmpty() -> 0.0
            t == h -> 1.0
            h.contains(t) || t.contains(h) -> 0.8
            else -> 0.0
        }
        val artistScore = when {
            target.artist.isBlank() || hit.artist.isBlank() -> 0.5
            normalize(target.artist) == normalize(hit.artist) -> 1.0
            normalize(hit.artist).contains(normalize(target.artist)) -> 0.8
            else -> 0.0
        }
        val durationScore = when {
            target.durationMs <= 0 || hit.durationMs <= 0 -> 0.5
            else -> {
                val diff = kotlin.math.abs(target.durationMs - hit.durationMs)
                if (diff <= 2000) 1.0 else if (diff >= 15_000) 0.0 else 1.0 - diff / 15_000.0
            }
        }
        return titleScore * 0.55 + artistScore * 0.30 + durationScore * 0.15
    }

    private fun normalize(text: String): String = text.lowercase()
        .replace(Regex("""[\s\-_()（）\[\]【】「」,.，。!！?？'"“”‘’]"""), "")
        .trim()
}
