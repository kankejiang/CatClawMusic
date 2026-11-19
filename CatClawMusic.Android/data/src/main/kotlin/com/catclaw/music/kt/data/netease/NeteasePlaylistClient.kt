package com.catclaw.music.kt.data.netease

import com.catclaw.music.kt.core.ai.SourceHttp
import kotlinx.coroutines.async
import kotlinx.coroutines.awaitAll
import kotlinx.coroutines.coroutineScope
import kotlinx.coroutines.sync.Semaphore
import kotlinx.coroutines.sync.withPermit
import okhttp3.OkHttpClient
import okhttp3.Request
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

    /** 音质档：标准 / 极高 / 无损 / Hi-Res / 高清臻音。匿名只能用前两档，无损及以上需要登录 cookie */
    enum class Quality(val eapiLevel: String) {
        Standard("standard"),
        Exhigh("exhigh"),
        Lossless("lossless"),
        Hires("hires"),
        Jyeffect("jyeffect"),
    }

    /** @return 拉取/解析失败返回 null，由调用方翻译成用户能读的错误文案 */
    suspend fun fetch(playlistId: String, cookie: String? = null): Playlist? {
        // 「我喜欢的音乐」874 首每次进入都全量拉一遍太慢：5 分钟内复用同一份结果。
        // key 带 cookie 指纹，换账号不会串数据。
        val key = "$playlistId@${cookie?.hashCode() ?: 0}"
        val now = System.currentTimeMillis()
        fetchCache[key]?.takeIf { now - it.first < FETCH_CACHE_TTL_MS }?.let { (_, cached) ->
            com.catclaw.music.kt.core.diagnostic.AppLog.d(
                "NeteaseTiming", "playlist cache-hit id=$playlistId tracks=${cached.tracks.size}",
            )
            return cached
        }
        val t0 = System.currentTimeMillis()
        val result = fetchViaEapi(playlistId, cookie) ?: fetchViaWeb(playlistId) ?: return null
        fetchCache[key] = now to result
        com.catclaw.music.kt.core.diagnostic.AppLog.d(
            "NeteaseTiming",
            "playlist fetch id=$playlistId tracks=${result.tracks.size} ${System.currentTimeMillis() - t0}ms",
        )
        return result
    }

    private val fetchCache = java.util.concurrent.ConcurrentHashMap<String, Pair<Long, Playlist>>()

    /**
     * 取播放直链。eapi 失败返回 null，调用方再退到 [playUrlFallback]。
     * 返回的地址带时效（几十分钟），**不能落库**。
     */
    suspend fun playUrl(
        songId: Long,
        quality: Quality = Quality.Exhigh,
        cookie: String? = null,
    ): String? {
        // 未登录请求无损及以上档会被风控，直接降到极高 —— 与插件 `GetPlayUrlWithTypeAsync` 同规则
        val level = if (quality.ordinal >= Quality.Lossless.ordinal && cookie.isNullOrBlank()) {
            Quality.Exhigh.eapiLevel
        } else {
            quality.eapiLevel
        }
        // 与插件 `GetEnhanceUrlEapiAsync` 完全一致：所有档位都固定 flac。
        // 曾按档位改过 aac，但桌面客户端指纹下 aac 组合会让部分会员歌被判成试听
        // （响应 code=200 + freeTrialInfo），对齐插件才稳定拿完整链。
        val encodeType = "flac"
        com.catclaw.music.kt.core.diagnostic.AppLog.d(
            "NeteaseVip",
            "playUrl req id=$songId level=$level enc=$encodeType " +
                "loggedIn=${!cookie.isNullOrBlank()} osPc=${cookie?.contains("os=pc") == true} " +
                "appver=${cookie?.contains("appver") == true}",
        )
        val raw = eapi.request(
            path = "/eapi/song/enhance/player/url/v1",
            params = linkedMapOf(
                // 官方客户端发的是字符串 "[123]"，不是数组
                "ids" to "[$songId]",
                "level" to level,
                "encodeType" to encodeType,
            ),
            rawCipherResponse = true,
            cookie = cookie,
        )
        if (raw == null) {
            com.catclaw.music.kt.core.diagnostic.AppLog.w("NeteaseVip", "playUrl eapi 失败 id=$songId")
            return null
        }
        val root = parseObject(raw)
        if (root == null) {
            com.catclaw.music.kt.core.diagnostic.AppLog.w("NeteaseVip", "playUrl 解析失败 id=$songId")
            return null
        }
        val data = root["data"] as? JsonArray
        if (data == null) {
            com.catclaw.music.kt.core.diagnostic.AppLog.w("NeteaseVip", "playUrl 无 data id=$songId code=${root["code"]}")
            return null
        }
        // 与插件 GetEnhanceUrlEapiAsync 同规则：**只认 code==200 的条目**。
        // 风控/降档响应可能带 code!=200 的占位 url（试听片段），照单全收就会"走试听路线"。
        for (item in data) {
            val obj = item as? JsonObject ?: continue
            val code = obj["code"]?.jsonPrimitive?.intOrNull ?: -1
            if (code != 200) {
                com.catclaw.music.kt.core.diagnostic.AppLog.d("NeteaseVip", "playUrl skip code=$code id=$songId")
                continue
            }
            val url = obj["url"]?.jsonPrimitive?.contentOrNull?.takeIf { it.length > 10 } ?: continue
            val trialValue = obj["freeTrialInfo"]
            val trial = trialValue != null && trialValue !is kotlinx.serialization.json.JsonNull
            val size = obj["size"]?.jsonPrimitive?.longOrNull ?: 0L
            val br = obj["br"]?.jsonPrimitive?.intOrNull ?: 0
            com.catclaw.music.kt.core.diagnostic.AppLog.d(
                "NeteaseVip",
                "playUrl resp id=$songId level=${obj["level"]} br=$br size=$size " +
                    "trial=$trial code=$code urlLen=${url.length}",
            )
            // 试听片段**不能当成功**：登录态失效/权限不足时服务端仍回 code=200，
            // 但会带 freeTrialInfo（30 秒片段）。照单全收就会"VIP 却在放试听"，
            // 必须跳过继续降档，让上层走 web 回退 / 会话续期。
            if (trial) {
                com.catclaw.music.kt.core.diagnostic.AppLog.w(
                    "NeteaseVip", "playUrl 试听片段，跳过 id=$songId level=${obj["level"]}",
                )
                continue
            }
            return url
        }
        com.catclaw.music.kt.core.diagnostic.AppLog.w("NeteaseVip", "playUrl 无 code=200 条目 id=$songId")
        return null
    }

    // ── eapi ───────────────────────────────────────────────────

    /**
     * 老 web 接口按码率取链（插件 `GetEnhanceUrlAsync` 同款，兜底链第 2 级）：
     * `music.163.com/api/song/enhance/player/url?id=&ids=[]&br=`。
     * **用户 Cookie 优先** —— 带 MUSIC_U 时 VIP 歌能拿到完整直链（eapi 被风控卡住时的
     * 同能力回退）；未登录用静态 cookie `os=pc; appver=8.9.70` 防风控，与插件一致。
     */
    suspend fun playUrlViaWeb(songId: Long, br: Int, cookie: String?): String? {
        val url = "https://music.163.com/api/song/enhance/player/url" +
            "?id=$songId&ids=[$songId]&br=$br"
        val headers = mapOf(
            "User-Agent" to WEB_UA,
            "Referer" to "https://music.163.com/",
            "Cookie" to (cookie ?: "os=pc; appver=8.9.70"),
        )
        val bytes = http.get(url, headers, ByteArray(0)) ?: return null
        val root = runCatching { JSON.parseToJsonElement(String(bytes, Charsets.UTF_8)) }
            .getOrNull() as? JsonObject ?: return null
        val data = root["data"] as? JsonArray ?: return null
        for (item in data) {
            val obj = item as? JsonObject ?: continue
            val code = obj["code"]?.jsonPrimitive?.intOrNull ?: -1
            val trialValue = obj["freeTrialInfo"]
            val trial = trialValue != null && trialValue !is kotlinx.serialization.json.JsonNull
            val play = obj["url"]?.jsonPrimitive?.contentOrNull?.takeIf { it.length > 10 }
            com.catclaw.music.kt.core.diagnostic.AppLog.d(
                "NeteaseVip",
                "playUrlViaWeb id=$songId br=${br / 1000}k code=$code trial=$trial " +
                    "size=${obj["size"]?.jsonPrimitive?.longOrNull ?: 0} hasUrl=${play != null} " +
                    "loggedIn=${!cookie.isNullOrBlank()}",
            )
            // 与 eapi 同规则：只有 code==200 且不带 freeTrialInfo 的才是完整链
            if (code != 200 || trial || play == null) continue
            return toHttps(play)
        }
        return null
    }

    /**
     * 免登录外链探测（插件方案 3 同款）：outer 地址会 302 到当次签名的 CDN。
     * **必须确认最终响应真的是音频** —— 实测 VIP/锁定歌的 outer 会 302 到
     * `music.163.com/404` 且**以 200 返回 HTML 页面**，只看 `isSuccessful`
     * 会把 HTML 当成直链，播放器随后报 `ERROR_CODE_PARSING_CONTAINER_UNSUPPORTED`。
     * 所以这里用 Content-Type 排除 html/json/text，只接受音频/二进制。
     * 只读响应头不读 body，成功返回 302 落地的最终 CDN 地址。
     */
    suspend fun outerUrl(songId: Long): String? = kotlinx.coroutines.withContext(kotlinx.coroutines.Dispatchers.IO) {
        runCatching<String?> {
            val request = Request.Builder()
                .url("https://music.163.com/song/media/outer/url?id=$songId.mp3")
                .header("User-Agent", WEB_UA)
                .header("Referer", "https://music.163.com/")
                .build()
            outerClient.newCall(request).execute().use { resp ->
                if (!resp.isSuccessful) return@use null
                val contentType = resp.header("Content-Type").orEmpty().lowercase()
                val audio = contentType.startsWith("audio/") ||
                    contentType.contains("octet-stream") ||
                    contentType.contains("mpegurl")
                if (!audio) {
                    com.catclaw.music.kt.core.diagnostic.AppLog.w(
                        "NeteaseVip",
                        "outer 非音频响应（判为不可用）id=$songId type=$contentType url=${resp.request.url}",
                    )
                    return@use null
                }
                resp.request.url.toString()
            }
        }.getOrNull()
    }

    /** http 直链换 https（插件 ToHttps 同规则：CDN 两种都出，https 保险） */
    private fun toHttps(url: String): String =
        if (url.startsWith("http://")) "https://" + url.removePrefix("http://") else url

    /** outer 探测专用：只读响应头，超时从紧（跟手体验优先于这个兜底） */
    private val outerClient: OkHttpClient = OkHttpClient.Builder()
        .connectTimeout(10, java.util.concurrent.TimeUnit.SECONDS)
        .readTimeout(15, java.util.concurrent.TimeUnit.SECONDS)
        .build()

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
            // 串行补全在 874 首的「我喜欢的音乐」上要跑 2~3 批 eapi，是打开慢的主因。
            // 改 2 路并发（再多会被网易云限流），结果仍按 trackIds 原顺序重排。
            kotlinx.coroutines.coroutineScope {
                val permits = kotlinx.coroutines.sync.Semaphore(2)
                trackIds.filter { it !in byId }.chunked(DETAIL_CHUNK).map { chunk ->
                    async {
                        permits.withPermit {
                            val detail = eapi.songDetailRaw(chunk, cookie) ?: return@withPermit emptyList()
                            val songs = parseObject(detail)?.get("songs") as? JsonArray ?: return@withPermit emptyList()
                            parseTracks(songs)
                        }
                    }
                }.awaitAll().forEach { list -> list.forEach { byId[it.id] = it } }
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
        private const val FETCH_CACHE_TTL_MS = 5 * 60_000L
        private val JSON = Json { ignoreUnknownKeys = true; isLenient = true }

        /** web 接口取链 UA（老接口不挑 UA，但要避免 OkHttp 默认值被风控） */
        private const val WEB_UA =
            "Mozilla/5.0 (Windows NT 10.0; WOW64) AppleWebKit/537.36 (KHTML, like Gecko) " +
                "Safari/537.36 Chrome/91.0.4472.164 NeteaseMusicDesktop/3.1.3.203419"

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
