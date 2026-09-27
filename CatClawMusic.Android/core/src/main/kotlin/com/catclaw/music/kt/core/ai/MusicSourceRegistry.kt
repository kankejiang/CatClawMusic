package com.catclaw.music.kt.core.ai

import java.security.MessageDigest
import java.util.regex.Pattern
import javax.crypto.Cipher
import javax.crypto.spec.SecretKeySpec
import kotlinx.serialization.json.Json
import kotlinx.serialization.json.JsonArray
import kotlinx.serialization.json.JsonElement
import kotlinx.serialization.json.JsonObject
import kotlinx.serialization.json.JsonPrimitive
import kotlinx.serialization.json.buildJsonArray
import kotlinx.serialization.json.buildJsonObject
import kotlinx.serialization.json.contentOrNull
import kotlinx.serialization.json.jsonObject
import kotlinx.serialization.json.jsonPrimitive
import kotlinx.serialization.json.longOrNull

/** 一次配置诊断的结果。字段名与 C# `Diagnosis` 对齐，方便两端读同一份日志。 */
data class Diagnosis(
    val ok: Boolean = false,
    val searchOk: Boolean = false,
    val songs: Int = 0,
    val firstResult: String = "",
    val urlOk: Boolean = false,
    val urlPreview: String = "",
    val httpOk: Boolean = false,
    val error: String = "",
)

/** 一次 HTTP 请求的规格：URL、参数模板、请求头、响应字符集。 */
data class SourceHttpSpec(
    val url: String = "",
    val params: Map<String, String> = emptyMap(),
    val headers: Map<String, String> = emptyMap(),
    val encoding: String = "utf8",
)

/** 酷我那类"单引号伪 JSON"的切块与取值正则，全部可配 —— 平台改版时改配置而不是改代码。 */
data class SourceRegexSpec(
    val blockSplit: String = "",
    val blockMarker: String = "",
    val idPattern: String = "",
    val namePattern: String = "",
    val artistPattern: String = "",
    val formatsPattern: String = "",
)

data class SourceSong(
    val id: String,
    val name: String = "",
    val artist: String = "",
    val formats: String = "",
) {
    /**
     * 该曲最高可用档位。规则照抄 C# `SourceSong.BestQuality`：
     * FORMATS 里出现 FLAC/APE 才算无损，MP3H 或带 "320" 算 320K，其余一律 128K。
     */
    val bestQuality: String
        get() = when {
            formats.contains("FLAC", ignoreCase = true) || formats.contains("APE", ignoreCase = true) -> "flac"
            formats.contains("MP3H", ignoreCase = true) || formats.contains("320", ignoreCase = true) -> "320k"
            else -> "128k"
        }
}

data class MusicSourceConfig(
    val id: String = "",
    val name: String = "",
    val template: String = "",
    val search: SourceHttpSpec = SourceHttpSpec(),
    val urlApi: SourceHttpSpec = SourceHttpSpec(),
    val qualityMap: Map<String, String> = emptyMap(),
    val regexes: SourceRegexSpec = SourceRegexSpec(),
    val enabled: Boolean = true,
)

/** 存储缝（C# 是静态类 + IAgentConfigStorage；这里做成实例，注册表才能在 JVM 里测完） */
interface SourceStorage {
    fun read(): String?
    fun write(value: String)
}

/**
 * 网络缝。**返回字节而不是字符串**是有意的：字符集由 `spec.encoding` 决定（酷我 r.s 是 GBK），
 * 如果在缝里就转成 String，GBK 解码这条路径就永远测不到 —— 而它坏起来的症状是
 * "搜得到歌但歌手名全是乱码"，最难查。
 */
interface SourceHttp {
    suspend fun get(url: String, headers: Map<String, String>, body: ByteArray): ByteArray?
    suspend fun postForm(url: String, form: Map<String, String>, headers: Map<String, String>): ByteArray?
}

/**
 * 音乐源注册表：源配置的存储、默认值、模板执行器。
 * 移植自 C# `MusicSourceRegistry.cs`（332 行），逐条对齐：模板名、内置源、参数占位符、
 * 切块正则、eapi 摘要与 AES-128-ECB 取链。
 *
 * 设计意图（注释里写的）保留：**平台改版时只改配置，不改代码**，
 * 所以正则、URL、参数、质量映射全部是数据而不是硬编码分支；只有"模板执行器"是代码。
 */
class MusicSourceRegistry(
    private val storage: SourceStorage,
    private val http: SourceHttp,
) {

    fun defaults(): List<MusicSourceConfig> = listOf(
        MusicSourceConfig(
            id = "kuwo",
            name = "酷我音乐",
            template = "kuwo_jsonp",
            search = SourceHttpSpec(
                url = "http://search.kuwo.cn/r.s",
                params = linkedMapOf(
                    "all" to "{keyword}", "ft" to "music", "itemset" to "web_2013",
                    "rformat" to "json", "encoding" to "utf8", "pn" to "0", "rn" to "10",
                ),
                encoding = "gbk",
            ),
            urlApi = SourceHttpSpec(
                url = "https://mobi.kuwo.cn/mobi.s",
                params = linkedMapOf(
                    "f" to "web", "rid" to "{id}", "br" to "{quality}", "source" to "jiakong",
                    "type" to "convert_url_with_sign", "surl" to "1",
                ),
                headers = mapOf("Referer" to "https://www.kuwo.cn/"),
            ),
            qualityMap = linkedMapOf("flac" to "2000kflac", "320k" to "320kmp3", "128k" to "128kmp3"),
            regexes = SourceRegexSpec(
                blockSplit = """\{'AARTIST""",
                blockMarker = "'SONGNAME'",
                idPattern = "'MUSICRID':\\s*'([^']*)'",
                namePattern = "'SONGNAME':\\s*'([^']*)'",
                artistPattern = "'ARTIST':\\s*'([^']*)'",
                formatsPattern = "'FORMATS':\\s*'([^']*)'",
            ),
        ),
        MusicSourceConfig(
            id = "netease",
            name = "网易云音乐",
            template = "netease_eapi",
            search = SourceHttpSpec(
                url = "https://music.163.com/api/search/get/web",
                params = linkedMapOf("csrf_token" to "", "type" to "1", "s" to "{keyword}", "limit" to "8"),
            ),
            urlApi = SourceHttpSpec(
                url = "https://interface3.music.163.com/eapi/song/enhance/player/url/v1",
                params = linkedMapOf(
                    "eapi_path" to "/api/song/enhance/player/url/v1",
                    "eapi_key" to "e82ckenh8dichen8",
                    "level" to "{quality}",
                    "encode_type" to "flac",
                    "immerse_type" to "c51",
                    "device_id" to "pyncm!",
                ),
            ),
            qualityMap = linkedMapOf("flac" to "lossless", "320k" to "exhigh", "128k" to "standard"),
        ),
    )

    /** 持久化覆盖默认；解析失败或为空都回落到内置（与 C# 同一宽容度：坏配置不该让功能消失） */
    fun all(): List<MusicSourceConfig> =
        runCatching { decodeList(storage.read()) }.getOrNull()?.takeIf { it.isNotEmpty() } ?: defaults()

    fun get(id: String): MusicSourceConfig? = all().firstOrNull { it.id == id && it.enabled }

    fun saveAll(sources: List<MusicSourceConfig>) = storage.write(encodeList(sources))

    fun upsert(source: MusicSourceConfig) {
        val list = all().toMutableList()
        val index = list.indexOfFirst { it.id == source.id }
        if (index >= 0) list[index] = source else list.add(source)
        saveAll(list)
    }

    suspend fun search(cfg: MusicSourceConfig, keyword: String): List<SourceSong> = when (cfg.template) {
        "kuwo_jsonp" -> kuwoSearch(cfg, keyword)
        "netease_eapi" -> neteaseSearch(cfg, keyword)
        else -> emptyList()
    }

    suspend fun url(cfg: MusicSourceConfig, song: SourceSong, quality: String): String? = when (cfg.template) {
        "kuwo_jsonp" -> kuwoUrl(cfg, song, quality)
        "netease_eapi" -> neteaseUrl(cfg, song, quality)
        else -> null
    }

    /**
     * 诊断一份配置能不能用：搜索 → 取链 → 直链可读，三步都跑真请求（C# 同一顺序、同一探针曲「七里香」）。
     *
     * ⚠ `ok` 的判据是 `searchOk && urlOk`，**不含** httpOk —— 依据是 C# `ValidateAsync` 的控制流：
     * 搜索无结果与取链失败各有一次 early return，而 Range 探测那一段没有，
     * 说明 CDN 对 1KB  Range 请求的怪癖不该否掉一份其实可用的配置。`httpOk` 仍然报出来供人看。
     */
    suspend fun validate(cfg: MusicSourceConfig, probeKeyword: String = "七里香"): Diagnosis {
        val songs = search(cfg, probeKeyword)
        if (songs.isEmpty()) {
            return Diagnosis(
                ok = false, searchOk = false, songs = 0,
                error = "搜索无结果：URL/参数/解析正则可能不正确（或接口已彻底更换）",
            )
        }
        val first = songs.first()
        val link = url(cfg, first, first.bestQuality)
        if (link.isNullOrEmpty()) {
            return Diagnosis(
                ok = false, searchOk = true, songs = songs.size,
                firstResult = "${first.name}-${first.artist}",
                error = "取链失败：url_api 参数或响应解析可能不正确",
            )
        }
        val preview = if (link.length > 110) link.substring(0, 110) + "..." else link
        val readable = runCatching {
            http.get(link, mapOf("Range" to "bytes=0-1023"), ByteArray(0)) != null
        }.getOrDefault(false)
        return Diagnosis(
            ok = true, searchOk = true, songs = songs.size,
            firstResult = "${first.name}-${first.artist}",
            urlOk = true, urlPreview = preview, httpOk = readable,
        )
    }

    // ── kuwo_jsonp ─────────────────────────────────────────────

    private suspend fun kuwoSearch(cfg: MusicSourceConfig, keyword: String): List<SourceSong> {
        val text = httpGet(cfg.search, mapOf("keyword" to keyword)) ?: return emptyList()
        return parseBlocks(cfg.regexes, text)
    }

    private suspend fun kuwoUrl(cfg: MusicSourceConfig, song: SourceSong, quality: String): String? {
        val br = cfg.qualityMap[quality] ?: "128kmp3"
        val rid = song.id.removePrefix("MUSIC_")
        val body = httpGet(cfg.urlApi, mapOf("id" to rid, "quality" to br)) ?: return null
        val data = runCatching { JSON.parseToJsonElement(body).jsonObject["data"] as? JsonObject }.getOrNull()
        val surl = (data?.get("surl") as? JsonPrimitive)?.contentOrNull
        if (!surl.isNullOrBlank() && surl.startsWith("http")) return surl
        return URL_IN_BODY.find(body)?.groupValues?.get(1)?.let { decodeUrlComponent(it) }
    }

    // ── netease_eapi ───────────────────────────────────────────

    private suspend fun neteaseSearch(cfg: MusicSourceConfig, keyword: String): List<SourceSong> {
        val text = httpGet(cfg.search, mapOf("keyword" to keyword)) ?: return emptyList()
        val result = runCatching { JSON.parseToJsonElement(text).jsonObject["result"] as? JsonObject }.getOrNull()
        val arr = result?.get("songs") as? JsonArray ?: return emptyList()
        return arr.mapNotNull { element ->
            val s = element as? JsonObject ?: return@mapNotNull null
            val id = (s["id"] as? JsonPrimitive)?.let { it.contentOrNull ?: it.longOrNull?.toString() }.orEmpty()
            if (id.isEmpty()) return@mapNotNull null
            val artists = (s["artists"] as? JsonArray)
                ?.mapNotNull { (it as? JsonObject)?.let { a -> a["name"]?.jsonPrimitive?.contentOrNull } }
                ?.joinToString(", ")
                .orEmpty()
            SourceSong(id = id, name = (s["name"] as? JsonPrimitive)?.contentOrNull.orEmpty(), artist = artists)
        }
    }

    private suspend fun neteaseUrl(cfg: MusicSourceConfig, song: SourceSong, quality: String): String? {
        val p = cfg.urlApi.params
        val level = cfg.qualityMap[quality] ?: "standard"
        val eapiPath = p["eapi_path"] ?: "/api/song/enhance/player/url/v1"
        val key = p["eapi_key"] ?: "e82ckenh8dichen8"
        val encodeType = p["encode_type"] ?: "flac"
        val immerse = p["immerse_type"] ?: "c51"
        val deviceId = p["device_id"] ?: "pyncm!"

        val payload = """{"ids":[${song.id}],"level":"$level","encodeType":"$encodeType","immerseType":"$immerse"}"""
        val digest = md5Hex("nobody${eapiPath}use${payload}md5forencrypt")
        val data = "$eapiPath-$EAPI_SALT-$payload-$EAPI_SALT-$digest"
        val paramsHex = aes128EcbHex(data, key)

        val body = http.postForm(
            url = cfg.urlApi.url,
            form = mapOf("params" to paramsHex),
            headers = mapOf(
                "Cookie" to "os=pc; appver=; osver=; deviceId=$deviceId",
                "Referer" to "https://music.163.com/",
            ),
        )?.toString(Charsets.UTF_8) ?: return null

        val arr = runCatching { (JSON.parseToJsonElement(body).jsonObject["data"] as? JsonArray) }.getOrNull()
        return arr?.mapNotNull { (it as? JsonObject)?.let { d -> d["url"]?.jsonPrimitive?.contentOrNull } }
            ?.firstOrNull { it.length > 10 }
    }

    // ── 共用：占位符、编码、切块解析 ───────────────────────────

    /** 与 C# 同一实现：GET 的 query 逐参数百分号编码，响应按 spec.encoding 解 */
    private suspend fun httpGet(spec: SourceHttpSpec, placeholders: Map<String, String>): String? {
        var url = fill(spec.url, placeholders)
        val query = spec.params.entries.joinToString("&") { (k, v) ->
            "${encodeComponent(k)}=${encodeComponent(fill(v, placeholders))}"
        }
        if (query.isNotEmpty()) url += (if (url.contains('?')) "&" else "?") + query
        val bytes = runCatching { http.get(url, spec.headers, ByteArray(0)) }.getOrNull() ?: return null
        return decode(bytes, spec.encoding)
    }

    private fun decode(bytes: ByteArray, encoding: String): String =
        if (encoding.equals("gbk", ignoreCase = true)) {
            runCatching { String(bytes, charset("GBK")) }.getOrElse { String(bytes, Charsets.UTF_8) }
        } else {
            String(bytes, Charsets.UTF_8)
        }

    companion object {
        private const val EAPI_SALT = "36cd479b6b5"
        private val JSON = Json { ignoreUnknownKeys = true }
        private val URL_IN_BODY = Regex("""url=([^&\s]+)""")

        fun fill(template: String, values: Map<String, String>): String =
            values.entries.fold(template) { acc, (k, v) -> acc.replace("{$k}", v) }

        fun encodeComponent(raw: String): String =
            java.net.URLEncoder.encode(raw, "UTF-8").replace("+", "%20")

        fun decodeUrlComponent(raw: String): String =
            runCatching { java.net.URLDecoder.decode(raw, "UTF-8") }.getOrDefault(raw)

        /** 酷我 r.s 的响应是"单引号伪 JSON"，只能按块切再逐块取值 */
        fun parseBlocks(r: SourceRegexSpec, text: String): List<SourceSong> {
            if (r.blockSplit.isEmpty() || r.idPattern.isEmpty()) return emptyList()
            return Pattern.compile(r.blockSplit).split(text, -1).mapNotNull { block ->
                if (r.blockMarker.isNotEmpty() && !block.contains(r.blockMarker)) return@mapNotNull null
                val id = find(r.idPattern, block) ?: return@mapNotNull null
                SourceSong(
                    id = id,
                    name = com.catclaw.music.kt.core.net.HtmlText.decodeEntities(find(r.namePattern, block).orEmpty()),
                    artist = com.catclaw.music.kt.core.net.HtmlText.decodeEntities(find(r.artistPattern, block).orEmpty()),
                    formats = find(r.formatsPattern, block).orEmpty(),
                )
            }
        }

        private fun find(pattern: String, input: String): String? =
            if (pattern.isEmpty()) null else Regex(pattern).find(input)?.groupValues?.getOrNull(1)

        fun md5Hex(input: String): String =
            MessageDigest.getInstance("MD5").digest(input.toByteArray(Charsets.UTF_8))
                .joinToString("") { "%02x".format(it) }

        /**
         * AES-128-ECB + **手工补块**：填充字节恒 ≥1（整 16 对齐时补一整块 0x10），
         * 与 C# 的 `pad = 16 - len % 16` 完全一致；用 JVM 的 `NoPadding` 而不是让
         * `PK5Padding` 代劳，是为了两边产出同一串十六进制（eapi 的签名进摘要，差一字节就取不到链）。
         */
        fun aes128EcbHex(input: String, key: String): String {
            val plain = input.toByteArray(Charsets.UTF_8)
            val pad = 16 - plain.size % 16
            val padded = plain.copyOf(plain.size + pad)
            padded.fill(pad.toByte(), plain.size, padded.size)
            val cipher = Cipher.getInstance("AES/ECB/NoPadding")
            cipher.init(Cipher.ENCRYPT_MODE, SecretKeySpec(key.toByteArray(Charsets.UTF_8), "AES"))
            return cipher.doFinal(padded).joinToString("") { "%02X".format(it) }
        }

        /** 测试用：把十六进制密文还原成明文（含去掉补块），用来独立验证加密链路而不是自证 */
        fun aes128EcbUnhex(hex: String, key: String): String {
            val bytes = hex.chunked(2).map { it.toInt(16).toByte() }.toByteArray()
            val cipher = Cipher.getInstance("AES/ECB/NoPadding")
            cipher.init(Cipher.DECRYPT_MODE, SecretKeySpec(key.toByteArray(Charsets.UTF_8), "AES"))
            val out = cipher.doFinal(bytes)
            val pad = out.last().toInt()
            return String(out.copyOfRange(0, out.size - pad), Charsets.UTF_8)
        }

        fun encodeList(sources: List<MusicSourceConfig>): String =
            JSON.encodeToString(JsonElement.serializer(), buildJsonArray { sources.forEach { add(encode(it)) } })

        internal fun decodeList(text: String?): List<MusicSourceConfig> {
            if (text.isNullOrBlank()) return emptyList()
            val arr = JSON.parseToJsonElement(text) as? JsonArray ?: error("not an array")
            return arr.map { decodeConfig(it.jsonObject) }
        }

        /** 解析 `update_music_source` 提交的单个配置；不是对象或字段缺失时返回 null */
        fun decodeOne(text: String): MusicSourceConfig? = runCatching {
            decodeConfig(JSON.parseToJsonElement(text).jsonObject)
        }.getOrNull()

        private fun encode(cfg: MusicSourceConfig): JsonElement = buildJsonObject {
            put("id", JsonPrimitive(cfg.id))
            put("name", JsonPrimitive(cfg.name))
            put("template", JsonPrimitive(cfg.template))
            put("enabled", JsonPrimitive(cfg.enabled))
            put("search", encodeSpec(cfg.search))
            put("urlApi", encodeSpec(cfg.urlApi))
            put(
                "qualityMap",
                buildJsonObject { cfg.qualityMap.forEach { (k, v) -> put(k, JsonPrimitive(v)) } },
            )
            put(
                "regexes",
                buildJsonObject {
                    put("blockSplit", JsonPrimitive(cfg.regexes.blockSplit))
                    put("blockMarker", JsonPrimitive(cfg.regexes.blockMarker))
                    put("idPattern", JsonPrimitive(cfg.regexes.idPattern))
                    put("namePattern", JsonPrimitive(cfg.regexes.namePattern))
                    put("artistPattern", JsonPrimitive(cfg.regexes.artistPattern))
                    put("formatsPattern", JsonPrimitive(cfg.regexes.formatsPattern))
                },
            )
        }

        private fun encodeSpec(spec: SourceHttpSpec): JsonElement = buildJsonObject {
            put("url", JsonPrimitive(spec.url))
            put("encoding", JsonPrimitive(spec.encoding))
            put("params", buildJsonObject { spec.params.forEach { (k, v) -> put(k, JsonPrimitive(v)) } })
            put("headers", buildJsonObject { spec.headers.forEach { (k, v) -> put(k, JsonPrimitive(v)) } })
        }

        /**
         * 读单个配置。**两种键名都收**：
         * `update_music_source` 给模型的示例是 snake_case（`url_api` / `quality_map` / `id_pattern`，照 C# 原样），
         * 而本端自己 `encode()` 落盘的是 camelCase。只认一种的话，模型提交的配置会静默变成空 urlApi，
         * 表现是"验证失败但看不出为什么"。
         */
        private fun decodeConfig(obj: JsonObject): MusicSourceConfig {
            fun pair(key: String): JsonObject? = (obj[key] as? JsonObject)
            val searchObj = pair("search")
            val urlApiObj = pair("urlApi") ?: pair("url_api")
            val qualityObj = pair("qualityMap") ?: pair("quality_map")
            val regexObj = pair("regexes")
            return MusicSourceConfig(
                id = obj.str("id"),
                name = obj.str("name"),
                template = obj.str("template"),
                enabled = (obj["enabled"] as? JsonPrimitive)?.let { p -> p.contentOrNull?.toBooleanStrictOrNull() } ?: true,
                search = decodeSpec(searchObj),
                urlApi = decodeSpec(urlApiObj),
                qualityMap = qualityObj?.mapValues { it.value.jsonPrimitive.content } ?: emptyMap(),
                regexes = regexObj?.let {
                    SourceRegexSpec(
                        blockSplit = it.strOr("blockSplit", "block_split"),
                        blockMarker = it.strOr("blockMarker", "block_marker"),
                        idPattern = it.strOr("idPattern", "id_pattern"),
                        namePattern = it.strOr("namePattern", "name_pattern"),
                        artistPattern = it.strOr("artistPattern", "artist_pattern"),
                        formatsPattern = it.strOr("formatsPattern", "formats_pattern"),
                    )
                } ?: SourceRegexSpec(),
            )
        }

        private fun JsonObject.strOr(camel: String, snake: String): String =
            (this[camel] ?: this[snake] as? JsonPrimitive)?.let { (it as? JsonPrimitive)?.contentOrNull }.orEmpty()

        private fun decodeSpec(obj: JsonObject?): SourceHttpSpec {
            if (obj == null) return SourceHttpSpec()
            return SourceHttpSpec(
                url = obj.str("url"),
                encoding = obj.str("encoding").ifBlank { "utf8" },
                params = (obj["params"] as? JsonObject)?.mapValues { it.value.jsonPrimitive.content } ?: emptyMap(),
                headers = (obj["headers"] as? JsonObject)?.mapValues { it.value.jsonPrimitive.content } ?: emptyMap(),
            )
        }

        private fun JsonObject.str(key: String): String = (this[key] as? JsonPrimitive)?.contentOrNull.orEmpty()
    }
}
