package com.catclaw.music.kt.core.plugin

import kotlinx.serialization.json.Json
import kotlinx.serialization.json.JsonArray
import kotlinx.serialization.json.JsonElement
import kotlinx.serialization.json.JsonObject
import kotlinx.serialization.json.JsonPrimitive
import kotlinx.serialization.json.booleanOrNull
import kotlinx.serialization.json.buildJsonArray
import kotlinx.serialization.json.buildJsonObject
import kotlinx.serialization.json.intOrNull
import kotlinx.serialization.json.jsonObject
import kotlinx.serialization.json.jsonPrimitive

/**
 * 插件协议 v2 的清单（`plugin.json`）契约层。
 *
 * **为什么要有 v2**：现状是「程序集即插件」——`.ccp` 就是裸 CLR PE，宿主 `Assembly.Load(byte[])`
 * 之后全程走虚方法表（`CatClawMusic.Core/Services/PluginManager.cs:565-583`）。
 * 那条路 Kotlin 侧没有对应物，所以双端通用的唯一解法是**换成交换格式**：
 * 进程外插件 + 换行分隔 JSON。本文件只定"说什么"（清单与协商），不说"怎么说"（见 [PluginProtocol]）。
 *
 * 命名沿用宿主市场 `index.json` 已有的 snake_case 风格（`display_name` / `download_url` / `sha256`），
 * 这样插件作者不必为两端各记一套键名。
 */
data class PluginManifest(
    val protocolVersion: Int,
    val id: String,
    val name: String,
    val version: String,
    val author: String = "",
    val description: String = "",
    val platforms: List<String> = emptyList(),
    val capabilities: List<String> = emptyList(),
    val entrypoint: String = "",
    val minHostVersion: String = "",
    val timeoutMs: Int = DEFAULT_TIMEOUT_MS,
    val sha256: String = "",
    val updateUrl: String = "",
) {
    fun toJson(): JsonElement = buildJsonObject {
        put("protocolVersion", JsonPrimitive(protocolVersion))
        put("id", JsonPrimitive(id))
        put("name", JsonPrimitive(name))
        put("version", JsonPrimitive(version))
        put("author", JsonPrimitive(author))
        put("description", JsonPrimitive(description))
        put("platforms", stringArray(platforms))
        put("capabilities", stringArray(capabilities))
        put("entrypoint", JsonPrimitive(entrypoint))
        put("minHostVersion", JsonPrimitive(minHostVersion))
        put("timeoutMs", JsonPrimitive(timeoutMs))
        put("sha256", JsonPrimitive(sha256))
        put("updateUrl", JsonPrimitive(updateUrl))
    }

    companion object {
        const val SUPPORTED_PROTOCOL = 2
        const val DEFAULT_TIMEOUT_MS = 8_000

        /**
         * 已知能力集合。**不在表里的能力不报错、只是不启用**（见 [validate]）——
         * 新插件带新能力跑在老宿主上时必须能继续提供它老宿主认识的那部分，
         * 否则一次插件升级就能把用户的音源全打挂。
         */
        val KNOWN_CAPABILITIES = setOf("search", "play", "lyrics", "playlist", "fm", "daily", "cover")

        private val JSON = Json { ignoreUnknownKeys = true }

        fun fromJson(text: String): PluginManifest? {
            val obj = runCatching { JSON.parseToJsonElement(text) as? JsonObject }.getOrNull() ?: return null
            fun s(key: String) = (obj[key] as? JsonPrimitive)?.takeIf { it.isString }?.content.orEmpty()
            fun i(key: String, fallback: Int) = (obj[key] as? JsonPrimitive)?.intOrNull ?: fallback
            fun arr(key: String) = (obj[key] as? JsonArray)?.mapNotNull { (it as? JsonPrimitive)?.takeIf { p -> p.isString }?.content }
                ?: emptyList()
            return PluginManifest(
                protocolVersion = i("protocolVersion", 0),
                id = s("id"),
                name = s("name"),
                version = s("version"),
                author = s("author"),
                description = s("description"),
                platforms = arr("platforms"),
                capabilities = arr("capabilities"),
                entrypoint = s("entrypoint"),
                minHostVersion = s("minHostVersion"),
                timeoutMs = i("timeoutMs", DEFAULT_TIMEOUT_MS).coerceIn(1_000, 120_000),
                sha256 = s("sha256"),
                updateUrl = s("updateUrl"),
            )
        }

        private fun stringArray(values: List<String>): JsonElement =
            buildJsonArray { values.forEach { add(JsonPrimitive(it)) } }
    }
}

/** 清单为什么不能用。消息直接进插件管理页，所以要写成给用户/作者看得懂的一句。 */
sealed interface ManifestProblem {
    val message: String

    data object MissingId : ManifestProblem {
        override val message = "缺少 id"
    }

    data object MalformedId : ManifestProblem {
        override val message = "id 只能用小写字母、数字和连字符"
    }

    /** 连 JSON 都读不出来 —— 这一类要在插件管理页单独露出来，否则用户只会看到"装了没反应" */
    data object Malformed : ManifestProblem {
        override val message = "plugin.json 不是合法 JSON"
    }

    data class UnsupportedProtocol(val found: Int) : ManifestProblem {
        override val message = "协议版本 $found 不支持，本宿主只认 ${PluginManifest.SUPPORTED_PROTOCOL}"
    }

    /** 平台不匹配不是"错误"，是"这台机器上跳过"，所以单独一类 */
    data class PlatformUnavailable(val hostPlatform: String) : ManifestProblem {
        override val message = "该插件不提供 $hostPlatform 版本"
    }

    data class HostTooOld(val required: String) : ManifestProblem {
        override val message = "需要宿主 $required 及以上"
    }
}

/**
 * 校验 + 平台过滤。`hostPlatform` 用市场 `index.json` 已经在用的取值
 * （`"android"` / `"windows"`），避免再造一套同义词。
 *
 * 返回 null 表示可用；未知能力不影响可用性，只从 [PluginManifest.capabilities] 里剔掉。
 */
fun PluginManifest.validate(hostPlatform: String): ManifestProblem? {
    if (id.isBlank()) return ManifestProblem.MissingId
    if (!ID_PATTERN.matches(id)) return ManifestProblem.MalformedId
    if (protocolVersion != PluginManifest.SUPPORTED_PROTOCOL) return ManifestProblem.UnsupportedProtocol(protocolVersion)
    if (platforms.isNotEmpty() && platforms.none { it.equals(hostPlatform, ignoreCase = true) }) {
        return ManifestProblem.PlatformUnavailable(hostPlatform)
    }
    return null
}

/** 本清单在这台机器上真正能用到的能力（宿主认识的子集） */
fun PluginManifest.effectiveCapabilities(): Set<String> = capabilities.filter { it in PluginManifest.KNOWN_CAPABILITIES }.toSet()

/**
 * 版本比较（`1.8.0` / `1.8` / `1.8.0-rc1` 都要能比）。
 * 只认「数字段逐位比」，后缀整体忽略 —— 与 C# 侧 `IsNewerVersion` 的宽松程度一致，
 * 严格 semver 反而会让插件作者因为 `-beta` 写法不同而升不了级。
 */
fun compareVersions(a: String, b: String): Int {
    val pa = numericSegments(a)
    val pb = numericSegments(b)
    for (i in 0 until maxOf(pa.size, pb.size)) {
        val x = pa.getOrElse(i) { 0 }
        val y = pb.getOrElse(i) { 0 }
        if (x != y) return if (x > y) 1 else -1
    }
    return 0
}

private fun numericSegments(version: String): List<Int> =
    version.substringBefore('-').split('.').map { it.takeWhile(Char::isDigit).toIntOrNull() ?: 0 }

/** 宿主版本是否满足插件声明的 `minHostVersion`（插件没声明就一律满足） */
fun hostSatisfies(hostVersion: String, manifest: PluginManifest): Boolean =
    manifest.minHostVersion.isBlank() || compareVersions(hostVersion, manifest.minHostVersion) >= 0

private val ID_PATTERN = Regex("^[a-z0-9][a-z0-9-]*$")
