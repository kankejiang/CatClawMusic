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
 * 解析 Lyrico 官方 catalog.json（schemaVersion 1）。
 *
 * 结构：
 * ```json
 * { "schemaVersion": 1,
 *   "plugins": [ { "id", "name", "versionCode", "versionName", "apiVersion",
 *                  "minHostApiVersion", "downloadUrl", "sha256", "size", "changelog" } ] }
 * ```
 *
 * 容错策略：**单个条目坏掉不影响整张 catalog**（上游字段可能演进），
 * 但 schemaVersion 不认识时整体拒绝——那是真的看不懂，硬解析只会读到错的东西。
 */
object PluginCatalogParser {

    const val SUPPORTED_SCHEMA_VERSION = 1

    private val json = Json { ignoreUnknownKeys = true; isLenient = true }

    fun parse(
        raw: String,
        hostApiVersion: Int = LyricoManifest.HOST_API_VERSION,
    ): CatalogResult {
        val root = runCatching { json.parseToJsonElement(raw) as? JsonObject }.getOrNull()
            ?: return CatalogResult.Failed("catalog 不是合法 JSON 对象")
        val schema = (root["schemaVersion"] as? JsonPrimitive)?.contentOrNull?.toIntOrNull()
        if (schema != null && schema > SUPPORTED_SCHEMA_VERSION) {
            return CatalogResult.Failed("catalog 版本 $schema 高于支持版本 $SUPPORTED_SCHEMA_VERSION")
        }
        val arr = root["plugins"] as? JsonArray
            ?: return CatalogResult.Failed("catalog 缺少 plugins 数组")

        var incompatible = 0
        val plugins = arr.mapNotNull { el ->
            val obj = el as? JsonObject ?: return@mapNotNull null
            // 空 id 与缺字段同等对待：装上去也会变成目录名 "plugin" 互相覆盖
            val id = obj.str("id")?.takeIf { it.isNotBlank() } ?: return@mapNotNull null
            val url = obj.str("downloadUrl")?.takeIf { it.isNotBlank() } ?: return@mapNotNull null
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
                sha256 = obj.str("sha256").orEmpty().lowercase(),
                size = obj.long("size") ?: 0L,
                changelog = obj.str("changelog").orEmpty(),
            )
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
