package com.catclaw.music.kt.data.netease

import java.math.BigInteger
import java.security.MessageDigest
import java.util.concurrent.TimeUnit
import javax.crypto.Cipher
import javax.crypto.spec.IvParameterSpec
import javax.crypto.spec.SecretKeySpec
import javax.inject.Inject
import javax.inject.Singleton
import kotlin.random.Random
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.withContext
import kotlinx.serialization.json.Json
import kotlinx.serialization.json.JsonObject
import kotlinx.serialization.json.contentOrNull
import kotlinx.serialization.json.intOrNull
import kotlinx.serialization.json.jsonPrimitive
import okhttp3.FormBody
import okhttp3.OkHttpClient
import okhttp3.Request

/**
 * 网易云 Web 端 weapi 加密通道 —— 逐行移植插件 `NeteaseWeapi.cs`。
 *
 * 算法（与 api-enhanced `util/crypto.js` 一致）：
 * 1. `params = AES-128-CBC(AES-128-CBC(json, 预设key), 随机16位key)`；
 * 2. `encSecKey = RSA_NoPadding(reverse(随机key), 固定公钥)`，输出 128 字节大端 hex。
 *
 * 为什么单独走 OkHttp 而不是复用 [com.catclaw.music.kt.core.ai.SourceHttp]：
 * **手机号登录的会话在响应头 `Set-Cookie` 里**（MUSIC_U），而 SourceHttp 只返回 body。
 * 少这一层就拿不到登录态，只能拿 body 里没有的字段硬凑 —— 那正是插件当初把
 * `RequestDetailedAsync` 单独拿出来的原因。
 */
@Singleton
class NeteaseWeapi @Inject constructor() {

    data class Result(
        val code: Int,
        val message: String?,
        val body: JsonObject?,
        /** 响应 Set-Cookie 拼成的会话串；只有登录类接口会有 */
        val cookie: String?,
    ) {
        val ok: Boolean get() = code == 200 && !cookie.isNullOrBlank()
    }

    private val client = OkHttpClient.Builder()
        .connectTimeout(10, TimeUnit.SECONDS)
        .readTimeout(20, TimeUnit.SECONDS)
        .callTimeout(25, TimeUnit.SECONDS)
        .followRedirects(true)
        .build()

    /**
     * @param path 形如 `/api/w/login/cellphone`（内部去掉 `/api/` 拼到 `/weapi/`）
     * @param cookie 已有会话（登录时传 null）
     */
    suspend fun call(path: String, body: Map<String, Any>, cookie: String? = null): Result =
        withContext(Dispatchers.IO) {
            runCatching {
                val paramsJson = buildJson(body)
                val secretKey = randomBase62(16)
                val params = aesCbcBase64(
                    aesCbcBase64(paramsJson, PRESET_KEY),
                    secretKey,
                )
                val encSecKey = rsaNoPaddingHex(secretKey.reversed())

                val weapiPath = if (path.startsWith("/api/")) path.removePrefix("/api/") else path.trimStart('/')
                val form = FormBody.Builder()
                    .add("params", params)
                    .add("encSecKey", encSecKey)
                    .add("csrf_token", csrfToken(cookie))
                    .build()
                val request = Request.Builder()
                    .url("https://music.163.com/weapi/$weapiPath")
                    .post(form)
                    .header("User-Agent", UA)
                    .header("Referer", "https://music.163.com/")
                    .apply { if (!cookie.isNullOrBlank()) header("Cookie", cookie) }
                    .build()

                client.newCall(request).execute().use { response ->
                    if (!response.isSuccessful) {
                        return@withContext Result(response.code, "HTTP ${response.code}", null, null)
                    }
                    val setCookies = response.headers("Set-Cookie")
                    val text = response.body?.string().orEmpty()
                    val root = runCatching { JSON.parseToJsonElement(text) as? JsonObject }.getOrNull()
                    val code = root?.get("code")?.jsonPrimitive?.intOrNull ?: -1
                    val message = root?.get("message")?.jsonPrimitive?.contentOrNull
                        ?: root?.get("msg")?.jsonPrimitive?.contentOrNull
                    val session = extractCookie(setCookies, root)
                    Result(code, message, root, session)
                }
            }.getOrElse { Result(-1, it.message, null, null) }
        }

    /** 登录响应里取会话：优先 Set-Cookie，其次 body 的 `cookie` 字段（与插件 ExtractLoginCookie 一致） */
    private fun extractCookie(setCookies: List<String>, body: JsonObject?): String? {
        if (setCookies.isNotEmpty()) {
            val joined = setCookies.map { it.substringBefore(';').trim() }
                .filter { it.contains('=') }
                .joinToString("; ")
            if (joined.isNotBlank()) return joined
        }
        return body?.get("cookie")?.jsonPrimitive?.contentOrNull?.takeIf { it.isNotBlank() }
    }

    // ── 加密 ───────────────────────────────────────────────────

    private fun aesCbcBase64(text: String, key: String): String {
        val cipher = Cipher.getInstance("AES/CBC/PKCS5Padding")
        cipher.init(
            Cipher.ENCRYPT_MODE,
            SecretKeySpec(key.toByteArray(Charsets.UTF_8), "AES"),
            IvParameterSpec(IV.toByteArray(Charsets.UTF_8)),
        )
        return java.util.Base64.getEncoder().encodeToString(cipher.doFinal(text.toByteArray(Charsets.UTF_8)))
    }

    /** 原始 RSA（NoPadding）：m^e mod n，右对齐到 128 字节，输出大写 hex */
    private fun rsaNoPaddingHex(input: String): String {
        val modulus = BigInteger(MODULUS_HEX, 16)
        val message = BigInteger(1, input.toByteArray(Charsets.UTF_8))
        val encrypted = message.modPow(BigInteger.valueOf(65537), modulus)
        val raw = encrypted.toByteArray()
        val body = if (raw.size > 128) raw.copyOfRange(raw.size - 128, raw.size) else raw
        val padded = ByteArray(128)
        System.arraycopy(body, 0, padded, padded.size - body.size, body.size)
        return padded.joinToString("") { "%02X".format(it) }
    }

    /** JSON 序列化：保持插入顺序、不转义非 ASCII（与 C# JsonSerializer 默认一致） */
    private fun buildJson(body: Map<String, Any>): String = buildString {
        append('{')
        body.entries.forEachIndexed { index, (key, value) ->
            if (index > 0) append(',')
            append('"').append(key).append("\":")
            when (value) {
                is Number, is Boolean -> append(value.toString())
                else -> append('"').append(value.toString().replace("\\", "\\\\").replace("\"", "\\\"")).append('"')
            }
        }
        append('}')
    }

    private fun randomBase62(length: Int): String =
        buildString(length) { repeat(length) { append(BASE62[Random.nextInt(BASE62.length)]) } }

    private fun csrfToken(cookie: String?): String {
        if (cookie.isNullOrBlank()) return ""
        for (part in cookie.split(';')) {
            val kv = part.trim().split('=', limit = 2)
            if (kv.size == 2 && (kv[0].trim() == "__csrf" || kv[0].trim() == "csrf_token")) return kv[1].trim()
        }
        return ""
    }

    companion object {
        private const val PRESET_KEY = "0CoJUm6Qyw8W8jud"
        private const val IV = "0102030405060708"
        private const val BASE62 = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789"
        private const val UA =
            "Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/537.36 (KHTML, like Gecko) " +
                "Chrome/124.0.0.0 Safari/537.36 Edg/124.0.0.0"

        /** api-enhanced 的 1024 位固定公钥模数（与插件 PEM 里导出的 modulus 相同） */
        private const val MODULUS_HEX =
            "00e0b509f6259df8642dbc35662901477df22677ec152b5ff68ace615bb7b725152b3ab17a876aea8a5aa76d2e417629" +
                "ec4ee341f56135fccf695280104e0312ecbda92557c93870114af6c9d05c4f7f0c3685b7a46bee255932575cce10b424" +
                "d813cfe4875d3e82047b97ddef52741d546b8e289dc6935b3ece0462db0a22b8e7"

        private val JSON = Json { ignoreUnknownKeys = true; isLenient = true }

        fun md5HexLower(text: String): String =
            MessageDigest.getInstance("MD5").digest(text.toByteArray(Charsets.UTF_8))
                .joinToString("") { "%02x".format(it) }
    }
}
