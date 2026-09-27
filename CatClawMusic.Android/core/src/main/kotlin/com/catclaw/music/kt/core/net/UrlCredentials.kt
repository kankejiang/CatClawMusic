package com.catclaw.music.kt.core.net

import java.util.Base64

/**
 * URL 内嵌凭据（`http://user:pass@host/…`）的统一处理。
 *
 * WebDAV 的播放/下载地址沿用 C# `BuildStreamUrl` 的形态，但无论 `HttpURLConnection`
 * 还是 ExoPlayer 的默认 HTTP 栈都**不会**把 userinfo 变成 `Authorization` 头 ——
 * 真机表现是每一次请求都 401。取流与下载两条路都走这里，避免各修一遍。
 */
object UrlCredentials {

    /** @return 去掉凭据的 URL，以及该带的 Basic 头（无 userinfo 时头为 null） */
    fun split(url: String): Pair<String, String?> {
        val schemeEnd = url.indexOf("://")
        if (schemeEnd < 0) return url to null
        val authorityStart = schemeEnd + 3
        val authorityEnd = url.indexOf('/', authorityStart).let { if (it < 0) url.length else it }
        val at = url.lastIndexOf('@', authorityEnd - 1)
        if (at <= authorityStart) return url to null

        val userInfo = url.substring(authorityStart, at)
        val decoded = percentDecode(userInfo)
        val credential = decoded.substringBefore(':') + ":" + decoded.substringAfter(':', "")
        val header = "Basic " + Base64.getEncoder().encodeToString(
            credential.toByteArray(Charsets.UTF_8),
        )
        return url.substring(0, authorityStart) + url.substring(at + 1) to header
    }

    /** 只解 `%XX`：URLDecoder 会把 `+` 当空格，密码里的加号不能被改坏 */
    fun percentDecode(value: String): String {
        if (!value.contains('%')) return value
        val out = StringBuilder(value.length)
        var i = 0
        while (i < value.length) {
            val c = value[i]
            if (c == '%' && i + 2 < value.length) {
                val hex = value.substring(i + 1, i + 3)
                val byte = hex.toIntOrNull(16)
                if (byte != null) {
                    // 连续 %XX 先攒成字节串，才能还原多字节字符（如中文）
                    val bytes = ArrayList<ByteArray>()
                    bytes.add(byteOf(byte))
                    var j = i + 3
                    while (j + 2 < value.length && value[j] == '%') {
                        val next = value.substring(j + 1, j + 3).toIntOrNull(16) ?: break
                        bytes.add(byteOf(next))
                        j += 3
                    }
                    out.append(bytes.fold(ByteArray(0)) { acc, b -> acc + b }.toString(Charsets.UTF_8))
                    i = j
                    continue
                }
            }
            out.append(c)
            i++
        }
        return out.toString()
    }

    private fun byteOf(value: Int): ByteArray = byteArrayOf(value.toByte())
}
