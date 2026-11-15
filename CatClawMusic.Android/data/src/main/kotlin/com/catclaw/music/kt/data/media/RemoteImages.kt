package com.catclaw.music.kt.data.media

import com.catclaw.music.kt.core.diagnostic.AppLog
import com.catclaw.music.kt.core.net.RemoteHttp

/**
 * 远程图片字节的唯一取口：封面 URL 与**音频内嵌封面**都从这里拿。
 *
 * 为什么不把这些逻辑写在 UI 层（`CoverArt`）：那里要自己 new 一个 OkHttpClient、
 * 自己处理自签证书、自己拼 Authorization 头 —— 正是这个模式让「封面」与「侧车歌词」
 * 两条链路各自漏掉了一整套 TLS 配置。UI 只该拿到 `ByteArray?`，然后解码。
 *
 * 对象而非注入：`CoverArt` 是纯 Composable，走 Hilt 注入要把整条链变成
 * `@Composable` 依赖，收益不抵复杂度。这里没有可变状态，单例安全。
 */
object RemoteImages {

    /**
     * 取一个远程图片 URL 的全部字节（侧车封面 `.jpg/.png` 用）。
     *
     * WebDAV 的封面 URL 形如 `http://user:pass@host/dav/cover.jpg`：
     * `HttpURLConnection` 不会把 userinfo 变成 `Authorization` 头（真机上是每次都 401），
     * 而且它也没地方塞自定义 TrustManager。这两件事 [RemoteHttp] 已经统一处理。
     */
    fun fetchBytes(url: String): ByteArray? = runCatching {
        RemoteHttp.client.newCall(RemoteHttp.request(url)).execute().use { response ->
            if (!response.isSuccessful) null else response.body?.bytes()
        }
    }.getOrNull()

    /**
     * 从**远程音频文件**里抽内嵌封面（FLAC 的 PICTURE / MP3 的 APIC）。
     *
     * 三步：头部窗口定位 → 窗口不够就精确 Range 图体 → 解出 JPEG/PNG 字节。
     * 绝不为了封面把整首歌拉一遍（无损单曲常有 80 MB）。
     *
     * @return 图片原始字节；没有内嵌图或取不到都回 null（调用方保留首字母占位）
     */
    fun fetchEmbeddedCover(url: String): ByteArray? = runCatching {
        val head = RemoteHttp.client
            .newCall(RemoteHttp.request(url, TagPicture.HEAD_WINDOW))
            .execute().use { response ->
                if (!response.isSuccessful) null else response.body?.bytes()
            } ?: return null

        val location = TagPicture.locate(head)
        if (location == null) {
            // Ogg 的图是注释包里的 base64，就在头部窗口内
            return TagPicture.extract(head)?.bytes
        }
        if (location.end <= head.size) {
            return TagPicture.extract(head)?.bytes
        }
        if (location.length <= 0 || location.length > TagPicture.MAX_COVER_BYTES) return null

        // 只补取尾部没覆盖的那段，拼起来就是 [0, end) 的连续字节
        val tail = RemoteHttp.client
            .newCall(
                RemoteHttp.request(url).newBuilder()
                    .header("Range", "bytes=${head.size}-${location.end - 1}")
                    .build(),
            )
            .execute().use { response ->
                if (!response.isSuccessful) null else response.body?.bytes()
            } ?: return null
        TagPicture.extract(head + tail)?.bytes
    }.onFailure {
        AppLog.w("CatClawCover", "embedded cover failed ${url.substringAfterLast('/')}: ${it.message}")
    }.getOrNull()
}
