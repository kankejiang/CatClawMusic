package com.catclaw.music.kt.data.ai

import android.content.Context
import com.catclaw.music.kt.core.ai.AgentRunSettings
import com.catclaw.music.kt.core.ai.LlmConfig
import dagger.hilt.android.qualifiers.ApplicationContext
import javax.inject.Inject
import javax.inject.Singleton
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.serialization.json.Json
import kotlinx.serialization.json.JsonArray
import kotlinx.serialization.json.JsonElement
import kotlinx.serialization.json.JsonObject
import kotlinx.serialization.json.JsonPrimitive
import kotlinx.serialization.json.booleanOrNull
import kotlinx.serialization.json.buildJsonArray
import kotlinx.serialization.json.doubleOrNull
import kotlinx.serialization.json.intOrNull
import kotlinx.serialization.json.jsonObject
import kotlinx.serialization.json.jsonPrimitive

/**
 * 多模型配置存储，键与语义对齐 C# `AgentService`：
 * 整个列表 JSON 化存 `all_configs`，当前生效项按 `current_config_name` 索引。
 *
 * 本项目没上 kotlinx 序列化插件（队列快照也是手写格式），所以这里手工拼 JSON ——
 * 与 `SubsonicClient` / `CatClawServerClient` 的读法保持同一套风格。
 */
@Singleton
class LlmConfigStore @Inject constructor(
    @ApplicationContext context: Context,
) {
    private val prefs = context.getSharedPreferences(PREFS, Context.MODE_PRIVATE)

    private val _configs = MutableStateFlow(loadAll())
    val configs: StateFlow<List<LlmConfig>> = _configs.asStateFlow()

    private val _currentName = MutableStateFlow(prefs.getString(KEY_CURRENT, DEFAULT_NAME) ?: DEFAULT_NAME)
    val currentName: StateFlow<String> = _currentName.asStateFlow()

    /** 当前生效配置：按名字找，找不到回落到默认值（与 C# `LoadConfig` 的兜底同形） */
    fun current(): LlmConfig =
        _configs.value.firstOrNull { it.name == _currentName.value } ?: LlmConfig()

    fun fallbacks(): List<LlmConfig> =
        _configs.value.filter { it.fallbackEnabled && it.enabled && it.name != _currentName.value }

    /** 对齐 C# `AgentService.IsConfigured`：启用 + 有地址 + 有密钥三者齐备才走云端 */
    fun isConfigured(): Boolean = current().let { it.enabled && it.apiUrl.isNotBlank() && it.apiKey.isNotBlank() }

    fun saveAll(list: List<LlmConfig>) {
        _configs.value = list
        prefs.edit().putString(KEY_ALL, encode(list)).apply()
    }

    fun save(config: LlmConfig) {
        val list = _configs.value.toMutableList()
        val index = list.indexOfFirst { it.name == config.name }
        if (index >= 0) list[index] = config else list.add(config)
        saveAll(list)
        setCurrentName(config.name)
    }

    fun delete(name: String) {
        saveAll(_configs.value.filterNot { it.name == name })
        if (_currentName.value == name) setCurrentName(DEFAULT_NAME)
    }

    fun setCurrentName(name: String) {
        _currentName.value = name
        prefs.edit().putString(KEY_CURRENT, name).apply()
    }

    var maxToolRounds: Int
        get() = prefs.getInt(KEY_ROUNDS, AgentRunSettings.DEFAULT_MAX_TOOL_ROUNDS)
        set(value) = prefs.edit().putInt(KEY_ROUNDS, value).apply()

    var reasoningEffort: String
        get() = prefs.getString(KEY_EFFORT, AgentRunSettings.DEFAULT_REASONING_EFFORT) ?: AgentRunSettings.DEFAULT_REASONING_EFFORT
        set(value) = prefs.edit().putString(KEY_EFFORT, value).apply()

    private fun loadAll(): List<LlmConfig> = runCatching {
        val text = prefs.getString(KEY_ALL, "[]") ?: "[]"
        (JSON.parseToJsonElement(text) as? JsonArray)?.map { element -> decode(element.jsonObject) }
            ?: emptyList()
    }.getOrDefault(emptyList())

    private fun decode(obj: JsonObject): LlmConfig = readConfigJson(obj)

    private fun encode(list: List<LlmConfig>): String {
        val array: JsonElement = buildJsonArray { list.forEach { c -> add(encodeConfigJson(c)) } }
        return JSON.encodeToString(JsonElement.serializer(), array)
    }

    companion object {
        /**
         * 单份配置的 JSON 形状只有一个定义处：设置页存盘、备份文件打包都走这两个函数，
         * 否则「备份里能读回来的配置」和「App 自己存的配置」会各自漂移。
         */
        fun encodeConfigJson(c: LlmConfig): JsonObject = JsonObject(
            buildMap<String, JsonElement> {
                put("name", JsonPrimitive(c.name))
                put("provider", JsonPrimitive(c.provider))
                put("apiUrl", JsonPrimitive(c.apiUrl))
                put("apiKey", JsonPrimitive(c.apiKey))
                put("model", JsonPrimitive(c.model))
                put("temperature", JsonPrimitive(c.temperature))
                put("maxTokens", JsonPrimitive(c.maxTokens))
                put("enabled", JsonPrimitive(c.enabled))
                put("fallbackEnabled", JsonPrimitive(c.fallbackEnabled))
                put("reasoningEffort", JsonPrimitive(c.reasoningEffort))
                put("topP", JsonPrimitive(c.topP))
                put("frequencyPenalty", JsonPrimitive(c.frequencyPenalty))
                put("presencePenalty", JsonPrimitive(c.presencePenalty))
                put("responseFormat", JsonPrimitive(c.responseFormat))
                put("maxCompletionTokens", JsonPrimitive(c.maxCompletionTokens))
                put("contextCaching", JsonPrimitive(c.contextCaching))
            },
        )

        fun readConfigJson(obj: JsonObject): LlmConfig {
            fun s(key: String, fallback: String) = (obj[key] as? JsonPrimitive)?.takeIf { it.isString }?.content ?: fallback
            fun i(key: String, fallback: Int) = (obj[key] as? JsonPrimitive)?.intOrNull ?: fallback
            fun d(key: String, fallback: Double) = (obj[key] as? JsonPrimitive)?.doubleOrNull ?: fallback
            fun b(key: String, fallback: Boolean) = (obj[key] as? JsonPrimitive)?.booleanOrNull ?: fallback
            return LlmConfig(
                name = s("name", DEFAULT_NAME),
                provider = s("provider", "deepseek"),
                apiUrl = s("apiUrl", "https://api.deepseek.com/v1"),
                apiKey = s("apiKey", ""),
                model = s("model", "deepseek-v4-flash"),
                temperature = d("temperature", 0.7),
                maxTokens = i("maxTokens", 2048),
                enabled = b("enabled", false),
                fallbackEnabled = b("fallbackEnabled", false),
                reasoningEffort = s("reasoningEffort", "disabled"),
                topP = d("topP", 1.0),
                frequencyPenalty = d("frequencyPenalty", 0.0),
                presencePenalty = d("presencePenalty", 0.0),
                responseFormat = s("responseFormat", "text"),
                maxCompletionTokens = i("maxCompletionTokens", 0),
                contextCaching = b("contextCaching", true),
            )
        }

        val JSON = Json { ignoreUnknownKeys = true }
        const val PREFS = "catclaw_state"
        const val KEY_ALL = "all_configs"
        const val KEY_CURRENT = "current_config_name"
        const val KEY_ROUNDS = AgentRunSettings.KEY_MAX_TOOL_ROUNDS
        const val KEY_EFFORT = AgentRunSettings.KEY_REASONING_EFFORT
        const val DEFAULT_NAME = "默认配置"
    }
}
