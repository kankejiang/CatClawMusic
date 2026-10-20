package com.catclaw.music.kt.core.plugin

import kotlinx.serialization.json.Json
import kotlinx.serialization.json.JsonArray
import kotlinx.serialization.json.JsonElement
import kotlinx.serialization.json.JsonObject
import kotlinx.serialization.json.JsonPrimitive
import kotlinx.serialization.json.intOrNull
import kotlinx.serialization.json.jsonPrimitive

/**
 * 插件市场清单（`index.json`）的契约层 —— 对应 C# `PluginMarketViewModel.ParseIndex`。
 *
 * 清单由市场仓库（`kankejiang/CatClawMusic.PluginMarket`）的 CI 维护，宿主只读不写。形状：
 * 根对象里 `$meta` 是市场自身信息，**其余每个根键就是一个插件的 `plugin_id`**。字段沿用 snake_case，
 * 与 [PluginManifest] 同一套键名（那边的注释里就点了这件事，只是一直没人实现）。
 *
 * 解析放在 `:core` 而不是界面层：清单格式是**外部契约**，解析错了界面只会表现为「市场是空的」，
 * 纯函数 + 测试才能把这个「空列表」和真正的空市场分开。
 */
data class MarketPlugin(
    /** 根键，形如 `author/name` */
    val pluginId: String,
    val name: String,
    val author: String,
    val displayName: String,
    val description: String,
    val shortDescription: String,
    val repo: String,
    val downloadUrl: String,
    val sha256: String,
    val version: String,
    /** -1 = 清单里没给 */
    val stars: Int = -1,
    val updatedAt: String = "",
    val platforms: List<String> = emptyList(),
    val tags: List<String> = emptyList(),
)

data class MarketIndex(val name: String, val plugins: List<MarketPlugin>)

object PluginMarketRules {

    const val MARKET_REPO = "kankejiang/CatClawMusic.PluginMarket"
    const val DEFAULT_MARKET_NAME = "插件市场"

    /**
     * 清单候选源，顺序即重试顺序：SHA 钉定直链（最新，绕过 jsDelivr 的分支解析 TTL 缓存）
     * → `@main` CDN（国内可达，但可能滞后）→ raw 兜底。与 C# `s_indexUrls` 同一批。
     */
    fun indexUrls(headSha: String = ""): List<String> = buildList {
        if (headSha.isNotBlank()) {
            add("https://cdn.jsdelivr.net/gh/$MARKET_REPO@$headSha/index.json")
        }
        add("https://cdn.jsdelivr.net/gh/$MARKET_REPO@main/index.json")
        add("https://raw.githubusercontent.com/$MARKET_REPO/main/index.json")
    }

    /** 取 main 的 HEAD SHA（拿不到就空着走 @main，属正常降级，不是错误） */
    const val HEAD_SHA_URL = "https://api.github.com/repos/kankejiang/CatClawMusic.PluginMarket/commits/main"

    private val JSON = Json { ignoreUnknownKeys = true }

    /** 从 GitHub commits API 的响应里取 `sha`；解析不出来返回空串 */
    fun headShaFrom(text: String): String {
        val obj = runCatching { JSON.parseToJsonElement(text) as? JsonObject }.getOrNull() ?: return ""
        return (obj["sha"] as? JsonPrimitive)?.takeIf { it.isString }?.content.orEmpty()
    }

    /** 解析清单；不是合法 JSON 或不是对象时返回 null（调用方据此提示「市场加载失败」） */
    fun parseIndex(text: String): MarketIndex? {
        val root = runCatching { JSON.parseToJsonElement(text) as? JsonObject }.getOrNull() ?: return null
        var name = DEFAULT_MARKET_NAME
        val plugins = ArrayList<MarketPlugin>()
        root.forEach { (key, value) ->
            if (key == META_KEY) {
                stringOf(value, "name")?.takeIf { it.isNotBlank() }?.let { name = it }
                return@forEach
            }
            val obj = value as? JsonObject ?: return@forEach
            // display_name 优先、回退 name：清单里两个都有，界面要的是能看的中文名
            val display = stringOf(obj, "display_name")?.takeIf { it.isNotBlank() }
                ?: stringOf(obj, "name").orEmpty()
            plugins += MarketPlugin(
                pluginId = key,
                name = stringOf(obj, "name").orEmpty(),
                author = stringOf(obj, "author").orEmpty(),
                displayName = display,
                description = stringOf(obj, "desc").orEmpty(),
                shortDescription = stringOf(obj, "short_desc").orEmpty(),
                repo = stringOf(obj, "repo").orEmpty(),
                downloadUrl = stringOf(obj, "download_url").orEmpty(),
                sha256 = stringOf(obj, "sha256").orEmpty(),
                version = stringOf(obj, "version").orEmpty(),
                stars = (obj["stars"] as? JsonPrimitive)?.intOrNull ?: -1,
                updatedAt = stringOf(obj, "updated_at").orEmpty(),
                platforms = stringsOf(obj, "platforms"),
                tags = stringsOf(obj, "tags"),
            )
        }
        return MarketIndex(name, plugins)
    }

    /**
     * 已安装的那个是不是同一个插件：作者与插件名都忽略大小写地相等。
     * 与 C# `MarkInstalledStatesAsync` 同一判据 —— 用 `pluginId` 整串比是不行的，
     * 市场那边是 `author/name`、插件自己报的是 `name`。
     */
    fun isInstalled(item: MarketPlugin, installedAuthor: String, installedId: String): Boolean =
        item.author.equals(installedAuthor, ignoreCase = true) && item.name.equals(installedId, ignoreCase = true)

    /**
     * 简版语义化版本比较：`a` 是否比 `b` 新（逐段数字比，缺的段按 0，逐段相等则 false）。
     *
     * 刻意**不**引入 semver 库：清单里的版本号是插件作者手写的，`1.0`、`v1.0.3`、`1.0.3-beta` 都出现过。
     * 非数字段按 0 处理，宁可漏报「可更新」也不要对着一堆后缀字符串给出错误结论。
     */
    fun isNewerVersion(a: String, b: String): Boolean {
        val pa = numericParts(a)
        val pb = numericParts(b)
        for (i in 0 until maxOf(pa.size, pb.size)) {
            val da = pa.getOrElse(i) { 0 }
            val db = pb.getOrElse(i) { 0 }
            if (da != db) return da > db
        }
        return false
    }

    private fun numericParts(version: String): List<Int> =
        version.trim().trimStart('v', 'V').split('.').map { it.trim().toIntOrNull() ?: 0 }

    // Kotlin 字符串里的 $ 要转义，否则 "$meta" 会被当成模板取变量
    private const val META_KEY = "\$meta"

    private fun stringOf(element: JsonElement, key: String): String? {
        val obj = element as? JsonObject ?: return null
        return (obj[key] as? JsonPrimitive)?.takeIf { it.isString }?.content
    }

    private fun stringsOf(obj: JsonObject, key: String): List<String> =
        (obj[key] as? JsonArray)
            ?.mapNotNull { (it as? JsonPrimitive)?.takeIf { p -> p.isString }?.content }
            .orEmpty()
}
