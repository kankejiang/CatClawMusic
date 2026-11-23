package com.catclaw.music.kt.lyrico.plugin

import com.catclaw.music.kt.core.diagnostic.AppLog
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.withContext
import kotlinx.coroutines.withTimeoutOrNull
import okhttp3.OkHttpClient
import okhttp3.Request

/**
 * 在线插件目录（catalog）客户端。
 *
 * 只做「取 JSON」「下载包」两件事，解析与比对分别在
 * [PluginCatalogParser] / [PluginUpdateChecker] 里，便于单测。
 *
 * 安全：下载后若 catalog 提供了 sha256 就必须匹配；不匹配直接拒绝安装，
 * 因为这意味着包在传输中被改过（或上游挂错文件）。
 */
class PluginCatalogClient(
    private val okHttpClient: OkHttpClient,
    /**
     * 拉取/下载的总预算。
     *
     * 原来是 20s：太紧。GitHub 的 release 资产（`release-assets.githubusercontent.com`）
     * 从国内网络直连经常超过 20 秒（实测 lrclib 单请求就要 8~20s，GitHub 资产更慢），
     * 超时后用户看到的就是「无法连接订阅源」，但其实是还没下完。
     * 放宽到 45s，与 LyricoHttpClient 的 callTimeout 对齐。
     */
    private val timeoutMs: Long = 45_000L,
) {

    /** 拉取并解析 catalog。 */
    suspend fun fetch(catalogUrl: String): CatalogResult = withContext(Dispatchers.IO) {
        val started = System.currentTimeMillis()
        val body = runCatching {
            withTimeoutOrNull(timeoutMs) { httpGet(catalogUrl) }
        }.onFailure {
            // 原来失败时只返回「无法连接订阅源」一句笼统提示，用户/日志都看不出原因。
            // 实测这类失败可能是超时、DNS 不通、或 HTTP 非 200，必须区分。
            AppLog.w(
                "LyricoCatalog",
                "拉取 catalog 失败（${System.currentTimeMillis() - started}ms）：" +
                    "${it.javaClass.simpleName}: ${it.message}",
                it,
            )
        }.getOrNull() ?: return@withContext CatalogResult.Failed(
            "无法连接订阅源（GitHub 资产在国内网络下常超时，检查网络或 VPN 后重试）",
        )

        // httpGet 内部吞掉了非 200 的情况，单独留一行好定位
        val got = System.currentTimeMillis() - started
        AppLog.d("LyricoCatalog", "拉取 catalog 成功（${got}ms，${body.length} 字符）")

        if (body.isBlank()) return@withContext CatalogResult.Failed("订阅源返回空内容")
        // 清单里的 asset 是相对文件名，下载地址 = 清单所在目录 + 文件名
        val baseUrl = catalogUrl.substringBeforeLast('/', missingDelimiterValue = "")
        PluginCatalogParser.parse(body, baseUrl = baseUrl)
    }

    /** 下载插件包并校验摘要。 */
    suspend fun download(plugin: CatalogPlugin): PluginDownloadOutcome =
        withContext(Dispatchers.IO) {
            val bytes = runCatching {
                withTimeoutOrNull(timeoutMs) { httpGetBytes(plugin.downloadUrl) }
            }.getOrNull() ?: return@withContext PluginDownloadOutcome.Failed("下载失败")

            if (bytes.isEmpty()) return@withContext PluginDownloadOutcome.Failed("下载内容为空")
            if (!PluginUpdateChecker.verify(bytes, plugin.sha256)) {
                return@withContext PluginDownloadOutcome.ChecksumMismatch
            }
            PluginDownloadOutcome.Success(bytes, verified = plugin.sha256.isNotBlank())
        }

    private fun httpGet(url: String): String? = runCatching {
        val request = Request.Builder().url(url).header("User-Agent", USER_AGENT).build()
        okHttpClient.newCall(request).execute().use { resp ->
            if (!resp.isSuccessful) {
                AppLog.w("LyricoCatalog", "订阅源 HTTP ${resp.code}: $url")
                null
            } else {
                resp.body?.string()
            }
        }
    }.onFailure {
        AppLog.w("LyricoCatalog", "订阅源请求异常：${it.javaClass.simpleName}: ${it.message}")
        null
    }.getOrNull()

    private fun httpGetBytes(url: String): ByteArray? = runCatching {
        val request = Request.Builder().url(url).header("User-Agent", USER_AGENT).build()
        okHttpClient.newCall(request).execute().use { resp ->
            if (!resp.isSuccessful) {
                AppLog.w("LyricoCatalog", "订阅源下载 HTTP ${resp.code}: $url")
                null
            } else {
                resp.body?.bytes()
            }
        }
    }.onFailure {
        AppLog.w("LyricoCatalog", "订阅源下载异常：${it.javaClass.simpleName}: ${it.message}")
        null
    }.getOrNull()

    companion object {
        private const val USER_AGENT = "CatClawMusic/1.0"

        /**
         * Lyrico 官方插件的默认订阅地址。
         *
         * 上游已从 `catalog.json` 改名为 `release-manifest.json`（实测 latest 会 404），
         * 且新版清单里只有 `asset` 文件名、没有完整下载地址，所以要靠清单 URL 的
         * 目录部分去拼（见 [PluginCatalogParser.parse] 的 baseUrl 参数）。
         */
        const val DEFAULT_CATALOG_URL =
            "https://github.com/Replica0110/Lyrico-Plugins/releases/latest/download/release-manifest.json"
    }
}

/** 下载结果。 */
sealed interface PluginDownloadOutcome {
    /** [verified] 表示有没有做过摘要校验（上游未提供 sha256 时为 false，不代表失败） */
    data class Success(val bytes: ByteArray, val verified: Boolean) : PluginDownloadOutcome

    /** 摘要不匹配：包可能被篡改，必须拒绝 */
    data object ChecksumMismatch : PluginDownloadOutcome

    data class Failed(val message: String) : PluginDownloadOutcome
}
