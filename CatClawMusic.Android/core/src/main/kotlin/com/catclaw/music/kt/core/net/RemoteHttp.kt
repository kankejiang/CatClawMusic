package com.catclaw.music.kt.core.net

import java.security.SecureRandom
import java.security.cert.X509Certificate
import java.util.concurrent.TimeUnit
import javax.net.ssl.SSLContext
import javax.net.ssl.X509TrustManager
import okhttp3.OkHttpClient
import okhttp3.Request

/**
 * 远程音乐（WebDAV / SMB 代理 / 自建服务）统一的 HTTP 客户端。
 *
 * ## 为什么要有这一个文件
 *
 * NAS 上的 WebDAV 基本都是自签证书（群晖 / QNAP / 极空间 / Trim 实测都是），
 * Android 的默认信任库必然校验失败。而 OkHttp 与 `HttpURLConnection` 各建一个 client，
 * 于是「信任自签证书」这件事被迫在**每一条用到远程音乐的链路**上各写一遍：
 *
 * | 链路 | 曾经的写法 |
 * | --- | --- |
 * | 列目录 / 导入 | `data/remote/Http.kt` 自己的 trust-all client |
 * | 播放取流 | `core/playback/RemoteAuthDataSources` 自己的 |
 * | 补元数据取头 | `data/media/AudioBytesReader` 自己的 |
 * | 侧车歌词 | `data/lyrics/OkHttpBytesFetcher` **漏了** ← 症状：远程有 .lrc 但永远不显示 |
 * | 封面 | `ui/player/CoverArt` 用 `HttpURLConnection` **漏了** ← 症状：远程封面永远首字母 |
 *
 * 漏掉一条就是一个静默失败的功能点，而每新增一条链路都会再漏一次 —— 2026-11-15 那轮
 * 就是连着修了三条才发现还有第四条。所以收敛成**唯一入口**：新链路只准从这里拿 client。
 *
 * ## 安全边界
 *
 * 这个 client 只服务**用户自己显式配置的私有服务器**（`ConnectionProfiles` 里的那条）。
 * 公网接口（网易云、LLM 服务商、GitHub）一律走各自带正常校验的 client，不进这里。
 * 这一点是刻意的：私有 NAS 的证书没法进系统信任库，而公网链路放弃校验等于开门。
 */
object RemoteHttp {

    /** 只信任「用户自己填的那个服务器」—— 因此不能对公网复用 */
    private val trustAllManager: X509TrustManager = object : X509TrustManager {
        override fun checkClientTrusted(chain: Array<out X509Certificate>?, authType: String?) = Unit
        override fun checkServerTrusted(chain: Array<out X509Certificate>?, authType: String?) = Unit
        override fun getAcceptedIssuers(): Array<X509Certificate> = emptyArray()
    }

    private val sslSocketFactory by lazy {
        SSLContext.getInstance("TLS")
            .apply { init(null, arrayOf(trustAllManager), SecureRandom()) }
            .socketFactory
    }

    /**
     * 单例：OkHttp 的连接池 / 线程池是按 client 实例复用的，多建一个就多一套连接，
     * 自动补全时 4 worker × 3 次 Range 能瞬间打出几十条 TLS 握手。
     */
    val client: OkHttpClient by lazy {
        builder()
            // WebDAV 的 302 会跨 http/https（明文的 CDN 跳转），交给 OkHttp 自己跟
            .followRedirects(true)
            .followSslRedirects(true)
            .readTimeout(30, TimeUnit.SECONDS)
            // 强制 HTTP/1.1：这台 NAS 在 HTTP/2 下「带 Range + Authorization 的 GET」
            // 会回 401（同 URL 同头走 1.1 是 206）。1.1 对 WebDAV 也本来就更稳 ——
            // 很多 NAS 的 h2 实现连 PROPFIND 都不支持。
            .protocols(listOf(okhttp3.Protocol.HTTP_1_1))
            .build()
    }

    /**
     * 已套好自签证书策略的 builder，供需要**自定义重定向**的调用方使用。
     *
     * 目前只有 WebDAV 的 PROPFIND 需要：它必须 `followRedirects(false)` 手工按方法语义重放，
     * 因为 OkHttp 默认会把 302 后的 PROPFIND 降级成 GET（多数 NAS 的常见坑）。
     * 其余链路一律用 [client]，不要另起。
     */
    fun builder(): OkHttpClient.Builder = OkHttpClient.Builder()
        .sslSocketFactory(sslSocketFactory, trustAllManager)
        .hostnameVerifier { _, _ -> true }
        .connectTimeout(10, TimeUnit.SECONDS)
        // 补封面/歌词时是「一串小请求」，长连接能省掉大部分握手
        .retryOnConnectionFailure(true)

    /**
     * 组一个请求：URL 里带 `user:pass@` 时摘掉并换成 `Authorization: Basic`。
     *
     * 这件事**必须**在这里做。`java.net` 与 OkHttp 都不会替调用方把 userinfo 变成凭据，
     * 表现是「地址明明对、每次都 401」。本项目在取流、下载、歌词、封面四条路上都踩过。
     *
     * @param rangeBytes 非空则加 `Range: bytes=0-(n-1)`，用于只取标签区
     */
    fun request(url: String, rangeBytes: Int? = null): Request {
        val (clean, authorization) = UrlCredentials.split(url)
        return Request.Builder().url(clean).apply {
            if (authorization != null) header("Authorization", authorization)
            if (rangeBytes != null && rangeBytes > 0) header("Range", "bytes=0-${rangeBytes - 1}")
        }.build()
    }
}
