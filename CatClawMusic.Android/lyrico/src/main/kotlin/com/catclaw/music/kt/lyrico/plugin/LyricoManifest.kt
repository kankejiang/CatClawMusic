package com.catclaw.music.kt.lyrico.plugin

import kotlinx.serialization.SerialName
import kotlinx.serialization.Serializable
import kotlinx.serialization.json.Json

/**
 * Lyrico 源插件 manifest.json。
 *
 * 字段取自 Lyrico 官方规范（https://replica0110.github.io/Lyrico/plugins/ ）；
 * 未知字段一律忽略，方便插件在规范前进时仍然能装载。
 */
@Serializable
data class LyricoManifest(
    val id: String = "",
    val name: String = "",
    val author: String = "",
    val description: String = "",
    val versionCode: Int = 0,
    val versionName: String = "",
    /** 插件使用的宿主 API 版本 */
    val apiVersion: Int = 0,
    /** 宿主至少要是这个版本，否则拒绝加载 */
    val minHostApiVersion: Int = 0,
    /** 入口脚本（相对插件根目录） */
    val entry: String = "source.js",
    /** 需要先拼进来的 lib 目录（按相对路径排序后依次前置） */
    val includeDirs: List<String> = emptyList(),
    /** 声明支持的能力：searchSongs / getLyrics / searchCovers */
    val capabilities: List<String> = emptyList(),
    val configFields: List<LyricoConfigField> = emptyList(),
    val icon: String? = null,
    val updateUrl: String? = null,
    val i18n: LyricoI18n? = null,
) {
    val hasGetLyrics: Boolean get() = capabilities.isEmpty() || "getLyrics" in capabilities
    val hasSearchSongs: Boolean get() = capabilities.isEmpty() || "searchSongs" in capabilities
    val hasSearchCovers: Boolean get() = capabilities.isEmpty() || "searchCovers" in capabilities

    companion object {
        /** 当前宿主实现的插件 API 版本（与 Lyrico 对齐到 5） */
        const val HOST_API_VERSION = 5

        private val json = Json {
            ignoreUnknownKeys = true
            isLenient = true
            coerceInputValues = true
        }

        fun parse(text: String): LyricoManifest? =
            runCatching { json.decodeFromString<LyricoManifest>(text) }.getOrNull()
    }
}

@Serializable
data class LyricoConfigField(
    val key: String = "",
    val title: String = "",
    val summary: String = "",
    val group: String = "",
    val type: String = "text",
    val defaultValue: String = "",
)

@Serializable
data class LyricoI18n(
    val defaultLocale: String = "en",
    @SerialName("resources") val resources: Map<String, String> = emptyMap(),
)
