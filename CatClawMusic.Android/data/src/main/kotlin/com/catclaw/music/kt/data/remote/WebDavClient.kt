package com.catclaw.music.kt.data.remote

import com.catclaw.music.kt.core.model.ConnectionProfile
import java.io.ByteArrayInputStream
import java.io.IOException
import java.util.Base64
import javax.xml.parsers.DocumentBuilderFactory
import kotlinx.serialization.json.Json
import kotlinx.serialization.json.JsonArray
import kotlinx.serialization.json.JsonObject
import kotlinx.serialization.json.booleanOrNull
import kotlinx.serialization.json.buildJsonObject
import kotlinx.serialization.json.contentOrNull
import kotlinx.serialization.json.intOrNull
import kotlinx.serialization.json.jsonObject
import kotlinx.serialization.json.jsonPrimitive
import kotlinx.serialization.json.longOrNull
import kotlinx.serialization.json.put

/**
 * WebDAV 客户端，行为对齐 C# `CatClawMusic.Data/WebDavService*.cs`：
 * Basic 认证用 UTF-8 字节（密码含中文时不能被 ASCII 截断）、PROPFIND 固定请求四个属性、
 * 301/302/307/308 **手动重放**（自动跟随会把 PROPFIND 变成 GET）、
 * depth>1 列全库用 `Depth: 899`、探测不到就依次试 `/dav`、`/webdav` 前缀，
 * 而 PROPFIND 返回 404/405 是 OpenList/Alist 的特征 → 自动切 REST 模式。
 *
 * 全部方法阻塞：调用方负责放到 IO 线程上。
 */
class WebDavClient(
    private val profile: ConnectionProfile,
    private val fetcher: HttpFetcher = UrlHttpFetcher,
) {

    enum class ServerType { Standard, OpenList }

    data class TestResult(val ok: Boolean, val message: String, val serverType: ServerType, val davPrefix: String)

    /** 探测出来的路径前缀（空串表示根）；OpenList 模式下由 /api 直连，不用前缀 */
    var davPrefix: String = ""
        private set

    var serverType: ServerType = ServerType.Standard
        private set

    private val authHeader: String? = profile.userName.takeIf { it.isNotEmpty() }?.let {
        "Basic " + Base64.getEncoder().encodeToString("$it:${profile.password}".toByteArray(Charsets.UTF_8))
    }

    private val json = Json { ignoreUnknownKeys = true; isLenient = true }

    /** 与 C# PropFindBody 逐字一致的模板 */
    private val propFindBody = """<?xml version='1.0' encoding='utf-8'?>
<D:propfind xmlns:D='DAV:'>
  <D:prop>
    <D:resourcetype/>
    <D:getcontentlength/>
    <D:getlastmodified/>
    <D:displayname/>
  </D:prop>
</D:propfind>"""

    fun buildUrl(path: String, isDirectory: Boolean = false): String {
        val clean = path.trim()
        val withPrefix = when {
            clean.isEmpty() || clean == "/" -> davPrefix
            davPrefix.isEmpty() -> clean
            clean == davPrefix || clean.startsWith("$davPrefix/") -> clean
            else -> davPrefix + "/" + clean.trimStart('/')
        }
        val suffix = if (isDirectory && !withPrefix.endsWith("/")) "/" else ""
        return "${profile.origin}${withPrefix}$suffix"
    }

    /** ExoPlayer 直接可用的播放地址：`http://user:pass@host:port/dav/path` */
    fun buildStreamUrl(path: String): String {
        val url = buildUrl(path)
        if (profile.userName.isEmpty()) return url
        val schemeEnd = url.indexOf("://")
        if (schemeEnd < 0) return url
        val user = encode(profile.userName)
        val pass = encode(profile.password)
        val insertAt = schemeEnd + 3
        return url.substring(0, insertAt) + "$user:$pass@" + url.substring(insertAt)
    }

    /** 依次试「用户给的 basePath → 空前缀 → /dav → /webdav」，与 C# ConnectionTest 的探测顺序一致 */
    fun test(): TestResult {
        val candidates = buildList {
            add(profile.basePath.trimEnd('/'))
            add("")
            add("/dav")
            add("/webdav")
        }
        for (candidate in candidates.distinct()) {
            davPrefix = candidate
            serverType = ServerType.Standard
            if (tryPropFind(buildUrl(candidate.ifEmpty { "/" }, isDirectory = true), depth = 0)) {
                return TestResult(true, "webdav $candidate", ServerType.Standard, candidate)
            }
            davPrefix = ""
            if (openListLogin()) {
                serverType = ServerType.OpenList
                return TestResult(true, "openlist", ServerType.OpenList, "")
            }
        }
        return TestResult(false, "PROPFIND 与 OpenList 登录都不通", serverType, davPrefix)
    }

    /** Depth 1：列当前目录的直接子项 */
    fun list(path: String): List<RemoteFile> = propFindEntries(buildUrl(path, isDirectory = true), depth = 1)

    /** Depth 899：一次拿全库；404/405 视为 OpenList 特征并自动切换 */
    fun listAll(path: String): List<RemoteFile> = try {
        propFindEntries(buildUrl(path, isDirectory = true), depth = DEPTH_ALL)
    } catch (e: IOException) {
        val message = e.message.orEmpty()
        if (serverType == ServerType.Standard && (message.contains("404") || message.contains("405"))) {
            if (openListLogin()) {
                serverType = ServerType.OpenList
                openListList(path)
            } else {
                throw e
            }
        } else {
            throw e
        }
    }

    /** OpenList/Alist：/api/fs/get 优先给 raw_url，没有就退到 `/d/{path}?sign=` */
    fun streamUrlFor(path: String): String = when (serverType) {
        ServerType.Standard -> buildStreamUrl(path)
        ServerType.OpenList -> {
            val data = runCatching {
                postJson("${profile.origin}/api/fs/get", buildJsonObject {
                    put("path", path)
                    put("password", "")
                })["data"]?.jsonObject
            }.getOrNull()
            val raw = data?.str("raw_url")
            when {
                !raw.isNullOrEmpty() -> raw
                else -> {
                    val sign = data?.str("sign")
                    val clean = path.trimStart('/')
                    "${profile.origin}/d/$clean" + if (sign.isNullOrEmpty()) "" else "?sign=$sign"
                }
            }
        }
    }

    fun openListList(path: String): List<RemoteFile> {
        val data = runCatching {
            postJson("${profile.origin}/api/fs/list", buildJsonObject {
                put("path", path.ifEmpty { "/" })
                put("page", 1)
                put("per_page", 0)
                put("refresh", false)
            })["data"]?.jsonObject
        }.getOrNull()
        val content = data?.get("content") as? JsonArray ?: return emptyList()
        return content.mapNotNull { element ->
            val o = element as? JsonObject ?: return@mapNotNull null
            val name = o.str("name") ?: return@mapNotNull null
            RemoteFile(
                name = name,
                path = path.trimEnd('/') + "/" + name,
                isDirectory = o.boolean("dir"),
                size = o.long("size"),
            )
        }
    }

    /** 登录并切到 OpenList 模式：探测内部用，配置里已写明是 OpenList 时也可以直接调 */
    internal fun useOpenList(): Boolean = openListLogin().also { if (it) serverType = ServerType.OpenList }

    private fun openListLogin(): Boolean = try {
        val result = fetcher.call(
            "POST",
            "${profile.origin}/api/login",
            mapOf("Content-Type" to "application/json"),
            buildJsonObject {
                put("username", profile.userName)
                put("password", profile.password)
            }.toString(),
        )
        if (result.status !in 200..299) false else {
            val root = runCatching { json.parseToJsonElement(result.body).jsonObject }.getOrNull()
            // OpenList/Alist 的返回是 {code:200, data:{token}}，code 不是 200 就算失败
            if ((root?.get("code")?.jsonPrimitive?.intOrNull ?: -1) != 200) false
            else {
                val token = (root?.get("data") as? JsonObject)?.str("token")
                if (token.isNullOrEmpty()) false else {
                    openListToken = token
                    true
                }
            }
        }
    } catch (e: IOException) {
        false
    }

    private var openListToken: String? = null

    /** token 过期时（401）重登一次再重试，与 C# OpenListSendAsync 同一条规则 */
    private fun postJson(url: String, body: JsonObject, allowRetry: Boolean = true): JsonObject {
        val headers = mutableMapOf("Content-Type" to "application/json")
        openListToken?.let { headers["Authorization"] = it }
        val result = fetcher.call("POST", url, headers, body.toString())
        if (result.status == 401 && allowRetry && openListLogin()) {
            return postJson(url, body, allowRetry = false)
        }
        if (result.status !in 200..299) throw IOException("HTTP ${result.status} for ${url.substringBefore('?')}")
        return json.parseToJsonElement(result.body).jsonObject
    }

    private fun tryPropFind(url: String, depth: Int): Boolean = try {
        val result = propFind(url, depth)
        result.status in 200..299
    } catch (e: IOException) {
        false
    }

    private fun propFindEntries(url: String, depth: Int): List<RemoteFile> {
        val result = propFind(url, depth)
        if (result.status !in 200..299) throw IOException("HTTP ${result.status} for ${url.substringBefore('?')}")
        return parseMultistatus(result.body)
    }

    /** PROPFIND + 手动重放：每跳都重发同样的方法与请求体 */
    private fun propFind(url: String, depth: Int): HttpResult {
        var current = url
        var redirects = 0
        while (true) {
            val headers = mutableMapOf(
                "Depth" to depth.toString(),
                // C# 的 StringContent(body, UTF8, "application/xml") 会带上 charset，逐字对齐
                "Content-Type" to "application/xml; charset=utf-8",
                "Accept" to "text/xml",
            )
            authHeader?.let { headers["Authorization"] = it }
            val result = fetcher.call("PROPFIND", current, headers, propFindBody)
            if (result.status in REDIRECTS && redirects < MAX_REDIRECT) {
                val location = result.location ?: throw IOException("服务器返回 ${result.status} 但缺少 Location 头")
                current = resolve(current, location)
                redirects++
                continue
            }
            return result
        }
    }

    private companion object {
        const val DEPTH_ALL = 899
        const val DAV = "DAV:"
    }
}

/**
 * multistatus XML → 文件列表。只解析列库需要的字段，目录项保留（由调用方过滤），
 * 与 C# 的 `href.Split('/').Last()` + displayname 优先规则一致。
 *
 * 元素一律按**本地名**匹配（忽略 `D:` / `d:` / 无前缀这些写法差异）：
 * Android 自带的 DOM 实现在命名空间处理上与 JVM 的 Xerces 不一致，
 * 用 getElementsByTagNameNS 在真机上会一条都取不到（实测 files=0）。
 */
internal fun parseMultistatus(xml: String): List<RemoteFile> {
    val root = safeParse(xml) ?: return emptyList()
    val files = ArrayList<RemoteFile>()
    for (response in descendants(root, "response")) {
        val prop = descendants(response, "prop").firstOrNull() ?: continue
        val href = textOf(child(response, "href"))
        if (href.isEmpty()) continue
        val resourcetype = child(prop, "resourcetype")
        val isDir = resourcetype != null && child(resourcetype, "collection") != null
        val displayName = textOf(child(prop, "displayname"))
        val rawName = href.trimEnd('/').split('/').lastOrNull { it.isNotEmpty() } ?: href
        files += RemoteFile(
            name = displayName.ifEmpty { decode(rawName) },
            // 路径原样保留（含 /dav 前缀）：C# 的 RemoteId 就是这个形态，
            // buildUrl 已有「带前缀不重复拼」的守卫
            path = normalizeHrefToPath(href),
            isDirectory = isDir,
            size = textOf(child(prop, "getcontentlength")).toLongOrNull() ?: 0L,
        )
    }
    return files
}

private fun localName(node: org.w3c.dom.Node): String =
    node.localName?.takeIf { it.isNotEmpty() } ?: node.nodeName.substringAfter(':')

private fun children(parent: org.w3c.dom.Element): List<org.w3c.dom.Element> =
    (0 until parent.childNodes.length)
        .map(parent.childNodes::item)
        .filterIsInstance<org.w3c.dom.Element>()

private fun child(parent: org.w3c.dom.Element, name: String): org.w3c.dom.Element? =
    children(parent).firstOrNull { localName(it) == name }

private fun descendants(parent: org.w3c.dom.Element, name: String): List<org.w3c.dom.Element> {
    val found = ArrayList<org.w3c.dom.Element>()
    fun walk(element: org.w3c.dom.Element) {
        for (child in children(element)) {
            if (localName(child) == name) found += child
            walk(child)
        }
    }
    if (localName(parent) == name) found += parent
    walk(parent)
    return found
}

/** href 可能是绝对 URL，取它的 path 部分（与 C# NormalizeHrefToPath 一致） */
private fun normalizeHrefToPath(href: String): String {
    if (href.isEmpty()) return "/"
    val lower = href.lowercase()
    if (lower.startsWith("http://") || lower.startsWith("https://")) {
        runCatching { return java.net.URI(href).rawPath ?: href }
    }
    return href
}

private fun decode(value: String): String =
    runCatching { java.net.URLDecoder.decode(value, "UTF-8") }.getOrDefault(value)

/**
 * XXE 防护：远程服务器返回的 XML 一律先拒绝含 DOCTYPE / ENTITY 的文档，
 * 再关校验与 XInclude。逐个 feature 单独 try —— Android 自带的 DOM 不认
 * Apache Xerces 那几个开关，整批设置会抛 ParserConfigurationException，
 * 结果就是「解析出 0 条」（真机上踩过：files=0 但服务器确实回了 207）。
 */
private fun safeParse(xml: String): org.w3c.dom.Element? {
    if (xml.contains("<!DOCTYPE", ignoreCase = true) || xml.contains("<!ENTITY", ignoreCase = true)) {
        return null
    }
    return runCatching {
        val factory = DocumentBuilderFactory.newInstance().apply {
            isNamespaceAware = true
            runCatching { isValidating = false }
            runCatching { isXIncludeAware = false }
            optionally("http://xml.org/sax/features/validation", false)
            optionally("http://apache.org/xml/features/disallow-doctype-decl", true)
            optionally("http://xml.org/sax/features/external-general-entities", false)
            optionally("http://xml.org/sax/features/external-parameter-entities", false)
        }
        factory.newDocumentBuilder().parse(ByteArrayInputStream(xml.toByteArray(Charsets.UTF_8))).documentElement
    }.onFailure { android.util.Log.w("CatClawRemote", "XML 解析失败", it) }.getOrNull()
}

private fun DocumentBuilderFactory.optionally(feature: String, value: Boolean) {
    runCatching { setFeature(feature, value) }
}

private fun textOf(element: org.w3c.dom.Element?): String = element?.textContent?.trim().orEmpty()

private fun JsonObject.str(key: String): String? = this[key]?.jsonPrimitive?.contentOrNull

/** 与 C# `Uri.EscapeDataString` 同用途（塞进 URL userinfo 段）：空格转 %20 而不是 + */
private fun encode(value: String): String =
    java.net.URLEncoder.encode(value, "UTF-8").replace("+", "%20")

private fun JsonObject.boolean(key: String): Boolean = this[key]?.jsonPrimitive?.booleanOrNull ?: false

private fun JsonObject.long(key: String): Long = this[key]?.jsonPrimitive?.longOrNull ?: 0L
