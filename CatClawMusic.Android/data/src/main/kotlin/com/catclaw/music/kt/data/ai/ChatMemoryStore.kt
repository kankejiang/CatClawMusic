package com.catclaw.music.kt.data.ai

import android.content.Context
import com.catclaw.music.kt.core.ai.ChatMemoryRules
import com.catclaw.music.kt.core.diagnostic.AppLog
import dagger.hilt.android.qualifiers.ApplicationContext
import java.io.File
import javax.inject.Inject
import javax.inject.Singleton
import kotlin.random.Random
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.SupervisorJob
import kotlinx.coroutines.launch
import kotlinx.coroutines.sync.Mutex
import kotlinx.coroutines.sync.withLock
import kotlinx.coroutines.withContext
import kotlinx.serialization.json.Json
import kotlinx.serialization.json.JsonArray
import kotlinx.serialization.json.JsonElement
import kotlinx.serialization.json.JsonObject
import kotlinx.serialization.json.JsonPrimitive
import kotlinx.serialization.json.buildJsonArray
import kotlinx.serialization.json.buildJsonObject
import kotlinx.serialization.json.jsonArray
import kotlinx.serialization.json.put

/**
 * AI 长期记忆的落盘存储 —— C# `ChatMemoryService` 的安卓对应物。
 *
 * 文件与键名照 MAUI：`ai_memory.json`，条目字段 `id/category/content/importance/createdAt/updatedAt`，
 * 这样用户从桌面端备份过来的记忆能被这边读到（反之亦然）。
 * JSON 手写而不上 `@Serializable`：`:data` 没挂 serialization 插件，
 * 而 `LlmConfigStore`、`BackupJson` 早就是手写 JsonElement —— 同一模块里两种风格更糟。
 *
 * 两件必须说清的事：
 * 1. `AgentService.memoryProvider` 是**同步**的（它在拼 prompt 的那一刻要字符串），
 *    所以内存里必须常备一份副本；副本在构造时于 IO 线程读一次盘，读之前给空串 ——
 *    宁可第一条消息少一段记忆，也不要在拼 prompt 的路上做磁盘 IO。
 * 2. 写盘全部串行（[writeMutex]）：提炼回来的批次和用户在设置页手动加的会撞车，
 *    而这份文件是整份覆盖写的，并发写就是丢数据。
 */
@Singleton
class ChatMemoryStore @Inject constructor(
    @ApplicationContext context: Context,
) {
    private val file = File(context.filesDir, FILE_NAME)
    private val writeMutex = Mutex()

    /** 有没有写过盘。首帧读盘要认这个标志：用户在读完成之前就在设置页加了一条的话，不能让盘上的旧内容盖回去 */
    private var written = false

    private val scope = CoroutineScope(SupervisorJob() + Dispatchers.IO)

    private val _items = MutableStateFlow<List<ChatMemoryRules.Item>>(emptyList())

    /** 设置页看这份；模型那边用 [promptText] */
    val items: StateFlow<List<ChatMemoryRules.Item>> = _items.asStateFlow()

    init {
        // 与 C# 的 `_ = LoadFromDiskAsync()` 同一个形状：不阻塞构造，读失败就当没有记忆
        scope.launch {
            runCatching {
                val text = if (file.isFile) file.readText() else ""
                writeMutex.withLock { if (!written) _items.value = ChatMemoryJson.decode(text) }
            }.onFailure { AppLog.w(TAG, "load memory failed", it) }
        }
    }

    /** 注入给 AgentService 的那段文本。没读到盘之前给空串，那条 `[记忆]` system 消息就不出现 */
    fun promptText(): String = ChatMemoryRules.renderAgentMemory(_items.value)

    /** 手动加一条（设置页那条路）。返回是否真的加进去了 —— 空白内容不算加 */
    suspend fun add(content: String, category: String = ChatMemoryRules.CATEGORY_PREFERENCE, importance: Int = ChatMemoryRules.DEFAULT_IMPORTANCE): Boolean {
        if (!ChatMemoryRules.isRecordable(content)) return false
        val now = System.currentTimeMillis()
        val item = ChatMemoryRules.Item(
            id = ChatMemoryRules.newId(_items.value.mapTo(HashSet()) { it.id }) { Random.nextInt() },
            category = category,
            content = content.trim(),
            importance = ChatMemoryRules.clampImportance(importance),
            createdAt = now,
        )
        persist(ChatMemoryRules.merge(_items.value, listOf(item), now))
        return true
    }

    suspend fun remove(id: String) {
        persist(_items.value.filterNot { it.id == id })
    }

    suspend fun clear() {
        persist(emptyList())
    }

    /**
     * 吃模型提炼回来的原文。
     *
     * 返回被收下的条数（0 是常态：模型判断"这段对话没什么值得记的"）。
     * 解析失败不抛 —— 提炼是后台动作，为一次坏 JSON 把聊天链路带崩不值当。
     */
    suspend fun absorbExtraction(raw: String): Int {
        val now = System.currentTimeMillis()
        val extracted = runCatching { ChatMemoryRules.parseExtracted(raw, now) { Random.nextInt() } }.getOrDefault(emptyList())
        if (extracted.isEmpty()) return 0
        val merged = ChatMemoryRules.merge(_items.value, extracted, now)
        persist(merged)
        AppLog.i(TAG, "absorbed ${extracted.size} memories, total ${merged.size}")
        return extracted.size
    }

    private suspend fun persist(next: List<ChatMemoryRules.Item>) {
        writeMutex.withLock {
            written = true
            _items.value = next
            val text = ChatMemoryJson.encode(next)
            withContext(Dispatchers.IO) {
                runCatching {
                    file.parentFile?.mkdirs()
                    file.writeText(text)
                }.onFailure { AppLog.w(TAG, "save memory failed", it) }
            }
        }
    }

    private companion object {
        const val TAG = "CatClawMemory"
        const val FILE_NAME = "ai_memory.json"
    }
}

/**
 * `ai_memory.json` 的读写格式。单独拎出来是为了**能被测**：
 * 这个文件是跨端交换格式（MAUI 备份过来的记忆要靠它对上键名），
 * 键名写错一个字母的表现是"读回来是空的"，不报错、不崩，只在用户身上显形。
 */
internal object ChatMemoryJson {

    private val JSON = Json { prettyPrint = true; encodeDefaults = false; ignoreUnknownKeys = true }

    fun encode(items: List<ChatMemoryRules.Item>): String {
        val root = buildJsonArray {
            items.forEach { i ->
                add(
                    buildJsonObject {
                        put("Id", i.id)
                        put("Category", i.category)
                        put("Content", i.content)
                        put("Importance", i.importance)
                        put("CreatedAt", i.createdAt)
                        // C# 那边可空字段会整个省略掉，这里也省略而不是写 null：
                        // 读侧两种都认，但生成的文件要和桌面端对得上眼
                        i.updatedAt?.let { put("UpdatedAt", it) }
                    },
                )
            }
        }
        return JSON.encodeToString(JsonElement.serializer(), root)
    }

    /** 多余键忽略、缺失键取默认、坏行丢掉而不是整份作废 —— 对面以后加字段时旧包不该崩 */
    fun decode(text: String): List<ChatMemoryRules.Item> {
        if (text.isBlank()) return emptyList()
        val array = runCatching { JSON.parseToJsonElement(text).jsonArray }.getOrNull() ?: return emptyList()
        return array.mapNotNull { element ->
            val obj = element as? JsonObject ?: return@mapNotNull null
            fun str(key: String): String? = (obj[key] as? JsonPrimitive)
                ?.takeIf { it.content != "null" }?.content
            fun num(key: String): Long? = str(key)?.toLongOrNull()
            val content = str("Content")?.trim().orEmpty()
            val id = str("Id")?.trim().orEmpty()
            if (content.isEmpty() || id.isEmpty()) return@mapNotNull null
            ChatMemoryRules.Item(
                id = id,
                category = str("Category")?.takeIf { it.isNotBlank() } ?: ChatMemoryRules.CATEGORY_PREFERENCE,
                content = content,
                importance = ChatMemoryRules.clampImportance(
                    str("Importance")?.toIntOrNull() ?: ChatMemoryRules.DEFAULT_IMPORTANCE,
                ),
                createdAt = num("CreatedAt") ?: 0L,
                updatedAt = num("UpdatedAt"),
            )
        }
    }
}
