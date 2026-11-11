package com.catclaw.music.kt.core.netease

import java.text.Normalizer
import kotlin.math.abs

/**
 * 一首本地曲目的最小匹配信息。
 *
 * `:core` 不认识 `:data` 的行类型（与 `SongListRules` 同一条边界），所以由调用方投影后传进来。
 */
data class MatchCandidate(
    val id: Int,
    val title: String,
    val artist: String,
    val durationSeconds: Int,
)

/**
 * 网易云歌单曲目 ↔ 本地库的匹配器。
 *
 * 导入的目标是「本地已有的用本地文件，本地没有的才记成在线曲」—— 这一步判定错了，
 * 用户会看到歌单里躺着一首同名但完全不同的歌，而且点开播的是别人的翻唱。
 * 所以规则按「证据强度」分三级，越弱的规则阈值越紧：
 *
 * 1. 标题键 + 艺术家键全等（归一化后）；
 * 2. 标题键 + 主艺术家键相等（网易云常把 `A、B` 写成 `A/B`）；
 * 3. 只有标题相等时，要求时长在高置信区间内；若同名候选不止一首，
 *    还必须艺术家名有包含关系，否则**放弃匹配**（宁缺勿错）。
 */
class SongMatcher(candidates: List<MatchCandidate>) {

    private val exact = HashMap<String, MatchCandidate>(candidates.size * 2)
    private val byTitle = HashMap<String, MutableList<MatchCandidate>>(candidates.size * 2)

    init {
        for (candidate in candidates) {
            val titleKey = SongMatchKey.title(candidate.title)
            if (titleKey.isEmpty()) continue
            val artistKey = SongMatchKey.artist(candidate.artist)
            // 同一首歌在本地可能有多个文件（不同比特率/格式），先到先得即可：
            // 导入只需要一个可播的 id，挑哪一份对歌单语义没有区别
            exact.putIfAbsent("$titleKey|$artistKey", candidate)
            if (artistKey.isNotEmpty()) {
                SongMatchKey.primaryArtist(candidate.artist).takeIf { it.isNotEmpty() && it != artistKey }?.let {
                    exact.putIfAbsent("$titleKey|$it", candidate)
                }
            }
            byTitle.getOrPut(titleKey) { mutableListOf() }.add(candidate)
        }
    }

    fun match(title: String, artist: String, durationSeconds: Int): MatchCandidate? {
        val titleKey = SongMatchKey.title(title)
        if (titleKey.isEmpty()) return null

        val artistKey = SongMatchKey.artist(artist)
        exact["$titleKey|$artistKey"]?.let { if (evidenceAccepts(it, durationSeconds)) return it }
        SongMatchKey.primaryArtist(artist).takeIf { it.isNotEmpty() }?.let { primary ->
            exact["$titleKey|$primary"]?.let { if (evidenceAccepts(it, durationSeconds)) return it }
        }

        val sameTitle = byTitle[titleKey] ?: return null
        if (sameTitle.size == 1) {
            val only = sameTitle.first()
            return only.takeIf { durationClose(durationSeconds, it.durationSeconds, TITLE_ONLY_TOLERANCE) }
        }
        // 同名多版本：先靠艺术家名收敛（包含关系即可，中文名/英文名不要求相等）
        val related = sameTitle.filter { relatedArtist(it.artist, artist) }
        if (related.isEmpty()) return null
        val timed = related.filter { durationClose(durationSeconds, it.durationSeconds, ARTIST_TOLERANCE) }
        return timed.minByOrNull { abs(it.durationSeconds - durationSeconds) }
    }

    /**
     * 标题与艺术家都对上时，时长只作为**反驳证据**：差得太远（超过 5 秒）说明是现场版/剪辑版，
     * 宁可让后面更弱的规则重新判；但任一侧时长为 0（扫描时没读到）不能当反证 ——
     * 标题+艺术家已经足够强，缺一个字段不该让整首歌匹配不上。
     */
    private fun evidenceAccepts(candidate: MatchCandidate, durationSeconds: Int): Boolean =
        durationSeconds <= 0 || candidate.durationSeconds <= 0 ||
            abs(candidate.durationSeconds - durationSeconds) <= ARTIST_TOLERANCE

    private fun relatedArtist(localArtist: String, remoteArtist: String): Boolean {
        val a = SongMatchKey.artist(localArtist)
        val b = SongMatchKey.artist(remoteArtist)
        if (a.isEmpty() || b.isEmpty()) return false
        return a.contains(b) || b.contains(a)
    }

    /** 任一侧缺时长（0）就不拿时长当证据，直接判否 —— 缺数据不该变成"匹配成功" */
    private fun durationClose(a: Int, b: Int, tolerance: Int): Boolean =
        a > 0 && b > 0 && abs(a - b) <= tolerance

    private companion object {
        /** 标题+艺术家都对上时的时长容差：只用来兜住不同来源的几秒误差 */
        const val ARTIST_TOLERANCE = 5

        /** 只有标题对得上时的时长容差：收紧到 2 秒，避免同名不同曲 */
        const val TITLE_ONLY_TOLERANCE = 2
    }
}

/**
 * 归一化规则。单独放出来是因为「匹配失败」和「归一化把两个名字变成同一个键」
 * 在日志里长得一样，测试只钉这里就能定位是哪一层的问题。
 */
object SongMatchKey {

    /** 括号里的都是装饰：`(feat. xx)`、`（Live）`、`[Remastered 2011]`、`【伴奏】` */
    private val BRACKETS = Regex("""[\(\[（【][^\)\]）】]*[\)\]）】]""")

    /** 标点、符号、空白全部丢掉：中英文混排时分隔符差异最大 */
    private val NOISE = Regex("""[\p{P}\p{S}\s]+""")

    /** 多艺术家分隔符：`A、B`、`A/B`、`A,B`、`A & B`、`A feat. B`、`A ft. B` */
    private val ARTIST_SPLIT = Regex("""\s*(?:/|、|,|，|;|；|&|＆|\bfeat\.?\b|\bft\.?\b|\bvs\.?\b)\s*""", RegexOption.IGNORE_CASE)

    fun title(raw: String): String = normalize(raw.replace(BRACKETS, " "))

    fun artist(raw: String): String = normalize(raw.replace(BRACKETS, " "))

    /** 主艺术家：歌曲详情里 `ar` 是数组，某些来源会写成一条长串，取第一段 */
    fun primaryArtist(raw: String): String {
        val first = ARTIST_SPLIT.split(raw.replace(BRACKETS, " ")).firstOrNull().orEmpty()
        return normalize(first)
    }

    private fun normalize(raw: String): String {
        // NFKC 顺带把全角字母数字折成半角（`ＡＢＣ`、`１２３` 在两边来源里都常见）
        val folded = runCatching { Normalizer.normalize(raw, Normalizer.Form.NFKC) }.getOrDefault(raw)
        return folded.lowercase().replace(NOISE, "")
    }
}
