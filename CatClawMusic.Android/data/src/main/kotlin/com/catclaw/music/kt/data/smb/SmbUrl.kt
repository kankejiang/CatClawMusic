package com.catclaw.music.kt.data.smb

import com.catclaw.music.kt.core.net.UrlCredentials

/** `smb://[user[:pass]@]host[:port]/share/path/to/file` 解析结果 */
data class SmbTarget(
    val host: String,
    val port: Int = SMB_DEFAULT_PORT,
    val user: String = "",
    val password: String = "",
    val domain: String = "",
    val share: String = "",
    /** 共享内的相对路径，分隔符已换成反斜杠且以 `\` 开头（与 C# 一致） */
    val remotePath: String = "\\",
) {
    val serviceKey: String get() = "$host|$port|$share|$user"
}

const val SMB_DEFAULT_PORT = 445

/** RFC 3986 未保留字符之外一律 %XX；与 `UrlCredentials.percentDecode` 互为逆运算 */
internal fun percentEncode(value: String): String = buildString {
    for (b in value.toByteArray(Charsets.UTF_8)) {
        val c = b.toInt() and 0xFF
        if ((c in 'a'.code..'z'.code) || (c in 'A'.code..'Z'.code) || (c in '0'.code..'9'.code) ||
            c == '-'.code || c == '_'.code || c == '.'.code
        ) {
            append(c.toChar())
        } else {
            append('%').append(c.toString(16).uppercase().padStart(2, '0'))
        }
    }
}

/**
 * 手工切分而不是 `java.net.URI`：后者对非 ASCII（中文共享路径）直接抛 `URISyntaxException`，
 * 而 .NET 的 `Uri` 是宽松的 —— C# 这条路能吃的 URL，这边也必须能吃。
 * 解析失败一律返回 null，不抛（与 C# `TryParseSmbUrl` 的 catch 同形）。
 */
fun parseSmbUrl(smbUrl: String): SmbTarget? = runCatching {
    if (!smbUrl.startsWith("smb://", ignoreCase = true)) return null
    val rest = smbUrl.substring("smb://".length)
    val slash = rest.indexOf('/')
    val authority = if (slash < 0) rest else rest.substring(0, slash)
    val rawPath = if (slash < 0) "" else rest.substring(slash + 1)

    val at = authority.lastIndexOf('@')
    val userInfo = if (at < 0) "" else authority.substring(0, at)
    val hostPort = if (at < 0) authority else authority.substring(at + 1)

    val colon = hostPort.lastIndexOf(':')
    val host = if (colon > 0) hostPort.substring(0, colon) else hostPort
    if (host.isEmpty()) return null
    var port = SMB_DEFAULT_PORT
    if (colon > 0 && colon < hostPort.length - 1) {
        val parsed = hostPort.substring(colon + 1).toIntOrNull() ?: return null
        // C#: uri.Port > 0 && uri.Port != 445 才覆盖 —— 「非默认端口才记」
        if (parsed > 0 && parsed != SMB_DEFAULT_PORT) port = parsed
    }

    // C# 对 UserInfo 先 UnescapeDataString，再按**第一个**冒号切 user/pass
    val decodedUser = UrlCredentials.percentDecode(userInfo)
    val sep = decodedUser.indexOf(':')
    val user: String
    val password: String
    if (sep > 0) {
        user = decodedUser.substring(0, sep)
        password = decodedUser.substring(sep + 1)
    } else {
        user = decodedUser
        password = ""
    }

    val decodedPath = UrlCredentials.percentDecode(rawPath)
    val firstSlash = decodedPath.indexOf('/')
    val share: String
    val remotePath: String
    if (firstSlash < 0) {
        share = decodedPath
        remotePath = "\\"
    } else {
        share = decodedPath.substring(0, firstSlash)
        remotePath = decodedPath.substring(firstSlash).replace('/', '\\')
    }
    if (share.isEmpty()) return null

    SmbTarget(host = host, port = port, user = user, password = password, share = share, remotePath = remotePath)
}.getOrNull()
