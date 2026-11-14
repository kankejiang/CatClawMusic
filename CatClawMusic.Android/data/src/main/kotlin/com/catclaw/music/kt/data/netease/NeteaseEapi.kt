package com.catclaw.music.kt.data.netease

import android.content.Context
import com.catclaw.music.kt.core.ai.SourceHttp
import dagger.hilt.android.qualifiers.ApplicationContext
import java.security.MessageDigest
import java.util.Base64
import javax.crypto.Cipher
import javax.crypto.spec.SecretKeySpec
import javax.inject.Inject
import javax.inject.Singleton
import kotlin.random.Random
import kotlinx.serialization.json.JsonObject
import kotlinx.serialization.json.JsonPrimitive
import kotlinx.serialization.json.buildJsonObject
import java.util.concurrent.TimeUnit
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.withContext
import kotlinx.serialization.json.put
import okhttp3.FormBody
import okhttp3.OkHttpClient
import okhttp3.Request

/**
 * 网易云桌面客户端 eapi 通道（`interface.music.163.com`）。
 *
 * 逐条移植自本地 `CatClawMusic.Plugins.Netease/NeteaseEapi.cs`：AES-128-ECB(PKCS5) +
 * MD5 摘要 + 伪装桌面客户端的设备指纹。之所以不在 Android 侧自创一套，是因为这几个参数
 * 都是**服务端风控认的**，抄错一个（尤其是 header 的 JSON 序列化顺序、`-36cd479b6b5-` 盐、
 * 大写 hex）都会静默失败，表现成"歌单能读、取链为空"这种很难查的症状。
 *
 * 两个与 C# 的差异：
 * - 设备指纹持久化到 SharedPreferences 而不是 `LocalApplicationData/netease_device.txt`：
 *   同一台设备上**必须跨进程稳定**，否则续期/风控会当作换了设备（C# 那边为此专门落盘）。
 * - 响应解密两种包装都试：eapi 有的接口返回裸密文、有的返回 base64 密文，插件里也是这么兜的。
 */
/**
 * eapi 通道的可替换接口。
 *
 * 抽这一层不是因为"可能换实现"，而是为了让 [NeteasePlaylistClient] 的解析逻辑能在 JVM 单测里跑：
 * 真 [NeteaseEapi] 需要 Android Context 存设备指纹，单测里造不出来；而歌单解析（两段式补齐、
 * 字段映射、封面 https 化）恰恰是最容易写错又最值得钉住的部分。
 */
interface NeteaseEapiChannel {
    suspend fun request(
        path: String,
        params: Map<String, Any>,
        rawCipherResponse: Boolean = true,
        cookie: String? = null,
    ): String?

    suspend fun songDetailRaw(songIds: List<Long>, cookie: String? = null): String?
}

@Singleton
class NeteaseEapi @Inject constructor(
    @ApplicationContext context: Context,
    private val http: SourceHttp,
) : NeteaseEapiChannel {

    private val prefs = context.getSharedPreferences("netease_eapi", Context.MODE_PRIVATE)

    private val deviceId: String by lazy {
        prefs.getString(KEY_DEVICE_ID, null) ?: randomHex(32).also { save(KEY_DEVICE_ID, it) }
    }
    private val clientSign: String by lazy {
        prefs.getString(KEY_CLIENT_SIGN, null)
            ?: ("${randomMac()}@@@${randomUpper(8)}@@@@@@${randomHex(64)}").also { save(KEY_CLIENT_SIGN, it) }
    }
    private val osVer: String by lazy {
        prefs.getString(KEY_OS_VER, null)
            ?: "Microsoft-Windows-10--build-${Random.nextInt(20000, 30000)}-64bit".also { save(KEY_OS_VER, it) }
    }
    private val mode: String by lazy {
        prefs.getString(KEY_MODE, null)
            ?: MAINBOARDS[Random.nextInt(MAINBOARDS.size)].also { save(KEY_MODE, it) }
    }

    /**
     * 调一个 eapi 接口，返回**解密后的 JSON 文本**；HTTP 非 2xx / 解密失败都返回 null。
     *
     * @param path 形如 `/eapi/v6/playlist/detail`（含 `/eapi/` 前缀，内部换成 `/api/` 参与摘要）
     * @param rawCipherResponse 响应体是否裸密文：`playlist/detail`、`player/url` 是裸的，
     *   `song/lyric` 是 base64 的。传错了会自动换另一种再试（两边都试一遍的代价是一次 CPU 解密）。
     * @param cookie 登录态；null 用匿名桌面客户端身份
     */
    override suspend fun request(
        path: String,
        params: Map<String, Any>,
        rawCipherResponse: Boolean,
        cookie: String?,
    ): String? {
        val header = buildHeader()
        val encrypted = encryptParams(path, params, header)

        val body = http.postForm(
            url = "https://interface.music.163.com$path",
            form = mapOf("params" to encrypted),
            headers = mapOf(
                "User-Agent" to UA,
                "Referer" to "https://music.163.com/",
                "Accept" to "*/*",
                "Cookie" to cookieWithClientIdentity(cookie),
            ),
        ) ?: return null

        return decryptBody(body, rawCipherResponse)
    }

    /** `/eapi/v3/song/detail` 批量取详情（歌单 trackIds 超出 tracks 时补全用） */
    override suspend fun songDetailRaw(songIds: List<Long>, cookie: String?): String? {
        if (songIds.isEmpty()) return null
        // 与 JS JSON.stringify 字节一致，不能带空格
        val c = songIds.joinToString(prefix = "[", postfix = "]") { """{"id":$it}""" }
        return request("/eapi/v3/song/detail", linkedMapOf("c" to c), cookie = cookie)
    }

    data class Detailed(val body: String?, val cookies: List<String>)

    /**
     * 与 [request] 相同，但**带回响应 Set-Cookie**。
     *
     * 会话续期（`/eapi/login/token/refresh`）只在响应头里轮换 MUSIC_U，
     * `SourceHttp` 拿不到头，所以这一条单独走 OkHttp —— 与 weapi 同样的理由。
     */
    suspend fun requestWithCookies(
        path: String,
        params: Map<String, Any>,
        rawCipherResponse: Boolean = true,
        cookie: String? = null,
    ): Detailed = withContext(Dispatchers.IO) {
        runCatching {
            val header = buildHeader()
            val encrypted = encryptParams(path, params, header)
            val request = Request.Builder()
                .url("https://interface.music.163.com$path")
                .post(FormBody.Builder().add("params", encrypted).build())
                .header("User-Agent", UA)
                .header("Referer", "https://music.163.com/")
                .header("Accept", "*/*")
                .header("Cookie", cookieWithClientIdentity(cookie))
                .build()
            client.newCall(request).execute().use { response ->
                val sets = response.headers("Set-Cookie")
                val bytes = response.body?.bytes() ?: ByteArray(0)
                Detailed(decryptBody(bytes, rawCipherResponse), sets)
            }
        }.getOrElse { Detailed(null, emptyList()) }
    }

    private fun buildHeader() = buildJsonObject {
        put("clientSign", clientSign)
        put("osver", osVer)
        put("deviceId", deviceId)
        put("os", "pc")
        put("appver", APP_VER)
        put("requestId", System.currentTimeMillis().toString())
    }

    private fun encryptParams(path: String, params: Map<String, Any>, header: JsonObject): String {
        val finalParams = buildJsonObject {
            for ((key, value) in params) {
                when (value) {
                    is Long -> put(key, JsonPrimitive(value))
                    is Int -> put(key, JsonPrimitive(value))
                    is Boolean -> put(key, JsonPrimitive(value))
                    is String -> put(key, JsonPrimitive(value))
                    else -> error("不支持的 eapi 参数类型：${value::class.simpleName}")
                }
            }
            // header 是**字符串**而不是对象：桌面客户端就是这么发的，改成对象签名对不上
            put("header", header.toString())
            put("e_r", true)
        }
        val encryptPath = if (path.startsWith("/eapi/")) "/api/" + path.removePrefix("/eapi/") else path
        val paramsText = finalParams.toString()
        val digest = md5Hex("nobody" + encryptPath + "use" + paramsText + "md5forencrypt")
        val data = encryptPath + SALT + paramsText + SALT + digest
        return aesEcbEncryptHex(data.toByteArray(Charsets.UTF_8))
    }

    private val client = OkHttpClient.Builder()
        .connectTimeout(10, TimeUnit.SECONDS)
        .readTimeout(20, TimeUnit.SECONDS)
        .callTimeout(25, TimeUnit.SECONDS)
        .build()

    /**
     * 登录态 Cookie 常常只有 `MUSIC_U / MUSIC_A / __csrf / NMTID`（扫码/手机号登录回填的），
     * 缺 `os=pc / appver / osver / deviceId`。此时服务端把请求当**普通网页访客**，
     * 黑胶会员也常只给 128k 或 30 秒试听片段（`freeTrialInfo`）。
     *
     * 这里把 [anonymousCookie] 的客户端身份字段补进登录 Cookie（已有同名字段以登录态为准），
     * 不动 MUSIC_U 等会话凭据。
     */
    private fun cookieWithClientIdentity(cookie: String?): String {
        if (cookie.isNullOrBlank()) return anonymousCookie()
        val map = linkedMapOf<String, String>()
        cookie.split(';').forEach { part ->
            val kv = part.trim().split('=', limit = 2)
            if (kv.size == 2 && kv[0].isNotBlank()) map[kv[0]] = kv[1]
        }
        anonymousCookie().split(';').forEach { part ->
            val kv = part.trim().split('=', limit = 2)
            if (kv.size == 2 && kv[0].isNotBlank() && kv[0] !in map) map[kv[0]] = kv[1]
        }
        return map.entries.joinToString("; ") { "${it.key}=${it.value}" }
    }

    private fun anonymousCookie(): String =
        "os=pc; deviceId=$deviceId; osver=$osVer; clientSign=$clientSign; " +
            "channel=netease; mode=$mode; appver=$APP_VER"

    private fun decryptBody(body: ByteArray, rawFirst: Boolean): String? {
        if (body.isEmpty()) return null
        val first = if (rawFirst) decryptRaw(body) else decryptBase64(body)
        val second = if (first.isNullOrBlank()) {
            if (rawFirst) decryptBase64(body) else decryptRaw(body)
        } else {
            null
        }
        val text = first ?: second ?: return null
        // 裸密文按 PKCS7 去填充后尾部可能残留可解析的垃圾字节，截到最后一个 '}'
        val end = text.lastIndexOf('}')
        return if (end >= 0 && end < text.length - 1) text.substring(0, end + 1) else text
    }

    private fun decryptRaw(cipherBytes: ByteArray): String? = runCatching {
        val cipher = Cipher.getInstance("AES/ECB/PKCS5Padding")
        cipher.init(Cipher.DECRYPT_MODE, SecretKeySpec(EAPI_KEY.toByteArray(Charsets.UTF_8), "AES"))
        String(cipher.doFinal(cipherBytes), Charsets.UTF_8)
    }.getOrNull()

    private fun decryptBase64(body: ByteArray): String? = runCatching {
        decryptRaw(Base64.getDecoder().decode(String(body, Charsets.UTF_8).trim()))
    }.getOrNull()

    /** eapi 要求**大写** hex（C# 用 Convert.ToHexString） */
    private fun aesEcbEncryptHex(data: ByteArray): String = runCatching {
        val cipher = Cipher.getInstance("AES/ECB/PKCS5Padding")
        cipher.init(Cipher.ENCRYPT_MODE, SecretKeySpec(EAPI_KEY.toByteArray(Charsets.UTF_8), "AES"))
        cipher.doFinal(data).joinToString("") { "%02X".format(it) }
    }.getOrDefault("")

    private fun md5Hex(text: String): String =
        MessageDigest.getInstance("MD5").digest(text.toByteArray(Charsets.UTF_8))
            .joinToString("") { "%02x".format(it) }

    private fun save(key: String, value: String) {
        prefs.edit().putString(key, value).apply()
    }

    private fun randomHex(length: Int): String {
        val chars = "0123456789abcdef"
        return buildString(length) { repeat(length) { append(chars[Random.nextInt(16)]) } }
    }

    private fun randomUpper(length: Int): String {
        val chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZ"
        return buildString(length) { repeat(length) { append(chars[Random.nextInt(26)]) } }
    }

    private fun randomMac(): String =
        (0 until 6).joinToString(":") { "%02X".format(Random.nextInt(256)) }

    private companion object {
        const val EAPI_KEY = "e82ckenh8dichen8"
        const val SALT = "-36cd479b6b5-"
        const val APP_VER = "3.1.3.203419"
        const val UA =
            "Mozilla/5.0 (Windows NT 10.0; WOW64) AppleWebKit/537.36 (KHTML, like Gecko) " +
                "Safari/537.36 Chrome/91.0.4472.164 NeteaseMusicDesktop/3.1.3.203419"

        const val KEY_DEVICE_ID = "device_id"
        const val KEY_CLIENT_SIGN = "client_sign"
        const val KEY_OS_VER = "os_ver"
        const val KEY_MODE = "mode"

        val MAINBOARDS = listOf(
            "MS-iCraft B760M WIFI",
            "ASUS ROG STRIX Z790",
            "MSI MAG B550 TOMAHAWK",
            "ASRock X670E Taichi",
            "GIGABYTE Z790 AORUS ELITE",
        )
    }
}
