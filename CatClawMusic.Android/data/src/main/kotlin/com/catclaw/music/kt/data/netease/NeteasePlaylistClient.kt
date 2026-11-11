package com.catclaw.music.kt.data.netease

import com.catclaw.music.kt.core.ai.SourceHttp
import javax.inject.Inject
import javax.inject.Singleton
import kotlinx.serialization.json.Json
import kotlinx.serialization.json.JsonArray
import kotlinx.serialization.json.JsonObject
import kotlinx.serialization.json.contentOrNull
import kotlinx.serialization.json.intOrNull
import kotlinx.serialization.json.jsonPrimitive
import kotlinx.serialization.json.longOrNull

/**
 * 网易云歌单详情 + 播放取链。
 *
 * **主链路走 eapi**（`/eapi/v6/playlist/detail`，`n=100000`）：本地插件项目
 * `CatClawMusic.Plugins.Netease` 已经把这些接口的参数、风控伪装与两段式取曲目验证过，
 * 这里照搬它的顺序：
 * 1. eapi 歌单详情一次拿 `tracks`（桌面客户端身份能突破网页版 1000 首上限）；
 * 2. `tracks` 少于 `trackIds` 时，按 `trackIds` 切片调 `/eapi/v3/song/detail` 批量补齐；
 *    否则超长歌单会少歌。
 *
 * eapi 整体失败（网络/风控/解密）时回落到网页版 `/api/v6/playlist/detail?n=1000` ——
 * 这条路匿名可读但会截断千首以上，所以只当兜底。
 *
 * 播放地址优先 eapi `enhance/player/url/v1`（带 cookie 可到无损），失败再给
 * `song/media/outer/url` 免登录跳转入口 —— 后者每次 302 到当次签名的 CDN，可以长期存库。
 */
@Singleton
class NeteasePlaylistClient @Inject constructor(
    private val http: SourceHttp,
    private val eapi: NeteaseEapiChannel,
) {

    data class Track(
        val id: Long,
        val title: String,
        val artist: String,
        val album: String,
        val durationSeconds: Int,
        val coverUrl: String?,
    )

    data class Playlist(
        val id: String,
        val name: String,
        val coverUrl: String?,
        /** 歌单自称的总数；可能大于 [tracks]（超长歌单接口只给前 1000 首） */
        val trackCount: Int,
        val tracks: List<Track>,
    )

    /** 音质档：标准 / 极高 / 无损。匿名只能用前两档，`lostless` 需要登录 cookie */
    enum class Quality(val eapiLevel: String) {
        Standard("standard"),
        Exhigh("exhigh"),
        Lossless("lossless"),
    }

    /** @return 拉取/解析失败返回 null，由调用方翻译成用户能读的错误文案 */
    suspend fun fetch(playlistId: String, cookie: String? = null): Playlist? =
        fetchViaEapi(playlistId, cookie) ?: fetchViaWeb(playlistId)

    /**
     * 取播放直链。eapi 失败返回 null，调用方再退到 [playUrlFallback]。
     * 返回的地址带时效（几十分钟），**不能落库**。
     */
    suspend fun playUrl(
        songId: Long,
        quality: Quality = Quality.Exhigh,
        cookie: String? = null,
    ): String? {
        // 未登录请求无损档会被风控，直接降到极高 —— 与插件 `GetPlayUrlWithTypeAsync` 同规则
        val level = if (quality == Quality.Lossless && cookie.isNullOrBlank()) {
            Quality.Exhigh.eapiLevel
        } else {
            quality.eapiLevel
        }
        val raw = eapi.request(
            path = "/eapi/song/enhance/player/url/v1",
            params = linkedMapOf(
                // 官方客户端发的是字符串 "[123]"，不是数组
                "ids" to "[$songId]",
                "level" to level,
                "encodeType" to "flac",
            ),
            rawCipherResponse = true,
            cookie = cookie,
        ) ?: return null
        val root = parseObject(raw) ?: return null
        val data = root["data"] as? JsonArray ?: return null
        return data.firstOrNull()
            ?.let { it as? JsonObject }
            ?.get("url")
            ?.jsonPrimitive
            ?.contentOrNull
            ?.takeIf { it.length > 10 }
    }

    // ── eapi ───────────────────────────────────────────────────

    private suspend fun fetchViaEapi(playlistId: String, cookie: String?): Playlist? {
        val id = playlistId.toLongOrNull() ?: return null
        val raw = eapi.request(
            path = "/eapi/v6/playlist/detail",
            params = linkedMapOf("id" to id, "n" to 100_000, "s" to 8),
            rawCipherResponse = true,
            cookie = cookie,
        ) ?: return null
        val root = parseObject(raw) ?: return null
        val playlist = root["playlist"] as? JsonObject ?: return null

        val quick = parseTracks(playlist["tracks"] as? JsonArray)
        val trackIds = (playlist["trackIds"] as? JsonArray)
            ?.mapNotNull { (it as? JsonObject)?.get("id")?.jsonPrimitive?.longOrNull }
            .orEmpty()

        val tracks = if (trackIds.size > quick.size) {
            // 两段式：先按 trackIds 建索引，缺的批量补，最后按歌单原顺序重排
            val byId = quick.associateBy { it.id }.toMutableMap()
            trackIds.filter { it !in byId }.chunked(DETAIL_CHUNK).forEach { chunk ->
                val detail = eapi.songDetailRaw(chunk, cookie) ?: return@forEach
                val songs = parseObject(detail)?.get("songs") as? JsonArray ?: return@forEach
                parseTracks(songs).forEach { byId[it.id] = it }
            }
            trackIds.mapNotNull { byId[it] }
        } else {
            quick
        }

        val name = playlist["name"]?.jsonPrimitive?.contentOrNull?.trim().orEmpty()
        if (name.isEmpty() && tracks.isEmpty()) return null
        return Playlist(
            id = playlistId,
            name = name.ifEmpty { "网易云歌单 $playlistId" },
            coverUrl = httpsImage(playlist["coverImgUrl"]?.jsonPrimitive?.contentOrNull),
            trackCount = playlist["trackCount"]?.jsonPrimitive?.intOrNull ?: tracks.size,
            tracks = tracks,
        )
    }

    // ── 网页版回落 ─────────────────────────────────────────────

    private suspend fun fetchViaWeb(playlistId: String): Playlist? {
        val url = "$DETAIL_API?id=$playlistId&n=$MAX_TRACKS"
        val bytes = http.get(url, webHeaders(), ByteArray(0)) ?: return null
        val text = runCatching { String(bytes, Charsets.UTF_8) }.getOrNull() ?: return null
        val root = parseObject(text) ?: return null
        if ((root["code"]?.jsonPrimitive?.intOrNull ?: -1) != 200) return null

        val playlist = root["playlist"] as? JsonObject ?: return null
        val name = playlist["name"]?.jsonPrimitive?.contentOrNull?.trim().orEmpty()
        val tracks = parseTracks(playlist["tracks"] as? JsonArray)
        if (name.isEmpty() && tracks.isEmpty()) return null

        return Playlist(
            id = playlistId,
            name = name.ifEmpty { "网易云歌单 $playlistId" },
            coverUrl = httpsImage(playlist["coverImgUrl"]?.jsonPrimitive?.contentOrNull),
            trackCount = playlist["trackCount"]?.jsonPrimitive?.intOrNull ?: tracks.size,
            tracks = tracks,
        )
    }

    // ── 解析 ───────────────────────────────────────────────────

    private fun parseTracks(array: JsonArray?): List<Track> =
        array?.mapNotNull { parseTrack(it as? JsonObject) }.orEmpty()

    private fun parseTrack(obj: JsonObject?): Track? {
        val o = obj ?: return null
        val id = o["id"]?.jsonPrimitive?.longOrNull ?: return null
        if (id <= 0) return null
        val title = o["name"]?.jsonPrimitive?.contentOrNull?.trim().orEmpty()
        if (title.isEmpty()) return null
        val artists = (o["ar"] as? JsonArray)
            ?.mapNotNull { (it as? JsonObject)?.get("name")?.jsonPrimitive?.contentOrNull }
            ?.joinToString(", ")
            .orEmpty()
        val album = o["al"] as? JsonObject
        return Track(
            id = id,
            title = title,
            artist = artists,
            album = album?.get("name")?.jsonPrimitive?.contentOrNull.orEmpty(),
            durationSeconds = ((o["dt"]?.jsonPrimitive?.longOrNull ?: 0L) / 1000).toInt(),
            coverUrl = httpsImage(album?.get("picUrl")?.jsonPrimitive?.contentOrNull),
        )
    }

    private fun parseObject(text: String): JsonObject? =
        runCatching { JSON.parseToJsonElement(text) as? JsonObject }.getOrNull()

    /**
     * 网易云给的封面是 `http://p2.music.126.net/...`，而应用只对网易云域名开了明文，
     * 列表里的封面走的是 `HttpURLConnection` 图片加载（不受 OkHttp 配置影响时更严格），
     * 统一升成 https 最稳。
     */
    private fun httpsImage(raw: String?): String? =
        raw?.takeIf { it.isNotBlank() }?.replaceFirst("http://", "https://")

    private fun webHeaders(): Map<String, String> = mapOf(
        "User-Agent" to WEB_UA,
        "Referer" to "https://music.163.com/",
        "Cookie" to "os=pc; appver=8.9.70",
    )

    companion object {
        private const val DETAIL_API = "https://music.163.com/api/v6/playlist/detail"
        private const val MAX_TRACKS = 1000
        private const val DETAIL_CHUNK = 500
        private const val WEB_UA =
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) " +
                "Chrome/124.0 Safari/537.36"

        private val JSON = Json { ignoreUnknownKeys = true; isLenient = true }

        /**
         * 免登录播放入口。302 到当次签名的 CDN 地址，所以可以长期存库；
         * `AudioCache` 以这个 URL 为键缓存到本地后即可离线播放。
         */
        fun playUrlFallback(songId: Long): String =
            "https://music.163.com/song/media/outer/url?id=$songId.mp3"

        /** 在线曲在 `RemoteId` 上的前缀，用来和 WebDAV/Subsonic 的服务器 id 区分 */
        const val REMOTE_PREFIX = "netease:"
    }
}
