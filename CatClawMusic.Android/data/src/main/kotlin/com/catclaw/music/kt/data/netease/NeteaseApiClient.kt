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
 * 网易云在线音乐客户端 —— 对应插件 `NetEaseOpenApiClient.cs`。
 *
 * 移植原则：**接口地址、参数名、解析字段与插件一致**，只把「C# 的 HttpClient + JsonDocument」
 * 换成项目里的 [SourceHttp] + kotlinx.serialization；把插件的 `Internal` 字典换成强类型字段。
 *
 * 接口分三类：
 * - 公开网页接口（搜索、歌单广场、榜单、艺人、专辑、歌词）——匿名可用；
 * - eapi 桌面通道（歌单全量、播放取链、歌词兜底）——见 [NeteaseEapi]；
 * - 登录态接口（我的歌单、红心、每日推荐、私人 FM）——需要 [NeteaseSession.cookie]。
 *
 * 所有方法失败都返回 null / 空列表，不抛异常：在线音乐是"锦上添花"，一个接口挂了
 * 不该让整页崩掉（与插件逐方法 try/catch 的收敛方式一致）。
 */
@Singleton
class NeteaseApiClient @Inject constructor(
    private val http: SourceHttp,
    private val eapi: NeteaseEapiChannel,
    private val session: NeteaseSession,
    private val playlistClient: NeteasePlaylistClient,
) {

    // ── 搜索 ───────────────────────────────────────────────────

    suspend fun searchSongs(keyword: String, page: Int = 1, pageSize: Int = 20): List<OnlineSong>? {
        if (keyword.isBlank()) return null
        val offset = (page - 1).coerceAtLeast(0) * pageSize
        val root = postForm(
            CLOUD_SEARCH,
            mapOf(
                "s" to keyword,
                "type" to "1",
                "offset" to offset.toString(),
                "limit" to pageSize.toString(),
            ),
        ) ?: return null
        val songs = ((root["result"] as? JsonObject)?.get("songs") as? JsonArray) ?: return null
        return songs.mapNotNull { parseSong(it as? JsonObject) }
    }

    suspend fun searchPlaylists(keyword: String, limit: Int = 20): List<OnlinePlaylist> {
        if (keyword.isBlank()) return emptyList()
        val root = postForm(
            CLOUD_SEARCH,
            mapOf("s" to keyword, "type" to "1000", "offset" to "0", "limit" to limit.toString()),
        ) ?: return emptyList()
        val list = ((root["result"] as? JsonObject)?.get("playlists") as? JsonArray) ?: return emptyList()
        return list.mapNotNull { parsePlaylist(it as? JsonObject) }
    }

    suspend fun searchArtists(keyword: String, limit: Int = 20): List<NeteaseArtist> {
        if (keyword.isBlank()) return emptyList()
        val root = postForm(
            CLOUD_SEARCH,
            mapOf("s" to keyword, "type" to "100", "offset" to "0", "limit" to limit.toString()),
        ) ?: return emptyList()
        val list = ((root["result"] as? JsonObject)?.get("artists") as? JsonArray) ?: return emptyList()
        return list.mapNotNull { parseArtist(it as? JsonObject) }
    }

    /** 搜索联想词（输入框实时建议） */
    suspend fun searchSuggest(keyword: String, limit: Int = 8): List<SearchSuggestion> {
        if (keyword.isBlank()) return emptyList()
        val root = getJson("$SUGGEST_API?s=${encode(keyword)}&limit=$limit") ?: return emptyList()
        val all = ((root["result"] as? JsonObject)?.get("allMatch") as? JsonArray) ?: return emptyList()
        return all.mapNotNull { element ->
            val obj = element as? JsonObject ?: return@mapNotNull null
            val word = obj["keyword"]?.jsonPrimitive?.contentOrNull?.trim().orEmpty()
            if (word.isEmpty()) return@mapNotNull null
            val type = when (obj["type"]?.jsonPrimitive?.intOrNull ?: 1) {
                10 -> "album"
                100 -> "artist"
                else -> "song"
            }
            SearchSuggestion(word, type)
        }
    }

    suspend fun hotSearch(limit: Int = 10): List<String> {
        val root = getJson(HOT_SEARCH_API) ?: return emptyList()
        val data = (root["data"] as? JsonArray) ?: return emptyList()
        return data.mapNotNull {
            (it as? JsonObject)?.get("searchWord")?.jsonPrimitive?.contentOrNull
        }.take(limit)
    }

    // ── 歌单广场 / 榜单 / 推荐 ──────────────────────────────────

    suspend fun playlists(category: String? = null, page: Int = 1, pageSize: Int = 60): List<OnlinePlaylist> {
        val cat = category?.trim()?.takeIf { it.isNotEmpty() && it != "全部" } ?: "全部"
        val offset = (page - 1).coerceAtLeast(0) * pageSize
        val root = getJson(
            "$PLAYLIST_LIST_API?cat=${encode(cat)}&order=hot&limit=$pageSize&offset=$offset",
        ) ?: return emptyList()
        val list = (root["playlists"] as? JsonArray) ?: return emptyList()
        return list.mapNotNull { parsePlaylist(it as? JsonObject) }
    }

    suspend fun toplists(): List<OnlinePlaylist> {
        val root = getJson(TOPLIST_API) ?: return emptyList()
        val list = (root["list"] as? JsonArray) ?: return emptyList()
        return list.mapNotNull { element ->
            val obj = element as? JsonObject ?: return@mapNotNull null
            OnlinePlaylist(
                id = obj["id"]?.jsonPrimitive?.longOrNull?.toString() ?: return@mapNotNull null,
                name = obj["name"]?.jsonPrimitive?.contentOrNull.orEmpty(),
                coverUrl = cover(toHttps(obj["coverImgUrl"]?.jsonPrimitive?.contentOrNull), 300),
                description = obj["description"]?.jsonPrimitive?.contentOrNull,
                songCount = obj["total"]?.jsonPrimitive?.intOrNull ?: 0,
            )
        }
    }

    /** 推荐歌单（匿名可用；登录后更个性化） */
    suspend fun recommendPlaylists(): List<OnlinePlaylist> {
        val root = getJson("$PERSONALIZED_API?limit=30") ?: return emptyList()
        val list = (root["result"] as? JsonArray) ?: return emptyList()
        return list.mapNotNull { parsePlaylist(it as? JsonObject) }
    }

    /** 每日推荐：需要登录，匿名返回空列表（与插件一致） */
    suspend fun dailyRecommend(limit: Int = 20): List<OnlineSong> {
        val root = getJson(DAILY_RECOMMEND_API) ?: return emptyList()
        val data = root["data"] as? JsonObject ?: return emptyList()
        val songs = (data["dailySongs"] as? JsonArray) ?: return emptyList()
        return songs.mapNotNull { parseSong(it as? JsonObject) }.take(limit)
    }

    // ── 歌单 / 专辑 / 艺人 ─────────────────────────────────────

    /** 歌单全部曲目：复用 eapi 两段式实现（超 1000 首也能拿全） */
    suspend fun playlistSongs(playlistId: String, pageSize: Int = 1000): List<OnlineSong> {
        val playlist = playlistClient.fetch(playlistId, session.cookie) ?: return emptyList()
        return playlist.tracks.take(if (pageSize > 0) pageSize else playlist.tracks.size).map {
            OnlineSong(
                id = it.id.toString(),
                title = it.title,
                artist = it.artist,
                album = it.album,
                durationMs = it.durationSeconds * 1000L,
                coverUrl = it.coverUrl ?: playlist.coverUrl,
            )
        }
    }

    suspend fun albumSongs(albumId: String): List<OnlineSong> {
        val root = getJson("$ALBUM_API/$albumId?id=$albumId") ?: return emptyList()
        val songs = (root["songs"] as? JsonArray) ?: return emptyList()
        return songs.mapNotNull { parseSong(it as? JsonObject) }
    }

    suspend fun artistTopSongs(artistId: String): List<OnlineSong> {
        val root = getJson("$ARTIST_TOP_API?id=$artistId") ?: return emptyList()
        val songs = (root["songs"] as? JsonArray) ?: return emptyList()
        return songs.mapNotNull { parseSong(it as? JsonObject) }
    }

    suspend fun artistAlbums(artistId: String, limit: Int = 50): List<NeteaseAlbum> {
        val root = getJson("$ARTIST_ALBUM_API/$artistId?limit=$limit&offset=0") ?: return emptyList()
        val list = (root["hotAlbums"] as? JsonArray) ?: return emptyList()
        return list.mapNotNull { element ->
            val obj = element as? JsonObject ?: return@mapNotNull null
            val id = obj["id"]?.jsonPrimitive?.longOrNull?.toString() ?: return@mapNotNull null
            val publish = obj["publishTime"]?.jsonPrimitive?.longOrNull ?: 0
            NeteaseAlbum(
                id = id,
                name = obj["name"]?.jsonPrimitive?.contentOrNull.orEmpty(),
                picUrl = cover(toHttps(obj["picUrl"]?.jsonPrimitive?.contentOrNull), 300),
                songCount = obj["size"]?.jsonPrimitive?.intOrNull ?: 0,
                artistName = (obj["artist"] as? JsonObject)?.get("name")?.jsonPrimitive?.contentOrNull,
                publishYear = if (publish > 0) {
                    java.text.SimpleDateFormat("yyyy", java.util.Locale.US)
                        .format(java.util.Date(publish))
                } else {
                    null
                },
            )
        }
    }

    suspend fun searchArtistsOnly(keyword: String, limit: Int = 20): List<NeteaseArtist> =
        searchArtists(keyword, limit)

    // ── 歌词 / 播放 ────────────────────────────────────────────

    /**
     * 歌词三流。顺序与插件一致：eapi 桌面通道 → 官方 `/api/song/lyric`。
     * 前者不必带登录态、限流更宽松；后者在 eapi 被风控时兜底。
     */
    suspend fun lyrics(songId: String): NeteaseLyrics? {
        if (songId.isBlank()) return null
        songId.toLongOrNull()?.let { id ->
            val raw = eapi.request(
                path = "/eapi/song/lyric/v1",
                params = linkedMapOf("id" to id, "lv" to "-1", "tv" to "-1", "rv" to "-1", "yv" to "-1"),
                rawCipherResponse = false,
                cookie = session.cookie,
            )
            parseLyrics(raw)?.let { return it }
        }
        val root = getJson("$LYRIC_API?id=$songId&lv=-1&tv=-1&rv=-1") ?: return null
        val lrc = (root["lrc"] as? JsonObject)?.get("lyric")?.jsonPrimitive?.contentOrNull
        if (lrc.isNullOrBlank()) return null
        return NeteaseLyrics(
            lrc = lrc,
            translation = (root["tlyric"] as? JsonObject)?.get("lyric")?.jsonPrimitive?.contentOrNull
                ?.takeIf { it.isNotBlank() },
            roma = (root["romalrc"] as? JsonObject)?.get("lyric")?.jsonPrimitive?.contentOrNull
                ?.takeIf { it.isNotBlank() },
        )
    }

    /** 播放直链：eapi 优先（登录可到无损），失败退免登录外链 */
    suspend fun playUrl(songId: String, quality: NeteasePlaylistClient.Quality = NeteasePlaylistClient.Quality.Exhigh): String? {
        val id = songId.toLongOrNull() ?: return null
        return playlistClient.playUrl(id, quality, session.cookie)
    }

    /** 免登录外链（302 到 CDN，可长期存库/缓存） */
    fun fallbackPlayUrl(songId: String): String? =
        songId.toLongOrNull()?.let { NeteasePlaylistClient.playUrlFallback(it) }

    // ── HTTP ───────────────────────────────────────────────────

    private suspend fun getJson(url: String): JsonObject? {
        val bytes = http.get(url, headers(), ByteArray(0)) ?: return null
        return parseObject(String(bytes, Charsets.UTF_8))
    }

    private suspend fun postForm(url: String, form: Map<String, String>): JsonObject? {
        val bytes = http.postForm(url, form, headers()) ?: return null
        return parseObject(String(bytes, Charsets.UTF_8))
    }

    private fun headers(): Map<String, String> = buildMap {
        put("User-Agent", UA)
        put("Referer", "https://music.163.com/")
        put("Cookie", session.cookie ?: "os=pc; appver=8.9.70")
    }

    // ── 解析 ───────────────────────────────────────────────────

    private fun parseSong(obj: JsonObject?): OnlineSong? {
        val s = obj ?: return null
        val id = s["id"]?.jsonPrimitive?.longOrNull ?: return null
        val artists = collectArtists(s["ar"]) .ifEmpty { collectArtists(s["artists"]) }
        val albumObj = (s["al"] as? JsonObject) ?: (s["album"] as? JsonObject)
        val albumName = albumObj?.get("name")?.jsonPrimitive?.contentOrNull.orEmpty()
        val coverRaw = albumObj?.get("picUrl")?.jsonPrimitive?.contentOrNull
        val duration = s["dt"]?.jsonPrimitive?.longOrNull
            ?: s["duration"]?.jsonPrimitive?.longOrNull
            ?: s["durationMs"]?.jsonPrimitive?.longOrNull
            ?: 0L
        val privilege = s["privilege"] as? JsonObject
        val fee = privilege?.get("fee")?.jsonPrimitive?.intOrNull ?: s["fee"]?.jsonPrimitive?.intOrNull ?: 0
        val st = privilege?.get("st")?.jsonPrimitive?.intOrNull ?: s["st"]?.jsonPrimitive?.intOrNull ?: 0
        val mvId = (s["mv"] as? JsonObject)?.get("id")?.jsonPrimitive?.longOrNull
            ?: s["mvid"]?.jsonPrimitive?.longOrNull
            ?: 0L
        return OnlineSong(
            id = id.toString(),
            title = s["name"]?.jsonPrimitive?.contentOrNull.orEmpty(),
            artist = artists.joinToString(" / "),
            album = albumName,
            durationMs = duration,
            coverUrl = cover(toHttps(coverRaw), 1000),
            vip = fee == 1 || fee == 4,
            blocked = st == -1,
            mvId = mvId,
        )
    }

    private fun collectArtists(array: kotlinx.serialization.json.JsonElement?): List<String> {
        val arr = array as? JsonArray ?: return emptyList()
        return arr.mapNotNull {
            (it as? JsonObject)?.get("name")?.jsonPrimitive?.contentOrNull?.takeIf { n -> n.isNotBlank() }
        }
    }

    private fun parsePlaylist(obj: JsonObject?): OnlinePlaylist? {
        val p = obj ?: return null
        val id = p["id"]?.jsonPrimitive?.longOrNull?.toString() ?: return null
        val rawCover = p["coverImgUrl"]?.jsonPrimitive?.contentOrNull
            ?: p["picUrl"]?.jsonPrimitive?.contentOrNull
        return OnlinePlaylist(
            id = id,
            name = p["name"]?.jsonPrimitive?.contentOrNull.orEmpty(),
            coverUrl = cover(toHttps(rawCover), 300),
            description = p["description"]?.jsonPrimitive?.contentOrNull,
            songCount = p["trackCount"]?.jsonPrimitive?.intOrNull
                ?: p["songCount"]?.jsonPrimitive?.intOrNull
                ?: 0,
            playCount = p["playCount"]?.jsonPrimitive?.longOrNull,
        )
    }

    private fun parseArtist(obj: JsonObject?): NeteaseArtist? {
        val a = obj ?: return null
        val id = a["id"]?.jsonPrimitive?.longOrNull?.toString() ?: return null
        val pic = a["img1v1Url"]?.jsonPrimitive?.contentOrNull
            ?: a["picUrl"]?.jsonPrimitive?.contentOrNull
        return NeteaseArtist(
            id = id,
            name = a["name"]?.jsonPrimitive?.contentOrNull.orEmpty(),
            picUrl = cover(toHttps(pic), 300),
            songCount = a["musicSize"]?.jsonPrimitive?.intOrNull ?: a["songSize"]?.jsonPrimitive?.intOrNull ?: 0,
            albumCount = a["albumSize"]?.jsonPrimitive?.intOrNull ?: 0,
        )
    }

    private fun parseLyrics(raw: String?): NeteaseLyrics? {
        val root = parseObject(raw ?: return null) ?: return null
        val lrc = (root["lrc"] as? JsonObject)?.get("lyric")?.jsonPrimitive?.contentOrNull
        if (lrc.isNullOrBlank()) return null
        return NeteaseLyrics(
            lrc = lrc,
            translation = (root["tlyric"] as? JsonObject)?.get("lyric")?.jsonPrimitive?.contentOrNull
                ?.takeIf { it.isNotBlank() },
            roma = (root["romalrc"] as? JsonObject)?.get("lyric")?.jsonPrimitive?.contentOrNull
                ?.takeIf { it.isNotBlank() },
        )
    }

    private fun parseObject(text: String): JsonObject? =
        runCatching { JSON.parseToJsonElement(text) as? JsonObject }.getOrNull()

    /** 网易云图片服务按尺寸裁剪（?param=WxH）；已带 param 时原样返回 */
    private fun cover(url: String?, size: Int): String? {
        val value = url?.takeIf { it.isNotBlank() } ?: return null
        if (value.contains("?param=")) return value
        val sep = if (value.contains('?')) "&" else "?"
        return "$value${sep}param=${size}y$size"
    }

    private fun toHttps(url: String?): String? =
        url?.replaceFirst("http://", "https://")

    private fun encode(raw: String): String =
        java.net.URLEncoder.encode(raw, "UTF-8").replace("+", "%20")

    companion object {
        private const val CLOUD_SEARCH = "https://music.163.com/api/cloudsearch/pc"
        private const val SUGGEST_API = "https://music.163.com/api/search/suggest/web"
        private const val HOT_SEARCH_API = "https://music.163.com/api/hotsearchlist/get"
        private const val PLAYLIST_LIST_API = "https://music.163.com/api/playlist/list"
        private const val TOPLIST_API = "https://music.163.com/api/toplist"
        private const val PERSONALIZED_API = "https://music.163.com/api/personalized/playlist"
        private const val DAILY_RECOMMEND_API = "https://music.163.com/api/v3/discovery/recommend/songs"
        private const val ALBUM_API = "https://music.163.com/api/album"
        private const val ARTIST_TOP_API = "https://music.163.com/api/artist/top/song"
        private const val ARTIST_ALBUM_API = "https://music.163.com/api/artist/albums"
        private const val LYRIC_API = "https://music.163.com/api/song/lyric"

        private const val UA =
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) " +
                "Chrome/124.0 Safari/537.36"

        private val JSON = Json { ignoreUnknownKeys = true; isLenient = true }
    }
}
