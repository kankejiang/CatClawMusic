package com.catclaw.music.kt.core.model

/**
 * 远程音源连接配置，字段与 C# `ConnectionProfile` 一一对应（DB 表 ConnectionProfiles）。
 * 端口默认值与 C# 不同处已注明：C# 的 5005 是猫爪服务器端口，WebDAV/Subsonic 各自在 UI 层给默认值。
 */
data class ConnectionProfile(
    val id: Int = 0,
    val name: String,
    val protocol: ProtocolType = ProtocolType.WebDAV,
    val host: String = "",
    val port: Int = 0,
    val userName: String = "",
    val password: String = "",
    val basePath: String = "",
    val isEnabled: Boolean = true,
    val apiVersion: String = "1.16.1",
    val clientName: String = "CatClawMusic",
    val useHttps: Boolean = false,
    val domainName: String? = null,
    val shareName: String? = null,
    /** 0 = 标准 WebDAV，1 = OpenList（影响 PROPFIND 深度与列表协议） */
    val serverType: Int = 0,
) {
    val origin: String get() = "${if (useHttps) "https" else "http"}://$host:$port"

    /** 与 C# GetBaseUrl() 同规则：BasePath 去掉尾部斜杠，空或 "/" 时不拼 */
    fun baseUrl(): String {
        val path = basePath.trimEnd('/')
        return if (path.isNotEmpty() && path != "/") "$origin$path" else origin
    }
}
