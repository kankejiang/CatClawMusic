package com.catclaw.music.kt.lyrico.plugin

import java.io.ByteArrayOutputStream
import java.security.MessageDigest
import java.util.Base64
import java.util.concurrent.ConcurrentHashMap
import java.util.zip.Inflater
import javax.crypto.Cipher
import javax.crypto.spec.SecretKeySpec
import javax.xml.parsers.DocumentBuilderFactory
import kotlinx.serialization.json.Json
import kotlinx.serialization.json.JsonArray
import kotlinx.serialization.json.JsonElement
import kotlinx.serialization.json.JsonObject
import kotlinx.serialization.json.JsonPrimitive
import kotlinx.serialization.json.add
import kotlinx.serialization.json.buildJsonArray
import kotlinx.serialization.json.buildJsonObject
import kotlinx.serialization.json.jsonArray
import kotlinx.serialization.json.jsonObject
import kotlinx.serialization.json.jsonPrimitive
import kotlinx.serialization.json.put
import kotlinx.serialization.json.putJsonObject
import okhttp3.MediaType.Companion.toMediaTypeOrNull
import okhttp3.OkHttpClient
import okhttp3.Request
import okhttp3.RequestBody
import okhttp3.RequestBody.Companion.toRequestBody
import org.w3c.dom.Document
import org.w3c.dom.Element
import org.w3c.dom.Node

/** 宿主日志出口：默认走 Android Log，单测里可换成内存实现。 */
fun interface LyricoLogSink {
    fun log(level: String, tag: String, message: String)
}

/**
 * [LyricoHostApi] 的默认实现。
 *
 * 除 OkHttp 外不依赖 Android 运行时（Base64/Crypto/Zip/XML 都用 JDK 自带实现），
 * 所以大部分逻辑可以直接在 JVM 单测里验证，不必上真机。
 */
class LyricoHost(
    private val okHttpClient: OkHttpClient,
    private val logSink: LyricoLogSink,
    private val userAgent: String = DEFAULT_USER_AGENT,
    private val appName: String = "CatClawMusic",
    private val appVersion: String = "1.0.0",
    /** 当前语言的 key -> 文案（来自插件 manifest 的 i18n 资源） */
    private val strings: Map<String, String> = emptyMap(),
) : LyricoHostApi {

    private val json = Json { ignoreUnknownKeys = true; isLenient = true }
    private val cache = ConcurrentHashMap<String, CacheEntry>()

    private data class CacheEntry(val value: String, val expireAt: Long)

    // ────────────────────────────── http ──────────────────────────────

    override fun httpGetText(url: String, optionsJson: String): String =
        execute(url, "GET", null, optionsJson).body

    override fun httpPostText(url: String, body: String, optionsJson: String): String =
        execute(url, "POST", body, optionsJson).body

    override fun httpGet(url: String, optionsJson: String): String =
        execute(url, "GET", null, optionsJson).toJson()

    override fun httpPost(url: String, body: String, optionsJson: String): String =
        execute(url, "POST", body, optionsJson).toJson()

    override fun httpPostBytesResponse(url: String, body: String, optionsJson: String): String =
        execute(url, "POST", body, optionsJson).toJson()

    private data class Resp(
        val code: Int,
        val message: String,
        val headers: Map<String, List<String>>,
        val body: String,
        val bodyBase64: String,
    ) {
        fun toJson(): String = buildJsonObject {
            put("code", code)
            put("message", message)
            putJsonObject("headers") {
                headers.forEach { (k, values) ->
                    // Set-Cookie 这类多值头保留成数组，其余按逗号拼接，与 Lyrico 行为一致
                    if (values.size > 1) {
                        put(k, buildJsonArray { values.forEach { add(it) } })
                    } else {
                        put(k, values.firstOrNull() ?: "")
                    }
                }
            }
            put("body", body)
            put("bodyBase64", bodyBase64)
        }.toString()
    }

    private fun execute(url: String, method: String, body: String?, optionsJson: String): Resp {
        val options = parseOptions(optionsJson)
        val builder = Request.Builder()
            .url(url)
            .header("User-Agent", options.userAgent ?: userAgent)
        options.headers.forEach { (k, v) ->
            runCatching { builder.header(k, v) }
        }
        if (method == "POST") {
            val mediaType = (options.contentType ?: "application/json").toMediaTypeOrNull()
            val requestBody: RequestBody = (body ?: "").toRequestBody(mediaType)
            builder.post(requestBody)
        }
        val client = if (options.followRedirects) {
            okHttpClient
        } else {
            okHttpClient.newBuilder().followRedirects(false).followSslRedirects(false).build()
        }
        return try {
            client.newCall(builder.build()).execute().use { resp ->
                val bytes = resp.body?.bytes() ?: ByteArray(0)
                val headers = resp.headers.toMultimap()
                Resp(
                    code = resp.code,
                    message = resp.message,
                    headers = headers,
                    body = runCatching { String(bytes, Charsets.UTF_8) }.getOrDefault(""),
                    bodyBase64 = Base64.getEncoder().encodeToString(bytes),
                )
            }
        } catch (t: Throwable) {
            logSink.log("warn", "LyricoHost", "http $method $url 失败：${t.message}")
            // 与 Lyrico 一致：异常时 code=0，正文空
            Resp(0, t.message ?: t.javaClass.simpleName, emptyMap(), "", "")
        }
    }

    private data class Options(
        val contentType: String?,
        val headers: Map<String, String>,
        val followRedirects: Boolean,
        val userAgent: String?,
    )

    private fun parseOptions(optionsJson: String): Options {
        if (optionsJson.isBlank()) return Options(null, emptyMap(), true, null)
        val obj = runCatching { json.parseToJsonElement(optionsJson).jsonObject }.getOrNull()
            ?: return Options(null, emptyMap(), true, null)
        val headers = (obj["headers"] as? JsonObject)?.entries
            ?.mapNotNull { (k, v) -> (v as? JsonPrimitive)?.content?.let { k to it } }
            ?.toMap() ?: emptyMap()
        return Options(
            contentType = obj["contentType"]?.jsonPrimitive?.contentOrNullSafe(),
            headers = headers,
            followRedirects = obj["followRedirects"]?.jsonPrimitive?.booleanOrNullSafe() ?: true,
            userAgent = headers.entries.firstOrNull { it.key.equals("User-Agent", true) }?.value,
        )
    }

    // ───────────────────────────── crypto ─────────────────────────────

    override fun cryptoMd5(text: String): String =
        MessageDigest.getInstance("MD5").digest(text.toByteArray(Charsets.UTF_8))
            .joinToString("") { "%02x".format(it) }

    override fun cryptoAesEcbPkcs5EncryptHex(text: String, key: String): String =
        aesEcb(text.toByteArray(Charsets.UTF_8), key, encrypt = true)
            .joinToString("") { "%02x".format(it) }

    override fun cryptoAesEcbPkcs5EncryptBase64(text: String, key: String): String =
        Base64.getEncoder().encodeToString(aesEcb(text.toByteArray(Charsets.UTF_8), key, true))

    override fun cryptoAesEcbPkcs5DecryptBase64ToText(base64: String, key: String): String {
        val data = runCatching { Base64.getDecoder().decode(base64) }.getOrNull() ?: return ""
        return runCatching { String(aesEcb(data, key, false), Charsets.UTF_8).trimEnd('\u0000') }
            .getOrDefault("")
    }

    private fun aesEcb(data: ByteArray, key: String, encrypt: Boolean): ByteArray {
        val raw = key.toByteArray(Charsets.UTF_8)
        // 与 Lyrico 一致：非 16/24/32 长度的 key 补齐/截断到最近合法长度
        val keyBytes = when (raw.size) {
            16, 24, 32 -> raw
            else -> {
                val target = if (raw.size >= 24) 24 else if (raw.size > 16) 24 else 16
                ByteArray(target).also { raw.copyInto(it, endIndex = minOf(raw.size, target)) }
            }
        }
        val cipher = Cipher.getInstance("AES/ECB/PKCS5Padding")
        cipher.init(
            if (encrypt) Cipher.ENCRYPT_MODE else Cipher.DECRYPT_MODE,
            SecretKeySpec(keyBytes, "AES"),
        )
        return cipher.doFinal(data)
    }

    // ───────────────────────────── base64 ─────────────────────────────

    override fun base64EncodeText(text: String): String =
        Base64.getEncoder().encodeToString(text.toByteArray(Charsets.UTF_8))

    override fun base64DecodeText(base64: String): String =
        runCatching { String(Base64.getDecoder().decode(base64), Charsets.UTF_8) }.getOrDefault("")

    override fun base64EncodeUrlText(text: String): String =
        Base64.getUrlEncoder().withoutPadding().encodeToString(text.toByteArray(Charsets.UTF_8))

    override fun base64DecodeUrlText(base64Url: String): String =
        runCatching {
            String(Base64.getUrlDecoder().decode(base64Url.trimEnd('=')), Charsets.UTF_8)
        }.getOrDefault("")

    override fun base64DropBytes(base64: String, count: Int): String {
        val data = runCatching { Base64.getDecoder().decode(base64) }.getOrNull()
            ?: return Base64.getEncoder().encodeToString(ByteArray(0))
        if (count >= data.size) return Base64.getEncoder().encodeToString(ByteArray(0))
        return Base64.getEncoder().encodeToString(data.copyOfRange(maxOf(0, count), data.size))
    }

    override fun base64EncodeBytes(bytesJson: String): String =
        Base64.getEncoder().encodeToString(bytesOf(bytesJson))

    override fun base64DecodeBytes(base64: String): String {
        val data = runCatching { Base64.getDecoder().decode(base64) }.getOrNull() ?: ByteArray(0)
        return buildJsonArray { data.forEach { add(it.toInt() and 0xFF) } }.toString()
    }

    // ───────────────────────────── bytes ──────────────────────────────

    override fun bytesXorBase64(base64: String, keyBytesJson: String): String {
        val data = runCatching { Base64.getDecoder().decode(base64) }.getOrNull() ?: ByteArray(0)
        val key = bytesOf(keyBytesJson)
        if (key.isEmpty()) return Base64.getEncoder().encodeToString(data)
        val out = ByteArray(data.size) { i -> (data[i].toInt() xor key[i % key.size].toInt()).toByte() }
        return Base64.getEncoder().encodeToString(out)
    }

    override fun bytesXor(inputBytesJson: String, keyBytesJson: String): String {
        val data = bytesOf(inputBytesJson)
        val key = bytesOf(keyBytesJson)
        if (key.isEmpty()) return buildJsonArray { data.forEach { add(it.toInt() and 0xFF) } }.toString()
        return buildJsonArray {
            data.forEachIndexed { i, b -> add((b.toInt() xor key[i % key.size].toInt()) and 0xFF) }
        }.toString()
    }

    // ────────────────────────── compression ──────────────────────────

    override fun compressionInflateBase64ToText(base64: String): String {
        val data = runCatching { Base64.getDecoder().decode(base64) }.getOrNull() ?: return ""
        return String(inflate(data), Charsets.UTF_8)
    }

    override fun compressionInflateBytesToText(bytesJson: String): String =
        String(inflate(bytesOf(bytesJson)), Charsets.UTF_8)

    /** zlib 优先、raw deflate 兜底（与 Lyrico 一致：两种压缩头都要能吃）。 */
    private fun inflate(data: ByteArray): ByteArray {
        if (data.isEmpty()) return ByteArray(0)
        inflateWithZlib(data)?.let { return it }
        return inflateRaw(data) ?: ByteArray(0)
    }

    private fun inflateWithZlib(data: ByteArray): ByteArray? = runCatching {
        val inflater = Inflater()
        inflater.setInput(data)
        val out = ByteArrayOutputStream()
        val buf = ByteArray(8192)
        while (!inflater.finished()) {
            val n = inflater.inflate(buf)
            if (n == 0) {
                if (inflater.needsInput() || inflater.needsDictionary()) break
            } else {
                out.write(buf, 0, n)
            }
        }
        inflater.end()
        out.toByteArray()
    }.getOrNull()?.takeIf { it.isNotEmpty() }

    private fun inflateRaw(data: ByteArray): ByteArray? = runCatching {
        val inflater = Inflater(true)
        inflater.setInput(data)
        val out = ByteArrayOutputStream()
        val buf = ByteArray(8192)
        while (!inflater.finished()) {
            val n = inflater.inflate(buf)
            if (n == 0) {
                if (inflater.needsInput() || inflater.needsDictionary()) break
            } else {
                out.write(buf, 0, n)
            }
        }
        inflater.end()
        out.toByteArray()
    }.getOrNull()?.takeIf { it.isNotEmpty() }

    // ───────────────────────────── cache ──────────────────────────────

    override fun cacheGet(key: String): String {
        val now = System.currentTimeMillis()
        val e = cache[key]
        if (e == null) return """{"hit":false}"""
        if (e.expireAt in 1 until now) {
            cache.remove(key)
            return """{"hit":false}"""
        }
        return buildJsonObject {
            put("hit", true)
            put("value", e.value)
        }.toString()
    }

    override fun cacheSet(key: String, value: String, ttlMs: Double) {
        val ttl = ttlMs.toLong()
        cache[key] = CacheEntry(value, if (ttl > 0) System.currentTimeMillis() + ttl else 0L)
    }

    override fun cacheRemove(key: String) {
        cache.remove(key)
    }

    override fun cacheClear() {
        cache.clear()
    }

    // ────────────────────────────── log ───────────────────────────────

    override fun logDebug(tag: String, message: String) = logSink.log("debug", tag, message)
    override fun logWarn(tag: String, message: String) = logSink.log("warn", tag, message)
    override fun logError(tag: String, message: String) = logSink.log("error", tag, message)

    // ─────────────────────────── runtime/app ──────────────────────────

    override fun runtimeGetInfo(): String = buildJsonObject {
        put("pluginApiVersion", LyricoManifest.HOST_API_VERSION)
        put("hostApiVersion", LyricoManifest.HOST_API_VERSION)
        put("engine", "quickjs")
        put("engineVersion", "")
        put(
            "supportedHostApis",
            buildJsonArray {
                SUPPORTED_APIS.forEach { add(it) }
            },
        )
    }.toString()

    override fun appGetUserAgent(): String = userAgent

    override fun appGetInfo(): String = buildJsonObject {
        put("name", appName)
        put("versionName", appVersion)
    }.toString()

    // ───────────────────────────── i18n ───────────────────────────────

    override fun i18nT(key: String, argsJson: String): String {
        val template = strings[key] ?: key
        if (argsJson.isBlank()) return template
        val args = runCatching { json.parseToJsonElement(argsJson).jsonArray }.getOrNull()
            ?: return template
        // Lyrico 插件的模板用 printf 风格：%1$s / %2$s / %1$d
        var result = template
        args.forEachIndexed { index, element ->
            val text = (element as? JsonPrimitive)?.content ?: ""
            val n = index + 1
            result = result.replace("%$n\$s", text).replace("%$n\$d", text)
        }
        // 兜底：剩余未编号的 %s 依次填入
        args.forEach { element ->
            if (!result.contains("%s")) return@forEach
            val text = (element as? JsonPrimitive)?.content ?: ""
            result = result.replaceFirst("%s", text)
        }
        return result
    }

    // ────────────────────────────── xml ───────────────────────────────

    override fun xmlGetRootAttributes(xml: String): String {
        val doc = parseXml(xml) ?: return "{}"
        val root = doc.documentElement ?: return "{}"
        return buildJsonObject {
            val attrs = root.attributes
            for (i in 0 until attrs.length) {
                val a = attrs.item(i)
                put(a.nodeName, a.nodeValue ?: "")
            }
        }.toString()
    }

    override fun xmlFindElements(xml: String, optionsJson: String): String {
        val doc = parseXml(xml) ?: return "[]"
        val opts = parseXmlOptions(optionsJson)
        val out = mutableListOf<JsonElement>()
        val all = doc.documentElement?.let { collectElements(it) } ?: emptyList()
        all.filter { it.tagName.equals(opts.tag, ignoreCase = true) }
            .filter { matchesAttrs(it, opts.attrs) }
            .forEach { out += elementToJson(it) }
        return JsonArray(out).toString()
    }

    override fun xmlRemoveElements(xml: String, optionsJson: String): String {
        val doc = parseXml(xml) ?: return xml
        val opts = parseXmlOptions(optionsJson)
        val root = doc.documentElement ?: return xml
        collectElements(root)
            .filter { it.tagName.equals(opts.tag, ignoreCase = true) }
            .filter { opts.attrs.isEmpty() || matchesAttrs(it, opts.attrs) }
            .forEach { el -> el.parentNode?.removeChild(el) }
        return serializeXml(doc)
    }

    override fun xmlReplaceChildrenByAttr(xml: String, optionsJson: String): String {
        val doc = parseXml(xml) ?: return xml
        val root = doc.documentElement ?: return xml
        val opts = runCatching { json.parseToJsonElement(optionsJson).jsonObject }.getOrNull()
            ?: return xml
        val targetTag = opts["targetTag"]?.jsonPrimitive?.contentOrNullSafe() ?: "p"
        val keyAttr = opts["keyAttr"]?.jsonPrimitive?.contentOrNullSafe()?.substringAfterLast(':') ?: "key"
        val replacements = (opts["replacements"] as? JsonObject)
            ?.mapValues { (_, v) ->
                when (v) {
                    is JsonPrimitive -> false to (v.contentOrNullSafe() ?: "")
                    is JsonObject -> {
                        val mode = v["mode"]?.jsonPrimitive?.contentOrNullSafe() ?: "text"
                        (mode == "xml") to (v["value"]?.jsonPrimitive?.contentOrNullSafe() ?: "")
                    }
                    else -> false to ""
                }
            } ?: emptyMap()

        collectElements(root)
            .filter { it.tagName.equals(targetTag, ignoreCase = true) }
            .forEach { el ->
                val key = el.getAttribute(keyAttr)
                val rep = replacements[key] ?: return@forEach
                val (isXml, value) = rep
                while (el.firstChild != null) el.removeChild(el.firstChild)
                if (isXml && value.isNotEmpty()) {
                    val frag = parseXml("<r>$value</r>")?.documentElement
                    if (frag != null) {
                        var child = frag.firstChild
                        while (child != null) {
                            val next = child.nextSibling
                            el.appendChild(doc.importNode(child, true))
                            child = next
                        }
                    }
                } else {
                    el.textContent = value
                }
            }

        (opts["rootAttributes"] as? JsonObject)?.forEach { (k, v) ->
            val value = (v as? JsonPrimitive)?.contentOrNullSafe() ?: return@forEach
            root.setAttribute(k, value)
        }
        return serializeXml(doc)
    }

    private data class XmlOptions(val tag: String, val attrs: Map<String, String>)

    private fun parseXmlOptions(optionsJson: String): XmlOptions {
        if (optionsJson.isBlank()) return XmlOptions("p", emptyMap())
        val obj = runCatching { json.parseToJsonElement(optionsJson).jsonObject }.getOrNull()
            ?: return XmlOptions("p", emptyMap())
        val tag = obj["tag"]?.jsonPrimitive?.contentOrNullSafe()
            ?: obj["targetTag"]?.jsonPrimitive?.contentOrNullSafe()
            ?: "p"
        val attrs = (obj["attrs"] as? JsonObject)?.entries
            ?.mapNotNull { (k, v) -> (v as? JsonPrimitive)?.content?.let { k.substringAfterLast(':') to it } }
            ?.toMap()
            ?: emptyMap()
        return XmlOptions(tag, attrs)
    }

    private fun matchesAttrs(el: Element, attrs: Map<String, String>): Boolean =
        attrs.all { (k, v) -> el.getAttribute(k).equals(v, ignoreCase = true) }

    private fun collectElements(root: Element): List<Element> {
        val out = mutableListOf<Element>()
        fun walk(node: Node) {
            if (node is Element) {
                out += node
                val children = node.childNodes
                for (i in 0 until children.length) walk(children.item(i))
            }
        }
        walk(root)
        return out
    }

    private fun elementToJson(el: Element): JsonObject = buildJsonObject {
        put("tag", el.tagName)
        putJsonObject("attrs") {
            val attrs = el.attributes
            for (i in 0 until attrs.length) {
                val a = attrs.item(i)
                put(a.nodeName, a.nodeValue ?: "")
            }
        }
        put("text", el.textContent ?: "")
        put("innerXml", innerXml(el))
        put(
            "children",
            buildJsonArray {
                val children = el.childNodes
                for (i in 0 until children.length) {
                    val c = children.item(i)
                    if (c is Element) add(elementToJson(c))
                }
            },
        )
    }

    private fun innerXml(el: Element): String {
        val sb = StringBuilder()
        val children = el.childNodes
        for (i in 0 until children.length) {
            val c = children.item(i)
            sb.append(serializeNode(c))
        }
        return sb.toString()
    }

    private fun serializeNode(node: Node): String = when (node.nodeType) {
        Node.TEXT_NODE, Node.CDATA_SECTION_NODE -> node.nodeValue ?: ""
        Node.ELEMENT_NODE -> {
            val el = node as Element
            val attrs = StringBuilder()
            val map = el.attributes
            for (i in 0 until map.length) {
                val a = map.item(i)
                attrs.append(' ').append(a.nodeName).append("=\"").append(a.nodeValue ?: "").append('"')
            }
            "<${el.tagName}$attrs>" + innerXml(el) + "</${el.tagName}>"
        }
        else -> ""
    }

    private fun parseXml(xml: String): Document? = runCatching {
        val factory = DocumentBuilderFactory.newInstance().apply {
            isNamespaceAware = false
            // 插件只解析公开接口返回的 XML，禁掉外部实体避免 XXE
            runCatching { setFeature("http://apache.org/xml/features/disallow-doctype-decl", true) }
            runCatching { setFeature("http://xml.org/sax/features/external-general-entities", false) }
            runCatching { setFeature("http://xml.org/sax/features/external-parameter-entities", false) }
        }
        factory.newDocumentBuilder().parse(xml.trim().byteInputStream())
    }.getOrNull()

    private fun serializeXml(doc: Document): String =
        serializeNode(doc.documentElement ?: return "")

    // ───────────────────────────── 工具 ──────────────────────────────

    private fun bytesOf(jsonText: String): ByteArray {
        if (jsonText.isBlank()) return ByteArray(0)
        val arr = runCatching { json.parseToJsonElement(jsonText).jsonArray }.getOrNull()
            ?: return ByteArray(0)
        return ByteArray(arr.size) { i ->
            (arr[i].jsonPrimitive.content.toIntOrNull() ?: 0).toByte()
        }
    }

    private fun JsonPrimitive.contentOrNullSafe(): String? =
        runCatching { content }.getOrNull()

    private fun JsonPrimitive.booleanOrNullSafe(): Boolean? =
        runCatching { content.toBooleanStrict() }.getOrNull()

    companion object {
        private const val DEFAULT_USER_AGENT =
            "Mozilla/5.0 (Linux; Android 14) AppleWebKit/537.36 (KHTML, like Gecko) CatClawMusic/1.0"

        private val SUPPORTED_APIS = listOf(
            "http.getText", "http.postText", "http.get", "http.post", "http.postBytesResponse",
            "crypto.md5", "crypto.aesEcbPkcs5EncryptHex", "crypto.aesEcbPkcs5EncryptBase64",
            "crypto.aesEcbPkcs5DecryptBase64ToText",
            "base64.encodeText", "base64.decodeText", "base64.encodeUrlText",
            "base64.decodeUrlText", "base64.dropBytes", "base64.encodeBytes", "base64.decodeBytes",
            "bytes.xor", "bytes.xorBase64",
            "compression.inflateBase64ToText", "compression.inflateBytesToText",
            "cache.get", "cache.set", "cache.remove", "cache.clear",
            "log.debug", "log.warn", "log.error",
            "runtime.getInfo", "app.getUserAgent", "app.getInfo",
            "i18n.t",
            "xml.getRootAttributes", "xml.findElements", "xml.removeElements",
            "xml.replaceChildrenByAttr",
        )
    }
}
