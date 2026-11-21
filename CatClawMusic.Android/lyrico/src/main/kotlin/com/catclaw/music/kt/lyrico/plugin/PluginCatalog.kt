package com.catclaw.music.kt.lyrico.plugin

import java.security.MessageDigest
import kotlinx.serialization.json.Json
import kotlinx.serialization.json.JsonArray
import kotlinx.serialization.json.JsonObject
import kotlinx.serialization.json.JsonPrimitive
import kotlinx.serialization.json.contentOrNull

/** 在线 catalog 里的一个可安装插件。 */
data class CatalogPlugin(
    val id: String,
    val name: String,
    val versionCode: Int,
    val versionName: String,
    val apiVersion: Int,
    val minHostApiVersion: Int,
    val downloadUrl: String,
    /** 上游给的包摘要，用于下载后校验；空表示未提供 */
    val sha256: String,
    val size: Long,
    val changelog: String,
)

/** catalog 拉取结果。 */
sealed interface CatalogResult {
    data class Success(
        val plugins: List<CatalogPlugin>,
        /** 因宿主 API 不兼容而不可安装的数量，界面可提示 */
        val incompatible: Int,
    ) : CatalogResult

    data class Failed(val message: String) : CatalogResult
}

/**
 * 解析 Lyrico 官方插件的发行清单。
 *
 * ## 上游格式有过变更（实测）
 *
 * 旧格式 `catalog.json`：
 * ```json
 * { "schemaVersion": 1,
 *   "plugins": [ { "id", "name", "versionCode", "versionName", "apiVersion",
 *                  "minHostApiVersion", "downloadUrl", "sha256", "size", "changelog" } ] }
 * ```
 *
 * **当前实际上游**是 `release-manifest.json`：
 * ```json
 * { "generatedAt": "...",
 *   "plugins": [ { "id", "name", "versionCode", "versionName", "root", "asset" } ],
 *   "assets": [ "<zip 文件名>" ] }
 * ```
 * 新格式**没有 downloadUrl / sha256 / minHostApiVersion** —— 下载地址要按
 * 「release tag + asset 文件名」拼出来。
 *
 * 两种都支持：先按新格式找（有 `asset` 字段），找不到再按旧格式找（有 `downloadUrl`）。
 * 只支持一种会在上游改格式时整个功能失效——本轮就是这样发现问题的。
 */
object PluginCatalogParser {

    const val SUPPORTED_SCHEMA_VERSION = 1

    private val json = Json { ignoreUnknownKeys = true; isLenient = true }

    /**
     * @param baseUrl 清单里 asset 是相对文件名时，用来拼下载地址的前缀
     *                （形如 `https://.../releases/download/v0.5.0/`）。
     *                通常由调用方用「清单 URL 去掉文件名」得到。
     */
    fun parse(
        raw: String,
        hostApiVersion: Int = LyricoManifest.HOST_API_VERSION,
        baseUrl: String = "",
    ): CatalogResult {
        val root = runCatching { json.parseToJsonElement(raw) as? JsonObject }.getOrNull()
            ?: return CatalogResult.Failed("catalog 不是合法 JSON 对象")

        // schemaVersion 只出现在旧格式里；新格式没有这个字段，不能因此拒绝
        val schema = (root["schemaVersion"] as? JsonPrimitive)?.contentOrNull?.toIntOrNull()
        if (schema != null && schema > SUPPORTED_SCHEMA_VERSION) {
            return CatalogResult.Failed("catalog 版本 $schema 高于支持版本 $SUPPORTED_SCHEMA_VERSION")
        }
        val arr = root["plugins"] as? JsonArray
            ?: return CatalogResult.Failed("catalog 缺少 plugins 数组")

        var incompatible = 0
        val plugins = arr.mapNotNull { el ->
            val obj = el as? JsonObject ?: return@mapNotNull null
            val id = obj.str("id")?.takeIf { it.isNotBlank() } ?: return@mapNotNull null

            // 下载地址：旧格式直接给 downloadUrl；新格式给 asset 文件名，需要拼 baseUrl
            val asset = obj.str("asset")?.takeIf { it.isNotBlank() }
            val url = obj.str("downloadUrl")?.takeIf { it.isNotBlank() }
                ?: asset?.let { a ->
                    if (a.startsWith("http")) a
                    else if (baseUrl.isNotBlank()) baseUrl.trimEnd('/') + "/" + a
                    else null
                }
                ?: return@mapNotNull null

            val minHost = obj.int("minHostApiVersion") ?: 1
            if (minHost > hostApiVersion) {
                incompatible++
                return@mapNotNull null
            }
            CatalogPlugin(
                id = id,
                name = obj.str("name") ?: id,
                versionCode = obj.int("versionCode") ?: 0,
                versionName = obj.str("versionName").orEmpty(),
                apiVersion = obj.int("apiVersion") ?: 0,
                minHostApiVersion = minHost,
                downloadUrl = url,
                // 新格式不提供摘要：留空，下载时按「未校验」处理并如实告知用户
                sha256 = obj.str("sha256").orEmpty().lowercase(),
                size = obj.long("size") ?: 0L,
                changelog = obj.str("changelog").orEmpty(),
            )
        }
        if (plugins.isEmpty() && arr.isNotEmpty()) {
            return CatalogResult.Failed("清单里没有可用的插件条目（可能是上游格式又变了）")
        }
        return CatalogResult.Success(plugins, incompatible)
    }

    private fun JsonObject.str(key: String): String? =
        (this[key] as? JsonPrimitive)?.contentOrNull

    private fun JsonObject.int(key: String): Int? =
        (this[key] as? JsonPrimitive)?.contentOrNull?.toIntOrNull()

    private fun JsonObject.long(key: String): Long? =
        (this[key] as? JsonPrimitive)?.contentOrNull?.toLongOrNull()
}

/** 已装版本与新版本的关系。 */
enum class PluginUpdateStatus {
    /** 未安装 */
    NOT_INSTALLED,
    /** 已是最新（versionCode 相同或更高） */
    UP_TO_DATE,
    /** 有更新 */
    UPDATE_AVAILABLE,
}

/** 把 catalog 与已装列表比对，得出每个插件该「安装」还是「更新」。 */
object PluginUpdateChecker {

    fun statusOf(
        catalog: CatalogPlugin,
        installedVersionCode: Int?,
    ): PluginUpdateStatus = when {
        installedVersionCode == null -> PluginUpdateStatus.NOT_INSTALLED
        catalog.versionCode > installedVersionCode -> PluginUpdateStatus.UPDATE_AVAILABLE
        else -> PluginUpdateStatus.UP_TO_DATE
    }

    /** 下载后校验摘要。上游没给 sha256 时**不当作校验通过**，而是明确「未校验」。 */
    fun verify(bytes: ByteArray, expectedSha256: String): Boolean {
        if (expectedSha256.isBlank()) return true
        val actual = MessageDigest.getInstance("SHA-256")
            .digest(bytes)
            .joinToString("") { "%02x".format(it) }
        return actual.equals(expectedSha256, ignoreCase = true)
    }
}
